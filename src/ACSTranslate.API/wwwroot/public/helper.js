function bufferedArray(size, callback) {
    let buffer = new Uint8Array();
    const flush = () => {
        if (buffer.length <= 0) return;
        callback(new Uint8Array(buffer.slice(0, size)));
        buffer = new Uint8Array(buffer.slice(size));
    }
    return {
        addData: (data) => {
            buffer = new Uint8Array([...buffer, ...new Uint8Array(data)]);
            if (buffer.length >= size) flush();
        },
        flush
    };
}

function atobInt16(data) {
    const binary = atob(data);
    const bytes = Uint8Array.from(binary, (c) => c.charCodeAt(0));
    return new Int16Array(bytes.buffer);
}

async function getAudioStream(sampleRate = 16000) {
    const enableAudioButton = document.getElementById("enableAudio");
    let stream;

    // First try automatic mic access
    try {
        stream = await navigator.mediaDevices.getUserMedia({
            audio: {
                channelCount: 1,
                sampleRate: sampleRate,
            }
        });
    } catch (error) {
        console.error("Error accessing audio stream:", error);
        stream = null;
    }

    // Fallback to button click
    if (!stream && enableAudioButton) {
        try {
            await new Promise((resolve) => {
                enableAudioButton.style.display = "block";
                enableAudioButton.onclick = () => {
                    enableAudioButton.style.display = "none";
                    resolve();
                };
            });
            stream = await navigator.mediaDevices.getUserMedia({
                audio: {
                    channelCount: 1,
                    sampleRate: sampleRate,
                }
            });
        } catch (error) {
            console.error("Error accessing audio stream:", error);
            stream = null;
        }
    }

    return stream;
}

async function setupAudioWorklet(sampleRate = 16000, bufferCallback = null) {
    const stream = await getAudioStream(sampleRate);

    // Load our audio context and audio worklet
    audioContext = new AudioContext({ sampleRate });
    await audioContext.audioWorklet.addModule("/public/audio.js");

    const voiceAgentNode = new AudioWorkletNode(audioContext, "VoiceAgentAudioWorklet");

    // Hook up speaker to the audio worklet (single channel splitter to up-mix to stereo)
    const splitter = audioContext.createChannelSplitter(1);
    voiceAgentNode.connect(splitter);
    splitter.connect(audioContext.destination);

    // Set up a data buffer
    const dataBuffer = bufferedArray(4800, bufferCallback);

    // Hook up microphone to the audio worklet
    voiceAgentNode.port.onmessage = (event) => dataBuffer?.addData(event.data.buffer);
    const recordMediaStreamSource = audioContext.createMediaStreamSource(stream);
    recordMediaStreamSource.connect(voiceAgentNode);

    return voiceAgentNode;
}