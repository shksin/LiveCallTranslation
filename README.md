# Live Call Translation

## Overview
A real-time multilingual communication capability between non-English-speaking customers and English-speaking agents and using AI-powered speech translation, ensuring seamless conversations through voice-to-voice interaction.

## EBC Demo flow

`User` <-> `Websocket` <-> `Speech Stream` <-> `Translate` <-> `Speech + Control Stream` <-> `Websocket` <-> `Agent`

## Local Testing
1. Install Azure CLI: [Install Azure CLI](https://docs.microsoft.com/en-us/cli/azure/install-azure-cli)
2. Install .NET 9: [Download .NET 9](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
3. Deploy an `Azure AI Speech` Resource, make sure you have the `Cognitive Services Speech User` role assigned to your user on the resource.
4. Copy *appsettings.json* to *appsettings.Development.json* and fill in the following values:
- `AzureAISpeech` -> `ResourceID`: Navigate to your Azure Speech resource in the Azure portal -> Properties -> Resource ID
- `AzureAISpeech` -> `Region`: Region where your Azure Speech resource is deployed
- `AuthCode`: Required only if you want to enable authentication on the user and agent interfaces. To access the site you will need to provide the auth code as a query parameter like `?code=<AuthCode>`. You can generate a random code or use a simple one for testing purposes.

5. To run the application locally, cd to `src/ACSTranslate.API` and execute the following command:
    ```sh
    dotnet run
    ```
6. Navigate to http://localhost:5058/agent/ to see the interface for agent.
7. Navigate to http://localhost:5058/user/ to see the interface for user.

## Deployment to Azure
1. Deploy an Web App (App Service) in Azure with .NET 9 (Windows) runtime stack.
2. Make sure the Web App has:
- A managed identity assigned with `Cognitive Services Speech User` role on the Azure AI Speech resource.
- Always On enabled
- **Only a single instance** (Scaling out to multiple instances is not supported in this version)
- Web Sockets enabled
- x64 platform set
3. Set the following environment variables in the Web App Configuration:
    - `AzureAISpeech__ResourceID`: Navigate to your Azure Speech resource in the Azure portal -> Properties -> Resource ID
    - `AzureAISpeech__Region`: Region where your Azure Speech resource is deployed
    - `AuthCode`: Required only if you want to enable authentication on the user and agent interfaces. To access the site you will need to provide the auth code as a query parameter like `?code=<AuthCode>`. You can generate a random code or use a simple one for testing purposes.
4. Publish the application locally, cd to `src/ACSTranslate.API` and execute the following command:
    ```sh
    dotnet publish -c Release -o ./bin/Publish -r win-x64 --self-contained true
    ```
5. In VS Code deploy the contents of the `./bin/Publish` folder to the Web App using the [Azure App Service extension](https://marketplace.visualstudio.com/items?itemName=ms-azuretools.vscode-azureappservice).