using Azure.Core;

namespace ACSTranslate;

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