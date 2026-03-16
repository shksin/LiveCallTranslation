# Genesys AudioHook v2 Integration — End-to-End Flow

## Overview

This application integrates with **Genesys Cloud AudioHook v2** to provide real-time voice translation for calls handled by Genesys Cloud contact centers. When a caller is connected through Genesys, their audio is streamed via the AudioHook WebSocket protocol, translated in real time using Azure AI Speech SDK, and delivered to an English-speaking agent through the browser-based agent UI.

Unlike the ACS telephony mode (which uses Azure Communication Services for call control), the Genesys integration receives audio directly from the Genesys platform — no ACS phone number or EventGrid subscription is required.

---

## Architecture

```
┌──────────┐          ┌────────────────────┐
│  Caller  │◄────────►│   Genesys Cloud    │
│ (Phone)  │  PSTN    │   Contact Center   │
└──────────┘          └────────┬───────────┘
                               │
                  AudioHook v2 WebSocket
                  (µ-law 8kHz mono audio)
                               │
                               ▼
                  ┌────────────────────────────────────┐
                  │   /ws/genesys                      │
                  │   GenesysWebSocketHandler           │
                  │                                    │
                  │  ① open → ProcessOpen → "opened"   │
                  │  ② Binary frames (µ-law 8kHz)      │
                  │  ③ ping → "pong"                   │
                  │  ④ close → "closed"                │
                  └────────┬───────────────────────────┘
                           │
                     MuLawConverter
                  µ-law 8kHz ↔ PCM 16kHz
                           │
                           ▼
                  ┌────────────────────────────────────┐
                  │       GenesysCallBridge             │
                  │                                    │
                  │  CallerAudioReader (Channel<byte[]>)│
                  │  SendToCallerAsync (PCM→µ-law→WS)  │
                  └────────┬───────────────────────────┘
                           │
                           ▼
┌──────────┐      ┌───────────────────────────────────────────────────┐
│  Agent   │◄────►│                CallManager                       │
│(Browser) │  WS  │                                                   │
│          │      │  ┌─────────────────┐     ┌─────────────────┐      │
└──────────┘      │  │ User→Agent      │     │ Agent→User      │      │
   ▲              │  │ Translator      │     │ Translator      │      │
   │              │  │                 │     │                 │      │
   │              │  │ STT (caller     │     │ STT (agent      │      │
   │              │  │   language)     │     │   language)     │      │
   │  Mixed       │  │ Translate       │     │ Translate       │      │
   │  Audio       │  │   → agent lang  │     │   → caller lang │      │
   │◄─────────────│  │ TTS (agent     │     │ TTS (caller     │      │
   │              │  │   voice)       │     │   voice)        │      │
   │              │  └────────┬────────┘     └────────┬────────┘      │
   │              │           │                       │               │
   │              │           ▼                       ▼               │
   │              │  ┌──────────────┐      ┌──────────────────┐       │
   │              │  │ DynamicMixer │      │ GenesysCallBridge│       │
   │              │  │ (→ Agent WS) │      │ (→ Genesys → Caller)    │
   │              │  └──────────────┘      └──────────────────┘       │
   │              └───────────────────────────────────────────────────┘
   │
   └── Transcription JSON + Mixed audio (50ms frames)
```

---

## End-to-End Flow — Step by Step

### 1. Genesys Connects via AudioHook v2

Genesys Cloud is configured with an **AudioHook integration** pointing to:

```
wss://<your-app-hostname>/ws/genesys
```

When a call is placed through Genesys, the platform opens a WebSocket connection to this endpoint, sending the `audiohook-session-id` as a header (or `sessionId` query parameter).

**Endpoint setup** (from `Program.cs`):
```
GET /ws/genesys → GenesysWebSocketHandler.HandleGenesysStreamAsync
```

The WebSocket is **unauthenticated by design** — the `/ws/genesys` path is excluded from the `AuthCode` middleware so Genesys can connect without credentials.

### 2. AudioHook Protocol Handshake (`open` / `opened`)

Once the WebSocket is established, Genesys sends a JSON **`open`** message containing:

| Field | Description |
|-------|-------------|
| `id` | AudioHook session ID |
| `seq` | Client sequence number |
| `parameters.conversationId` | Genesys conversation ID |
| `parameters.inputVariables.language` | Caller's language hint (optional) |

The handler:
1. Creates a `GenesysCallBridge` via the `GenesysCallBridgeManager` singleton.
2. Calls `bridge.ProcessOpen(doc)` to extract `conversationId` and `callerLanguage`.
3. Creates a call record via `CallService.CreateGenesysCallAsync(conversationId, userLanguage)` — this makes the call visible in the agent UI with status `Waiting`.
4. Responds with an **`opened`** message selecting the audio format:

```json
{
  "version": "2",
  "type": "opened",
  "parameters": {
    "media": [
      { "type": "audio", "format": "PCMU", "channels": ["external"], "rate": 8000 }
    ]
  }
}
```

This tells Genesys to stream audio in **µ-law (PCMU) 8kHz mono** format.

### 3. Audio Streaming (Binary Frames)

With the handshake complete, Genesys begins sending **binary WebSocket frames** containing µ-law 8kHz audio from the caller.

For each binary frame, `GenesysWebSocketHandler`:
1. Extracts the raw µ-law bytes.
2. Converts to **PCM 16kHz 16-bit mono** via `MuLawConverter.MuLaw8kToPcm16k()` (linear interpolation 2x upsample).
3. Pushes the PCM data to `GenesysCallBridge.PushCallerAudio()` — writing to a bounded `Channel<byte[]>`.

### 4. Agent Connects from Browser UI

The agent opens the browser UI at `/agent/` and connects via WebSocket to `/api/agent/ws`. The agent sees the Genesys call listed as a waiting call (identified by the `genesys:` prefix on `CallerId`).

When the agent clicks **Connect** and selects languages, the frontend sends:

```json
{
  "type": "connect",
  "callId": "<guid>",
  "options": {
    "UserLanguage": "es-MX",
    "AgentLanguage": "en-US",
    "AgentAudioOptions": { ... }
  }
}
```

### 5. CallManager Starts Translation Pipeline

`CallManager.ConnectAgentAsync` detects the call's `IncomingCallContext` starts with `"genesys:"` and routes to `RunGenesysCallAsync()`.

This method:
1. **Finds the bridge** — scans `GenesysCallBridgeManager` for the matching `conversationId`.
2. **Creates two translators** via `ITranslatorFactory.CreateAsync()`:
   - **User→Agent**: Caller's language → Agent's language (e.g., Spanish → English)
   - **Agent→User**: Agent's language → Caller's language (e.g., English → Spanish)
3. **Starts the DynamicMixer** — mixes original + translated audio streams for the agent's headset.
4. **Sends a welcome message** to the caller via TTS (if configured for the language).

### 6. Bidirectional Translation

Two parallel tasks run for the duration of the call:

#### Caller → Agent (User→Agent direction)
```
GenesysCallBridge.CallerAudioReader  (PCM 16kHz)
        │
        ├──► DynamicMixer.AddUserOriginalAudio()      → Agent hears original
        │
        └──► userToAgentTranslator.SendData()
                    │
                    ├── STT: Recognize caller speech
                    ├── Translate: Caller lang → Agent lang
                    ├── TTS: Synthesize agent-language audio
                    │
                    ├──► DynamicMixer.AddUserTranslatedAudio() → Agent hears translation
                    └──► agentWs: transcription JSON            → Agent sees subtitles
```

#### Agent → Caller (Agent→User direction)
```
Agent WebSocket (PCM 16kHz from browser mic)
        │
        ├──► DynamicMixer.AddAgentOriginalAudio()     → Agent hears own voice
        │
        └──► agentToUserTranslator.SendData()
                    │
                    ├── STT: Recognize agent speech
                    ├── Translate: Agent lang → Caller lang
                    ├── TTS: Synthesize caller-language audio
                    │
                    ├──► DynamicMixer.AddAgentTranslatedAudio() → Agent hears own translation
                    └──► GenesysCallBridge.SendToCallerAsync()  → Caller hears translation
                              │
                              ├── MuLawConverter.Pcm16kToMuLaw8k() (downsample + encode)
                              └── WebSocket binary frame → Genesys → Caller
```

### 7. Protocol Keepalive

During the call, Genesys sends **`ping`** messages. The handler responds with **`pong`** to keep the connection alive.

### 8. Call Termination

The call ends when either side disconnects:

| Trigger | Action |
|---------|--------|
| Genesys sends `close` | Handler responds with `closed`, calls `bridge.SignalDisconnect()` |
| Genesys sends `disconnect` | Handler calls `bridge.SignalDisconnect()` |
| WebSocket closes | Handler calls `bridge.SignalDisconnect()` |
| Agent disconnects | `CallManager` cancels the linked `CancellationTokenSource` |

On termination:
1. `GenesysCallBridge.SignalDisconnect()` completes the audio channel and cancels the disconnect token.
2. `CallManager.RunGenesysCallAsync` detects either `callerAudioTask` or `agentReceiveTask` completing, cancels the other.
3. Call status is set to `Ended` in the database.
4. The bridge is removed from `GenesysCallBridgeManager`.
5. Translators and mixer are disposed.

---

## Audio Format Conversion

Genesys AudioHook uses **µ-law (PCMU) at 8kHz mono**, while the Azure AI Speech SDK requires **PCM 16-bit at 16kHz mono**. The `MuLawConverter` handles bidirectional conversion:

| Direction | Conversion | Method |
|-----------|-----------|--------|
| Genesys → Translator | µ-law 8kHz → PCM 16kHz | `MuLaw8kToPcm16k()` — Decompress via lookup table, 2x upsample with linear interpolation |
| Translator → Genesys | PCM 16kHz → µ-law 8kHz | `Pcm16kToMuLaw8k()` — 2x downsample (decimation), encode via µ-law compression |

---

## Key Components

| Component | File | Responsibility |
|-----------|------|----------------|
| `GenesysWebSocketHandler` | `Genesys/GenesysWebSocketHandler.cs` | Handles the AudioHook v2 WebSocket protocol (open/close/ping/binary) |
| `GenesysCallBridge` | `Genesys/GenesysCallBridge.cs` | Bridges audio between Genesys and the translation pipeline via channels |
| `GenesysCallBridgeManager` | `Genesys/GenesysCallBridge.cs` | Singleton registry of active bridges, keyed by session ID |
| `MuLawConverter` | `Genesys/MuLawConverter.cs` | µ-law 8kHz ↔ PCM 16kHz audio format conversion |
| `CallManager.RunGenesysCallAsync` | `CallManager.cs` | Orchestrates translators, mixer, and audio routing for a Genesys call |
| `CallService.CreateGenesysCallAsync` | `Service/CallService.cs` | Creates the call record (with `genesys:` prefix) so it appears in the agent UI |

---

## Configuration

### Genesys Cloud Setup

1. **Create an AudioHook integration** in Genesys Cloud Admin:
   - **WebSocket URI**: `wss://<your-app-hostname>/ws/genesys`
   - **Audio format**: PCMU (µ-law), 8kHz, mono
   - **Channel**: `external` (caller side)
   - Genesys automatically sends the `audiohook-session-id` header on the WebSocket connection. The server uses this to track each session. If the header is absent, it falls back to a `?sessionId=` query parameter.

2. **Configure an Architect flow** (or interaction routing) to attach the AudioHook integration to calls that need translation.

3. **Optional**: Set an `inputVariable` named `language` on the AudioHook action to pass the caller's language (e.g., `es-MX`). If not provided, the agent selects the language from the UI dropdown when connecting.

4. **No authentication required on the Genesys side** — the `/ws/genesys` endpoint is excluded from the application's `AuthCode` middleware, so no credentials need to be configured in Genesys. If your deployment requires additional security, implement IP allowlisting at the network layer (e.g., Azure App Service access restrictions).

### Application Settings

No additional application settings are needed beyond the base configuration. The Genesys integration only requires:

| Setting | Description | Required |
|---------|-------------|----------|
| `AzureAISpeech:ResourceID` | Azure AI Speech resource ID | Yes |
| `AzureAISpeech:Region` | Region of the Speech resource | Yes |
| `AuthCode` | Auth code for the agent UI (does not affect the `/ws/genesys` endpoint) | Optional |

ACS-specific settings (`ACS:Endpoint`, `EventGrid:TopicResourceID`, etc.) are **not required** for the Genesys integration.

---

## Sequence Diagram

```
Genesys Cloud          Application Server             Agent Browser
     │                        │                            │
     │──── WS Connect ───────►│                            │
     │     /ws/genesys        │                            │
     │                        │                            │
     │──── open (JSON) ──────►│                            │
     │     {conversationId,   │── CreateGenesysCallAsync──►│
     │      language}         │   (call appears in UI)     │
     │                        │                            │
     │◄─── opened (JSON) ────│                            │
     │     {media: PCMU 8k}   │                            │
     │                        │                            │
     │──── Binary (µ-law) ───►│                            │
     │──── Binary (µ-law) ───►│ (buffered in bridge)       │
     │──── Binary (µ-law) ───►│                            │
     │                        │                            │
     │     ping ─────────────►│                            │
     │◄──── pong ─────────────│                            │
     │                        │                            │
     │                        │◄── connect {callId, opts} ─│
     │                        │    Agent picks language     │
     │                        │                            │
     │                        │── RunGenesysCallAsync() ───│
     │                        │   Create translators        │
     │                        │                            │
     │                        │──── enable ───────────────►│
     │                        │                            │
     │══ Bidirectional audio translation begins ═══════════│
     │                        │                            │
     │── Binary (µ-law) ─────►│ → PCM 16k → STT →         │
     │                        │   Translate → TTS →        │
     │                        │   DynamicMixer ───────────►│ (translated audio)
     │                        │                  ─────────►│ (transcription JSON)
     │                        │                            │
     │                        │◄── Binary (PCM 16k) ──────│ (agent speaks)
     │◄── Binary (µ-law) ────│ ← PCM→µ-law ← TTS ←      │
     │  (caller hears         │   Translate ← STT          │
     │   translated agent)    │                            │
     │                        │                            │
     │──── close ────────────►│                            │
     │◄─── closed ───────────│── acsCallDisconnected ────►│
     │                        │   Call status → Ended      │
     │                        │                            │
```

---

## Differences from ACS Mode

| Aspect | ACS Mode | Genesys Mode |
|--------|----------|--------------|
| **Call source** | PSTN via Azure Communication Services | PSTN via Genesys Cloud |
| **Audio delivery** | ACS Media Streaming WebSocket (PCM 16kHz) | AudioHook v2 WebSocket (µ-law 8kHz) |
| **Audio conversion** | None (native PCM 16kHz) | µ-law 8kHz ↔ PCM 16kHz via `MuLawConverter` |
| **Call control** | ACS SDK (AnswerCallAsync, media streaming) | Genesys AudioHook protocol (open/opened/close/closed) |
| **Bridge class** | `ACSCallBridge` | `GenesysCallBridge` |
| **Handler class** | `ACSWebSocketHandler` | `GenesysWebSocketHandler` |
| **Call ID source** | EventGrid IncomingCall event | AudioHook `open` message `conversationId` |
| **Call record prefix** | Direct call context | `genesys:{conversationId}` |
| **Required Azure services** | ACS + EventGrid + AI Speech | AI Speech only |
