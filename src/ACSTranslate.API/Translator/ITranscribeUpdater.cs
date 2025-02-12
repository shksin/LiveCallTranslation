namespace ACSTranslate;

public interface ITranscribeUpdater
{
    Guid UpdateText(string? nativeText = null, string? translatedText = null);
    Guid FinalizeText(string? nativeText = null, string? translatedText = null);
}