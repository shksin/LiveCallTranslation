using Azure.Messaging;

namespace ACSTranslate;

public interface IEventGridHandler
{
    string[] EventTypes { get; }
    Task HandleEventAsync(CloudEvent cloudEvent);
}
