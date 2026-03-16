# Genesys AudioHook v2 Simulator

## Overview

The Genesys Simulator is a **browser-based tool** that emulates a Genesys Cloud AudioHook v2 caller connection. It allows you to test the end-to-end translation flow without needing a Genesys Cloud environment or a real phone call.

The simulator is built into the **User Interface** page at `/user/` and implements the full AudioHook v2 client protocol — including the `open`/`opened` handshake, µ-law audio encoding/decoding, binary audio streaming, and `ping` keepalives.

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

### 3. Select "Genesys Simulator" Mode

The user page presents three connection modes:

| Mode | Description |
|------|-------------|
| 🌐 **Default (Browser)** | Direct WebSocket connection for browser-to-browser testing |
| 📞 **ACS (Phone)** | Displays the ACS phone number to call |
| 🎧 **Genesys Simulator** | Simulates a Genesys AudioHook v2 caller |

Click **🎧 Genesys Simulator**, then click **Connect**.

### 4. Grant Microphone Access

The browser will request microphone permission. The simulator captures audio from your microphone at 16kHz, converts it to µ-law 8kHz, and sends it to the server — exactly as Genesys Cloud would.

### 5. Wait for Agent to Connect

Once connected, the status shows:
```
🎧 Genesys Simulator — speaking (waiting for agent to connect from Agent page)...
```

The simulated call appears in the **Agent UI** (`/agent/`) as a waiting call with a `genesys:sim-XXXXXXXX` caller ID. The agent selects the caller's language and clicks Connect to start translation.

### 6. Speak and Listen

- **Your microphone audio** → µ-law 8kHz → server → translated → agent hears it
- **Agent speaks** → translated → µ-law 8kHz → your browser speakers

Both sides see live transcription and hear translated audio in real time.

---

## How It Works

### Connection Flow

```
Browser (Simulator)                    Server (/ws/genesys)
       │                                      │
       │── WebSocket Connect ────────────────►│
       │   ws(s)://host/ws/genesys            │
       │   ?sessionId=<uuid>                  │
       │                                      │
       │── open (JSON) ──────────────────────►│
       │   {                                  │
       │     version: "2",                    │── CreateGenesysCallAsync()
       │     type: "open",                    │   (call visible in Agent UI)
       │     id: sessionId,                   │
       │     parameters: {                    │
       │       conversationId: "sim-xxxx",    │
       │       media: [PCMU 8kHz]             │
       │     }                                │
       │   }                                  │
       │                                      │
       │◄── opened (JSON) ───────────────────│
       │   {media: [PCMU 8kHz external]}      │
       │                                      │
       │   audioEnabled = true                │
       │                                      │
       │══ Bidirectional audio ══════════════►│
       │── Binary (µ-law 8kHz) ─────────────►│ → PCM 16kHz → translator
       │◄── Binary (µ-law 8kHz) ─────────────│ ← PCM 16kHz ← translator
       │                                      │
       │── ping (every 15s) ────────────────►│
       │◄── pong ────────────────────────────│
       │                                      │
       │◄── closed / disconnect ─────────────│
       │   socket.close()                     │
```

### Audio Pipeline (Simulator → Server)

```
Browser Microphone (Float32 @ 48kHz)
        │
        ▼
AudioWorkletProcessor (resample to 16kHz, convert to Int16)
        │
        ▼
pcm16kToMuLaw8k() — downsample 2x + µ-law encode
        │
        ▼
WebSocket binary frame (µ-law 8kHz bytes)
        │
        ▼
Server: MuLawConverter.MuLaw8kToPcm16k() — decode + upsample 2x
        │
        ▼
Azure AI Speech Translator (STT → Translate → TTS)
```

### Audio Pipeline (Server → Simulator)

```
Azure AI Speech TTS output (PCM 16kHz)
        │
        ▼
Server: MuLawConverter.Pcm16kToMuLaw8k() — downsample + encode
        │
        ▼
WebSocket binary frame (µ-law 8kHz bytes)
        │
        ▼
Browser: muLaw8kToPcm16k() — decode + upsample 2x → Int16 PCM
        │
        ▼
AudioWorkletProcessor (convert Int16 to Float32 → speakers)
```

---

## µ-Law Codec (Client-Side)

The simulator includes a JavaScript µ-law codec that mirrors the server-side `MuLawConverter`:

| Function | Direction | Description |
|----------|-----------|-------------|
| `pcm16kToMuLaw8k(int16Array)` | Send | Downsample PCM 16kHz by 2x (decimation), encode each sample to µ-law |
| `muLaw8kToPcm16k(muLawData)` | Receive | Decode µ-law to PCM, upsample 2x with linear interpolation |
| `encodeMuLaw(sample)` | Internal | Encode a single PCM sample to µ-law byte |
| `muLawDecompressTable[256]` | Internal | Precomputed lookup table for µ-law → PCM decoding |

---

## Simulated AudioHook Messages

The simulator generates the same JSON messages that Genesys Cloud sends:

### `open` (sent on connect)
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

### `ping` (every 15 seconds)
```json
{
  "version": "2",
  "id": "<session-uuid>",
  "type": "ping",
  "seq": 2,
  "serverseq": 1,
  "parameters": {}
}
```

---

## Simulator vs. Real Genesys Connection

| Aspect | Simulator | Real Genesys |
|--------|-----------|--------------|
| **Audio source** | Browser microphone | PSTN caller via Genesys Cloud |
| **WebSocket origin** | Same-origin browser WS | External Genesys server |
| **Session ID** | Client-generated UUID (query param) | Genesys-provided (`audiohook-session-id` header) |
| **Conversation ID** | `sim-XXXXXXXX` (random) | Real Genesys conversation ID |
| **Participant info** | Hardcoded dummy values | Real caller ANI/DNIS |
| **Audio format** | Identical — µ-law 8kHz mono | µ-law 8kHz mono |
| **Protocol messages** | Identical — open/opened/ping/pong/close | Full AudioHook v2 protocol |
| **Authentication** | None (same-origin, `/ws/genesys` is unauthenticated) | Same — no auth on `/ws/genesys` |

---

## Troubleshooting

| Problem | Cause | Fix |
|---------|-------|-----|
| "Audio setup failed" | Microphone permission denied | Allow microphone access in browser, reload |
| Call doesn't appear in Agent UI | `open` handshake failed | Check browser console for WebSocket errors |
| No translated audio heard | Agent hasn't connected yet | Open `/agent/` and click Connect on the waiting call |
| Garbled audio | Sample rate mismatch | Ensure browser supports 16kHz recording (most modern browsers do) |
| Connection drops silently | Ping interval too long | Check for network issues; the simulator pings every 15s |
