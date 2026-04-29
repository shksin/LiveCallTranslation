using Azure.Core;
using Microsoft.CognitiveServices.Speech;
using System.Net;

public class CognitiveServicesAuth(
    TokenCredential _credential,
    AzureAISpeechConfig _config,
    ILogger<CognitiveServicesAuth> _logger
)
{
    private DateTimeOffset? _cacheValidTill;
    private string? _cachedToken;
    private static readonly TimeSpan _skew = TimeSpan.FromMinutes(5);
    public string Region => _config.Region;
    public string? Endpoint => _config.Endpoint;

    public async Task ValidateConnectivityAsync(CancellationToken ct = default)
    {
        string host = !string.IsNullOrEmpty(_config.Endpoint)
            ? new Uri(_config.Endpoint).Host
            : $"{_config.Region}.cognitiveservices.azure.com";

        try
        {
            await Dns.GetHostAddressesAsync(host, ct);
            _logger.LogInformation("Azure AI Speech host resolved: {Host}", host);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Azure AI Speech host '{host}' could not be resolved. " +
                "Verify the endpoint/region config and that private DNS is reachable.", ex);
        }
    }
    public async Task ValidateAuthTokenAsync(CancellationToken ct = default)
    {
        var token = await GetAuthTokenAsync(ct);
        var endpoint = !string.IsNullOrEmpty(_config.Endpoint)
            ? _config.Endpoint
            : $"wss://{_config.Region}.stt.speech.microsoft.com/speech/universal/v2";

        try
        {
            var speechConfig = SpeechConfig.FromEndpoint(new Uri(endpoint), "");
            speechConfig.AuthorizationToken = token;

            using var recognizer = new SpeechRecognizer(speechConfig);
            var result = await recognizer.RecognizeOnceAsync();

            if (result.Reason == ResultReason.Canceled)
            {
                var cancellation = CancellationDetails.FromResult(result);
                if (cancellation.ErrorCode == CancellationErrorCode.ConnectionFailure
                    || cancellation.ErrorCode == CancellationErrorCode.AuthenticationFailure)
                {
                    _logger.LogError(
                        "SpeechService auth token validation failed. Endpoint: {Endpoint}, ErrorCode: {ErrorCode}, Details: {Details}",
                        endpoint, cancellation.ErrorCode, cancellation.ErrorDetails);
                    return;
                }
            }

            _logger.LogInformation("SpeechService auth token validated successfully against endpoint: {Endpoint}", endpoint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SpeechService auth token validation failed with exception. Endpoint: {Endpoint}",
                endpoint);
        }
    }

    public async Task<string> GetAuthTokenAsync(CancellationToken ct = default)
    {
        if (_cacheValidTill.HasValue && _cachedToken is not null && DateTimeOffset.UtcNow < _cacheValidTill.Value)
        {
            _logger.LogInformation("Using cached token, valid till {ValidTill}", _cacheValidTill);
            return _cachedToken;
        }

        var entraAuth = await _credential.GetTokenAsync(
            new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]),
            ct);

        _cachedToken = $"aad#{_config.ResourceID}#{entraAuth.Token}";
        _cacheValidTill = entraAuth.ExpiresOn - _skew;
        if (entraAuth.RefreshOn.HasValue && entraAuth.RefreshOn - _skew < _cacheValidTill)
        {
            _cacheValidTill = entraAuth.RefreshOn - _skew;
        }
        _logger.LogInformation("Fetched new token, valid till {ValidTill}", _cacheValidTill);
        return _cachedToken;
    }
    public async Task KeepWarmAsync()
    {
        while (true)
        {
            try
            {
                _logger.LogInformation("Warming token");
                await GetAuthTokenAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error warming token");
            }
            await Task.Delay(TimeSpan.FromMinutes(5));
        }
    }
}