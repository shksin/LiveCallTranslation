# Direct Browser Mode — End-to-End Flow

## Overview

Direct Browser mode provides a **browser-to-browser** WebSocket connection for real-time voice translation without any telephony infrastructure. Both the caller (user) and the agent connect through their web browsers, with audio captured via the browser's microphone and played back through the speaker.

This mode requires **no ACS, EventGrid, or Genesys configuration** — only an Azure AI Speech resource is needed.

---

## Architecture

```
┌──────────┐   WebSocket    ┌──────────────────────────────────┐   WebSocket    ┌──────────┐
│   User   │◄──────────────►│          CallManager             │◄──────────────►│  Agent   │
│(Browser) │  /api/user/ws  │                                  │ /api/agent/ws  │(Browser) │
│          │                │  ┌────────────┐  ┌────────────┐  │                │          │
│ Mic ──►  │   PCM audio    │  │ User→Agent │  │ Agent→User │  │   PCM audio    │  ◄── Mic │
│  ◄── Spk │   + JSON msgs  │  │ Translator │  │ Translator │  │   + JSON msgs  │  Spk ──► │
└──────────┘                │  └─────┬──────┘  └──────┬─────┘  │                └──────────┘
                            │        │                │        │
                            │        ▼                ▼        │
                            │  ┌──────────────────────────┐    │
                            │  │     DynamicMixer         │    │
                            │  │  (mixes original + TTS)  │    │
                            │  └──────────────────────────┘    │
                            └──────────────────────────────────┘
```

---

## End-to-End Flow — Step by Step

### 1. User Opens the User Page

Navigate to `/user/` and select **Default Browser** mode. The browser requests microphone access and sets up a PCM audio worklet (16kHz, 16-bit, mono).

### 2. User WebSocket Connection

The user page connects to:

```
ws://host/api/user/ws
```

`CallManager.ConnectUserAsync()` assigns a new `callId` (GUID), registers the user's WebSocket in the call dictionary, and broadcasts a call state update to all connected agents.

### 3. Agent Sees the Waiting Call

On the agent page (`/agent/`), a WebSocket connection to `/api/agent/ws` receives call state updates. The new call appears with status `UserConnected`. The agent selects the caller's language and clicks **Connect**.

### 4. Agent Sends Connect Message

The agent UI sends a JSON message:

```json
{
  "type": "connect",
  "callId": "<guid>",
  "options": {
    "userLanguage": "zh-CN",
    "agentLanguage": "en-US",
    "agentAudioOptions": {
      "userOriginal": false,
      "userTranslated": true,
      "agentOriginal": false,
      "agentTranslated": false
    }
  }
}
```

### 5. Translation Pipeline Starts

`CallManager.RunCallAsync()` creates two bidirectional translators using the Azure AI Speech SDK:

| Direction | Pipeline | Output |
|-----------|----------|--------|
| **User → Agent** | User audio → STT (caller language) → Translate → TTS (agent voice) | Translated audio + transcription sent to agent |
| **Agent → User** | Agent audio → STT (agent language) → Translate → TTS (caller voice) | Translated audio sent to user browser |

Both sides receive an `enable` message to start streaming audio.

### 6. Audio Flow

- **User speaks**: PCM audio is captured by the browser's AudioWorklet, base64-encoded, sent via WebSocket → `CallManager` feeds it to the User→Agent translator → translated audio is mixed by `DynamicMixer` and sent to the agent.
- **Agent speaks**: Same flow in reverse → translated audio is sent directly to the user's browser for playback.

The `DynamicMixer` combines original and translated audio streams based on the agent's audio options (e.g., hear only translated, or both original and translated).

### 7. Transcriptions

Both directions emit real-time transcriptions (partial and final) to the agent UI:

```json
{
  "type": "transcription",
  "source": "user",
  "originalText": "你好",
  "translatedText": "Hello",
  "isFinal": true
}
```

### 8. Call Ends

When either side disconnects (closes browser tab, clicks disconnect, or loses connection):
- Both receive loops detect the closure
- The `CancellationTokenSource` is cancelled, stopping both translators
- A `disconnect` message is sent to both sides
- The user page auto-refreshes; the agent returns to the call list

---

## WebSocket Messages

### User → Server

| Message | Description |
|---------|-------------|
| `{ "type": "audio", "data": "<base64>" }` | PCM 16kHz 16-bit mono audio frame |

### Server → User

| Message | Description |
|---------|-------------|
| `{ "type": "ping" }` | Keepalive (every 15s) |
| `{ "type": "enable" }` | Agent connected — start sending audio |
| `{ "type": "audio", "data": "<base64>" }` | Translated audio from agent |
| `{ "type": "disconnect" }` | Call ended — triggers page refresh |
| `{ "type": "refresh" }` | Force page refresh |

### Agent → Server

| Message | Description |
|---------|-------------|
| `{ "type": "connect", "callId": "...", "options": { ... } }` | Connect to a waiting call with language options |
| `{ "type": "audio", "data": "<base64>" }` | PCM 16kHz 16-bit mono audio frame |
| `{ "type": "audioOptions", ... }` | Update audio mixing preferences mid-call |

### Server → Agent

| Message | Description |
|---------|-------------|
| `{ "type": "ping" }` | Keepalive (every 15s) |
| `{ "type": "languages", "languages": { ... } }` | Available language list (sent on connect) |
| `{ "type": "update", "updates": [ ... ] }` | Call state updates (new calls, status changes) |
| `{ "type": "enable" }` | Call established — start sending audio |
| `{ "type": "transcription", "source": "user\|agent", ... }` | Real-time transcription |
| `{ "type": "disconnect" }` | Call ended |

---

## Key Components

| Component | File | Purpose |
|-----------|------|---------|
| `CallManager` | `CallManager.cs` | Orchestrates call lifecycle, creates translators, routes audio |
| `WebSocketManager` | `WebSocketManager.cs` | Handles WebSocket upgrade, keepalive pings, graceful close |
| `DynamicMixer` | `DynamicMixer.cs` | Mixes multiple audio streams (original + translated) for agent playback |
| `AISpeechTranslator` | `Translation/AISpeechTranslator.cs` | 3-stage pipeline: STT → Translate → TTS using Azure AI Speech SDK |
| User UI | `wwwroot/user/index.html` | Browser-based caller interface with AudioWorklet |
| Agent UI | `wwwroot/agent/index.html` | Browser-based agent interface with call management |

---

## API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/user/ws` | WebSocket | User (caller) audio connection |
| `/api/agent/ws` | WebSocket | Agent audio + call control connection |
| `/api/calls/waiting` | GET | Lists waiting calls (used by agent UI) |
| `/api/calls/languages` | GET | Returns available translation languages |

---

## Audio Format

| Property | Value |
|----------|-------|
| Sample Rate | 16,000 Hz |
| Bit Depth | 16-bit |
| Channels | Mono |
| Encoding | PCM (signed 16-bit little-endian) |
| Transport | Base64-encoded in JSON WebSocket messages |

---

## Configuration

Direct Browser mode requires only the **core settings** — no ACS, EventGrid, or Genesys configuration needed:

| Setting | Description |
|---------|-------------|
| `AzureAISpeech:ResourceID` | Azure AI Speech resource ID |
| `AzureAISpeech:Region` | Region of the Speech resource |

---

## How to Use

1. Start the application: `dotnet run` from `src/ACSTranslate.API`
2. Open the **Agent** page: `http://localhost:5058/agent/`
3. Open the **User** page: `http://localhost:5058/user/`
4. On the User page, select **Default Browser** mode and click **Connect**
5. The agent page shows the waiting call — select the caller's language and click **Connect**
6. Both sides can now speak and hear real-time translated audio
