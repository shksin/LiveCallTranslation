namespace ACSTranslate.Genesys;

/// <summary>
/// Converts between µ-law 8kHz mono (Genesys AudioHook format) and PCM 16kHz 16-bit mono (translator format).
/// </summary>
public static class MuLawConverter
{
    private const int MuLawBias = 0x84;
    private const int MuLawClip = 32635;

    private static readonly short[] MuLawDecompressTable = BuildDecompressTable();

    private static short[] BuildDecompressTable()
    {
        var table = new short[256];
        for (int i = 0; i < 256; i++)
        {
            int muLaw = ~i;
            int sign = (muLaw & 0x80);
            int exponent = (muLaw >> 4) & 0x07;
            int mantissa = muLaw & 0x0F;
            int sample = ((mantissa << 3) + MuLawBias) << exponent;
            sample -= MuLawBias;
            table[i] = (short)(sign != 0 ? -sample : sample);
        }
        return table;
    }

    /// <summary>
    /// Decode µ-law bytes to PCM 16-bit samples at 8kHz, then upsample 2x to 16kHz.
    /// Input: µ-law 8kHz mono. Output: PCM 16kHz 16-bit mono (little-endian bytes).
    /// </summary>
    public static byte[] MuLaw8kToPcm16k(byte[] muLawData)
    {
        // Each µ-law byte → one 16-bit sample at 8kHz. Upsample 2x → 2 samples per input sample.
        var pcm16k = new byte[muLawData.Length * 2 * 2]; // 2x upsample, 2 bytes per sample
        int outIdx = 0;

        short prevSample = 0;
        for (int i = 0; i < muLawData.Length; i++)
        {
            short sample = MuLawDecompressTable[muLawData[i]];

            // Linear interpolation: insert midpoint between previous and current sample
            short mid = (short)((prevSample + sample) / 2);
            pcm16k[outIdx++] = (byte)(mid & 0xFF);
            pcm16k[outIdx++] = (byte)((mid >> 8) & 0xFF);

            pcm16k[outIdx++] = (byte)(sample & 0xFF);
            pcm16k[outIdx++] = (byte)((sample >> 8) & 0xFF);

            prevSample = sample;
        }

        return pcm16k;
    }

    /// <summary>
    /// Downsample PCM 16kHz 16-bit mono to 8kHz, then encode as µ-law.
    /// Input: PCM 16kHz 16-bit mono (little-endian bytes). Output: µ-law 8kHz mono bytes.
    /// </summary>
    public static byte[] Pcm16kToMuLaw8k(byte[] pcm16kData)
    {
        int sampleCount = pcm16kData.Length / 2;
        int outputSamples = sampleCount / 2; // Downsample 2x
        var muLaw = new byte[outputSamples];

        for (int i = 0; i < outputSamples; i++)
        {
            // Take every other sample (simple decimation)
            int srcIdx = i * 2 * 2; // 2x skip, 2 bytes per sample
            if (srcIdx + 1 >= pcm16kData.Length) break;
            short sample = (short)(pcm16kData[srcIdx] | (pcm16kData[srcIdx + 1] << 8));
            muLaw[i] = EncodeMuLaw(sample);
        }

        return muLaw;
    }

    private static byte EncodeMuLaw(short sample)
    {
        int sign = (sample >> 8) & 0x80;
        if (sign != 0) sample = (short)-sample;
        if (sample > MuLawClip) sample = MuLawClip;
        sample = (short)(sample + MuLawBias);

        int exponent = 7;
        int mask = 0x4000;
        for (; exponent > 0; exponent--)
        {
            if ((sample & mask) != 0) break;
            mask >>= 1;
        }

        int mantissa = (sample >> (exponent + 3)) & 0x0F;
        byte muLawByte = (byte)(~(sign | (exponent << 4) | mantissa));
        return muLawByte;
    }
}
