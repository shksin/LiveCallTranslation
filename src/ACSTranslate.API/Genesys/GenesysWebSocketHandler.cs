using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ACSTranslate.Genesys;

/// <summary>
/// Handles a Genesys AudioHook v2 WebSocket connection.
/// Implements the AudioHook v2 protocol: open/opened, close/closed, ping/pong, binary audio relay.
/// Mirrors ACSWebSocketHandler but uses µ-law 8kHz ↔ PCM 16kHz conversion.
/// </summary>
public class GenesysWebSocketHandler
{
    private readonly CallService _callService;
    private readonly GenesysCallBridgeManager _bridgeManager;
    private readonly ILogger<GenesysWebSocketHandler> _logger;

    public GenesysWebSocketHandler(
        CallService callService,
        GenesysCallBridgeManager bridgeManager,
        ILogger<GenesysWebSocketHandler> logger)
    {
        _callService = callService;
        _bridgeManager = bridgeManager;
        _logger = logger;
    }

    public async Task HandleGenesysStreamAsync(string sessionId, WebSocket webSocket, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Genesys AudioHook WebSocket connected for session {SessionId}", sessionId);

        var bridge = _bridgeManager.GetOrCreate(sessionId);
        bridge.SetGenesysWebSocket(webSocket);

        var buffer = new byte[64 * 1024];
        Guid? callId = null;
        int audioPacketCount = 0;

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, bridge.DisconnectToken);
            var ct = linkedCts.Token;

            while (webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    // Binary frame = µ-law 8kHz audio from caller
                    var muLawData = new byte[result.Count];
                    Buffer.BlockCopy(buffer, 0, muLawData, 0, result.Count);

                    var pcm16k = MuLawConverter.MuLaw8kToPcm16k(muLawData);
                    bridge.PushCallerAudio(pcm16k);

                    audioPacketCount++;
                    if (audioPacketCount % 100 == 1)
                    {
                        _logger.LogInformation("Genesys audio packets received: {Count}, µ-law size: {MuLawSize}, PCM size: {PcmSize}",
                            audioPacketCount, result.Count, pcm16k.Length);
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    callId = await ProcessTextMessageAsync(sessionId, json, bridge, webSocket, callId, ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling Genesys WebSocket for session {SessionId}", sessionId);
        }
        finally
        {
            bridge.SignalDisconnect();
            if (callId.HasValue)
            {
                await _callService.SetCallStatusAsync(callId.Value, CallStatus.Ended);
            }
            _bridgeManager.Remove(sessionId);
            _logger.LogInformation("Genesys WebSocket closed for session {SessionId}, total audio packets: {Count}", sessionId, audioPacketCount);
        }
    }

    private async Task<Guid?> ProcessTextMessageAsync(string sessionId, string json, GenesysCallBridge bridge, WebSocket webSocket, Guid? callId, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();
            bridge.TrackClientSeq(doc);

            switch (type)
            {
                case "open":
                    _logger.LogInformation("Genesys open message for session {SessionId}: {Json}", sessionId, json);
                    bridge.ProcessOpen(doc);

                    // Create a call record so the agent UI can see it
                    // Language will be selected by the agent from the UI dropdown when connecting
                    var userLanguage = bridge.CallerLanguage ?? "en-US";
                    var call = await CreateGenesysCallAsync(bridge.ConversationId ?? sessionId, userLanguage);
                    callId = call.Id;

                    // Respond with "opened" — select PCMU 8kHz mono
                    var openedParams = new
                    {
                        media = new[]
                        {
                            new { type = "audio", format = "PCMU", channels = new[] { "external" }, rate = 8000 }
                        }
                    };
                    var openedMsg = bridge.CreateServerMessage("opened", openedParams);
                    await SendTextAsync(webSocket, openedMsg, ct);
                    _logger.LogInformation("Sent 'opened' for session {SessionId}, call {CallId}", sessionId, callId);
                    break;

                case "close":
                    _logger.LogInformation("Genesys close for session {SessionId}", sessionId);
                    var closedMsg = bridge.CreateServerMessage("closed");
                    await SendTextAsync(webSocket, closedMsg, ct);
                    bridge.SignalDisconnect();
                    break;

                case "ping":
                    var pongMsg = bridge.CreateServerMessage("pong");
                    await SendTextAsync(webSocket, pongMsg, ct);
                    break;

                case "disconnect":
                    _logger.LogInformation("Genesys disconnect for session {SessionId}", sessionId);
                    bridge.SignalDisconnect();
                    break;

                default:
                    _logger.LogDebug("Genesys unhandled message type '{Type}' for session {SessionId}", type, sessionId);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process Genesys text message for session {SessionId}", sessionId);
        }

        return callId;
    }

    private async Task<Call> CreateGenesysCallAsync(string conversationId, string userLanguage)
    {
        // Create a call record with a "genesys:" prefix context so we can identify Genesys calls
        var call = await _callService.CreateGenesysCallAsync(conversationId, userLanguage);
        _logger.LogInformation("Created Genesys call {CallId} for conversation {ConversationId}", call.Id, conversationId);
        return call;
    }

    private static async Task SendTextAsync(WebSocket ws, string message, CancellationToken ct)
    {
        if (ws.State != WebSocketState.Open) return;
        var bytes = Encoding.UTF8.GetBytes(message);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }
}
