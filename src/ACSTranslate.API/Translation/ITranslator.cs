namespace ACSTranslate.Translation;

/// <summary>
/// Interface for real-time translation implementations.
/// Uses Azure Speech SDK (3-stage: STT → Translate → TTS).
/// </summary>
public interface ITranslator : IDisposable
{
    /// <summary>
    /// Send raw audio data (PCM 16kHz 16-bit mono) to the translator.
    /// </summary>
    void SendData(byte[] data);

    /// <summary>
    /// Attach a callback for transcription output.
    /// Called with (originalText, translatedText, isFinal).
    /// </summary>
    void AttachTranscribeOutput(Func<string, string, bool, Task> callback, string? translationShortCode = null);

    /// <summary>
    /// Attach a callback for synthesized speech output.
    /// Called with audio data chunks (PCM 16kHz 16-bit mono).
    /// </summary>
    void AttachSpeechOutput(Func<byte[], Task> callback, string? translationShortCode = null);

    /// <summary>
    /// Attach debug logging.
    /// </summary>
    void AttachDebugLogging(ILogger logger);

    /// <summary>
    /// Speak text and deliver audio via callback.
    /// </summary>
    Task SpeakAsync(string text, Func<byte[], Task> callback);
}

/// <summary>
/// Factory for creating translator instances.
/// </summary>
public interface ITranslatorFactory
{
    /// <summary>
    /// Create a translator instance for the given language pair.
    /// </summary>
    Task<ITranslator> CreateAsync(LanguageConfig inputLanguage, LanguageConfig outputLanguage);
}
