using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.Messaging;
using Microsoft.Extensions.Logging;

namespace ACSTranslate;

public interface IEventGridHandler
{
    string[] EventTypes { get; }
    Task HandleEventAsync(CloudEvent cloudEvent);
}

public class InboundCallHandler : IEventGridHandler
{
    private readonly CallService _callService;
    private readonly ACSService _acsService;
    private readonly ILogger<InboundCallHandler> _logger;
    private static readonly TimeSpan _maxEventAge = TimeSpan.FromMinutes(3);

    public InboundCallHandler(
        CallService callService,
        ACSService acsService,
        ILogger<InboundCallHandler> logger)
    {
        _callService = callService;
        _acsService = acsService;
        _logger = logger;
    }

    public string[] EventTypes { get; } = ["Microsoft.Communication.IncomingCall"];

    public async Task HandleEventAsync(CloudEvent cloudEvent)
    {
        _logger.LogInformation("Received incoming call event");
        
        if (cloudEvent.Time == null || cloudEvent.Time.Value.Add(_maxEventAge) < DateTimeOffset.UtcNow)
        {
            _logger.LogWarning("Event is too old, ignoring");
            return;
        }

        var incomingCallData = cloudEvent.Data?.ToObjectFromJson<IncomingCallData>();
        if (incomingCallData == null)
        {
            _logger.LogWarning("No data found in event");
            return;
        }

        // Check if this is a call to our inbound number
        if (_acsService.MatchesInboundNumber(incomingCallData.to.rawId))
        {
            var caller = incomingCallData.from.rawId.Split(":").LastOrDefault() ?? "Unknown";
            
            // Check for language in custom context
            var language = "en-US";
            if (incomingCallData.customContext?.voipHeaders?.TryGetValue("language", out var lang) == true)
            {
                language = lang;
            }

            await _callService.CreateCallAsync(incomingCallData.incomingCallContext, caller, language);
        }
        else
        {
            _logger.LogInformation("Incoming call to {To} does not match inbound number", incomingCallData.to.rawId);
        }
    }

    private record IncomingCallData(
        string incomingCallContext, 
        CallPartyData to, 
        CallPartyData from, 
        CustomContextData? customContext);
    
    private record CallPartyData(string kind, string rawId);
    private record CustomContextData(Dictionary<string, string>? voipHeaders);
}
