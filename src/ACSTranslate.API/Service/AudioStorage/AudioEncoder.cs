using System.Text;
using ACSTranslate.Core;

namespace ACSTranslate;

public class AudioEncoder
{
    public static byte[] EncodeToWav(GlobalAudioFormat sourceFormat, byte[] data)
    {
        if (sourceFormat != GlobalAudioFormat.Pcm16KMono16Bit)
        {
            throw new NotSupportedException("Only Pcm16KMono16Bit is supported");
        }

        using MemoryStream memoryStream = new();
        using (BinaryWriter writer = new(memoryStream))
        {
            uint sizeOfHeader = 44;
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write((uint)(data.Length + sizeOfHeader - 8)); // Chunk size
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write((uint)16); // Subchunk1Size
            writer.Write((ushort)1); // AudioFormat 1=PCM
            writer.Write((ushort)1); // NumChannels 1=Mono
            writer.Write((uint)16000); // SampleRate, 16khz
            writer.Write((uint)16000 * 2); // ByteRate
            writer.Write((ushort)2); // BlockAlign 2=16-bit mono
            writer.Write((ushort)16); // BitsPerSample
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write((uint)data.Length); // Subchunk2Size
            writer.Write(data);
        }
        // This is a placeholder for the actual encoding logic
        return memoryStream.ToArray();
    }
}