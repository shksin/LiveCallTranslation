# Direct Browser Mode

## Overview

Direct Browser mode connects a caller and an agent through their web browsers using WebSocket — no phone number, no Genesys subscription, and no telephony infrastructure required. Both sides use their computer microphone and speakers.

This is the simplest way to use the translation system. It is ideal for:

- **Local development and testing** — run the whole system on a single machine with two browser tabs
- **Remote scenarios without a phone** — a non-English speaker and an English agent connect from anywhere over the internet, using just their browser
- **Demos** — quickly demonstrate the translation capability without any telephony setup

The only Azure resource needed is an **Azure AI Speech** resource.

---

## When to Use This Mode

| Scenario | Recommended Mode |
| -------- | ---------------- |
| Local testing or development | **Direct Browser** ← this doc |
| Real phone callers via Azure | [ACS Integration](ACS_INTEGRATION.md) |
| Callers through Genesys Cloud contact center | [Genesys Integration](GENESYS_INTEGRATION.md) |
| Testing Genesys integration without Genesys Cloud | [Genesys Simulator](GENESYS_SIMULATOR.md) |

---

## Prerequisites

- A modern browser with microphone support (Chrome or Edge recommended)
- The application running locally or deployed to Azure
- An **Azure AI Speech** resource configured in `appsettings.json`
- Microphone access — both the user and agent browser will request microphone permission

---

## Configuration

Direct Browser mode only requires the Azure AI Speech settings. No telephony configuration is needed.

| Setting | Description | Example |
| ------- | ----------- | ------- |
| `AzureAISpeech:ResourceID` | Azure AI Speech resource ID (Portal → Resource → Properties → Resource ID) | `/subscriptions/.../Microsoft.CognitiveServices/accounts/my-speech` |
| `AzureAISpeech:Region` | Azure region of the Speech resource | `eastus`, `australiaeast` |
| `AuthCode` | Auth code for the Agent UI (optional) | `my-secret-code` |

---

## How to Use

### 1. Start the Application

```sh
cd src/ACSTranslate.API
dotnet run
```

### 2. Open the Agent Page

Open a browser tab and navigate to:

```text
http://localhost:5058/agent/?code=<your-auth-code>
```

If no auth code is configured, omit the `?code=` parameter. The agent page connects automatically and waits for incoming calls.

### 3. Open the User Page

Open a **second** browser tab (or a different browser/device) and navigate to:

```text
http://localhost:5058/user/
```

Select **🌐 Default (Browser)** mode and click **Connect**. Grant microphone access when prompted.

The status shows:

```text
Connected. Waiting for agent...
```

### 4. Agent Connects to the Call

Back on the Agent page, the waiting call appears in the call list. The agent:

1. Selects the **caller's language** (the language the user will speak) from the dropdown
2. Configures **audio mixing options** (see [Audio Mixing Options](#audio-mixing-options) below)
3. Clicks **Connect**

### 5. Speak and Listen

Once the agent connects, both sides are live:

- **User speaks** → their speech is translated → agent hears it in their language + sees live transcription
- **Agent speaks** → their speech is translated → user hears it in their language

### 6. Ending the Call

Either side can end the call by clicking **Disconnect** or closing the browser tab. The user page auto-refreshes and becomes available again. The agent returns to the call list.

---

## Audio Mixing Options

When the agent connects, they can configure what audio they hear through their headset. These options can also be changed mid-call.

| Option | What it controls |
| ------ | ---------------- |
| **User Original Audio** | Hear the caller's raw voice in their native language |
| **User Translated Audio** | Hear the translated version of what the caller said (in the agent's language) |
| **Agent Original Audio** | Hear your own voice played back |
| **Agent Translated Audio** | Hear the translated version of what you said (in the caller's language) — useful to verify the translation quality |

**Typical setup**: Enable **User Translated Audio** only — the agent hears the caller in English and the caller hears the agent in their language. Enabling original audio alongside translated can be useful when verifying translation accuracy.

---

## How It Works — Technical Detail

### Connection Flow

```text
User Browser (/user/)              Server                 Agent Browser (/agent/)
       │                              │                            │
       │── WebSocket connect ────────►│                            │
       │   /api/user/ws               │── call state update ──────►│
       │                              │   (new call appears)       │
       │   status: Waiting            │                            │
       │                              │◄── connect (callId, lang) ─│
       │                              │    Agent selects language  │
       │                              │                            │
       │◄── enable ──────────────────│──── enable ───────────────►│
       │   (start sending audio)      │   (start sending audio)    │
       │                              │                            │
       │══ Bidirectional audio + translation ════════════════════│
       │                              │                            │
       │── audio (base64 PCM) ───────►│── STT→Translate→TTS ──────►│ translated audio
       │                              │                    ───────►│ transcription
       │◄── audio (base64 PCM) ───────│◄── STT→Translate→TTS ──────│ agent speaks
       │   (translated agent voice)   │                            │
       │                              │                            │
       │── disconnect / tab close ───►│                            │
       │◄── disconnect ───────────────│──── disconnect ───────────►│
       │   (page refreshes)           │                            │
```

### Audio Pipeline

Both sides use the browser's `AudioWorklet` to capture microphone input:

```text
Browser Microphone (Float32 @ browser native rate — typically 48kHz)
    │
    ▼
AudioWorkletProcessor — resample to 16kHz, convert to Int16
    │
    ▼
Base64 encode
    │
    ▼
WebSocket JSON message: { "type": "audio", "data": "<base64>" }
    │
    ▼
Server — decode base64 → raw PCM bytes
    │
    ▼
Azure AI Speech: Speech-to-Text → Translate → Text-to-Speech
    │
    ▼
Base64 encode → WebSocket JSON → other browser → AudioWorklet → speakers
```

### Audio Format

Audio is always uncompressed PCM — no codec conversion is needed (unlike Genesys which uses µ-law 8kHz).

| Property | Value |
| -------- | ----- |
| Sample rate | 16,000 Hz |
| Bit depth | 16-bit signed |
| Channels | Mono |
| Encoding | PCM little-endian |
| Transport | Base64-encoded in JSON WebSocket messages |

---

## WebSocket Message Reference

### User → Server

| Message | When sent | Description |
| ------- | --------- | ----------- |
| `{ "type": "audio", "data": "<base64>" }` | Continuously while speaking | PCM 16kHz audio frame |

### Server → User

| Message | When sent | Description |
| ------- | --------- | ----------- |
| `{ "type": "ping" }` | Every 15 seconds | Keepalive — no action needed |
| `{ "type": "enable" }` | Agent connects | Start sending audio |
| `{ "type": "audio", "data": "<base64>" }` | Agent speaks | Translated agent audio — play through speakers |
| `{ "type": "disconnect" }` | Call ends | Page refreshes automatically |
| `{ "type": "refresh" }` | Server restart or error | Force page refresh |

### Agent → Server

| Message | When sent | Description |
| ------- | --------- | ----------- |
| `{ "type": "connect", "callId": "...", "options": { ... } }` | Agent clicks Connect | Start the call with language + audio options |
| `{ "type": "audio", "data": "<base64>" }` | Continuously while speaking | PCM 16kHz audio frame |
| `{ "type": "audioOptions", ... }` | Agent changes audio settings | Update audio mixing preferences mid-call |

### Server → Agent

| Message | When sent | Description |
| ------- | --------- | ----------- |
| `{ "type": "ping" }` | Every 15 seconds | Keepalive — no action needed |
| `{ "type": "languages", "languages": { ... } }` | On WebSocket connect | Full list of supported languages and voice names |
| `{ "type": "update", "updates": [ ... ] }` | Call state changes | New calls, status changes — updates the call list |
| `{ "type": "enable" }` | Translation pipeline ready | Start sending audio |
| `{ "type": "transcription", "source": "user\|agent", ... }` | Speech recognised | Real-time transcription (partial and final) |
| `{ "type": "disconnect" }` | Call ends | Return to call list |

---

## Troubleshooting

| Problem | Likely Cause | Fix |
| ------- | ------------ | --- |
| "Audio setup failed" on user page | Microphone permission denied | Click Allow when prompted; check browser site permissions and reload |
| Call does not appear in Agent UI | User WebSocket did not connect | Check the browser console on the user page for WebSocket errors |
| No translated audio heard on either side | Agent has not connected yet | On the Agent page, find the waiting call and click Connect |
| Audio is very choppy or garbled | Network latency or Azure Speech region mismatch | Ensure `AzureAISpeech:Region` matches the actual region of your Speech resource; check network quality |
| Agent page shows no waiting calls after user connects | Agent page was opened before the user connected | Refresh the Agent page — it polls for waiting calls on load |
| Both sides disconnect immediately after connecting | Translation pipeline failed to start | Check app logs for Azure AI Speech authentication errors; verify managed identity RBAC is assigned |
| Echo heard through speakers | Browser not suppressing microphone feedback | Use headphones, or enable echo cancellation in your OS audio settings |

---

## Key Components (Developer Reference)

| Component | File | Purpose |
| --------- | ---- | ------- |
| `CallManager` | `CallManager.cs` | Orchestrates call lifecycle — pairs user and agent, creates translators, routes audio |
| `WebSocketManager` | `WebSocketManager.cs` | Handles WebSocket upgrade, 15s keepalive pings, and graceful close |
| `DynamicMixer` | `DynamicMixer.cs` | Mixes original and translated audio streams for the agent's headset |
| `AISpeechTranslator` | `Translation/AISpeechTranslator.cs` | 3-stage pipeline: Speech-to-Text → Translate → Text-to-Speech using Azure AI Speech SDK |
| User UI | `wwwroot/user/index.html` | Caller interface — AudioWorklet capture, WebSocket connection, mode selector |
| Agent UI | `wwwroot/agent/index.html` | Agent interface — call list, language selection, audio controls, live transcription |

---

## API Endpoints Reference

| Endpoint | Method | Auth | Description |
| -------- | ------ | ---- | ----------- |
| `/api/user/ws` | WebSocket | None | User (caller) audio and control connection |
| `/api/agent/ws` | WebSocket | AuthCode | Agent audio, call control, and transcription |
| `/api/calls/waiting` | GET | AuthCode | Lists waiting calls for the Agent UI |
| `/api/calls/languages` | GET | AuthCode | Returns available translation language list |
