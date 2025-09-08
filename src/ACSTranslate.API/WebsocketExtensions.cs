using System.Net.WebSockets;
using System.Text;

public static class WebsocketExtensions
{
    public static async Task SendAsync<T>(this WebSocket ws, T obj, CancellationToken ct)
        => await ws.SendAsync(
            new ArraySegment<byte>(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(obj))),
            WebSocketMessageType.Text,
            true,
            ct);
    public static async Task BestEffortSendAsync<T>(this WebSocket ws, T obj, CancellationToken ct)
    {
        try
        {
            await ws.SendAsync(obj, ct);
        }
        catch (Exception)
        {
            // Ignore any errors sending best-effort messages
        }
    }
    public static async Task<string?> ReceiveStringAsync(this WebSocket ws, byte[] buffer, CancellationToken ct)
    {
        StringBuilder stringBuilder = new();
        while (true)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType == WebSocketMessageType.Text)
                stringBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (result.EndOfMessage) break;
        }
        if (stringBuilder.Length == 0) return string.Empty;
        return stringBuilder.ToString();
    }
}