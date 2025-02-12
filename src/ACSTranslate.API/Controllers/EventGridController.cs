using Azure.Messaging;
using Microsoft.AspNetCore.Mvc;

namespace ACSTranslate;

[Route(EventGridEndpoint)]
[Consumes("application/json")]
[Produces("application/json")]
public class EventGridController(
    IEnumerable<IEventGridHandler> _handlers,
    ILogger<EventGridController> _logger
) : ControllerBase
{
    internal const string EventGridEndpoint = "/api/events";
    [HttpOptions]
    public ActionResult EndpointValidation()
    {
        var webhookRequest = WebHookRequest.FromHeaders(Request);
        var webhookResponse = webhookRequest.ToResponse() with
        {
            WebHookAllowedRate = "*"
        };
        webhookResponse.AppendToHeaders(Response);
        _logger.LogInformation("Webhook validation response: {Response}", webhookResponse);
        return Ok();
    }

    [HttpPost]
    public async Task<ActionResult<CallViewModel>> ReceiveEvent()
    {
        try
        {
            var requestData = await BinaryData.FromStreamAsync(Request.Body);
            var events = CloudEvent.ParseMany(requestData);
            var tasks = events?.SelectMany(x => GetHandlers(x.Type).Select(y => y.HandleEventAsync(x))).ToArray();
            _logger.LogInformation("Processing {Count} tasks for {EventCount} events", tasks?.Length, events?.Length);
            if (tasks == null || tasks.Length == 0)
            {
                return BadRequest("No events found");
            }
            await Task.WhenAll(tasks);
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing event");
            return StatusCode(503, ex.Message);
        }
    }
    private IEnumerable<IEventGridHandler> GetHandlers(string eventType)
        => _handlers.Where(x => x.EventTypes.Contains(eventType, StringComparer.OrdinalIgnoreCase));
}