using Azure.Communication.CallAutomation;
using Azure.Communication.Identity;
using Azure.Core;
using Microsoft.Extensions.Logging;

namespace ACSTranslate;

public class ACSService
{
    private readonly CommunicationIdentityClient? _identityClient;
    private readonly CallAutomationClient? _callClient;
    private readonly ACSConfig _config;
    private readonly ILogger<ACSService> _logger;

    public ACSService(
        ACSConfig config,
        TokenCredential? credential,
        ILogger<ACSService> logger)
    {
        _config = config;
        _logger = logger;
        
        if (config.IsConfigured && !string.IsNullOrWhiteSpace(config.Endpoint) && credential != null)
        {
            var endpoint = new Uri(config.Endpoint);
            _identityClient = new CommunicationIdentityClient(endpoint, credential);
            _callClient = new CallAutomationClient(endpoint, credential);
            _logger.LogInformation("ACS Service initialized with endpoint: {Endpoint}", config.Endpoint);
        }
        else
        {
            _logger.LogWarning("ACS Service not configured - ACS features will be disabled");
        }
    }

    public bool IsConfigured => _config.IsConfigured;
    public string? InboundNumber => _config.InboundNumber;

    public async Task<AccessToken> GetACSTokenAsync()
    {
        if (_identityClient == null) throw new InvalidOperationException("ACS not configured");
        var user = await _identityClient.CreateUserAsync();
        var tokenResponse = await _identityClient.GetTokenAsync(user, scopes: [CommunicationTokenScope.VoIP]);
        _logger.LogInformation("ACS token issued for user: {User}", user.Value.Id);
        return tokenResponse.Value;
    }

    public async Task AnswerCallAsync(string incomingCallContext, Uri callbackEndpoint)
    {
        if (_callClient == null) throw new InvalidOperationException("ACS not configured");
        
        // Answer the call with basic options
        var answerCallResult = await _callClient.AnswerCallAsync(
            new AnswerCallOptions(incomingCallContext, callbackEndpoint)
        );
        
        _logger.LogInformation("Answered call with context: {Context}, CallConnectionId: {CallConnectionId}", 
            incomingCallContext, answerCallResult.Value.CallConnection.CallConnectionId);
    }

    public bool MatchesInboundNumber(string rawId)
    {
        if (string.IsNullOrWhiteSpace(_config.InboundNumber)) return false;
        var formattedNumber = $"4:{System.Text.RegularExpressions.Regex.Replace(_config.InboundNumber, @"[^0-9+]", string.Empty).Trim()}";
        return formattedNumber.Equals(rawId, StringComparison.OrdinalIgnoreCase);
    }
}
