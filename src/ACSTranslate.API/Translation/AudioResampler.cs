namespace ACSTranslate.Translation;

/// <summary>
/// Audio resampling utilities for Voice Live API compatibility.
/// Voice Live API requires PCM16 24kHz audio, while telephony typically uses 8kHz µ-law.
/// Uses cubic interpolation for high-quality resampling.
/// </summary>
public static class AudioResampler
{
    /// <summary>
    /// µ-law decoding lookup table for converting µ-law encoded audio to PCM16.
    /// </summary>
    private static readonly short[] MuLawDecodeTable = new short[]
    {
        -32124, -31100, -30076, -29052, -28028, -27004, -25980, -24956,
        -23932, -22908, -21884, -20860, -19836, -18812, -17788, -16764,
        -15996, -15484, -14972, -14460, -13948, -13436, -12924, -12412,
        -11900, -11388, -10876, -10364, -9852, -9340, -8828, -8316,
        -7932, -7676, -7420, -7164, -6908, -6652, -6396, -6140,
        -5884, -5628, -5372, -5116, -4860, -4604, -4348, -4092,
        -3900, -3772, -3644, -3516, -3388, -3260, -3132, -3004,
        -2876, -2748, -2620, -2492, -2364, -2236, -2108, -1980,
        -1884, -1820, -1756, -1692, -1628, -1564, -1500, -1436,
        -1372, -1308, -1244, -1180, -1116, -1052, -988, -924,
        -876, -844, -812, -780, -748, -716, -684, -652,
        -620, -588, -556, -524, -492, -460, -428, -396,
        -372, -356, -340, -324, -308, -292, -276, -260,
        -244, -228, -212, -196, -180, -164, -148, -132,
        -120, -112, -104, -96, -88, -80, -72, -64,
        -56, -48, -40, -32, -24, -16, -8, 0,
        32124, 31100, 30076, 29052, 28028, 27004, 25980, 24956,
        23932, 22908, 21884, 20860, 19836, 18812, 17788, 16764,
        15996, 15484, 14972, 14460, 13948, 13436, 12924, 12412,
        11900, 11388, 10876, 10364, 9852, 9340, 8828, 8316,
        7932, 7676, 7420, 7164, 6908, 6652, 6396, 6140,
        5884, 5628, 5372, 5116, 4860, 4604, 4348, 4092,
        3900, 3772, 3644, 3516, 3388, 3260, 3132, 3004,
        2876, 2748, 2620, 2492, 2364, 2236, 2108, 1980,
        1884, 1820, 1756, 1692, 1628, 1564, 1500, 1436,
        1372, 1308, 1244, 1180, 1116, 1052, 988, 924,
        876, 844, 812, 780, 748, 716, 684, 652,
        620, 588, 556, 524, 492, 460, 428, 396,
        372, 356, 340, 324, 308, 292, 276, 260,
        244, 228, 212, 196, 180, 164, 148, 132,
        120, 112, 104, 96, 88, 80, 72, 64,
        56, 48, 40, 32, 24, 16, 8, 0
    };

    /// <summary>
    /// Decodes µ-law encoded audio bytes to PCM16 samples.
    /// </summary>
    public static short[] DecodeMuLaw(byte[] muLawData)
    {
        var pcm = new short[muLawData.Length];
        for (int i = 0; i < muLawData.Length; i++)
        {
            pcm[i] = MuLawDecodeTable[muLawData[i]];
        }
        return pcm;
    }

    /// <summary>
    /// Encodes PCM16 samples to µ-law bytes.
    /// </summary>
    public static byte[] EncodeMuLaw(short[] pcmData)
    {
        var muLaw = new byte[pcmData.Length];
        for (int i = 0; i < pcmData.Length; i++)
        {
            muLaw[i] = LinearToMuLaw(pcmData[i]);
        }
        return muLaw;
    }

    private static byte LinearToMuLaw(short sample)
    {
        const int MULAW_MAX = 0x1FFF;
        const int MULAW_BIAS = 33;
        
        int sign = (sample >> 8) & 0x80;
        if (sign != 0)
            sample = (short)-sample;
        if (sample > MULAW_MAX)
            sample = MULAW_MAX;
        
        sample = (short)(sample + MULAW_BIAS);
        int exponent = 7;
        for (int expMask = 0x4000; (sample & expMask) == 0 && exponent > 0; exponent--, expMask >>= 1) { }
        
        int mantissa = (sample >> (exponent + 3)) & 0x0F;
        byte muLawByte = (byte)(~(sign | (exponent << 4) | mantissa));
        
        return muLawByte;
    }

    /// <summary>
    /// Converts PCM16 bytes to short array.
    /// </summary>
    public static short[] BytesToPcm16(byte[] bytes)
    {
        var samples = new short[bytes.Length / 2];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = BitConverter.ToInt16(bytes, i * 2);
        }
        return samples;
    }

    /// <summary>
    /// Converts PCM16 short array to bytes.
    /// </summary>
    public static byte[] Pcm16ToBytes(short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            var sampleBytes = BitConverter.GetBytes(samples[i]);
            bytes[i * 2] = sampleBytes[0];
            bytes[i * 2 + 1] = sampleBytes[1];
        }
        return bytes;
    }

    /// <summary>
    /// Upsamples PCM16 audio using cubic interpolation.
    /// Used to convert 8kHz telephony audio to 24kHz for Voice Live API.
    /// </summary>
    /// <param name="input">Input PCM16 samples</param>
    /// <param name="factor">Upsampling factor (e.g., 3 for 8kHz → 24kHz)</param>
    /// <returns>Upsampled PCM16 samples</returns>
    public static short[] UpsampleCubic(short[] input, int factor)
    {
        if (input.Length < 2)
            return input;

        var output = new short[input.Length * factor];

        for (int i = 0; i < input.Length; i++)
        {
            // Get surrounding samples for cubic interpolation (with boundary handling)
            double p0 = input[Math.Max(0, i - 1)];
            double p1 = input[i];
            double p2 = input[Math.Min(input.Length - 1, i + 1)];
            double p3 = input[Math.Min(input.Length - 1, i + 2)];

            for (int j = 0; j < factor; j++)
            {
                double t = (double)j / factor;
                
                // Cubic interpolation formula
                double a = -0.5 * p0 + 1.5 * p1 - 1.5 * p2 + 0.5 * p3;
                double b = p0 - 2.5 * p1 + 2 * p2 - 0.5 * p3;
                double c = -0.5 * p0 + 0.5 * p2;
                double d = p1;

                double value = a * t * t * t + b * t * t + c * t + d;
                
                // Clamp to valid range
                value = Math.Max(-32768, Math.Min(32767, value));
                output[i * factor + j] = (short)Math.Round(value);
            }
        }

        return output;
    }

    /// <summary>
    /// Downsamples PCM16 audio using averaging.
    /// Used to convert 24kHz Voice Live API output to 8kHz for telephony.
    /// </summary>
    /// <param name="input">Input PCM16 samples</param>
    /// <param name="factor">Downsampling factor (e.g., 3 for 24kHz → 8kHz)</param>
    /// <returns>Downsampled PCM16 samples</returns>
    public static short[] Downsample(short[] input, int factor)
    {
        if (factor <= 1)
            return input;

        var outputLength = input.Length / factor;
        var output = new short[outputLength];

        for (int i = 0; i < outputLength; i++)
        {
            // Average the samples in each group
            long sum = 0;
            for (int j = 0; j < factor; j++)
            {
                int idx = i * factor + j;
                if (idx < input.Length)
                    sum += input[idx];
            }
            output[i] = (short)(sum / factor);
        }

        return output;
    }

    /// <summary>
    /// Converts 8kHz µ-law telephony audio to 24kHz PCM16 for Voice Live API.
    /// </summary>
    public static byte[] ConvertTelephonyToVoiceLive(byte[] muLawData)
    {
        // Decode µ-law to PCM16
        var pcm8k = DecodeMuLaw(muLawData);
        
        // Upsample 8kHz → 24kHz (3x)
        var pcm24k = UpsampleCubic(pcm8k, 3);
        
        // Convert to bytes
        return Pcm16ToBytes(pcm24k);
    }

    /// <summary>
    /// Converts 24kHz PCM16 from Voice Live API to 8kHz µ-law for telephony.
    /// </summary>
    public static byte[] ConvertVoiceLiveToTelephony(byte[] pcm24kBytes)
    {
        // Convert bytes to samples
        var pcm24k = BytesToPcm16(pcm24kBytes);
        
        // Downsample 24kHz → 8kHz (3x)
        var pcm8k = Downsample(pcm24k, 3);
        
        // Encode to µ-law
        return EncodeMuLaw(pcm8k);
    }

    /// <summary>
    /// Converts PCM16 16kHz audio to 24kHz for Voice Live API.
    /// </summary>
    public static byte[] Upsample16kTo24k(byte[] pcm16kBytes)
    {
        var pcm16k = BytesToPcm16(pcm16kBytes);
        
        // 16kHz → 24kHz is 1.5x, we'll use 3x upsample then 2x downsample
        var upsampled = UpsampleCubic(pcm16k, 3);  // 16kHz → 48kHz
        var output = Downsample(upsampled, 2);      // 48kHz → 24kHz
        
        return Pcm16ToBytes(output);
    }

    /// <summary>
    /// Converts PCM16 24kHz audio to 16kHz.
    /// </summary>
    public static byte[] Downsample24kTo16k(byte[] pcm24kBytes)
    {
        var pcm24k = BytesToPcm16(pcm24kBytes);
        
        // 24kHz → 16kHz is 2/3x, we'll use 2x upsample then 3x downsample
        var upsampled = UpsampleCubic(pcm24k, 2);  // 24kHz → 48kHz
        var output = Downsample(upsampled, 3);      // 48kHz → 16kHz
        
        return Pcm16ToBytes(output);
    }
}
