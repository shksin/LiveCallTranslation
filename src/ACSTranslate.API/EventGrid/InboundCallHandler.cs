using Azure.Messaging;

namespace ACSTranslate;

class InboundCallHandler(
    CallService _callService,
    ACSService _acsService,
    ILogger<InboundCallHandler> _logger
) : IEventGridHandler
{
    private static readonly TimeSpan _maxEventAge = TimeSpan.FromMinutes(3);
    public string[] EventTypes { get; } = [
        "Microsoft.Communication.IncomingCall"
    ];

    public async Task HandleEventAsync(CloudEvent cloudEvent)
    {
        _logger.LogInformation("Received incoming call event: {Event}", cloudEvent);
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

        if (_acsService.RawIdIsInboundNumber(incomingCallData.to.rawId) || await _acsService.RawIdIsUserApplication(incomingCallData.to.rawId))
        {
            var caller = incomingCallData.from.rawId.Split(":")[1];

            if (incomingCallData.customContext.voipHeaders.TryGetValue("name", out var callerName))
            {
                // Make sure caller name only contains letters, numbers, and spaces
                callerName = new string([.. callerName.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))]);

                if (!string.IsNullOrWhiteSpace(callerName))
                {
                    caller = callerName;
                }
            }

            // Check language settings
            var languageCode = "";
            if (incomingCallData.customContext.voipHeaders.TryGetValue("language", out var callerLanguage)
                && !string.IsNullOrWhiteSpace(callerLanguage))
            {
                languageCode = callerLanguage;
            }

            var languageConfig = TranslationConfig.GetConfig(languageCode);

            var call = await _callService.CreateCallAsync(incomingCallData.incomingCallContext, caller, languageConfig);

            _logger.LogInformation("Created call: {Call}", call);
        }
        else if (await _acsService.RawIdIsServerApplication(incomingCallData.to.rawId))
        {
            var callId = Guid.Parse(incomingCallData.customContext.voipHeaders["callId"]);

            var call = await _callService.ConnectCallAsync(callId, incomingCallData.incomingCallContext);

            _logger.LogInformation("Connecting call: {Call}", call);
        }
        else
        {
            _logger.LogInformation("To {To} did not match expected parties, ignoring event", incomingCallData.to.rawId);

            return;
        }
    }

    private record IncomingCallData(string incomingCallContext, CallPartyData to, CallPartyData from, CustomContextData customContext);
    private record CallPartyData(string kind, string rawId);
    private record CustomContextData(Dictionary<string, string> voipHeaders);
}