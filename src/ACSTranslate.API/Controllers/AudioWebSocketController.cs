using System.Net.WebSockets;
using Microsoft.AspNetCore.Mvc;

namespace ACSTranslate;

[Route("ws/audio")]
public class AudioWebSocketController(AudioWebSocketService _audioService) : ControllerBase
{
    [Route("{callId:guid}/{consumerType:alpha}")]
    public async Task AgentAudio(Guid callId, ConsumerType consumerType)
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var cancellationToken = HttpContext.RequestAborted;
        using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();

        try
        {
            await _audioService.ConnectAsync(callId, consumerType, webSocket, cancellationToken);
        }
        catch (TaskCanceledException)
        {
            // Fall through to finally to gracefully close the connection
        }
        catch (OperationCanceledException)
        {
            // No need to handle this, it's most likely just the client forcefully closing the connection
        }
        catch (Exception)
        {
            // Catch it if it is actually an error
            if (webSocket.State != WebSocketState.Closed && webSocket.State != WebSocketState.Aborted)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, "Error", cancellationToken);
            }
        }
        finally
        {

            if (webSocket.State != WebSocketState.Closed && webSocket.State != WebSocketState.Aborted)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancellationToken);
            }
        }
    }
}