using ACSTranslate.Core;
using Microsoft.CognitiveServices.Speech;

namespace ACSTranslate;

public static class AudioFormatConfigExtensions
{
    public static SpeechSynthesisOutputFormat ToSpeechSynthesisOutputFormat(this AudioFormatConfig audioFormat) => audioFormat.GlobalAudioFormat switch
    {
        GlobalAudioFormat.Pcm16KMono16Bit => SpeechSynthesisOutputFormat.Raw16Khz16BitMonoPcm,
        _ => throw new Exception($"Audio format {audioFormat.GlobalAudioFormat} not configured for speech synthesis")
    };
}