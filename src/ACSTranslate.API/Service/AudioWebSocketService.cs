using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace ACSTranslate;

public class AudioWebSocketService(
    CallService _calls,
    AzureAISpeechTranslatorFactory _translatorFactory,
    TranscribeService _transcribeService,
    IAudioStorage _audioStorage,
    ILogger<AudioWebSocketService> _logger
)
{
    private readonly ConcurrentDictionary<Guid, AudioWebSocketHandler> _handlers = new();
    public Task ConnectAsync(Guid guid, ConsumerType consumerType, WebSocket ws, CancellationToken cancellationToken)
        => consumerType switch
        {
            ConsumerType.Agent => ConnectAgentAsync(guid, ws, cancellationToken),
            ConsumerType.User => ConnectUserAsync(guid, ws, cancellationToken),
            _ => throw new ArgumentException("Invalid consumer type", nameof(consumerType))
        };
    public async Task ConnectUserAsync(Guid guid, WebSocket ws, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Connecting user to call {CallId}", guid);
        var call = await _calls.SetCallToWaiting(guid);
        var translationConfig = TranslationConfig.GetConfig(call.UserLanguage);
        var transcriptionStreamer = _transcribeService.GetStreamer(guid);
        var handler = _handlers.GetOrAdd(call.Id, _ => new AudioWebSocketHandler(_translatorFactory, translationConfig, _audioStorage.ForCall(guid), _logger));
        try
        {
            _logger.LogInformation("Connecting user to call {CallId}", call.Id);
            await handler.ConnectUser(ws, transcriptionStreamer, translationConfig.WelcomeMessage, translationConfig.ConnectedMessage, cancellationToken);
        }
        finally
        {
            _handlers.Remove(call.Id, out _);
        }
    }
    public async Task ConnectAgentAsync(Guid guid, WebSocket ws, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Connecting agent to call {CallId}", guid);
        var call = await _calls.SetCallToAnswered(guid);
        var transcriptionStreamer = _transcribeService.GetStreamer(guid);
        if (_handlers.TryGetValue(call.Id, out var handler))
        {
            await handler.ConnectAgent(ws, transcriptionStreamer, "You are now connected.", cancellationToken);
        }
        else
        {
            _logger.LogWarning("No handler found for call {CallId}, closing websocket", guid);
            await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Closing", cancellationToken);
        }
    }
}
