#pragma warning disable OPENAI001, OPENAI002

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;

namespace ACSTranslate.Translation;

/// <summary>
/// Voice Live API translator using direct WebSocket connection.
/// Uses the /voice-live/realtime endpoint for enhanced features:
/// - Server-side echo cancellation
/// - Azure deep noise suppression
/// - Azure Speech voices (high-quality multilingual TTS)
/// - 24kHz PCM16 audio format
/// </summary>
public class VoiceLiveWebSocketTranslator : ITranslator
{
    private const string ApiVersion = "2025-05-01-preview";
    private const string ModelName = "gpt-4o-realtime-preview";

    private readonly AzureOpenAIConfig _config;
    private readonly LanguageConfig _inputLanguage;
    private readonly LanguageConfig _outputLanguage;
    private readonly ILogger? _logger;
    private readonly bool _useTelephonyResampling;
    private readonly bool _useAzureSpeechVoices;

    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;

    private Func<string, string, bool, Task>? _transcribeCallback;
    private Func<byte[], Task>? _speechCallback;
    private readonly StringBuilder _currentTranscript = new();
    private readonly StringBuilder _currentTranslation = new();

    public VoiceLiveWebSocketTranslator(
        AzureOpenAIConfig config,
        LanguageConfig inputLanguage,
        LanguageConfig outputLanguage,
        ILogger? logger = null,
        bool useTelephonyResampling = false,
        bool useAzureSpeechVoices = true)
    {
        _config = config;
        _inputLanguage = inputLanguage;
        _outputLanguage = outputLanguage;
        _logger = logger;
        _useTelephonyResampling = useTelephonyResampling;
        _useAzureSpeechVoices = useAzureSpeechVoices;
    }

    public static async Task<VoiceLiveWebSocketTranslator> CreateAsync(
        AzureOpenAIConfig config,
        LanguageConfig inputLanguage,
        LanguageConfig outputLanguage,
        ILogger? logger = null,
        bool useTelephonyResampling = false,
        bool useAzureSpeechVoices = true)
    {
        var instance = new VoiceLiveWebSocketTranslator(
            config, inputLanguage, outputLanguage, logger, 
            useTelephonyResampling, useAzureSpeechVoices);
        await instance.ConnectAsync();
        return instance;
    }

    private async Task ConnectAsync()
    {
        var endpoint = _config.Endpoint ?? throw new InvalidOperationException("Azure OpenAI endpoint not configured");
        var deployment = _config.DeploymentName ?? "gpt-4o-realtime-preview";

        // Build Voice Live WebSocket URL
        // Format: wss://{resource}.openai.azure.com/openai/realtime?api-version={version}&deployment={deployment}
        var uri = new Uri(endpoint);
        var wsUrl = $"wss://{uri.Host}/openai/realtime?api-version={ApiVersion}&deployment={deployment}";

        _logger?.LogInformation("Connecting to Voice Live API: {Url}", wsUrl);

        _webSocket = new ClientWebSocket();

        // Set authentication header
        if (!string.IsNullOrWhiteSpace(_config.ApiKey))
        {
            _webSocket.Options.SetRequestHeader("api-key", _config.ApiKey);
            _logger?.LogInformation("Using API key authentication");
        }
        else
        {
            // Use Managed Identity
            var credential = new DefaultAzureCredential();
            var tokenRequestContext = new TokenRequestContext(new[] { "https://cognitiveservices.azure.com/.default" });
            var token = await credential.GetTokenAsync(tokenRequestContext);
            _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {token.Token}");
            _logger?.LogInformation("Using Managed Identity authentication");
        }

        await _webSocket.ConnectAsync(new Uri(wsUrl), CancellationToken.None);
        _logger?.LogInformation("WebSocket connected");

        // Configure session
        await ConfigureSessionAsync();

        // Start receiving messages
        _cts = new CancellationTokenSource();
        _receiveTask = Task.Run(() => ReceiveMessagesAsync(_cts.Token));
    }

    private async Task ConfigureSessionAsync()
    {
        if (_webSocket == null) return;

        var instructions = $@"You are a real-time interpreter. Your task is to translate spoken audio from {_inputLanguage.Language} to {_outputLanguage.Language}.

Rules:
1. Translate naturally and conversationally, maintaining the speaker's tone and intent
2. Output ONLY the translation - no explanations, no meta-commentary
3. Preserve emotional nuance and cultural context where possible
4. Handle partial utterances gracefully - translate what you hear
5. If speech is unclear, make your best interpretation rather than asking for clarification
6. Respond quickly with translated audio - prioritize low latency";

        // Build voice configuration
        object voiceConfig;
        if (_useAzureSpeechVoices)
        {
            var azureVoiceName = VoiceLiveTranslator.GetAzureSpeechVoice(_outputLanguage.Code);
            voiceConfig = new { type = "azure-speech", voice = azureVoiceName };
            _logger?.LogInformation("Using Azure Speech voice: {Voice}", azureVoiceName);
        }
        else
        {
            voiceConfig = MapLanguageToVoiceName(_outputLanguage);
            _logger?.LogInformation("Using OpenAI voice: {Voice}", voiceConfig);
        }

        var sessionConfig = new
        {
            type = "session.update",
            session = new
            {
                modalities = new[] { "audio", "text" },
                instructions = instructions,
                voice = voiceConfig,
                input_audio_format = "pcm16",
                output_audio_format = "pcm16",
                input_audio_transcription = new
                {
                    model = "whisper-1"
                },
                turn_detection = new
                {
                    type = "server_vad",
                    threshold = 0.5,
                    prefix_padding_ms = 300,
                    silence_duration_ms = 700
                },
                // Voice Live API enhanced features
                input_audio_noise_reduction = new
                {
                    type = "near_field"
                },
                // Note: Echo cancellation requires specific telephony setup
                // input_audio_echo_cancellation = new { type = "server_echo_cancellation" }
            }
        };

        var json = JsonSerializer.Serialize(sessionConfig, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        _logger?.LogDebug("Sending session config: {Config}", json);
        await SendMessageAsync(json);
    }

    private static string MapLanguageToVoiceName(LanguageConfig language)
    {
        return language.Code.ToLower() switch
        {
            var code when code.StartsWith("en") => "alloy",
            var code when code.StartsWith("es") => "alloy",
            var code when code.StartsWith("fr") => "alloy",
            var code when code.StartsWith("de") => "echo",
            var code when code.StartsWith("it") => "alloy",
            var code when code.StartsWith("pt") => "alloy",
            var code when code.StartsWith("ja") => "shimmer",
            var code when code.StartsWith("ko") => "shimmer",
            var code when code.StartsWith("zh") => "shimmer",
            var code when code.StartsWith("hi") => "shimmer",
            var code when code.StartsWith("ar") => "echo",
            _ => "alloy"
        };
    }

    private async Task SendMessageAsync(string message)
    {
        if (_webSocket?.State != WebSocketState.Open) return;

        var bytes = Encoding.UTF8.GetBytes(message);
        await _webSocket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None);
    }

    private async Task ReceiveMessagesAsync(CancellationToken ct)
    {
        if (_webSocket == null) return;

        var buffer = new byte[64 * 1024]; // 64KB buffer
        var messageBuilder = new StringBuilder();

        try
        {
            while (!ct.IsCancellationRequested && _webSocket.State == WebSocketState.Open)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger?.LogInformation("WebSocket closed by server");
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                    if (result.EndOfMessage)
                    {
                        var message = messageBuilder.ToString();
                        messageBuilder.Clear();
                        await ProcessMessageAsync(message);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger?.LogDebug("Receive loop cancelled");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in WebSocket receive loop");
        }
    }

    private async Task ProcessMessageAsync(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeElement))
                return;

            var type = typeElement.GetString();
            _logger?.LogTrace("Received message type: {Type}", type);

            switch (type)
            {
                case "session.created":
                    _logger?.LogInformation("Voice Live session created");
                    break;

                case "session.updated":
                    _logger?.LogInformation("Voice Live session configured");
                    break;

                case "input_audio_buffer.speech_started":
                    _logger?.LogTrace("Speech started");
                    break;

                case "input_audio_buffer.speech_stopped":
                    _logger?.LogTrace("Speech stopped");
                    break;

                case "conversation.item.input_audio_transcription.completed":
                    if (root.TryGetProperty("transcript", out var transcriptElement))
                    {
                        var transcript = transcriptElement.GetString() ?? "";
                        _currentTranscript.Clear();
                        _currentTranscript.Append(transcript);
                        _logger?.LogTrace("Input transcript: {Text}", transcript);
                    }
                    break;

                case "response.audio_transcript.delta":
                    if (root.TryGetProperty("delta", out var deltaElement))
                    {
                        var delta = deltaElement.GetString() ?? "";
                        _currentTranslation.Append(delta);

                        if (_transcribeCallback != null)
                        {
                            await _transcribeCallback(
                                _currentTranscript.ToString(),
                                _currentTranslation.ToString(),
                                false);
                        }
                    }
                    break;

                case "response.audio_transcript.done":
                    if (_transcribeCallback != null)
                    {
                        await _transcribeCallback(
                            _currentTranscript.ToString(),
                            _currentTranslation.ToString(),
                            true);
                    }
                    _currentTranslation.Clear();
                    break;

                case "response.audio.delta":
                    if (root.TryGetProperty("delta", out var audioDeltaElement))
                    {
                        var base64Audio = audioDeltaElement.GetString();
                        if (!string.IsNullOrEmpty(base64Audio) && _speechCallback != null)
                        {
                            var audioBytes = Convert.FromBase64String(base64Audio);

                            // Convert to telephony format if needed
                            if (_useTelephonyResampling && audioBytes.Length > 0)
                            {
                                audioBytes = AudioResampler.ConvertVoiceLiveToTelephony(audioBytes);
                            }

                            await _speechCallback(audioBytes);
                        }
                    }
                    break;

                case "response.done":
                    _logger?.LogTrace("Response complete");
                    _currentTranscript.Clear();
                    break;

                case "error":
                    if (root.TryGetProperty("error", out var errorElement))
                    {
                        var errorMsg = errorElement.TryGetProperty("message", out var msgEl)
                            ? msgEl.GetString()
                            : "Unknown error";
                        _logger?.LogError("Voice Live API error: {Error}", errorMsg);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing WebSocket message");
        }
    }

    public void SendData(byte[] data)
    {
        if (_webSocket?.State != WebSocketState.Open) return;

        try
        {
            byte[] audioToSend = data;

            // Convert telephony audio to Voice Live format if needed
            if (_useTelephonyResampling && data.Length > 0)
            {
                audioToSend = AudioResampler.ConvertTelephonyToVoiceLive(data);
                _logger?.LogTrace("Resampled {Original} bytes to {Resampled} bytes", data.Length, audioToSend.Length);
            }

            // Send as input_audio_buffer.append message
            var message = new
            {
                type = "input_audio_buffer.append",
                audio = Convert.ToBase64String(audioToSend)
            };

            var json = JsonSerializer.Serialize(message);
            _ = SendMessageAsync(json);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error sending audio data");
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
        // Logger already attached via constructor
    }

    public async Task SpeakAsync(string text, Func<byte[], Task> callback)
    {
        if (_webSocket?.State != WebSocketState.Open) return;

        try
        {
            var previousCallback = _speechCallback;
            _speechCallback = callback;

            // Create a conversation item with text
            var createItem = new
            {
                type = "conversation.item.create",
                item = new
                {
                    type = "message",
                    role = "user",
                    content = new[]
                    {
                        new
                        {
                            type = "input_text",
                            text = $"[Announce in {_outputLanguage.Language}]: {text}"
                        }
                    }
                }
            };

            await SendMessageAsync(JsonSerializer.Serialize(createItem));

            // Trigger response
            var createResponse = new { type = "response.create" };
            await SendMessageAsync(JsonSerializer.Serialize(createResponse));

            // Wait for audio generation
            await Task.Delay(2000);

            _speechCallback = previousCallback;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in SpeakAsync");
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

        if (_webSocket?.State == WebSocketState.Open)
        {
            try
            {
                _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disposing", CancellationToken.None)
                    .Wait(TimeSpan.FromSeconds(2));
            }
            catch { }
        }

        _webSocket?.Dispose();
        _cts?.Dispose();

        _logger?.LogInformation("Voice Live WebSocket session disposed");
    }
}

/// <summary>
/// Factory for creating Voice Live WebSocket-based translators.
/// </summary>
public class VoiceLiveWebSocketTranslatorFactory : ITranslatorFactory
{
    private readonly AzureOpenAIConfig _config;
    private readonly ILoggerFactory _loggerFactory;
    private readonly bool _useTelephonyResampling;
    private readonly bool _useAzureSpeechVoices;

    public VoiceLiveWebSocketTranslatorFactory(
        AzureOpenAIConfig config,
        ILoggerFactory loggerFactory,
        bool useTelephonyResampling = false,
        bool useAzureSpeechVoices = true)
    {
        _config = config;
        _loggerFactory = loggerFactory;
        _useTelephonyResampling = useTelephonyResampling;
        _useAzureSpeechVoices = useAzureSpeechVoices;
    }

    public Translator Mode => Translator.VoiceLive;

    public async Task<ITranslator> CreateAsync(LanguageConfig inputLanguage, LanguageConfig outputLanguage)
    {
        var logger = _loggerFactory.CreateLogger<VoiceLiveWebSocketTranslator>();
        return await VoiceLiveWebSocketTranslator.CreateAsync(
            _config, inputLanguage, outputLanguage, logger,
            _useTelephonyResampling, _useAzureSpeechVoices);
    }
}
