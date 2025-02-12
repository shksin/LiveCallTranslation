namespace ACSTranslate;

public record TranslatorOptions(string InputLanguage, string OutputLanguage, string OutputVoice)
{
    public string OutputLanguageCode
        => OutputLanguage.Split('-')[0]; // This is not technically correct, but it's good enough for now
}
