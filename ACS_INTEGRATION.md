# ACS (Azure Communication Services) Integration

## Overview

This application integrates with **Azure Communication Services (ACS)** to provide real-time voice translation for PSTN phone calls. Callers dial a real phone number, are answered automatically, and their speech is translated in real time so an English-speaking agent can communicate with them through the browser-based Agent UI — and vice versa.

Unlike the Genesys integration (which receives audio from an existing contact center), the ACS mode handles the full telephony stack: phone number ownership, call answering, and audio streaming are all managed through Azure.

---

## How ACS Telephony Works

### What is Azure Communication Services?

**Azure Communication Services (ACS)** is a cloud telephony platform. It lets you programmatically own a phone number, answer calls, and stream audio — without needing physical phone infrastructure. In this integration, ACS acts as the telephony layer: it receives the caller's PSTN call and opens a real-time audio stream to this application.

### What is Azure EventGrid?

**Azure EventGrid** is an Azure event routing service. When a call arrives at your ACS phone number, ACS does not directly call your application — instead, it publishes an `IncomingCall` event to an EventGrid System Topic. EventGrid then delivers that event as an HTTP webhook to your application's `/api/events` endpoint.

This decoupling means your application does not need to maintain a persistent connection to ACS. It just exposes a webhook URL and waits to be called.

### What is the EventGrid System Topic?

An EventGrid **System Topic** is a pre-built event source that ACS creates within your Azure subscription when you set it up. You do not create the System Topic manually — it is automatically available once you have an ACS resource. You only need to create a **subscription** on that topic pointing to your app's webhook URL.

> **Good news**: This application auto-creates and maintains the EventGrid subscription on startup. You only need to provide the System Topic's resource ID in your app settings and grant the correct RBAC role.

### How the pieces connect

```text
Phone Caller
    │
    │  dials your ACS phone number
    ▼
Azure Communication Services
    │
    │  fires an IncomingCall event
    ▼
Azure EventGrid System Topic
    │
    │  delivers webhook to your app
    ▼
This Application  (POST /api/events)
    │
    │  answers the call via ACS SDK
    │  ACS opens a media streaming WebSocket back to the app
    ▼
Real-time audio stream  (wss://your-app/ws/acs/{callId})
    │
    │  Azure AI Speech: STT → Translate → TTS
    ▼
Agent Browser  (/agent/)
    │  hears translated caller audio + live transcription
    │  speaks — translated back to caller over the phone
```

Key points:

- **ACS initiates the audio WebSocket** — your app does not connect to ACS. After the call is answered, ACS opens a WebSocket to your app and starts streaming audio.
- **Audio format** — ACS streams audio as **PCM 16kHz 16-bit mono**, the same format Azure AI Speech requires. No audio conversion is needed (unlike Genesys which uses µ-law 8kHz).
- **The EventGrid subscription auto-configures** on startup — the app creates or updates it automatically if RBAC is set correctly.

---

## Azure Setup

You need to complete these steps once before the first call can be received.

### Step 1 — Create an Azure Communication Services Resource

1. In the [Azure Portal](https://portal.azure.com), click **Create a resource**
2. Search for **Communication Services** and select it
3. Fill in the subscription, resource group, and a resource name
4. Click **Review + Create**, then **Create**
5. Once created, open the resource and note the **endpoint URL** (e.g. `https://my-acs.communication.azure.com/`) — this is your `ACS:Endpoint` setting

### Step 2 — Acquire a Phone Number

1. Open your ACS resource in the Azure Portal
2. In the left menu, click **Phone Numbers**
3. Click **+ Get** to acquire a new number
4. Select your country, number type (**Toll-free** or **Local**), and calling features (**Inbound calls** must be enabled)
5. Complete the purchase
6. Note the full phone number (e.g. `+18335276165`) — this is your `ACS:InboundNumber` setting

### Step 3 — Locate the EventGrid System Topic Resource ID

The EventGrid System Topic for your ACS resource is created automatically by Azure. You need its resource ID for the app settings.

1. In the Azure Portal, search for **Event Grid System Topics**
2. Find the topic associated with your ACS resource (it typically has the same name as your ACS resource)
3. Open it and go to **Properties**
4. Copy the **Resource ID** (e.g. `/subscriptions/.../resourceGroups/.../providers/Microsoft.EventGrid/systemTopics/my-topic`) — this is your `EventGrid:TopicResourceID` setting

> If no System Topic appears, go to your ACS resource → **Events** in the left menu. Creating any event subscription from there will cause the System Topic to be generated.

### Step 4 — Assign RBAC Roles to the App's Managed Identity

The application uses a **managed identity** to authenticate with Azure services. You must assign these roles before starting the app.

1. In the Azure Portal, go to your **App Service** (or wherever the app is hosted)
2. Under **Settings → Identity**, enable **System assigned** managed identity and save
3. Assign the following roles:

| Role | Where to assign | Why |
| ---- | --------------- | --- |
| `Cognitive Services Speech User` | Azure AI Speech resource → Access control (IAM) | Authenticate for STT/translation/TTS |
| `Contributor` | ACS resource → Access control (IAM) | Answer calls and manage media streaming |
| `EventGrid EventSubscription Contributor` | EventGrid System Topic → Access control (IAM) | Auto-create the EventGrid webhook subscription |

For each role assignment:

1. Open the target resource in the Azure Portal
2. Click **Access control (IAM)** in the left menu
3. Click **+ Add → Add role assignment**
4. Select the role, click **Next**
5. Under **Assign access to**, select **Managed identity**
6. Click **+ Select members**, find your App Service, and select it
7. Click **Review + assign**

### Step 5 — Configure Application Settings

Set the following in your `appsettings.json` (local) or Azure App Service application settings (production):

| Setting | Description | Example |
| ------- | ----------- | ------- |
| `AzureAISpeech:ResourceID` | Azure AI Speech resource ID (Portal → Resource → Properties → Resource ID) | `/subscriptions/.../Microsoft.CognitiveServices/accounts/my-speech` |
| `AzureAISpeech:Region` | Region of the Speech resource | `eastus`, `australiaeast` |
| `ACS:Endpoint` | ACS resource endpoint URL | `https://my-acs.communication.azure.com/` |
| `ACS:InboundNumber` | Phone number to accept calls on, or `*` to accept all | `+18335276165` |
| `EventGrid:TopicResourceID` | EventGrid system topic resource ID | `/subscriptions/.../systemTopics/my-topic` |
| `Inbound:Hostname` | Your app's public hostname — used for callback and WebSocket URLs | `myapp.azurewebsites.net` |
| `AuthCode` | Auth code for the Agent UI | `my-secret-code` |

For Azure App Service, use `__` as the section separator:

```text
AzureAISpeech__ResourceID=/subscriptions/.../accounts/my-speech
AzureAISpeech__Region=australiaeast
ACS__Endpoint=https://my-acs.communication.azure.com/
ACS__InboundNumber=+18335276165
EventGrid__TopicResourceID=/subscriptions/.../systemTopics/my-topic
AuthCode=my-secret-code
```

> `Inbound:Hostname` does not need to be set on Azure App Service — it is automatically read from the `WEBSITE_HOSTNAME` environment variable. Only set it if you are using a custom domain or running behind a reverse proxy.

### Step 6 — Azure App Service Requirements

When deploying to Azure App Service:

- **WebSockets must be enabled** (App Service → Configuration → General settings → Web sockets: On)
- **Always On must be enabled** — prevents the app from going to sleep and missing incoming call events
- **Platform must be x64**
- **Only a single instance** is supported — scaling out to multiple instances is not supported in this version
- The app must use **.NET 9** runtime

---

## Agent Setup

When a translated call arrives, the agent needs to:

1. Open the **Agent UI** at `https://<your-app-hostname>/agent/?code=<auth-code>` in their browser
2. The incoming call appears in the call list automatically
3. Select the **caller's language** (the language the caller is speaking) from the dropdown
4. Click **Connect**
5. The agent will hear the caller's words translated in real time and see a live transcription
6. When the agent speaks, their words are automatically translated and played to the caller over the phone

---

## Testing Locally

ACS requires your application to be publicly reachable so it can send EventGrid webhooks and open the media streaming WebSocket. For local development:

1. Use a tunnelling tool such as [ngrok](https://ngrok.com) or the [VS Code Dev Tunnels](https://code.visualstudio.com/docs/remote/tunnels) feature to expose your local port
2. Set `Inbound:Hostname` in `appsettings.Development.json` to the tunnel's public hostname (without `https://`)
3. On startup, the app will register the EventGrid subscription pointing to the tunnel URL
4. Dial the ACS phone number — the call will be routed through the tunnel to your local app

---

## How It Works — Technical Detail

This section explains the internal flow for developers and integrators.

### Full Call Flow — Step by Step

#### 1. Caller Dials the ACS Phone Number

A PSTN caller dials the number configured in `ACS:InboundNumber`. ACS receives the call and publishes a `Microsoft.Communication.IncomingCall` event to the EventGrid System Topic.

#### 2. EventGrid Delivers the Webhook

EventGrid delivers the event as an HTTP POST to:

```text
POST /api/events
```

The `EventGridController` parses the CloudEvent and routes it to `InboundCallHandler`.

**Auto-configuration on startup**: `EventGridSubscriptionManager` automatically creates or updates the EventGrid subscription on app startup so it points to the correct `/api/events` URL. This handles:

- First deploy (subscription does not exist yet)
- Hostname changes (subscription URL becomes stale)
- Subscriptions stuck in `AwaitingManualAction` state (deleted and recreated)

This requires the `EventGrid EventSubscription Contributor` RBAC role on the System Topic.

#### 3. InboundCallHandler Processes the Event

The handler:

1. Validates the event is not stale (rejects events older than 3 minutes)
2. Checks the destination number matches `ACS:InboundNumber` (or accepts all if set to `*`)
3. Extracts the caller ID and optional `language` from VoIP custom context headers
4. Calls `CallService.CreateCallAsync()` to answer the call

#### 4. Call Is Answered with Media Streaming

`CallService.CreateCallAsync()`:

1. Creates a call record in the database (status: `New`)
2. Answers the call via the ACS SDK with:
   - **Callback URI**: `https://<hostname>/api/calls/{callId}/callback` — ACS posts call state events here (e.g. disconnect)
   - **Media streaming URI**: `wss://<hostname>/ws/acs/{callId}` — ACS opens a WebSocket here to stream audio
   - **Streaming config**: PCM 16kHz mono, unmixed, bidirectional, start immediately
3. Stores the `CallConnectionId` returned by ACS
4. Sets call status to `Waiting`

> **Important**: Media streaming starts at answer time (`StartMediaStreaming = true`). It must **NOT** be started again — doing so resets the ACS WebSocket and breaks the audio bridge.

#### 5. ACS Opens the Media Streaming WebSocket

ACS connects to:

```text
wss://<hostname>/ws/acs/{callId}
```

`ACSWebSocketHandler` receives this connection, creates an `ACSCallBridge` for the session, and enters a receive loop. ACS sends two message types:

| Message Type | Content |
| ------------ | ------- |
| `AudioMetadata` | Stream configuration — logged, no action needed |
| `AudioData` | Base64-encoded PCM 16kHz 16-bit mono audio from the caller |

Each `AudioData` frame is decoded and pushed into the call bridge's audio channel.

#### 6. Agent Connects and Starts Translation

The agent opens the Agent UI, sees the waiting call, selects languages, and clicks Connect. The UI sends:

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

`CallManager` detects this is an ACS call and starts two translation pipelines:

##### Caller → Agent

```text
Caller audio arrives from ACS (PCM 16kHz)
  → Azure AI Speech: Speech-to-Text (caller's language)
  → Azure AI Speech: Translate to agent's language
  → Azure AI Speech: Text-to-Speech (agent's language voice)
  → DynamicMixer → Agent browser (translated audio + live transcription)
```

##### Agent → Caller

```text
Agent microphone audio (PCM 16kHz from browser)
  → Azure AI Speech: Speech-to-Text (agent's language)
  → Azure AI Speech: Translate to caller's language
  → Azure AI Speech: Text-to-Speech (caller's language voice)
  → ACS media streaming WebSocket → Caller's phone
```

#### 7. ACS Callback Events

During the call, ACS posts state-change events to:

```text
POST /api/calls/{callId}/callback
```

When a `CallDisconnected` event is received, the call is marked `Ended` even if the audio WebSocket is still open.

#### 8. Call Termination

| Trigger | What happens |
| ------- | ------------ |
| Caller hangs up | ACS closes media streaming WebSocket + sends `CallDisconnected` callback |
| ACS WebSocket closes | Bridge signals disconnect, translation pipeline stops |
| Agent disconnects from UI | CallManager cancels the pipeline, ACS WebSocket closes |

On termination, the audio channel is completed, translators and mixer are disposed, call status is set to `Ended`, and the agent UI is notified.

---

## WebSocket Message Format

ACS audio is transported as JSON WebSocket text frames (not binary).

**Receiving caller audio (ACS → App):**

```json
{
  "kind": "AudioData",
  "audioData": {
    "data": "<base64-encoded PCM 16kHz bytes>",
    "timestamp": "...",
    "participantRawID": "...",
    "silent": false
  }
}
```

**Sending translated audio back (App → ACS):**

```json
{
  "kind": "AudioData",
  "audioData": {
    "data": "<base64-encoded PCM 16kHz bytes>"
  }
}
```

---

## Hostname Resolution

The app auto-detects the public hostname used for ACS callback URLs and EventGrid webhook registration:

| Priority | Source | When to use |
| -------- | ------ | ----------- |
| 1 | `WEBSITE_HOSTNAME` environment variable | Set automatically on Azure App Service |
| 2 | `Inbound:Hostname` app setting | Custom domains, reverse proxies, or local tunnels |
| 3 | `localhost:5000` | Fallback — not usable for real ACS calls |

---

## Full Sequence Diagram

```text
Caller (Phone)     ACS          EventGrid        App Server              Agent Browser
     │              │               │                │                         │
     │── Dial ─────►│               │                │                         │
     │              │── IncomingCall event ─────────►│                         │
     │              │               │  POST /api/events                        │
     │              │               │                │── validate + answer     │
     │              │◄── AnswerCallAsync ────────────│   (callback + WS URIs)  │
     │◄─ Connected ─│               │                │                         │
     │  (ringing    │               │                │── call record created   │
     │   stops)     │               │                │   status: Waiting ─────►│ (appears in UI)
     │              │               │                │                         │
     │              │── WS Connect ──────────────────►│                         │
     │              │   wss://.../ws/acs/{callId}    │                         │
     │              │── AudioMetadata ───────────────►│                         │
     │              │── AudioData (PCM) ─────────────►│ buffered in bridge      │
     │              │                                │                         │
     │              │                                │◄── connect (language) ──│
     │              │                                │    agent selects lang   │
     │              │                                │── translation starts    │
     │              │                                │──── enable ────────────►│
     │              │                                │                         │
     │══ Bidirectional translation active ════════════════════════════════════│
     │              │                                │                         │
     │── speaks ───►│── AudioData ───────────────────►│── STT→Translate→TTS ──►│ translated audio
     │              │                                │                 ───────►│ transcription
     │              │                                │◄── PCM audio ───────────│ agent speaks
     │◄── hears ────│◄── AudioData (PCM) ────────────│ STT→Translate→TTS       │
     │  translated  │                                │                         │
     │  agent voice │                                │                         │
     │── hangs up ─►│                                │                         │
     │              │── WS Close ────────────────────►│                         │
     │              │── CallDisconnected callback ───►│── status: Ended ───────►│
```

---

## Troubleshooting

| Problem | Likely Cause | Fix |
| ------- | ------------ | --- |
| Calls are not being received | EventGrid subscription not registered or pointing to wrong URL | Check app startup logs for `EventGridSubscriptionManager` errors; verify `EventGrid:TopicResourceID` is correct and RBAC is assigned |
| EventGrid subscription stuck in `AwaitingManualAction` | Azure blocked the subscription (usually a first-time webhook validation failure) | Restart the app — it will delete and recreate the subscription automatically |
| Call is answered but no audio from caller | ACS media streaming WebSocket did not connect | Check that `Inbound:Hostname` resolves correctly and the `/ws/acs/` path is reachable; WebSockets must be enabled in App Service |
| Agent UI does not show the incoming call | Call record not created — InboundCallHandler rejected the event | Check that `ACS:InboundNumber` matches the number dialled, or set it to `*` to accept all |
| Caller cannot hear translated agent speech | Audio send to ACS WebSocket failing | Check app logs for errors on `ACSCallBridge.SendToCallerAsync`; verify the ACS WebSocket is still open |
| Translation starts but quality is poor | Azure AI Speech region mismatch or resource quota | Ensure `AzureAISpeech:Region` matches the actual region of your Speech resource |
| App throws authentication errors on startup | Managed identity not assigned or RBAC roles missing | Verify all three RBAC role assignments are in place (Speech, ACS, EventGrid) |
| Call drops when agent disconnects but caller is still on the line | Expected behaviour in current version | The call ends when either party disconnects — there is no hold/transfer support |

---

## Key Components (Developer Reference)

| Component | File | Responsibility |
| --------- | ---- | -------------- |
| `EventGridController` | `Controllers/EventGridController.cs` | Receives EventGrid webhook — handles validation handshake and CloudEvent dispatch |
| `InboundCallHandler` | `EventGrid/InboundCallHandler.cs` | Processes `IncomingCall` events — validates number, extracts language, answers call |
| `EventGridSubscriptionManager` | `EventGrid/EventGridSubscriptionManager.cs` | Auto-creates/updates the EventGrid subscription on startup |
| `ACSService` | `Service/ACSService.cs` | ACS SDK wrapper — answers calls, configures media streaming |
| `ACSWebSocketHandler` | `Service/ACSWebSocketHandler.cs` | Handles the ACS media streaming WebSocket — parses `AudioData`/`AudioMetadata` frames |
| `ACSCallBridge` | `Service/ACSCallBridge.cs` | Buffers audio between the ACS WebSocket and the translation pipeline |
| `CallService` | `Service/CallService.cs` | Call lifecycle management — create, answer, status updates |
| `CallManager.RunACSCallAsync` | `CallManager.cs` | Orchestrates translators, mixer, and audio routing for an ACS call |

---

## API Endpoints Reference

| Endpoint | Method | Auth | Purpose |
| -------- | ------ | ---- | ------- |
| `/api/events` | OPTIONS | None | EventGrid webhook validation handshake |
| `/api/events` | POST | None | Receives EventGrid CloudEvents (`IncomingCall`) |
| `/ws/acs/{callId}` | WebSocket | None | ACS media streaming — bidirectional audio |
| `/api/calls/{callId}/callback` | POST | None | ACS call state callbacks (disconnect detection) |
| `/api/calls/waiting` | GET | AuthCode | Lists waiting calls for the Agent UI |
| `/api/calls/acs/config` | GET | None | Returns ACS configuration status and inbound number |
| `/api/calls/acs/token` | GET | AuthCode | Issues a VoIP token for ACS client SDK |
| `/api/agent/ws` | WebSocket | AuthCode | Agent UI — call control and audio |
