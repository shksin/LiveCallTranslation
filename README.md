# Live Call Translation

## Overview
Real-time multilingual voice translation between non-English-speaking callers and English-speaking agents. Powered by Azure Communication Services (ACS) for telephony, Genesys Cloud AudioHook v2 for contact center integration, and Azure AI Speech SDK for speech recognition, translation, and synthesis — delivering seamless voice-to-voice interaction.

### Connection Modes

| Mode | Description |
|------|-------------|
| **ACS Telephony** | Callers dial in via PSTN; calls are answered by ACS with bidirectional media streaming |
| **Genesys AudioHook v2** | Audio streamed from Genesys Cloud contact center via WebSocket |
| **Genesys Simulator** | Browser-based AudioHook simulator for testing without a Genesys environment |
| **Direct Browser** | Browser-to-browser WebSocket mode for local testing without telephony |

## Documentation

| Document | Description |
|----------|-------------|
| [ACS Integration](ACS_INTEGRATION.md) | Detailed ACS telephony integration — architecture, call flow, EventGrid setup |
| [Genesys Integration](GENESYS_INTEGRATION.md) | Genesys AudioHook v2 integration — architecture, protocol, deployment |
| [Genesys Simulator](GENESYS_SIMULATOR.md) | Browser-based Genesys AudioHook simulator usage guide |

## Architecture

For mode-specific architecture diagrams, see [ACS Integration](ACS_INTEGRATION.md) and [Genesys Integration](GENESYS_INTEGRATION.md).

### Translation Pipeline (Azure AI Speech SDK)

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


### ACS Telephony Settings

See [ACS Integration](ACS_INTEGRATION.md) for ACS, EventGrid, and Inbound configuration.

### Optional Settings

| Setting | Description | Example |
|---------|-------------|---------|
| `AzureTenantId` | Azure AD tenant for `DefaultAzureCredential` | `16b3c013-d300-468d-ac64-7eda0820b6d3` |


### Environment Variable Format (for App Service)

Settings use `__` as the section separator:

```
AzureAISpeech__ResourceID=/subscriptions/.../accounts/my-speech
AzureAISpeech__Region=australiaeast
```

For ACS-specific environment variables, see [ACS Integration](ACS_INTEGRATION.md).

### Azure RBAC Requirements

| Role | Resource | Purpose |
|------|----------|---------|
| `Cognitive Services Speech User` | Azure AI Speech resource | Token-based auth for STT/TTS |

For ACS and EventGrid RBAC requirements, see [ACS Integration](ACS_INTEGRATION.md).

## Local Testing
1. Install Azure CLI: [Install Azure CLI](https://docs.microsoft.com/en-us/cli/azure/install-azure-cli)
2. Install .NET 9: [Download .NET 9](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
3. Deploy an `Azure AI Speech` Resource, make sure you have the `Cognitive Services Speech User` role assigned to your user on the resource.
4. Copy *appsettings.json* to *appsettings.Development.json* and fill in the following values:
- `AzureAISpeech` → `ResourceID`: Navigate to your Azure Speech resource in the Azure portal → Properties → Resource ID
- `AzureAISpeech` → `Region`: Region where your Azure Speech resource is deployed

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
    - For ACS telephony settings, see [ACS Integration](ACS_INTEGRATION.md)
4. Publish the application locally, cd to `src/ACSTranslate.API` and execute the following command:
    ```sh
    dotnet publish -c Release -o ./bin/Publish -r win-x64 --self-contained true
    ```
5. In VS Code deploy the contents of the `./bin/Publish` folder to the Web App using the [Azure App Service extension](https://marketplace.visualstudio.com/items?itemName=ms-azuretools.vscode-azureappservice).