# Live Call Translation

## Overview
Real-time multilingual voice translation between non-English-speaking callers and English-speaking agents. Powered by Azure Communication Services (ACS) for telephony and Azure AI Speech SDK for speech recognition, translation, and synthesis — delivering seamless voice-to-voice interaction.

## Architecture

### End-to-End Flow (ACS Telephony Mode)

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
                    │      EventGrid Controller   │
                    │  POST /api/events           │
                    └──────┬──────────────────────┘
                           │
                    ② AnswerCallAsync (with media streaming config)
                           │
                           ▼
                    ┌─────────────────────────────┐
                    │      ACS Media Streaming    │
                    │  wss://host/ws/acs/{callId} │
                    │  (PCM 16kHz 16-bit mono)    │
                    └──────┬──────────────────────┘
                           │
                    ③ Bidirectional audio stream
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

### Translation Pipeline Detail (Azure AI Speech SDK)

Each direction of translation uses a 3-stage pipeline:

```
┌─────────────────┐     ┌──────────────────┐     ┌─────────────────┐
│ 1. Speech-to-   │     │ 2. Translation   │     │ 3. Text-to-     │
│    Text (STT)   │────►│    (built-in)    │────►│    Speech (TTS) │
│                 │     │                  │     │                 │
│ TranslationRec- │     │ Source language   │     │ SpeechSynthe-   │
│ ognizer listens │     │ → Target language │     │ sizer generates │
│ to raw PCM      │     │                  │     │ audio in target │
│ audio stream    │     │ Partial + Final  │     │ language/voice  │
└─────────────────┘     └──────────────────┘     └─────────────────┘
     ▲                                                  │
     │                                                  │
  Raw PCM audio                                  Translated PCM audio
  (16kHz, 16-bit, mono)                          (16kHz, 16-bit, mono)
```

### Direct WebSocket Mode (Browser-to-Browser)

For local testing without ACS/telephony:

```
┌──────────┐   WebSocket    ┌──────────────────┐   WebSocket    ┌──────────┐
│   User   │◄──────────────►│   CallManager    │◄──────────────►│  Agent   │
│(Browser) │  /api/user/ws  │                  │ /api/agent/ws  │(Browser) │
└──────────┘                │  Same translator │                └──────────┘
                            │  pipeline as ACS │
                            └──────────────────┘
```

### Step-by-Step ACS Call Flow

| Step | Trigger | Component | Action |
|------|---------|-----------|--------|
| 1 | Phone call arrives | ACS → EventGrid | `IncomingCall` event fired |
| 2 | EventGrid webhook | `EventGridController` | Routes to `InboundCallHandler` |
| 3 | Inbound handler | `CallService.CreateCallAsync` | Answers call with media streaming (PCM 16kHz mono, bidirectional) |
| 4 | ACS connects WS | `ACSWebSocketHandler` | Receives caller audio, pushes to `ACSCallBridge` channel |
| 5 | Agent clicks Connect | `CallManager.ConnectAgentAsync` | Creates two translators (user→agent, agent→user) |
| 6 | Caller speaks | User→Agent translator | STT → Translate → TTS → `DynamicMixer` → Agent WS |
| 7 | Agent speaks | Agent→User translator | STT → Translate → TTS → `ACSCallBridge` → ACS → Caller |
| 8 | Either disconnects | `CallManager` | Cancels all tasks, disposes translators, cleans bridge |

## Important Notes
- Clicking the "Disconnect" button twice on the agent interface will refresh it if it becomes unresponsive.
- The User interface will auto refresh on call end, or if the websocket connection is lost. You should expect to see it pop back up as available a few seconds after the call ends.
- Media streaming is started when the call is answered — it must **not** be started again or the WebSocket resets.
- Only a **single App Service instance** is supported (no scale-out).

## Configuration

### Required Settings

| Setting | Description | Example |
|---------|-------------|---------|
| `AzureAISpeech:ResourceID` | Azure AI Speech resource ID (Portal → Properties → Resource ID) | `/subscriptions/.../Microsoft.CognitiveServices/accounts/my-speech` |
| `AzureAISpeech:Region` | Region of the Speech resource | `eastus`, `australiaeast` |
| `Translator` | Translator implementation to use | `AISpeech` |

### ACS Telephony Settings (required for phone calls)

| Setting | Description | Example |
|---------|-------------|---------|
| `ACS:Endpoint` | ACS resource endpoint | `https://my-acs.communication.azure.com/` |
| `ACS:InboundNumber` | Phone number to accept calls on, or `*` for all | `+18335276165` |
| `EventGrid:TopicResourceID` | EventGrid system topic for ACS events | `/subscriptions/.../systemTopics/my-topic` |
| `Inbound:Hostname` | App hostname for callbacks (auto-detected on Azure via `WEBSITE_HOSTNAME`) | `myapp.azurewebsites.net` |

### Optional Settings

| Setting | Description | Example |
|---------|-------------|---------|
| `AzureTenantId` | Azure AD tenant for `DefaultAzureCredential` | `16b3c013-d300-468d-ac64-7eda0820b6d3` |
| `AuthCode` | Query-string auth code for agent/user UIs (`?code=<value>`) | `my-secret-code` |

### Environment Variable Format (for App Service)

Settings use `__` as the section separator:

```
AzureAISpeech__ResourceID=/subscriptions/.../accounts/my-speech
AzureAISpeech__Region=australiaeast
Translator=AISpeech
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

## Local Testing
1. Install Azure CLI: [Install Azure CLI](https://docs.microsoft.com/en-us/cli/azure/install-azure-cli)
2. Install .NET 9: [Download .NET 9](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
3. Deploy an `Azure AI Speech` Resource, make sure you have the `Cognitive Services Speech User` role assigned to your user on the resource.
4. Copy *appsettings.json* to *appsettings.Development.json* and fill in the following values:
- `AzureAISpeech` → `ResourceID`: Navigate to your Azure Speech resource in the Azure portal → Properties → Resource ID
- `AzureAISpeech` → `Region`: Region where your Azure Speech resource is deployed
- `AuthCode`: Required only if you want to enable authentication on the user and agent interfaces. To access the site you will need to provide the auth code as a query parameter like `?code=<AuthCode>`. You can generate a random code or use a simple one for testing purposes.

5. To run the application locally, cd to `src/ACSTranslate.API` and execute the following command:
    ```sh
    dotnet run
    ```
6. Navigate to http://localhost:5058/agent/ to see the interface for agent.
7. Navigate to http://localhost:5058/user/ to see the interface for user.

> **Note:** Direct WebSocket mode (browser-to-browser) works without ACS configuration. ACS telephony mode requires the ACS, EventGrid, and Inbound settings.

## Deployment to Azure
1. Deploy a Web App (App Service) in Azure with .NET 9 (Windows) runtime stack.
2. Make sure the Web App has:
- A managed identity assigned with `Cognitive Services Speech User` role on the Azure AI Speech resource.
- Always On enabled
- **Only a single instance** (Scaling out to multiple instances is not supported in this version)
- Web Sockets enabled
- x64 platform set
3. Set the following environment variables in the Web App Configuration:
    - `AzureAISpeech__ResourceID`: Navigate to your Azure Speech resource in the Azure portal → Properties → Resource ID
    - `AzureAISpeech__Region`: Region where your Azure Speech resource is deployed
    - `Translator`: Set to `AISpeech`
    - `ACS__Endpoint`: Your ACS resource endpoint (required for phone calls)
    - `ACS__InboundNumber`: Phone number to accept calls on (required for phone calls)
    - `EventGrid__TopicResourceID`: EventGrid system topic resource ID (required for phone calls)
    - `AuthCode`: Required only if you want to enable authentication on the user and agent interfaces. To access the site you will need to provide the auth code as a query parameter like `?code=<AuthCode>`. You can generate a random code or use a simple one for testing purposes.
4. Publish the application locally, cd to `src/ACSTranslate.API` and execute the following command:
    ```sh
    dotnet publish -c Release -o ./bin/Publish -r win-x64 --self-contained true
    ```
5. In VS Code deploy the contents of the `./bin/Publish` folder to the Web App using the [Azure App Service extension](https://marketplace.visualstudio.com/items?itemName=ms-azuretools.vscode-azureappservice).