import { CallClient } from "@azure/communication-calling";
import { AzureCommunicationTokenCredential } from '@azure/communication-common';

const callClient = new CallClient();
let call;
let callAgent;
let deviceManager;
let tokenCredential;
let endpointToDial;

const statusElement = document.getElementById('status');
const connectButton = document.getElementById('connect');
const disconnectButton = document.getElementById('disconnect');
const nameInput = document.getElementById('name');
const languageInput = document.getElementById('language');

fetch('/api/calls/user/voip/auth')
    .then(res => res.json())
    .then(async userToken => {
        endpointToDial = userToken.endpointToDial;
        tokenCredential = new AzureCommunicationTokenCredential(userToken.token);
        callAgent = await callClient.createCallAgent(tokenCredential);
        deviceManager = await callClient.getDeviceManager();
        await deviceManager.askDevicePermission({ audio: true });
        statusElement.innerText = 'Ready';
        connectButton.disabled = false;
    });

connectButton.addEventListener('click', async () => {
    connectButton.disabled = true;
    disconnectButton.disabled = false;
    statusElement.innerText = 'Connecting...';
    call = callAgent.startCall([{ communicationUserId: endpointToDial }], {
        customContext: {
            voipHeaders: [
                { key: 'name', value: nameInput.value },
                { key: 'language', value: languageInput.value }
            ]
        }
    });
    call.on('stateChanged', async () => {
        console.log(`Call state changed: ${call.state}`);
        if (call.state === 'Connected') {
            statusElement.innerText = 'Connected';
        } else if (call.state === 'Disconnected') {
            statusElement.innerText = 'Disconnected';
            disconnectButton.disabled = true;
            connectButton.disabled = false;
        }
    });

});

disconnectButton.addEventListener('click', async () => {
    disconnectButton.disabled = true;
    connectButton.disabled = false;
    try {
        call.hangUp();
    }
    catch (e) {
        console.error(e);
    }
    statusElement.innerText = 'Ready';
});
