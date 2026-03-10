#pragma warning disable OPENAI001, OPENAI002 // Suppress experimental API warnings

using System.ClientModel;
using System.Text;
using Azure.AI.OpenAI;
using Azure.Identity;
using OpenAI.RealtimeConversation;

namespace ACSTranslate.Translation;

/// <summary>
/// GPT-4o Realtime API based translator implementation (Voice Live mode).
/// Uses single model for end-to-end audio translation with server-side VAD.
/// Provides lower latency compared to the 3-stage Speech SDK approach.
/// 
/// Voice Live API Enhancements:
/// - Server-side echo cancellation and noise reduction (when supported by API)
/// - Audio resampling for telephony compatibility (8kHz µ-law ↔ 24kHz PCM16)
/// - Azure Speech voice support for high-quality multilingual TTS
/// </summary>
public class VoiceLiveTranslator : ITranslator
{
    private readonly AzureOpenAIConfig _config;
    private readonly LanguageConfig _inputLanguage;
    private readonly LanguageConfig _outputLanguage;
    private readonly ILogger? _logger;
    
    // Audio processing options
    private readonly bool _useTelephonyResampling;
    
    private RealtimeConversationClient? _client;
    private RealtimeConversationSession? _session;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    
    private Func<string, string, bool, Task>? _transcribeCallback;
    private Func<byte[], Task>? _speechCallback;
    private readonly StringBuilder _currentTranscript = new();
    private readonly StringBuilder _currentTranslation = new();

    public VoiceLiveTranslator(
        AzureOpenAIConfig config,
        LanguageConfig inputLanguage,
        LanguageConfig outputLanguage,
        ILogger? logger = null,
        bool useTelephonyResampling = false)
    {
        _config = config;
        _inputLanguage = inputLanguage;
        _outputLanguage = outputLanguage;
        _logger = logger;
        _useTelephonyResampling = useTelephonyResampling;
    }

    public static async Task<VoiceLiveTranslator> CreateAsync(
        AzureOpenAIConfig config,
        LanguageConfig inputLanguage,
        LanguageConfig outputLanguage,
        ILogger? logger = null,
        bool useTelephonyResampling = false)
    {
        var instance = new VoiceLiveTranslator(config, inputLanguage, outputLanguage, logger, useTelephonyResampling);
        await instance.StartAsync();
        return instance;
    }

    private async Task StartAsync()
    {
        // Create Azure OpenAI client for Realtime API
        // Use Managed Identity (DefaultAzureCredential) if API key is not provided
        AzureOpenAIClient azureClient;
        var endpoint = new Uri(_config.Endpoint ?? throw new InvalidOperationException("Azure OpenAI endpoint not configured"));
        
        if (!string.IsNullOrWhiteSpace(_config.ApiKey))
        {
            _logger?.LogInformation("Using API key authentication for Azure OpenAI");
            var credential = new ApiKeyCredential(_config.ApiKey);
            azureClient = new AzureOpenAIClient(endpoint, credential);
        }
        else
        {
            _logger?.LogInformation("Using Managed Identity (DefaultAzureCredential) for Azure OpenAI");
            azureClient = new AzureOpenAIClient(endpoint, new DefaultAzureCredential());
        }
        
        _client = azureClient.GetRealtimeConversationClient(_config.DeploymentName ?? "gpt-4o-realtime-preview");
        _session = await _client.StartConversationSessionAsync();

        // Configure the session for translation
        var instructions = $@"You are a real-time interpreter. Your task is to translate spoken audio from {_inputLanguage.Language} to {_outputLanguage.Language}.

Rules:
1. Translate naturally and conversationally, maintaining the speaker's tone and intent
2. Output ONLY the translation - no explanations, no meta-commentary
3. Preserve emotional nuance and cultural context where possible
4. Handle partial utterances gracefully - translate what you hear
5. If speech is unclear, make your best interpretation rather than asking for clarification
6. Respond quickly with translated audio - prioritize low latency";

        await _session.ConfigureSessionAsync(new ConversationSessionOptions
        {
            Instructions = instructions,
            Voice = MapLanguageToVoice(_outputLanguage),
            InputAudioFormat = ConversationAudioFormat.Pcm16,
            OutputAudioFormat = ConversationAudioFormat.Pcm16,
            InputTranscriptionOptions = new ConversationInputTranscriptionOptions
            {
                Model = "whisper-1"
            },
            // Voice Live API recommended settings:
            // - threshold: 0.5 (balanced sensitivity)
            // - silence duration: 700ms (allows for natural pauses in speech)
            // - prefix padding: 300ms (captures speech onset)
            TurnDetectionOptions = ConversationTurnDetectionOptions.CreateServerVoiceActivityTurnDetectionOptions(
                detectionThreshold: 0.5f,
                prefixPaddingDuration: TimeSpan.FromMilliseconds(300),
                silenceDuration: TimeSpan.FromMilliseconds(700)  // Match Voice Live API recommendation
            )
        });

        // Start receiving responses
        _cts = new CancellationTokenSource();
        _receiveTask = Task.Run(() => ReceiveResponsesAsync(_cts.Token));
        
        _logger?.LogInformation("GPT-4o Realtime session started for {Input} → {Output}", 
            _inputLanguage.Language, _outputLanguage.Language);
    }

    /// <summary>
    /// Maps language to GPT-4o Realtime voice.
    /// Note: For higher quality multilingual TTS, consider using Azure Speech voices
    /// via the Voice Live API's azure-standard voice type (requires direct WebSocket API).
    /// Example: { type: 'azure-standard', name: 'es-ES-Ximena:DragonHDLatestNeural' }
    /// </summary>
    private static ConversationVoice MapLanguageToVoice(LanguageConfig language)
    {
        // Map language to appropriate GPT-4o Realtime voice
        // Available SDK voices: alloy, echo, shimmer
        // Voice characteristics:
        // - Alloy: Neutral, versatile (good default for most languages)
        // - Echo: Deep, authoritative (good for formal/professional contexts)
        // - Shimmer: Bright, energetic (good for Asian languages)
        return language.Code.ToLower() switch
        {
            var code when code.StartsWith("en") => ConversationVoice.Alloy,   // Neutral for English
            var code when code.StartsWith("es") => ConversationVoice.Alloy,   // Warm for Spanish
            var code when code.StartsWith("fr") => ConversationVoice.Alloy,   // Elegant for French
            var code when code.StartsWith("de") => ConversationVoice.Echo,    // Authoritative for German
            var code when code.StartsWith("it") => ConversationVoice.Alloy,   // Warm for Italian
            var code when code.StartsWith("pt") => ConversationVoice.Alloy,   // Clear for Portuguese
            var code when code.StartsWith("ja") => ConversationVoice.Shimmer, // Polite for Japanese
            var code when code.StartsWith("ko") => ConversationVoice.Shimmer, // Polite for Korean
            var code when code.StartsWith("zh") => ConversationVoice.Shimmer, // Measured for Chinese
            var code when code.StartsWith("hi") => ConversationVoice.Shimmer, // Warm for Hindi
            var code when code.StartsWith("ar") => ConversationVoice.Echo,    // Formal for Arabic
            var code when code.StartsWith("ru") => ConversationVoice.Echo,    // Deep for Russian
            var code when code.StartsWith("nl") => ConversationVoice.Alloy,   // Clear for Dutch
            var code when code.StartsWith("pl") => ConversationVoice.Alloy,   // Calm for Polish
            var code when code.StartsWith("tr") => ConversationVoice.Alloy,   // Warm for Turkish
            var code when code.StartsWith("vi") => ConversationVoice.Shimmer, // Bright for Vietnamese
            var code when code.StartsWith("th") => ConversationVoice.Shimmer, // Polite for Thai
            _ => ConversationVoice.Alloy  // Default neutral voice
        };
    }

    /// <summary>
    /// Gets the recommended Azure Speech voice name for a language.
    /// Use with Voice Live API's azure-standard voice type for higher quality.
    /// </summary>
    public static string GetAzureSpeechVoice(string languageCode)
    {
        return languageCode.ToLower() switch
        {
            var code when code.StartsWith("en-us") => "en-US-JennyMultilingualNeural",
            var code when code.StartsWith("en-gb") => "en-GB-SoniaNeural",
            var code when code.StartsWith("en") => "en-US-AriaNeural",
            var code when code.StartsWith("es-mx") => "es-MX-DaliaNeural",
            var code when code.StartsWith("es") => "es-ES-ElviraNeural",
            var code when code.StartsWith("fr-ca") => "fr-CA-SylvieNeural",
            var code when code.StartsWith("fr") => "fr-FR-DeniseNeural",
            var code when code.StartsWith("de") => "de-DE-KatjaNeural",
            var code when code.StartsWith("it") => "it-IT-ElsaNeural",
            var code when code.StartsWith("pt-br") => "pt-BR-FranciscaNeural",
            var code when code.StartsWith("pt") => "pt-PT-RaquelNeural",
            var code when code.StartsWith("ja") => "ja-JP-NanamiNeural",
            var code when code.StartsWith("ko") => "ko-KR-SunHiNeural",
            var code when code.StartsWith("zh-cn") => "zh-CN-XiaoxiaoNeural",
            var code when code.StartsWith("zh-tw") => "zh-TW-HsiaoChenNeural",
            var code when code.StartsWith("zh") => "zh-CN-XiaoxiaoNeural",
            var code when code.StartsWith("hi") => "hi-IN-SwaraNeural",
            var code when code.StartsWith("ar") => "ar-SA-ZariyahNeural",
            var code when code.StartsWith("ru") => "ru-RU-DariyaNeural",
            var code when code.StartsWith("nl") => "nl-NL-ColetteNeural",
            var code when code.StartsWith("pl") => "pl-PL-AgnieszkaNeural",
            var code when code.StartsWith("tr") => "tr-TR-EmelNeural",
            var code when code.StartsWith("vi") => "vi-VN-HoaiMyNeural",
            var code when code.StartsWith("th") => "th-TH-PremwadeeNeural",
            _ => "en-US-JennyMultilingualNeural"  // Multilingual fallback
        };
    }

    private async Task ReceiveResponsesAsync(CancellationToken ct)
    {
        if (_session == null) return;

        try
        {
            await foreach (var update in _session.ReceiveUpdatesAsync(ct))
            {
                switch (update)
                {
                    case ConversationInputTranscriptionFinishedUpdate transcriptUpdate:
                        // Original speech transcription
                        var originalText = transcriptUpdate.Transcript;
                        _currentTranscript.Clear();
                        _currentTranscript.Append(originalText);
                        _logger?.LogTrace("Input transcript: {Text}", originalText);
                        break;

                    case ConversationItemStreamingAudioTranscriptionFinishedUpdate transcriptionFinished:
                        // Final translation text from output audio transcription
                        _currentTranslation.Clear();
                        _currentTranslation.Append(transcriptionFinished.Transcript);
                        if (_transcribeCallback != null)
                        {
                            await _transcribeCallback(
                                _currentTranscript.ToString(),
                                _currentTranslation.ToString(),
                                true);
                        }
                        _currentTranslation.Clear();
                        break;

                    case ConversationItemStreamingPartDeltaUpdate audioDelta:
                        // Translated audio chunk
                        if (audioDelta.AudioBytes != null && audioDelta.AudioBytes.ToArray().Length > 0 && _speechCallback != null)
                        {
                            var audioData = audioDelta.AudioBytes.ToArray();
                            
                            // If telephony resampling is enabled, convert 24kHz PCM16 back to 8kHz µ-law
                            if (_useTelephonyResampling && audioData.Length > 0)
                            {
                                audioData = AudioResampler.ConvertVoiceLiveToTelephony(audioData);
                            }
                            
                            await _speechCallback(audioData);
                        }
                        // Also handle text delta for intermediate translation display
                        if (!string.IsNullOrEmpty(audioDelta.AudioTranscript))
                        {
                            _currentTranslation.Append(audioDelta.AudioTranscript);
                            if (_transcribeCallback != null)
                            {
                                await _transcribeCallback(
                                    _currentTranscript.ToString(),
                                    _currentTranslation.ToString(),
                                    false);
                            }
                        }
                        break;

                    case ConversationResponseFinishedUpdate responseFinished:
                        _logger?.LogTrace("Response complete");
                        _currentTranscript.Clear();
                        break;

                    case ConversationErrorUpdate errorUpdate:
                        _logger?.LogError("GPT-4o Realtime error: {Error}", errorUpdate.Message);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger?.LogDebug("Receive loop cancelled");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in GPT-4o Realtime receive loop");
        }
    }

    public void SendData(byte[] data)
    {
        if (_session == null) return;

        try
        {
            byte[] audioToSend = data;
            
            // If telephony resampling is enabled, convert 8kHz µ-law to 24kHz PCM16
            // Voice Live API expects 24kHz PCM16 audio for optimal quality
            if (_useTelephonyResampling && data.Length > 0)
            {
                audioToSend = AudioResampler.ConvertTelephonyToVoiceLive(data);
                _logger?.LogTrace("Resampled {Original} bytes to {Resampled} bytes (8kHz µ-law → 24kHz PCM16)", 
                    data.Length, audioToSend.Length);
            }
            
            // Send audio data to the realtime session
            _session.SendInputAudio(new BinaryData(audioToSend));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error sending audio data to GPT-4o Realtime");
        }
    }

    public void AttachTranscribeOutput(Func<string, string, bool, Task> callback, string? translationShortCode = null)
    {
        _transcribeCallback = callback;
    }

    public void AttachSpeechOutput(Func<byte[], Task> callback, string? translationShortCode = null)
    {
        _speechCallback = callback;
    }

    public void AttachDebugLogging(ILogger logger)
    {
        // Logger is already attached via constructor
    }

    public async Task SpeakAsync(string text, Func<byte[], Task> callback)
    {
        if (_session == null) return;

        try
        {
            // For direct TTS requests, we can use the conversation to generate speech
            // This is mainly used for system messages like "connected" announcements
            var previousCallback = _speechCallback;
            _speechCallback = callback;

            // Send a text item for the model to respond to
            var item = ConversationItem.CreateUserMessage(new[] { 
                ConversationContentPart.CreateInputTextPart($"[Announce in {_outputLanguage.Language}]: {text}") 
            });
            await _session.AddItemAsync(item);
            await _session.StartResponseAsync();

            // Wait briefly for audio to be generated
            await Task.Delay(2000);
            
            _speechCallback = previousCallback;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in SpeakAsync for GPT-4o Realtime");
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        
        try
        {
            _receiveTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch { }

        _session?.Dispose();
        _cts?.Dispose();
        
        _logger?.LogInformation("GPT-4o Realtime session disposed");
    }
}

/// <summary>
/// Factory for creating GPT-4o Realtime based translators.
/// </summary>
public class VoiceLiveTranslatorFactory : ITranslatorFactory
{
    private readonly AzureOpenAIConfig _config;
    private readonly ILoggerFactory _loggerFactory;

    public VoiceLiveTranslatorFactory(AzureOpenAIConfig config, ILoggerFactory loggerFactory)
    {
        _config = config;
        _loggerFactory = loggerFactory;
    }

    public Translator Mode => Translator.VoiceLive;

    public async Task<ITranslator> CreateAsync(LanguageConfig inputLanguage, LanguageConfig outputLanguage)
    {
        var logger = _loggerFactory.CreateLogger<VoiceLiveTranslator>();
        return await VoiceLiveTranslator.CreateAsync(_config, inputLanguage, outputLanguage, logger);
    }
}
