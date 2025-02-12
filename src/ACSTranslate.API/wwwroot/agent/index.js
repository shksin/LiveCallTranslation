import { CallClient } from "@azure/communication-calling";
import { AzureCommunicationTokenCredential } from '@azure/communication-common';

const callClient = new CallClient();
let call;
let callAgent;
let deviceManager;
let tokenCredential;
let serverAppId;

const callList = document.getElementById('callList');
const transcriptionList = document.getElementById('transcriptionList');

function connectCall(callId, button) {
    button.disabled = true;
    if (!serverAppId || !callAgent) {
        alert('Call agent not ready');
        return;
    }
    if (call) {
        alert('Already on a call');
        return;
    }
    call = callAgent.startCall(
        [{ communicationUserId: serverAppId }],
        { customContext: { voipHeaders : [
            { key: "callId", value: callId }
        ]}}
    );
    const es = new EventSource(`/api/calls/${callId}/transcription`);
    es.onmessage = e => {
        const x = JSON.parse(e.data);
        //if (!!x.ping) return;
        console.log(x);
        upsertTranscriptionElement(x);
    };
}

function updateCalls() {
    const es = new EventSource(`/api/calls/events`);
    es.onmessage = e => {
        const x = JSON.parse(e.data);
        console.log(x);
        upsertCallElement(x);
    };

    /*fetch('/api/calls')
        .then(res => res.json())
        .then(calls => {
            callList.innerHTML = '';
            calls.forEach(call => {
                upsertCallElement(call);
            });
        });*/
}

function upsertTranscriptionElement(transcription) {
    const element_id = `transcription-${transcription.id}`;
    let element = document.getElementById(element_id);
    if (!element) {
        element = document.createElement('li');
        element.id = element_id;

        const statusText = document.createElement('div');
        statusText.style.fontSize = 'smaller';
        statusText.textContent = transcription.user + '   (' + transcription.sentAt.split(".")[0] + ')';
        element.appendChild(statusText);

        const nativeText = document.createElement('span');
        nativeText.className = 'native';
        element.appendChild(nativeText);

        const translatedText = document.createElement('span');
        translatedText.className = 'translated';
        element.appendChild(translatedText);

        transcriptionList.appendChild(element);
    }

    const nativeText = element.querySelector('.native');
    nativeText.textContent = transcription.nativeText;

    const translatedText = element.querySelector('.translated');
    translatedText.textContent = transcription.translatedText;
}

function upsertCallElement(call) {
    const element_id = `call-${call.id}`;
    const existingElement = document.getElementById(element_id);
    if (existingElement) {
        existingElement.querySelector('div').textContent = call.status;
        existingElement.querySelector('button').style.display = call.status !== 'Waiting' ? 'none' : 'block';
        return;
    }
    const li = document.createElement('li');
    li.id = element_id;

    const boldText = document.createElement('b');
    boldText.textContent = call.callerId;
    boldText.title = call.id;
    li.appendChild(boldText);

    const statusText = document.createElement('div');
    statusText.style.fontSize = 'smaller';
    statusText.textContent = call.status;
    li.appendChild(statusText);

    const button = document.createElement('button');
    button.textContent = 'Connect';
    button.className = 'is-active';
    button.style.display = call.status !== 'Waiting' ? 'none' : 'block';
    button.onclick = () => connectCall(call.id, button);
    li.appendChild(button);

    callList.appendChild(li);
}

// Connect to the Azure Communication Services
fetch('/api/calls/acsauth')
        .then(res => res.json())
        .then(async userToken => {
            serverAppId = userToken.serverId;
            tokenCredential = new AzureCommunicationTokenCredential(userToken.token);
            callAgent = await callClient.createCallAgent(tokenCredential);
            deviceManager = await callClient.getDeviceManager();
            await deviceManager.askDevicePermission({ audio: true });
            console.log('Call agent created');
            updateCalls();
        });

