using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace ACSTranslate.Genesys;

/// <summary>
/// Bridges audio between a Genesys AudioHook v2 WebSocket session and the agent-side translation pipeline.
/// Mirrors ACSCallBridge but handles µ-law 8kHz ↔ PCM 16kHz conversion.
/// </summary>
public class GenesysCallBridge : IDisposable
{
    private readonly Channel<byte[]> _callerAudioToAgent = Channel.CreateBounded<byte[]>(200);
    private readonly CancellationTokenSource _disconnectCts = new();
    private WebSocket? _genesysWebSocket;

    // AudioHook protocol state
    private string _sessionId = "";
    private int _lastServerSeq;
    private int _lastClientSeq;

    /// <summary>Reader for caller audio (already converted to PCM 16kHz) — consumed by the translation pipeline.</summary>
    public ChannelReader<byte[]> CallerAudioReader => _callerAudioToAgent.Reader;

    /// <summary>Token cancelled when either side disconnects.</summary>
    public CancellationToken DisconnectToken => _disconnectCts.Token;

    /// <summary>Conversation ID from the Genesys open message.</summary>
    public string? ConversationId { get; private set; }

    /// <summary>Caller language hint from Genesys input variables.</summary>
    public string? CallerLanguage { get; private set; }

    /// <summary>Called by GenesysWebSocketHandler to register the Genesys WebSocket.</summary>
    public void SetGenesysWebSocket(WebSocket ws) => _genesysWebSocket = ws;

    /// <summary>Called when caller audio arrives (already converted to PCM 16kHz 16-bit mono).</summary>
    public void PushCallerAudio(byte[] pcm16kAudio)
        => _callerAudioToAgent.Writer.TryWrite(pcm16kAudio);

    /// <summary>
    /// Send translated audio back to the Genesys caller.
    /// Converts PCM 16kHz → µ-law 8kHz and sends as binary WebSocket message.
    /// </summary>
    public async Task SendToCallerAsync(byte[] pcm16kAudio, CancellationToken ct)
    {
        var ws = _genesysWebSocket;
        if (ws == null || ws.State != WebSocketState.Open) return;

        var muLawData = MuLawConverter.Pcm16kToMuLaw8k(pcm16kAudio);
        if (muLawData.Length > 0)
        {
            await ws.SendAsync(new ArraySegment<byte>(muLawData), WebSocketMessageType.Binary, true, ct);
        }
    }

    /// <summary>
    /// Process an AudioHook v2 "open" message. Responds with "opened".
    /// </summary>
    public void ProcessOpen(JsonDocument doc)
    {
        var root = doc.RootElement;
        _sessionId = root.GetProperty("id").GetString() ?? "";
        _lastClientSeq = root.GetProperty("seq").GetInt32();

        if (root.TryGetProperty("parameters", out var parameters))
        {
            if (parameters.TryGetProperty("conversationId", out var convId))
                ConversationId = convId.GetString();

            if (parameters.TryGetProperty("inputVariables", out var inputVars))
            {
                if (inputVars.TryGetProperty("language", out var lang))
                    CallerLanguage = lang.GetString();
            }
        }
    }

    /// <summary>Build an AudioHook v2 server message.</summary>
    public string CreateServerMessage(string type, object? parameters = null)
    {
        var msg = new Dictionary<string, object?>
        {
            ["version"] = "2",
            ["id"] = _sessionId,
            ["type"] = type,
            ["seq"] = ++_lastServerSeq,
            ["clientseq"] = _lastClientSeq,
            ["parameters"] = parameters ?? new { }
        };
        return JsonSerializer.Serialize(msg);
    }

    /// <summary>Update client seq tracking when a text message is received.</summary>
    public void TrackClientSeq(JsonDocument doc)
    {
        if (doc.RootElement.TryGetProperty("seq", out var seq))
            _lastClientSeq = seq.GetInt32();
    }

    /// <summary>Signal that the call is over.</summary>
    public void SignalDisconnect()
    {
        _callerAudioToAgent.Writer.TryComplete();
        try { _disconnectCts.Cancel(); } catch { }
    }

    public void Dispose()
    {
        SignalDisconnect();
        _disconnectCts.Dispose();
    }
}

/// <summary>
/// Manages GenesysCallBridge instances keyed by session ID.
/// Registered as a singleton so both GenesysWebSocketHandler and CallManager can access the same bridges.
/// </summary>
public class GenesysCallBridgeManager
{
    private readonly ConcurrentDictionary<string, GenesysCallBridge> _bridges = new();

    public GenesysCallBridge GetOrCreate(string sessionId)
        => _bridges.GetOrAdd(sessionId, _ => new GenesysCallBridge());

    public bool TryGet(string sessionId, out GenesysCallBridge? bridge)
        => _bridges.TryGetValue(sessionId, out bridge);

    public GenesysCallBridge? FindByConversationId(string conversationId)
        => _bridges.Values.FirstOrDefault(b => b.ConversationId == conversationId);

    public void Remove(string sessionId)
    {
        if (_bridges.TryRemove(sessionId, out var bridge))
            bridge.Dispose();
    }
}
