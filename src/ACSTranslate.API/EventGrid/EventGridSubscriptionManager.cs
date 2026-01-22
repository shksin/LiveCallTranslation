using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.EventGrid;
using Azure.ResourceManager.EventGrid.Models;

namespace ACSTranslate;

public class EventGridSubscriptionManager(
    EventGridConfig? _config,
    ArmClient _armClient,
    IEnumerable<IEventGridHandler> _handlers,
    InboundConfig _inboundConfig,
    ILogger<EventGridSubscriptionManager> _logger
)
{
    private readonly string _subscriptionName = "ACSTranslateSubscription";

    private EventGridSubscriptionData BuildEventGridSubscription()
    {
        var filter = new EventSubscriptionFilter();
        foreach (var eventType in _handlers.SelectMany(x => x.EventTypes).Distinct())
        {
            filter.IncludedEventTypes.Add(eventType);
        }

        return new EventGridSubscriptionData
        {
            EventDeliverySchema = EventDeliverySchema.CloudEventSchemaV1_0,
            Destination = new WebHookEventSubscriptionDestination
            {
                Endpoint = _inboundConfig.EventsUri
            },
            Filter = filter,
            RetryPolicy = new EventSubscriptionRetryPolicy
            {
                MaxDeliveryAttempts = 4,
                EventTimeToLiveInMinutes = 5
            }
        };
    }

    public async Task TryAutoConfigureAsync()
    {
        _logger.LogInformation("Starting EventGrid subscription auto-configuration...");
        _logger.LogInformation("EventGrid Topic Resource ID: {TopicResourceID}", _config?.TopicResourceID ?? "(null)");
        
        if (string.IsNullOrWhiteSpace(_config?.TopicResourceID))
        {
            _logger.LogWarning("EventGrid Topic Resource ID is not configured. Skipping subscription auto configuration.");
            return;
        }

        try
        {
            _logger.LogInformation("Configuring EventGrid subscription '{SubscriptionName}'...", _subscriptionName);
            _logger.LogInformation("Target endpoint: {Endpoint}", _inboundConfig.EventsUri);
            
            var topicResource = _armClient.GetSystemTopicResource(new ResourceIdentifier(_config.TopicResourceID));
            _logger.LogInformation("Getting EventGrid topic resource...");
            
            var topic = await topicResource.GetAsync();
            _logger.LogInformation("Got EventGrid topic: {TopicName}", topic.Value.Data.Name);
            
            var subscriptions = topic.Value.GetSystemTopicEventSubscriptions();
            
            // Check if subscription exists and delete if it's in a bad state
            try
            {
                var existingSubscription = await subscriptions.GetAsync(_subscriptionName);
                var provisioningState = existingSubscription.Value.Data.ProvisioningState?.ToString();
                _logger.LogInformation("Existing subscription found. State: {State}", provisioningState);
                
                // If the subscription is in AwaitingManualAction state or the endpoint is wrong, delete and recreate
                if (provisioningState == "AwaitingManualAction" || 
                    existingSubscription.Value.Data.Destination is WebHookEventSubscriptionDestination webhook &&
                    webhook.Endpoint?.ToString() != _inboundConfig.EventsUri.ToString())
                {
                    _logger.LogWarning("Subscription is in '{State}' state or has wrong endpoint. Deleting...", provisioningState);
                    Console.WriteLine($"[EventGrid] Deleting subscription in {provisioningState} state");
                    await existingSubscription.Value.DeleteAsync(WaitUntil.Completed);
                    _logger.LogInformation("Existing subscription deleted successfully");
                    Console.WriteLine("[EventGrid] Subscription deleted, will recreate");
                }
                else
                {
                    _logger.LogInformation("Subscription already exists with correct endpoint. Skipping recreation.");
                    Console.WriteLine("[EventGrid] Subscription exists with correct endpoint");
                    return;
                }
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogInformation("No existing subscription found. Will create new one.");
                Console.WriteLine("[EventGrid] No existing subscription, will create new");
            }
            
            var subscription = BuildEventGridSubscription();
            
            _logger.LogInformation("Creating/updating subscription...");
            Console.WriteLine($"[EventGrid] Creating subscription with endpoint: {_inboundConfig.EventsUri}");
            var result = await subscriptions.CreateOrUpdateAsync(WaitUntil.Completed, _subscriptionName, subscription);
            
            _logger.LogInformation("EventGrid subscription '{SubscriptionName}' successfully configured to {Endpoint}", 
                _subscriptionName, _inboundConfig.EventsUri);
            Console.WriteLine("[EventGrid] Subscription created successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure EventGrid subscription: {Message}", ex.Message);
            return;
        }
    }
}

public static class EventGridSubscriptionManagerExtensions
{
    public static async Task TryAutoConfigureEventGridSubscriptionAsync(this IHost app)
        => await app.Services.GetRequiredService<EventGridSubscriptionManager>().TryAutoConfigureAsync();
}
