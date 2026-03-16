# ACS (Azure Communication Services) Integration — End-to-End Flow

## Overview

This application integrates with **Azure Communication Services (ACS)** to provide real-time voice translation for PSTN phone calls. When a caller dials the configured ACS phone number, the call is answered automatically with bidirectional media streaming enabled. The caller's audio is translated in real time using Azure AI Speech SDK and delivered to an English-speaking agent through the browser-based agent UI — and vice versa.

---

## Architecture

```
┌──────────┐     PSTN      ┌──────────────────────────┐
│  Caller  │◄─────────────►│  Azure Communication     │
│ (Phone)  │               │  Services (ACS)          │
└──────────┘               └──────┬───────────────────┘
                                  │
                    ① IncomingCall event (EventGrid)
                                  │
                                  ▼
                    ┌─────────────────────────────┐
                    │      EventGridController    │
                    │  POST /api/events           │
                    └──────┬──────────────────────┘
                           │
                    ② InboundCallHandler → CallService.CreateCallAsync
                       (AnswerCallAsync with media streaming config)
                           │
                           ▼
                    ┌─────────────────────────────┐
                    │      ACS Media Streaming    │
                    │  wss://host/ws/acs/{callId} │
                    │  (PCM 16kHz 16-bit mono)    │
                    └──────┬──────────────────────┘
                           │
                    ③ ACSWebSocketHandler parses AudioData
                       pushes PCM to ACSCallBridge
                           │
                           ▼
┌──────────┐       ┌──────────────────────────────────────────────────┐
│  Agent   │◄─────►│               CallManager                       │
│(Browser) │  WS   │                                                  │
│          │       │  ┌────────────────┐    ┌────────────────┐        │
└──────────┘       │  │ User→Agent     │    │ Agent→User     │        │
   ▲               │  │ Translator     │    │ Translator     │        │
   │               │  │                │    │                │        │
   │               │  │ ④ STT (caller  │    │ ⑦ STT (agent   │        │
   │               │  │   language)    │    │   language)    │        │
   │               │  │ ⑤ Translate    │    │ ⑧ Translate    │        │
   │  Mixed        │  │   → agent lang │    │   → caller lang│        │
   │  Audio        │  │ ⑥ TTS (agent  │    │ ⑨ TTS (caller  │        │
   │◄──────────────│  │   voice)      │    │   voice)       │        │
   │               │  └───────┬────────┘    └───────┬────────┘        │
   │               │          │                     │                 │
   │               │          ▼                     ▼                 │
   │               │  ┌──────────────┐    ┌──────────────────┐        │
   │               │  │ DynamicMixer │    │ ACSCallBridge    │        │
   │               │  │ (→ Agent WS) │    │ (→ ACS → Caller) │        │
   │               │  └──────────────┘    └──────────────────┘        │
   │               └──────────────────────────────────────────────────┘
   │
   └── Transcription JSON + Mixed audio (50ms frames)
```

---

## End-to-End Flow — Step by Step

### 1. Caller Dials the ACS Phone Number

A PSTN caller dials the phone number configured in `ACS:InboundNumber`. ACS receives the call and fires a **`Microsoft.Communication.IncomingCall`** event via Azure EventGrid.

### 2. EventGrid Delivers the Webhook

The EventGrid system topic delivers the event to:
```
POST /api/events
```

The `EventGridController` parses the CloudEvent and routes it to `InboundCallHandler` (which is registered for `Microsoft.Communication.IncomingCall` events).

**Auto-configuration**: On application startup, `EventGridSubscriptionManager.TryAutoConfigureAsync()` automatically creates or updates the EventGrid subscription to point to the app's `/api/events` endpoint. This means:
- On first deploy, the subscription is created automatically.
- If the app hostname changes, the subscription is updated.
- If the subscription is in a bad state (`AwaitingManualAction`), it is deleted and recreated.

### 3. InboundCallHandler Processes the Call

The handler:
1. Validates the event is not stale (max age: 3 minutes).
2. Checks if the incoming call's destination number matches `ACS:InboundNumber` (supports wildcard `*` for accepting all calls).
3. Extracts the caller ID and optional `language` from VoIP custom context headers.
4. Calls `CallService.CreateCallAsync()`.

### 4. Call Is Answered with Media Streaming

`CallService.CreateCallAsync()` performs several actions:

1. **Creates a call record** in the in-memory database with status `New`.
2. **Answers the call** via `ACSService.AnswerCallAsync()` with:
   - A callback URI: `https://<hostname>/api/calls/{callId}/callback` — for ACS to report call state changes (e.g., disconnect).
   - A media streaming WebSocket URI: `wss://<hostname>/ws/acs/{callId}` — for bidirectional audio streaming.
   - Media streaming config: **PCM 16kHz mono, unmixed, bidirectional, start immediately**.
3. Stores the `CallConnectionId` returned by ACS.
4. Sets the call status to `Waiting`.

> **Important**: Media streaming is started at answer time via `StartMediaStreaming = true`. It must **NOT** be started again later — doing so would reset the ACS WebSocket connection and break the bridge.

### 5. ACS Media Streaming WebSocket Connects

ACS opens a WebSocket connection to:
```
wss://<hostname>/ws/acs/{callId}
```

`ACSWebSocketHandler.HandleACSStreamAsync()`:
1. Looks up the call in the database to verify it exists.
2. Creates an `ACSCallBridge` via the `ACSCallBridgeManager` singleton.
3. Registers the WebSocket on the bridge.
4. Enters a receive loop, parsing ACS streaming messages.

ACS sends two types of messages:

| Message Kind | Content |
|-------------|---------|
| `AudioMetadata` | Stream configuration metadata (logged) |
| `AudioData` | Base64-encoded PCM 16kHz 16-bit mono audio from the caller |

For each `AudioData` message, the handler decodes the base64 payload and pushes the raw PCM bytes into `ACSCallBridge.PushCallerAudio()` via a bounded `Channel<byte[]>`.

### 6. Agent Connects from Browser UI

The agent opens `/agent/` and connects via WebSocket to `/api/agent/ws`. The ACS call appears as a waiting call. When the agent clicks **Connect** and selects languages:

```json
{
  "type": "connect",
  "callId": "<guid>",
  "options": {
    "UserLanguage": "es-MX",
    "AgentLanguage": "en-US",
    "AgentAudioOptions": {
      "UserOriginalAudio": false,
      "UserTranslatedAudio": true,
      "AgentOriginalAudio": false,
      "AgentTranslatedAudio": false
    }
  }
}
```

### 7. CallManager Starts Translation Pipeline

`CallManager.ConnectAgentAsync` detects the call has a non-Genesys `IncomingCallContext` and routes to `RunACSCallAsync()`.

This method:
1. **Waits for the ACS bridge** — polls `ACSCallBridgeManager` for up to 5 seconds until the ACS WebSocket has connected and registered the bridge.
2. **Creates two translators** via `ITranslatorFactory.CreateAsync()`:
   - **User→Agent**: Caller's language → Agent's language (e.g., Spanish → English)
   - **Agent→User**: Agent's language → Caller's language (e.g., English → Spanish)
3. **Starts the DynamicMixer** — mixes original + translated audio streams for the agent's headset.
4. **Sends a welcome message** to the caller via TTS (if configured for the language).

### 8. Bidirectional Translation

Two parallel tasks run for the duration of the call:

#### Caller → Agent (User→Agent direction)
```
ACSCallBridge.CallerAudioReader  (PCM 16kHz from ACS)
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
                    └──► ACSCallBridge.SendToCallerAsync()      → Caller hears translation
                              │
                              ├── Wrap as JSON: { kind: "AudioData", audioData: { data: "<base64>" } }
                              └── WebSocket text frame → ACS → Caller phone
```

### 9. ACS Callback Events

During the call, ACS sends callback events to:
```
POST /api/calls/{callId}/callback
```

The `CallsController.CallCallback` endpoint parses these events. When a `Microsoft.Communication.CallDisconnected` event is received, the call status is set to `Ended`.

### 10. Call Termination

The call ends when either side disconnects:

| Trigger | Action |
|---------|--------|
| Caller hangs up | ACS closes the media streaming WebSocket + sends `CallDisconnected` callback |
| ACS WebSocket closes | `ACSWebSocketHandler` calls `bridge.SignalDisconnect()` |
| Agent disconnects | `CallManager` cancels the linked `CancellationTokenSource` |

On termination:
1. `ACSCallBridge.SignalDisconnect()` completes the audio channel and cancels the disconnect token.
2. `CallManager.RunACSCallAsync` detects either `callerAudioTask` or `agentReceiveTask` completing, cancels the other.
3. Call status is set to `Ended`.
4. The bridge is removed from `ACSCallBridgeManager`.
5. Translators and mixer are disposed.
6. A `disconnect` message is sent to the agent WebSocket, and an `acsCallDisconnected` event is fired so the agent UI updates.

---

## Audio Format

ACS media streaming natively uses **PCM 16kHz 16-bit mono** — the same format required by the Azure AI Speech SDK. No audio format conversion is needed (unlike the Genesys integration which requires µ-law ↔ PCM conversion).

| Direction | Format | Transport |
|-----------|--------|-----------|
| ACS → Server | PCM 16kHz 16-bit mono (base64 JSON) | WebSocket text frame (`kind: "AudioData"`) |
| Server → ACS | PCM 16kHz 16-bit mono (base64 JSON) | WebSocket text frame (`kind: "AudioData"`) |

---

## Key Components

| Component | File | Responsibility |
|-----------|------|----------------|
| `EventGridController` | `Controllers/EventGridController.cs` | Receives EventGrid webhook events (validation + CloudEvent dispatch) |
| `InboundCallHandler` | `EventGrid/InboundCallHandler.cs` | Handles `IncomingCall` events — validates number, creates call |
| `EventGridSubscriptionManager` | `EventGrid/EventGridSubscriptionManager.cs` | Auto-configures the EventGrid subscription on app startup |
| `ACSService` | `Service/ACSService.cs` | ACS SDK wrapper — answers calls, configures media streaming |
| `ACSWebSocketHandler` | `Service/ACSWebSocketHandler.cs` | Handles the ACS media streaming WebSocket (parses AudioData/AudioMetadata) |
| `ACSCallBridge` | `Service/ACSCallBridge.cs` | Bridges audio between ACS WebSocket and translation pipeline via channels |
| `ACSCallBridgeManager` | `Service/ACSCallBridge.cs` | Singleton registry of active bridges, keyed by call ID (Guid) |
| `CallService` | `Service/CallService.cs` | Call lifecycle — create, answer, status updates |
| `CallsController` | `Controllers/CallsController.cs` | REST endpoints — waiting calls, ACS callbacks, ACS config/token |
| `CallManager.RunACSCallAsync` | `CallManager.cs` | Orchestrates translators, mixer, and audio routing for an ACS call |

---

## API Endpoints

| Endpoint | Method | Auth | Purpose |
|----------|--------|------|---------|
| `/api/events` | OPTIONS | None | EventGrid webhook validation handshake |
| `/api/events` | POST | None | Receives EventGrid CloudEvents (IncomingCall) |
| `/ws/acs/{callId}` | WebSocket | None | ACS media streaming (bidirectional audio) |
| `/api/calls/{callId}/callback` | POST | AuthCode | ACS call state callbacks (disconnect detection) |
| `/api/calls/waiting` | GET | AuthCode | Lists waiting calls (used by agent UI) |
| `/api/calls/acs/config` | GET | None | Returns ACS configuration status and inbound number |
| `/api/calls/acs/token` | GET | AuthCode | Issues a VoIP token for ACS client SDK |
| `/api/agent/ws` | WebSocket | AuthCode | Agent UI WebSocket (call control + audio) |

---

## Sequence Diagram

```
Caller (Phone)        ACS            EventGrid        Server                    Agent Browser
     │                 │                 │                │                           │
     │── Dial ────────►│                 │                │                           │
     │                 │                 │                │                           │
     │                 │── IncomingCall ─►│                │                           │
     │                 │   CloudEvent    │── POST ───────►│                           │
     │                 │                 │  /api/events   │                           │
     │                 │                 │                │                           │
     │                 │                 │                │── InboundCallHandler      │
     │                 │                 │                │   Validate number match   │
     │                 │                 │                │                           │
     │                 │◄── AnswerCallAsync ──────────────│                           │
     │                 │   (callback URI + WS URI +       │                           │
     │                 │    media streaming config)       │                           │
     │                 │                                  │                           │
     │◄── Connected ──│                                  │                           │
     │   (call active) │                                  │── Call record created     │
     │                 │                                  │   Status: Waiting         │
     │                 │                                  │                           │
     │                 │── WS Connect ───────────────────►│                           │
     │                 │   wss://host/ws/acs/{callId}     │                           │
     │                 │                                  │                           │
     │                 │── AudioMetadata ────────────────►│── ACSWebSocketHandler     │
     │                 │── AudioData (base64 PCM) ──────►│   → ACSCallBridge         │
     │                 │── AudioData ───────────────────►│     (buffered in channel)  │
     │                 │                                  │                           │
     │                 │                                  │◄── connect {callId} ──────│
     │                 │                                  │    Agent picks language   │
     │                 │                                  │                           │
     │                 │                                  │── RunACSCallAsync()       │
     │                 │                                  │   Create translators      │
     │                 │                                  │                           │
     │                 │                                  │──── enable ──────────────►│
     │                 │                                  │                           │
     │═══ Bidirectional audio translation begins ═════════════════════════════════════│
     │                 │                                  │                           │
     │── Voice ──────►│── AudioData ──►│                 │── STT → Translate → TTS  │
     │  (caller        │                                  │── DynamicMixer ─────────►│
     │   speaks)       │                                  │   (translated audio)      │
     │                 │                                  │── transcription JSON ───►│
     │                 │                                  │                           │
     │                 │                                  │◄── PCM audio ────────────│
     │                 │◄── AudioData (base64 PCM) ──────│   (agent speaks)          │
     │◄── Voice ──────│  (translated to caller language)  │                           │
     │  (hears agent   │                                  │                           │
     │   translated)   │                                  │                           │
     │                 │                                  │                           │
     │── Hang up ────►│                                  │                           │
     │                 │── WS Close ────────────────────►│                           │
     │                 │── CallDisconnected callback ───►│                           │
     │                 │                                  │── acsCallDisconnected ──►│
     │                 │                                  │   Status: Ended          │
```

---

## Configuration

### Required Azure Resources

| Resource | Purpose |
|----------|---------|
| **Azure Communication Services** | PSTN telephony, call control, media streaming |
| **ACS Phone Number** | The PSTN number callers dial |
| **Azure EventGrid System Topic** | Delivers `IncomingCall` events from ACS to the app |
| **Azure AI Speech** | Speech-to-text, translation, text-to-speech |

### Application Settings

| Setting | Description | Example |
|---------|-------------|---------|
| `AzureAISpeech:ResourceID` | Azure AI Speech resource ID | `/subscriptions/.../Microsoft.CognitiveServices/accounts/my-speech` |
| `AzureAISpeech:Region` | Region of the Speech resource | `eastus`, `australiaeast` |
| `ACS:Endpoint` | ACS resource endpoint | `https://my-acs.communication.azure.com/` |
| `ACS:InboundNumber` | Phone number to accept calls on, or `*` for all | `+18335276165` |
| `EventGrid:TopicResourceID` | EventGrid system topic resource ID for ACS events | `/subscriptions/.../systemTopics/my-topic` |
| `Inbound:Hostname` | App hostname for callbacks (auto-detected on Azure via `WEBSITE_HOSTNAME`) | `myapp.azurewebsites.net` |
| `AuthCode` | Query-string auth code for agent/user UIs | `my-secret-code` |

### Environment Variable Format (for Azure App Service)

```
AzureAISpeech__ResourceID=/subscriptions/.../accounts/my-speech
AzureAISpeech__Region=australiaeast
ACS__Endpoint=https://my-acs.communication.azure.com/
ACS__InboundNumber=+18335276165
EventGrid__TopicResourceID=/subscriptions/.../systemTopics/my-topic
AuthCode=my-secret-code
```

### Azure RBAC Requirements

| Role | Resource | Purpose |
|------|----------|---------|
| `Cognitive Services Speech User` | Azure AI Speech resource | Token-based auth for STT/TTS |
| `Contributor` (or equivalent) | ACS resource | Answer calls, manage media streams |
| `EventGrid EventSubscription Contributor` | EventGrid system topic | Auto-register webhook subscription |

---

## Hostname Resolution

The application auto-detects the hostname used for ACS callbacks and EventGrid webhooks:

| Priority | Source | When |
|----------|--------|------|
| 1 | `WEBSITE_HOSTNAME` env var | Running on Azure App Service (set automatically) |
| 2 | `Inbound:Hostname` setting | Explicit override |
| 3 | `localhost:5000` | Fallback default |

The resolved hostname is used to construct:
- **Callback URI**: `https://<hostname>/api/calls/{callId}/callback`
- **Media streaming URI**: `wss://<hostname>/ws/acs/{callId}`
- **Events URI**: `https://<hostname>/api/events`

---

## Important Notes

- **Media streaming starts at answer time** — it must NOT be started again via `StartMediaStreamingAsync()` or the ACS WebSocket connection will reset.
- **Only a single App Service instance** is supported — the in-memory call state and WebSocket connections are not distributed.
- **EventGrid subscription auto-configures** on startup — no manual webhook setup needed if RBAC is correctly assigned.
- **Callback endpoint detects disconnects** — when ACS fires `CallDisconnected`, the call is marked `Ended` even if the WebSocket hasn't closed yet.
- The `/ws/acs/{callId}` and `/api/events` endpoints are exempt from `AuthCode` authentication so ACS and EventGrid can reach them.
