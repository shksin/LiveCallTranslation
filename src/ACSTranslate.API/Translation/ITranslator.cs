namespace ACSTranslate.Translation;

/// <summary>
/// Interface for real-time translation implementations.
/// Supports both Azure Speech SDK and GPT-4o Realtime approaches.
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
/// Factory for creating translator instances based on configuration.
/// </summary>
public interface ITranslatorFactory
{
    /// <summary>
    /// Create a translator instance for the given language pair.
    /// </summary>
    Task<ITranslator> CreateAsync(LanguageConfig inputLanguage, LanguageConfig outputLanguage);
    
    /// <summary>
    /// The translator type being used by this factory.
    /// </summary>
    Translator Mode { get; }
}

/// <summary>
/// Provides access to all configured translator factories, allowing per-call mode selection.
/// </summary>
public class TranslatorFactoryProvider
{
    private readonly Dictionary<Translator, ITranslatorFactory> _factories = new();
    private readonly Translator _defaultMode;

    public TranslatorFactoryProvider(Translator defaultMode)
    {
        _defaultMode = defaultMode;
    }

    public void Register(ITranslatorFactory factory)
        => _factories[factory.Mode] = factory;

    public ITranslatorFactory GetFactory(Translator? mode = null)
        => _factories.TryGetValue(mode ?? _defaultMode, out var f) ? f : _factories[_defaultMode];

    public ITranslatorFactory GetFactory(string? modeName)
    {
        if (!string.IsNullOrEmpty(modeName) && Enum.TryParse<Translator>(modeName, ignoreCase: true, out var mode))
            return GetFactory(mode);
        return GetFactory();
    }

    public IReadOnlyList<string> AvailableModes => _factories.Keys.Select(k => k.ToString()).ToList();

    public string DefaultMode => _defaultMode.ToString();
}
