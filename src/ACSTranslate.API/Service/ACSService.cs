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

    public async Task<string> AnswerCallAsync(string incomingCallContext, Uri callbackEndpoint, Uri? mediaStreamingWebSocketUri = null)
    {
        if (_callClient == null) throw new InvalidOperationException("ACS not configured");
        
        var answerOptions = new AnswerCallOptions(incomingCallContext, callbackEndpoint);
        
        // Configure media streaming if WebSocket URI is provided
        if (mediaStreamingWebSocketUri != null)
        {
            var mediaStreamingOptions = new MediaStreamingOptions(
                MediaStreamingAudioChannel.Unmixed
            )
            {
                TransportUri = mediaStreamingWebSocketUri,
                EnableBidirectional = true,
                AudioFormat = AudioFormat.Pcm16KMono,
                StartMediaStreaming = true
            };
            answerOptions.MediaStreamingOptions = mediaStreamingOptions;
            _logger.LogInformation("Configured media streaming to: {WebSocketUri}", mediaStreamingWebSocketUri);
        }
        
        var answerCallResult = await _callClient.AnswerCallAsync(answerOptions);
        
        var callConnectionId = answerCallResult.Value.CallConnection.CallConnectionId;
        _logger.LogInformation("Answered call with context: {Context}, CallConnectionId: {CallConnectionId}", 
            incomingCallContext, callConnectionId);
        
        return callConnectionId;
    }

    public async Task PlayTextToCallerAsync(string callConnectionId, string text, string voiceName)
    {
        if (_callClient == null) throw new InvalidOperationException("ACS not configured");
        
        var callConnection = _callClient.GetCallConnection(callConnectionId);
        var callMedia = callConnection.GetCallMedia();
        
        var playSource = new TextSource(text)
        {
            VoiceName = voiceName
        };
        
        await callMedia.PlayToAllAsync(playSource);
        _logger.LogInformation("Playing text to caller on call {CallConnectionId}", callConnectionId);
    }

    public async Task StartMediaStreamingAsync(string callConnectionId)
    {
        if (_callClient == null) throw new InvalidOperationException("ACS not configured");
        
        var callConnection = _callClient.GetCallConnection(callConnectionId);
        var callMedia = callConnection.GetCallMedia();
        
        await callMedia.StartMediaStreamingAsync();
        _logger.LogInformation("Started media streaming for call {CallConnectionId}", callConnectionId);
    }

    public bool MatchesInboundNumber(string rawId)
    {
        if (string.IsNullOrWhiteSpace(_config.InboundNumber)) return false;
        
        // Handle wildcard - accept all calls
        if (_config.InboundNumber.Trim() == "*") return true;
        
        var formattedNumber = $"4:{System.Text.RegularExpressions.Regex.Replace(_config.InboundNumber, @"[^0-9+]", string.Empty).Trim()}";
        return formattedNumber.Equals(rawId, StringComparison.OrdinalIgnoreCase);
    }
}
