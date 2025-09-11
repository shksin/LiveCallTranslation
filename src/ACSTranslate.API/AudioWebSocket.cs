using System.Collections.ObjectModel;
using System.Net.WebSockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

public class AudioWebSocket(
    WebSocket _webSocket
)
{
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.MathematicalOperators),
        WriteIndented = true
    };

    public bool Connected => _webSocket.State == WebSocketState.Open;

    public async Task SendErrorAsync(string message, CancellationToken ct)
        => await SendAsync(new ErrorWebSocketMessage(message), ct);

    public async Task SendEnableAsync(CancellationToken ct)
        => await SendAsync(WebSocketMessage.Enable, ct);

    public async Task SendAudioAsync(byte[] data, CancellationToken ct)
        => await SendAudioAsync(data, 0, data.Length, ct);

    public async Task SendAudioAsync(byte[] data, int offset, int count, CancellationToken ct)
        => await SendAsync(new AudioWebSocketMessage(data, offset, count), ct);

    public async Task SendDisconnectAsync(CancellationToken ct)
        => await BestEffortSendAsync(WebSocketMessage.Disconnect, ct);

    protected private async Task SendAsync<T>(T obj, CancellationToken ct)
        => await _webSocket.SendAsync(
            new ArraySegment<byte>(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(obj, _jsonSerializerOptions))),
            WebSocketMessageType.Text,
            true,
            ct);

    private async Task BestEffortSendAsync<T>(T obj, CancellationToken ct)
    {
        try
        {
            await SendAsync(obj, ct);
        }
        catch (Exception)
        {
            // Ignore any errors sending best-effort messages
        }
    }

    public async Task<string?> ReceiveStringAsync(byte[] buffer, CancellationToken ct)
    {
        StringBuilder stringBuilder = new();
        while (true)
        {
            var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType == WebSocketMessageType.Text)
                stringBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (result.EndOfMessage) break;
        }
        if (stringBuilder.Length == 0) return string.Empty;
        return stringBuilder.ToString();
    }

    private record WebSocketMessage(
        [property: JsonPropertyName("type")]
        string Type
    )
    {
        public static WebSocketMessage Enable { get; } = new("enable");
        public static WebSocketMessage Disconnect { get; } = new("disconnect");
    }
    private record ErrorWebSocketMessage(
        [property: JsonPropertyName("message")]
        string Message
    ) : WebSocketMessage("error");
    private record AudioWebSocketMessage(
        [property: JsonPropertyName("data")]
        string Data
    ) : WebSocketMessage("audio")
    {
        public AudioWebSocketMessage(byte[] data, int offset, int count)
            : this(Convert.ToBase64String(data, offset, count))
        { }
    }
}

public class AgentWebSocket(WebSocket ws) : AudioWebSocket(ws)
{
    public Task SendEventUpdates(CallEventUpdate callUpdate, CancellationToken ct)
        => SendEventUpdates([callUpdate], ct);
    public async Task SendEventUpdates(IEnumerable<CallEventUpdate> callUpdates, CancellationToken ct)
        => await SendAsync(new
        {
            type = "update",
            updates = callUpdates
        }, ct);

    public async Task SendLanguagesAsync(ReadOnlyDictionary<string, string> languages, CancellationToken ct)
        => await SendAsync(new
        {
            type = "languages",
            languages = languages
        }, ct);

    public async Task SendTranscriptionAsync(string source, string originalText, string translatedText, bool isFinal, CancellationToken ct)
        => await SendAsync(new
        {
            type = "transcription",
            source,
            originalText,
            translatedText,
            isFinal
        }, ct);
}
public class UserWebSocket : AudioWebSocket
{
    public UserWebSocket(WebSocket ws) : base(ws) { }

    public async Task SendRefreshAsync(CancellationToken ct)
        => await SendAsync(new { type = "refresh" }, ct);
}