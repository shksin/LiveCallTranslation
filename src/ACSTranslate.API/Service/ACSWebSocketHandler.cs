using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ACSTranslate;

/// <summary>
/// Handles the ACS media streaming WebSocket connection.
/// Acts as a relay: pushes caller audio into the ACSCallBridge,
/// and reads translated agent audio from the bridge to send back to ACS.
/// Translation is handled by CallManager when the agent connects.
/// </summary>
public class ACSWebSocketHandler
{
    private readonly CallService _callService;
    private readonly ACSCallBridgeManager _bridgeManager;
    private readonly ILogger<ACSWebSocketHandler> _logger;

    public ACSWebSocketHandler(
        CallService callService,
        ACSCallBridgeManager bridgeManager,
        ILogger<ACSWebSocketHandler> logger)
    {
        _callService = callService;
        _bridgeManager = bridgeManager;
        _logger = logger;
    }

    public async Task HandleACSStreamAsync(Guid callId, WebSocket webSocket, CancellationToken cancellationToken)
    {
        var call = await _callService.GetCallAsync(callId);
        if (call == null)
        {
            _logger.LogWarning("Call {CallId} not found", callId);
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Call not found", cancellationToken);
            return;
        }

        _logger.LogInformation("ACS WebSocket connection established for call {CallId}", callId);

        var bridge = _bridgeManager.GetOrCreate(callId);
        bridge.SetACSWebSocket(webSocket);

        var buffer = new byte[64 * 1024];

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, bridge.DisconnectToken);
            var ct = linkedCts.Token;

            while (webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    ProcessACSMessage(json, bridge);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling ACS WebSocket for call {CallId}", callId);
        }
        finally
        {
            bridge.SignalDisconnect();
            await _callService.SetCallStatusAsync(callId, CallStatus.Ended);
            _bridgeManager.Remove(callId);
            _logger.LogInformation("ACS WebSocket connection closed for call {CallId}", callId);
        }
    }

    private int _audioPacketCount = 0;

    private void ProcessACSMessage(string json, ACSCallBridge bridge)
    {
        try
        {
            var message = JsonSerializer.Deserialize<ACSStreamingMessage>(json);
            if (message?.kind == "AudioData" && message.audioData?.data != null)
            {
                var audioBytes = Convert.FromBase64String(message.audioData.data);
                if (audioBytes.Length > 0)
                {
                    bridge.PushCallerAudio(audioBytes);
                    _audioPacketCount++;
                    if (_audioPacketCount % 100 == 1)
                    {
                        _logger.LogInformation("ACS audio packets received so far: {Count}, last size: {Size} bytes", _audioPacketCount, audioBytes.Length);
                    }
                }
            }
            else if (message?.kind == "AudioMetadata")
            {
                _logger.LogInformation("ACS AudioMetadata received: {Json}", json);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process ACS message");
        }
    }

    private class ACSStreamingMessage
    {
        public string? kind { get; set; }
        public AudioData? audioData { get; set; }
    }

    private class AudioData
    {
        public string? data { get; set; }
    }
}
