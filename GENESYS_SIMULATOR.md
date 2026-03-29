# Genesys AudioHook v2 Simulator

## Overview

The Genesys Simulator is a **browser-based testing tool** that acts as a stand-in for a real Genesys Cloud caller. It lets you test the full end-to-end translation flow — microphone input, audio conversion, translation, and playback — without needing a Genesys Cloud subscription or a real phone call.

You open it in a browser tab, speak into your microphone, and the system treats it exactly like an incoming Genesys call. An agent opens the Agent UI in another tab, connects to the waiting call, selects languages, and translation begins in both directions.

> **How faithful is the simulation?** The simulator sends the same WebSocket messages and the same audio format (µ-law 8kHz) as real Genesys Cloud. The server processes both through identical code. The only differences are metadata: a simulated call uses a browser-generated ID and dummy phone numbers instead of real Genesys conversation data.

---

## Prerequisites

- The application must be running (`dotnet run` from `src/ACSTranslate.API`)
- A modern browser with microphone access (Chrome or Edge recommended)
- The auth code configured in your `appsettings.json` (check the `AuthCode` setting — if not set, omit the `?code=` parameter)

---

## How to Use

### 1. Start the Application

```sh
cd src/ACSTranslate.API
dotnet run
```

### 2. Open the User Page

Navigate to:
```
http://localhost:5058/user/?code=<your-auth-code>
```

Replace `<your-auth-code>` with the value from your `appsettings.json`. If no auth code is configured, just go to `http://localhost:5058/user/`.

### 3. Select "Genesys Simulator" Mode

The page shows four connection modes. Select **🎧 Genesys Simulator**:

| Mode | When to use |
|------|-------------|
| 🎧 **Genesys Simulator** | Test the Genesys integration using your browser microphone — no Genesys Cloud needed |
| 📡 **Genesys** | Shows the WebSocket URL to paste into your Genesys Cloud AudioHook configuration |
| 📞 **ACS (Phone)** | Shows the Azure Communication Services phone number to dial |
| 🌐 **Default (Browser)** | Direct browser-to-browser testing with no telephony involved |

Click **Connect**.

### 4. Grant Microphone Access

The browser will ask for microphone permission — click **Allow**. The simulator captures your microphone, converts the audio to the format Genesys uses (µ-law 8kHz), and streams it to the server in real time.

### 5. Wait for an Agent to Connect

After connecting, the page shows:
```
🎧 Genesys Simulator — speaking (waiting for agent to connect from Agent page)...
```

The simulated call now appears in the **Agent UI** at `/agent/` as a waiting call. Its caller ID will look like `genesys:sim-XXXXXXXX`.

**The agent must now:**

1. Open `/agent/` in a separate browser tab
2. Find the waiting call in the list
3. Select the caller's language (the language you will speak) and the agent's language
4. Click **Connect**

Translation starts as soon as the agent connects.

### 6. Speak and Listen

Once the agent connects:

- **You (simulator)** speak into your microphone → your speech is translated → the agent hears it in their language
- **The agent** speaks → their speech is translated → you hear it through your speakers

Both sides also see live transcription in real time.

### 7. Ending the Call

Either side can end the call:

- **Agent**: clicks Disconnect or closes the browser tab
- **Simulator**: closes the browser tab or refreshes the page

When the call ends, the server sends a `closed` message and the WebSocket connection is torn down. The call record is marked as **Ended** in the system.

---

## How It Works

### Connection Flow

The simulator implements the Genesys AudioHook v2 protocol. Here is the full sequence from browser connect to active audio:

```
Browser (/user/)                       Server (/ws/genesys)
        │                                      │
        │── 1. WebSocket connect ─────────────►│
        │      ws://host/ws/genesys            │
        │      ?sessionId=<uuid>               │
        │                                      │
        │── 2. "open" message (JSON) ─────────►│ Creates call record
        │      {                               │ Call appears in Agent UI
        │        type: "open",                 │
        │        conversationId: "sim-xxxx",   │
        │        media: [PCMU 8kHz]            │
        │      }                               │
        │                                      │
        │◄── 3. "opened" message (JSON) ───────│ Handshake complete
        │       {media: [PCMU 8kHz external]}  │ Browser starts sending audio
        │                                      │
        │── 4. Binary audio frames ───────────►│ µ-law 8kHz → PCM 16kHz
        │      (µ-law 8kHz, continuous)        │ → Azure AI Speech (STT→Translate→TTS)
        │                                      │
        │◄── 4. Binary audio frames ───────────│ PCM 16kHz → µ-law 8kHz
        │       (µ-law 8kHz, translated)       │ → played on your speakers
        │                                      │
        │── 5. "ping" (every 15s) ────────────►│ Keepalive
        │◄── 5. "pong" ────────────────────────│
        │                                      │
        │◄── 6. "closed" ──────────────────────│ Call ended
        │       socket.close()                 │
```

### Audio Pipeline: Microphone → Agent

Your browser microphone produces audio at 48kHz. Genesys requires µ-law 8kHz. The browser handles the full conversion before sending:

```
Microphone input (Float32 @ 48kHz — browser native)
        │
        ▼
AudioWorklet — resample to 16kHz, convert to 16-bit integers (Int16)
        │
        ▼
pcm16kToMuLaw8k() — drop every other sample (8kHz), encode to µ-law
        │
        ▼
WebSocket binary frame sent to server
        │
        ▼
Server decodes µ-law → PCM 16kHz (with interpolation to smooth the upsampled audio)
        │
        ▼
Azure AI Speech — Speech-to-Text → Translate → Text-to-Speech
        │
        ▼
Agent hears translated audio
```

### Audio Pipeline: Agent → Your Speakers

Translated speech from the agent comes back the same way in reverse:

```
Azure AI Speech TTS output (PCM 16kHz)
        │
        ▼
Server — downsample to 8kHz, encode to µ-law
        │
        ▼
WebSocket binary frame sent to browser
        │
        ▼
Browser decodes µ-law → PCM 16kHz (upsample with linear interpolation)
        │
        ▼
AudioWorklet — convert Int16 to Float32, play through speakers
```

### What is µ-law?

µ-law (pronounced "mu-law") is an audio compression format used in telephony, particularly in North America. It stores audio samples more efficiently than raw PCM by applying a logarithmic scale — quieter sounds get more precision, louder sounds get less. Genesys AudioHook uses µ-law at 8kHz as its wire format. The simulator encodes and decodes this entirely in the browser using a JavaScript codec that mirrors the server-side implementation.

---

## AudioHook Protocol Messages

The simulator sends and receives the following JSON messages over the WebSocket.

### `open` — sent by simulator on connect

Announces the session and declares the audio format. The `conversationId` is how the call is identified in the Agent UI.

```json
{
  "version": "2",
  "id": "<session-uuid>",
  "type": "open",
  "seq": 1,
  "serverseq": 0,
  "position": "0:00:00.000",
  "parameters": {
    "organizationId": "sim-org",
    "conversationId": "sim-XXXXXXXX",
    "participant": {
      "id": "sim-participant",
      "ani": "+15551234567",
      "dnis": "+15559876543"
    },
    "media": [
      { "type": "audio", "format": "PCMU", "channels": ["external"], "rate": 8000 }
    ]
  }
}
```

### `opened` — sent by server in response

Confirms the handshake and selects the audio format. Once this is received, the simulator starts streaming audio.

```json
{
  "version": "2",
  "id": "<session-uuid>",
  "type": "opened",
  "seq": 1,
  "clientseq": 1,
  "parameters": {
    "media": [
      { "type": "audio", "format": "PCMU", "channels": ["external"], "rate": 8000 }
    ]
  }
}
```

### `ping` / `pong` — keepalive (every 15 seconds)

Sent by the simulator to keep the connection alive. The server responds with `pong`.

```json
{ "version": "2", "id": "<session-uuid>", "type": "ping", "seq": 2, "serverseq": 1, "parameters": {} }
```

### `close` / `closed` — call teardown

Sent by either side to end the call. The server responds with `closed` before closing the socket.

---

## Simulator vs. Real Genesys Connection

The server processes both through exactly the same code. The differences are only in where the connection comes from and what metadata is attached.

| Aspect | Simulator | Real Genesys Cloud |
|--------|-----------|-------------------- |
| **Who opens the WebSocket** | Your browser | Genesys Cloud servers (external) |
| **Audio source** | Your microphone | PSTN caller routed through Genesys |
| **Session ID** | Random UUID generated in the browser, passed as `?sessionId=` in the URL | Provided by Genesys as an HTTP header (`audiohook-session-id`) |
| **Conversation ID** | `sim-XXXXXXXX` — randomly generated each time | Real Genesys conversation GUID |
| **Caller phone number** | Hardcoded dummy (`+15551234567`) | Actual caller ANI from the phone network |
| **Call ID in Agent UI** | `genesys:sim-XXXXXXXX` | `genesys:<real-conversation-id>` |
| **Audio format on the wire** | µ-law 8kHz mono — identical | µ-law 8kHz mono |
| **Protocol messages** | Identical — open / opened / ping / pong / close | Same AudioHook v2 protocol |
| **Authentication** | None | None (the `/ws/genesys` endpoint is unauthenticated) |

---

## Troubleshooting

| Problem | Likely Cause | Fix |
|---------|------------- | ----|
| "Audio setup failed" | Microphone permission denied | Click Allow when prompted, or check browser site permissions and reload |
| Call doesn't appear in Agent UI | The `open` handshake failed before a call record was created | Open the browser DevTools console and look for WebSocket errors |
| No translated audio heard | The agent has not connected yet | Open `/agent/` in another tab, find the waiting call, select languages, and click Connect |
| Agent UI shows no waiting calls | The simulator disconnected before the agent opened the page | Refresh `/user/`, reconnect in Genesys Simulator mode, then open `/agent/` |
| Audio sounds garbled or robotic | Sample rate mismatch during conversion | Ensure you are using a supported browser (Chrome or Edge); Safari may have issues with AudioWorklet sample rates |
| Connection drops after ~45 seconds | Ping keepalive not reaching the server | Check for network proxies or firewalls that close idle WebSocket connections; the simulator pings every 15s |
| Language not being detected correctly | The agent selected the wrong caller language | The agent must select the language *you* are speaking, not their own language |
