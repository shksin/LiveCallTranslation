using ACSTranslate.Core;
using Azure.Communication.CallAutomation;
using Azure.Communication.Identity;
using Azure.Core;

namespace ACSTranslate;

public class ACSService(
    CommunicationIdentityClient _identityClient,
    CallAutomationClient _callClient,
    ACSConfig _config,
    ILogger<ACSService> _logger
)
{
    private readonly LazyAsync<string> _serverApplicationId = new(() => _identityClient.CreateUserAsync().ContinueWith(t => t.Result.Value.Id));
    private readonly LazyAsync<string> _userApplicationId = new(() => _identityClient.CreateUserAsync().ContinueWith(t => t.Result.Value.Id));



    public async Task<string> GetServerApplicationACSIdentity() => await _serverApplicationId.GetAsync();
    public async Task<string> GetUserApplicationACSIdentity() => await _userApplicationId.GetAsync();

    public async Task<bool> RawIdIsServerApplication(string rawId)
        => (await GetServerApplicationACSIdentity()).Equals(rawId, StringComparison.OrdinalIgnoreCase);

    public async Task<bool> RawIdIsUserApplication(string rawId)
        => (await GetUserApplicationACSIdentity()).Equals(rawId, StringComparison.OrdinalIgnoreCase);

    public bool RawIdIsInboundNumber(string rawId)
        => _config.MatchesInboundNumber(rawId);

    public async Task<AccessToken> GetACSToken()
    {
        var user = await _identityClient.CreateUserAsync();
        var tokenResponse = await _identityClient.GetTokenAsync(user, scopes: [CommunicationTokenScope.VoIP]);
        _logger.LogInformation("Token issued for user: {User}", user.Value.Id);
        return tokenResponse.Value;
    }

    public async Task AnswerCallAsync(string incomingCallContext, Uri callbackEndpoint, Uri websocketEndpoint)
    {
        await _callClient.AnswerCallAsync(new AnswerCallOptions(incomingCallContext, callbackEndpoint)
        {
            MediaStreamingOptions = new MediaStreamingOptions(
                        websocketEndpoint,
                        MediaStreamingContent.Audio,
                        MediaStreamingAudioChannel.Mixed,
                        MediaStreamingTransport.Websocket,
                        true
                    )
            {
                EnableBidirectional = true,
                AudioFormat = GetAudioFormat()
            }
        });
    }
    public async Task DisconnectCallAsync(string callConnectionId)
    {
        await _callClient.GetCallConnection(callConnectionId).HangUpAsync(true);
    }
    private AudioFormat GetAudioFormat() => AudioFormatConfig.Current.GlobalAudioFormat switch
    {
        GlobalAudioFormat.Pcm16KMono16Bit => AudioFormat.Pcm16KMono,
        _ => throw new Exception($"Audio format {AudioFormatConfig.Current.GlobalAudioFormat} not configured for ACS")
    };
}