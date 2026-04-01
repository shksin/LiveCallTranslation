# Genesys AudioHook v2 Integration

## Overview

This application integrates with **Genesys Cloud** to provide real-time voice translation for contact center calls. When a caller speaks a language the agent doesn't understand, Genesys streams the caller's audio to this application, which translates it and delivers the translated speech back — all within the live call, in real time.

The integration uses the **Genesys AudioHook v2** protocol. From Genesys's perspective, this application is an external audio processor attached to a call flow. No Azure Communication Services phone numbers or EventGrid subscriptions are needed — only the Azure AI Speech resource and a publicly reachable URL for this application.

---

## How Genesys AudioHook Works

**AudioHook** is a Genesys Cloud feature that lets you connect an external service to a live call's audio stream. When a call hits a configured AudioHook action in an Architect flow, Genesys opens a WebSocket connection to your external service and starts streaming the caller's audio in real time. Your service processes the audio and can send audio back — in this case, the translated speech.

The connection works like this:

```text
Phone Caller
    │
    │  (PSTN / Voice call)
    ▼
Genesys Cloud Contact Center
    │
    │  AudioHook v2 — WebSocket
    │  Streams caller audio out, receives translated audio back
    ▼
This Application  (/ws/genesys)
    │
    │  Azure AI Speech
    │  Speech-to-Text → Translate → Text-to-Speech
    ▼
Agent Browser  (/agent/)
    │  Hears translated caller audio
    │  Speaks — their words are translated back to the caller
```

Key points about how the connection works:

- **Genesys initiates the WebSocket** — your application does not call out to Genesys. Genesys connects to your application when a call reaches the AudioHook action in the flow.
- **Audio flows both ways** — Genesys sends caller audio to the application; the application sends translated audio back to Genesys, which plays it to the caller.
- **The connection is per-call** — a new WebSocket is opened for each call and closed when the call ends.
- **Audio format** — Genesys sends audio as µ-law encoded 8kHz mono (PCMU), a standard telephony format. The application handles all conversion to and from this format automatically. See [Audio Format — µ-law 8kHz Explained](#audio-format--µ-law-8khz-explained) below for details.

---

## Audio Format — µ-law 8kHz Explained

You will see the term **µ-law 8kHz** (also written PCMU) throughout this document. Here is what it means:

### µ-law (pronounced "mu-law")

µ-law is a way of **compressing audio samples** used in telephone networks. Instead of storing every audio sample at full precision, it uses a logarithmic scale — more detail is given to quiet sounds (which the human ear is more sensitive to) and less detail to loud sounds. The result is the same perceived voice quality at roughly half the data of uncompressed audio.

It is the standard audio encoding used in North American telephony and is what Genesys AudioHook uses on the wire.

### 8kHz (8,000 samples per second)

This is the **sample rate** — how many audio snapshots are captured per second. 8kHz has been the telephone standard for decades. It captures enough of the human voice frequency range (300Hz–3,400Hz) to be clearly intelligible, but is not suitable for music or high-fidelity audio.

For context:

| Format | Sample rate | Used for |
| ------ | ----------- | -------- |
| Telephone / Genesys AudioHook | 8kHz | Voice calls |
| Azure AI Speech (required) | 16kHz | Speech recognition and synthesis |
| CD audio | 44.1kHz | Music |

### Why this matters for the integration

Azure AI Speech requires **uncompressed PCM audio at 16kHz**. Genesys delivers **µ-law compressed audio at 8kHz**. The application bridges this gap automatically on every call:

- **Receiving from Genesys**: decode µ-law → raw PCM, then upsample 8kHz → 16kHz using linear interpolation
- **Sending back to Genesys**: downsample 16kHz → 8kHz, then encode PCM → µ-law

You do not need to configure anything for this — it happens transparently in the audio pipeline.

---

## WebSocket Endpoint

The URL you configure in Genesys Cloud must point to this application's AudioHook endpoint:

```text
wss://<your-app-hostname>/ws/genesys
```

| Environment | Example URL |
| ----------- | ----------- |
| Azure App Service | `wss://your-app-name.azurewebsites.net/ws/genesys` |
| Custom domain | `wss://translate.yourcompany.com/ws/genesys` |
| Local testing | Use the Genesys Simulator instead — see [GENESYS_SIMULATOR.md](GENESYS_SIMULATOR.md) |

**Requirements for the URL:**

- Must use `wss://` (secure WebSocket over TLS) — Genesys Cloud requires HTTPS/WSS
- The application must be publicly reachable from the internet (Genesys Cloud connects from their cloud infrastructure)
- No authentication credentials are required — the `/ws/genesys` endpoint intentionally has no auth so Genesys can connect without credentials. If you need to restrict access, use IP allowlisting at the network/firewall level (e.g., Azure App Service access restrictions for Genesys Cloud IP ranges)

---

## Genesys Cloud Configuration

There are two parts to configuring Genesys: creating an **AudioHook integration** (sets up the connection to this application) and adding an **AudioHook action to a call flow** (decides which calls use the translation).

### Part 1 — Create the AudioHook Integration

This registers this application as an audio processor in your Genesys Cloud org.

1. Log into **Genesys Cloud** and go to **Admin**
2. Under **Integrations**, click **Integrations**
3. Click **+ Add Integration** (top right)
4. Search for **AudioHook** and select it, then click **Install**
5. Give the integration a name, for example: `Live Call Translation`
6. Open the **Configuration** tab and set:
   - **Connection URI**: `wss://<your-app-hostname>/ws/genesys`
   - **Credentials**: leave blank — no authentication is required
7. Open the **Advanced** tab and set the **Channel** to `external` (this streams the external/caller audio, not the agent audio)
8. Toggle the integration to **Active** and save

> After saving, Genesys will attempt a test connection handshake to the WebSocket URL. Check that the application is running and the URL is reachable. A successful handshake completes the `open`/`opened` exchange described in the [Protocol Handshake](#2-audiohook-protocol-handshake) section below.

### Part 2 — Add AudioHook to a Call Flow (Architect)

This tells Genesys which calls should be sent to the translation service.

1. Go to **Admin > Architect**
2. Open the **Inbound Call Flow** that handles your multilingual calls (or create a new one)
3. In the flow, find the point where the caller should be connected to the translation service — typically before transferring to a queue or agent
4. Add a **Call AudioHook** action (search the toolbox for "AudioHook"):
   - **Integration**: select the `Live Call Translation` integration you created above
   - **Input variables** (optional): set a variable named `language` to the caller's language code if you know it in advance (e.g., `es-MX` for Mexican Spanish). If not set, the agent selects the language from the UI when they connect. See [Supported Languages](#supported-languages) for valid values.
5. After the AudioHook action, connect the flow to your agent queue as normal — the AudioHook runs alongside the call, it does not replace normal call routing
6. Save and publish the flow

> The AudioHook action runs **concurrently** with the rest of the flow. The call is still routed to an agent queue — the AudioHook simply streams the audio to this application at the same time. The agent handles the call in Genesys normally; they also open the **Agent UI** (`/agent/`) to hear translated audio and see transcriptions.

### Part 3 — Agent Setup

Agents handling translated calls need to:

1. Open the **Agent UI** at `https://<your-app-hostname>/agent/?code=<auth-code>` in their browser
2. When a translated call comes in, the call appears in the Agent UI as a waiting call
3. Select the caller's language and click **Connect**
4. They will hear the caller's words in English (or their configured language) and see a live transcription
5. When they speak, their words are automatically translated and sent to the caller

---

## Supported Languages

The caller's language can be passed from Genesys as the `language` input variable, or selected by the agent in the UI. Use BCP-47 language tags:

| Language | Code |
| -------- | ---- |
| Spanish (Mexico) | `es-MX` |
| Spanish (Spain) | `es-ES` |
| French | `fr-FR` |
| Portuguese (Brazil) | `pt-BR` |
| Arabic | `ar-SA` |
| Mandarin Chinese | `zh-CN` |
| Hindi | `hi-IN` |
| Japanese | `ja-JP` |
| Korean | `ko-KR` |
| Russian | `ru-RU` |
| Italian | `it-IT` |
| German | `de-DE` |

For the full list of supported languages and voice names, see `TranslationConfig.cs` in the application source.

---

## Application Settings Required

The Genesys integration only requires the Azure AI Speech settings. ACS (phone) settings are not needed.

| Setting | Description | Required |
| ------- | ----------- | -------- |
| `AzureAISpeech:ResourceID` | Azure AI Speech resource ID | Yes |
| `AzureAISpeech:Region` | Azure region of the Speech resource (e.g., `eastus`) | Yes |
| `AuthCode` | Auth code for the Agent UI — does **not** affect the `/ws/genesys` endpoint | Optional |

These are set in `appsettings.json` or as Azure App Service application settings.

---

## Testing Without Genesys Cloud

Use the built-in **Genesys Simulator** to test the full translation flow without a Genesys Cloud account. The simulator runs in your browser and sends the same AudioHook protocol messages and audio format as real Genesys.

See [GENESYS_SIMULATOR.md](GENESYS_SIMULATOR.md) for instructions.

---

## How It Works — Technical Detail

This section describes the internal flow for developers and integrators who need to understand what happens after Genesys connects.

### 1. WebSocket Connection

When a call reaches the AudioHook action in the Architect flow, Genesys opens a WebSocket to `/ws/genesys`. The `audiohook-session-id` is passed as an HTTP header; if absent, the application falls back to a `?sessionId=` query parameter.

The endpoint is handled by `GenesysWebSocketHandler`. It creates a `GenesysCallBridge` for the session, which buffers audio between the Genesys WebSocket and the translation pipeline.

### 2. AudioHook Protocol Handshake

Once connected, Genesys sends a JSON `open` message:

```json
{
  "version": "2",
  "type": "open",
  "id": "<session-uuid>",
  "seq": 1,
  "parameters": {
    "conversationId": "<genesys-conversation-id>",
    "participant": { "ani": "+15551234567", "dnis": "+15559876543" },
    "inputVariables": { "language": "es-MX" },
    "media": [{ "type": "audio", "format": "PCMU", "channels": ["external"], "rate": 8000 }]
  }
}
```

The application:

1. Extracts the `conversationId` and optional `language` from the message
2. Creates a call record in the database (status: `Waiting`) — this is what makes the call visible in the Agent UI
3. Responds with `opened`, selecting PCMU 8kHz as the audio format:

```json
{
  "version": "2",
  "type": "opened",
  "parameters": {
    "media": [{ "type": "audio", "format": "PCMU", "channels": ["external"], "rate": 8000 }]
  }
}
```

Audio streaming begins immediately after `opened` is sent.

### 3. Audio Streaming

Genesys sends binary WebSocket frames containing µ-law 8kHz mono audio. For each frame, the application:

1. Decodes µ-law bytes → PCM 16kHz 16-bit mono (with linear interpolation upsampling)
2. Queues the PCM audio for the translation pipeline

When the translation pipeline produces output (agent's translated speech), the application:

1. Downsamples PCM 16kHz → 8kHz and encodes as µ-law
2. Sends binary WebSocket frames back to Genesys
3. Genesys plays the audio to the caller over the phone

### 4. Translation Pipeline

When an agent connects from the Agent UI and selects languages, two translation pipelines start:

#### Caller → Agent

```text
Genesys audio (µ-law 8kHz)
  → decode to PCM 16kHz
  → Azure AI Speech: Speech-to-Text (caller's language)
  → Azure AI Speech: Translate to agent's language
  → Azure AI Speech: Text-to-Speech (agent's language voice)
  → DynamicMixer → Agent browser (translated audio + transcription)
```

#### Agent → Caller

```text
Agent browser audio (PCM 16kHz)
  → Azure AI Speech: Speech-to-Text (agent's language)
  → Azure AI Speech: Translate to caller's language
  → Azure AI Speech: Text-to-Speech (caller's language voice)
  → encode to µ-law 8kHz
  → Genesys WebSocket → Caller's phone
```

### 5. Keepalive

Genesys sends `ping` messages periodically. The application responds with `pong` to keep the connection alive.

### 6. Call Termination

| Event | What happens |
| ----- | ------------ |
| Genesys sends `close` | Application responds `closed`, ends the session |
| Genesys sends `disconnect` | Application ends the session |
| WebSocket drops | Application cleans up the session |
| Agent disconnects from UI | Application signals disconnect, Genesys WebSocket closes |

On termination: the audio channel is completed, all translators and the mixer are disposed, the call record is set to `Ended`, and the bridge is removed from memory.

---

## Full Sequence Diagram

```text
Genesys Cloud          Application (/ws/genesys)      Agent Browser (/agent/)
     │                        │                               │
     │── WebSocket open ─────►│                               │
     │── "open" (JSON) ──────►│ Creates call record           │
     │◄── "opened" (JSON) ───│ Call appears in Agent UI ────►│
     │                        │                               │
     │══ Caller audio ════════════════════════════════════════│
     │── Binary (µ-law) ─────►│                               │
     │── Binary (µ-law) ─────►│ (buffered, waiting for agent) │
     │                        │                               │
     │── ping ───────────────►│                               │
     │◄── pong ───────────────│                               │
     │                        │                               │
     │                        │◄── connect (callId, language)─│
     │                        │    Agent selects languages     │
     │                        │                               │
     │                        │    Translation starts          │
     │                        │                               │
     │── Binary (µ-law) ─────►│── STT → Translate → TTS ─────►│ (translated audio)
     │                        │                        ──────►│ (transcription text)
     │                        │                               │
     │◄── Binary (µ-law) ────│◄── STT → Translate → TTS ────│ (agent speaks)
     │  Caller hears agent    │                               │
     │  in their language     │                               │
     │                        │                               │
     │── "close" ────────────►│                               │
     │◄── "closed" ──────────│── call ended ────────────────►│
     │                        │                               │
```

---

## Differences from ACS Mode

| Aspect | ACS Mode | Genesys Mode |
| ------ | -------- | ------------ |
| **Call source** | PSTN via Azure Communication Services | PSTN via Genesys Cloud |
| **Audio delivery** | ACS Media Streaming WebSocket (PCM 16kHz) | AudioHook v2 WebSocket (µ-law 8kHz) |
| **Audio conversion** | None (native PCM 16kHz) | µ-law 8kHz ↔ PCM 16kHz |
| **Call control** | ACS SDK + EventGrid | Genesys AudioHook protocol |
| **Required Azure services** | ACS + EventGrid + AI Speech | AI Speech only |
| **Who initiates the call** | Caller dials ACS phone number | Caller dials Genesys queue number |

---

## Troubleshooting

| Problem | Likely Cause | Fix |
| ------- | ------------ | --- |
| Genesys shows AudioHook connection failed | Application is not reachable or not running | Verify the WSS URL is correct and the app is running; check that the URL uses `wss://` not `ws://` |
| Call does not appear in Agent UI | `open` handshake not completed | Check application logs for errors on `/ws/genesys`; verify the Genesys integration is set to Active |
| Agent UI shows waiting call but no audio after connecting | Bridge lookup failed — conversationId mismatch | Check that the Architect flow's AudioHook action fires before the call is answered by the agent |
| Caller cannot hear translated agent speech | Audio send back to Genesys failing | Check application logs; verify Genesys WebSocket is still open when agent speaks |
| Language not being translated correctly | Wrong language code passed in `inputVariables` | Verify the `language` variable value matches a supported BCP-47 code; or leave it unset and have the agent select it manually |
| Call drops after ~30 seconds of silence | Genesys ping timeout | Ensure the application is responding to `ping` with `pong`; check for application restarts or errors |
