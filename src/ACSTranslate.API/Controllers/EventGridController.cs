using System.Text.Json;
using Azure.Messaging;
using Microsoft.AspNetCore.Mvc;

namespace ACSTranslate;

[Route("api/eventgrid")]
[ApiController]
public class EventGridController : ControllerBase
{
    public const string EventGridEndpoint = "/api/eventgrid";
    
    private readonly IEnumerable<IEventGridHandler> _handlers;
    private readonly ILogger<EventGridController> _logger;

    public EventGridController(
        IEnumerable<IEventGridHandler> handlers,
        ILogger<EventGridController> logger)
    {
        _handlers = handlers;
        _logger = logger;
    }

    [HttpPost]
    [HttpOptions]
    public async Task<IActionResult> HandleEvent()
    {
        // Handle Event Grid validation
        if (Request.Headers.TryGetValue("aeg-event-type", out var eventType) && eventType == "SubscriptionValidation")
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            var events = JsonSerializer.Deserialize<JsonElement[]>(body);
            
            if (events != null && events.Length > 0)
            {
                var validationCode = events[0].GetProperty("data").GetProperty("validationCode").GetString();
                return Ok(new { validationResponse = validationCode });
            }
        }

        // Handle Cloud Events
        try
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            var cloudEvents = CloudEvent.ParseMany(BinaryData.FromString(body));
            
            foreach (var cloudEvent in cloudEvents)
            {
                var handler = _handlers.FirstOrDefault(h => 
                    h.EventTypes.Contains(cloudEvent.Type, StringComparer.OrdinalIgnoreCase));
                
                if (handler != null)
                {
                    await handler.HandleEventAsync(cloudEvent);
                }
                else
                {
                    _logger.LogInformation("No handler for event type: {EventType}", cloudEvent.Type);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Event Grid event");
        }

        return Ok();
    }
}
