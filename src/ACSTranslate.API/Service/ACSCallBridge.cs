using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace ACSTranslate;

/// <summary>
/// Bridges audio between an ACS media streaming WebSocket and the agent-side translation pipeline.
/// </summary>
public class ACSCallBridge : IDisposable
{
    private readonly Channel<byte[]> _callerAudioToAgent = Channel.CreateBounded<byte[]>(200);
    private readonly CancellationTokenSource _disconnectCts = new();
    private WebSocket? _acsWebSocket;

    /// <summary>Reader for caller audio — consumed by the agent-side translation pipeline.</summary>
    public ChannelReader<byte[]> CallerAudioReader => _callerAudioToAgent.Reader;

    /// <summary>Token cancelled when either side disconnects.</summary>
    public CancellationToken DisconnectToken => _disconnectCts.Token;

    /// <summary>Called by ACSWebSocketHandler to register the ACS WebSocket.</summary>
    public void SetACSWebSocket(WebSocket ws) => _acsWebSocket = ws;

    /// <summary>Called by ACSWebSocketHandler when caller audio arrives from ACS.</summary>
    public void PushCallerAudio(byte[] audio)
        => _callerAudioToAgent.Writer.TryWrite(audio);

    /// <summary>Called by the agent-side pipeline to send translated audio back to the ACS caller.</summary>
    public async Task SendToCallerAsync(byte[] translatedAudio, CancellationToken ct)
    {
        var ws = _acsWebSocket;
        if (ws == null || ws.State != WebSocketState.Open) return;

        var response = new
        {
            kind = "AudioData",
            audioData = new { data = Convert.ToBase64String(translatedAudio) }
        };
        var json = JsonSerializer.Serialize(response);
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
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
/// Manages ACSCallBridge instances keyed by call ID.
/// Registered as a singleton so both ACSWebSocketHandler and CallManager can access the same bridges.
/// </summary>
public class ACSCallBridgeManager
{
    private readonly ConcurrentDictionary<Guid, ACSCallBridge> _bridges = new();

    public ACSCallBridge GetOrCreate(Guid callId)
        => _bridges.GetOrAdd(callId, _ => new ACSCallBridge());

    public bool TryGet(Guid callId, out ACSCallBridge? bridge)
        => _bridges.TryGetValue(callId, out bridge);

    public void Remove(Guid callId)
    {
        if (_bridges.TryRemove(callId, out var bridge))
            bridge.Dispose();
    }
}
