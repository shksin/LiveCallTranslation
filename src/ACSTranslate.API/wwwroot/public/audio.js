class VoiceAgentAudioWorklet extends AudioWorkletProcessor {
    constructor() {
        super();
        this.port.onmessage = this.handleMessage.bind(this);
        this.port.on;
        this.buffer = [];
    }

    handleMessage(event) {
        if (event.data === null) {
            this.buffer = [];
            return;
        }
        const len = event.data.length
        for (let i = 0; i < len; i++) {
            this.buffer.push(event.data[i])
        }
    }
    
    largePush(src, dest) { }

    process(inputs, outputs, parameters) {
        const input = inputs[0];
        if (input.length > 0) {
            const float32Buffer = input[0];
            const int16Buffer = this.convertFloat32ToInt16(float32Buffer);
            this.port.postMessage(int16Buffer);
        }

        const output = outputs[0];
        const channel = output[0];
        if (this.buffer.length > channel.length) {
            const toProcess = this.buffer.splice(0, channel.length);
            channel.set(toProcess.map((v) => v / 32768));
        } else {
            channel.set(this.buffer.map((v) => v / 32768));
            this.buffer = [];
        }

        return true;
    }

    convertFloat32ToInt16(float32Array) {
        const int16Array = new Int16Array(float32Array.length);
        for (let i = 0; i < float32Array.length; i++) {
            let val = Math.floor(float32Array[i] * 0x7fff);
            val = Math.max(-0x8000, Math.min(0x7fff, val));
            int16Array[i] = val;
        }
        return int16Array;
    }
}

registerProcessor("VoiceAgentAudioWorklet", VoiceAgentAudioWorklet);