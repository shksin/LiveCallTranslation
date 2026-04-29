using System.Net.WebSockets;

public class WebSocketManager(ILogger<WebSocketManager> _logger)
{
    public async Task UpgradeAsync(HttpContext context, Func<WebSocket, CancellationToken, Task> callback)
    {
        if (context.WebSockets.IsWebSocketRequest)
        {
            using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            _logger.LogInformation("WebSocket open");

            var pingTask = Task.Run(async () =>
            {
                while (webSocket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
                {
                    try
                    {
                        await webSocket.SendAsync(
                                new ArraySegment<byte>(System.Text.Encoding.UTF8.GetBytes("{\"type\":\"ping\"}")),
                                WebSocketMessageType.Text,
                                true,
                                context.RequestAborted);
                        await Task.Delay(15000, context.RequestAborted);
                    }
                    catch (TaskCanceledException) { }
                }
            });

            try
            {
                await callback(webSocket, context.RequestAborted);
            }
            catch (TaskCanceledException)
            {
                // Fall through to finally to gracefully close the connection
            }
            catch (OperationCanceledException)
            {
                // No need to handle this, it's most likely just the client forcefully closing the connection
            }
            catch (Exception e)
            {
                // Catch it if it is actually an error
                _logger.LogError(e, "Websocket error. Message: {Message}, StackTrace: {StackTrace}", e.Message, e.ToString());
                if (webSocket.State != WebSocketState.Closed && webSocket.State != WebSocketState.Aborted)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                }
            }
            finally
            {

                if (webSocket.State != WebSocketState.Closed && webSocket.State != WebSocketState.Aborted)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                }
                _logger.LogInformation("WebSocket closed");
            }

        }
        else context.Response.StatusCode = StatusCodes.Status400BadRequest;
    }
}