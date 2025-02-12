namespace ACSTranslate.Core;

// This is a fixed config for now
public record AudioFormatConfig(GlobalAudioFormat GlobalAudioFormat)
{
    public static AudioFormatConfig Current { get; } = new(GlobalAudioFormat.Pcm16KMono16Bit);
}
public enum GlobalAudioFormat
{
    Pcm16KMono16Bit
}