using Azure.Core;
using Azure.Identity;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddConfig().MapConfigPart(x => x.AzureAISpeech);

builder.Services.AddSingleton<WebSocketManager>();
builder.Services.AddSingleton<CallManager>();
builder.Services.AddSingleton<CognitiveServicesAuth>();
builder.Services.AddSingleton<TokenCredential>(context
    => new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
        
    }));

var app = builder.Build();

// Basic auth using query string code
app.Use(async (context, next) =>
{
    var authCode = context.RequestServices.GetRequiredService<Config>().AuthCode;
    if (!string.IsNullOrEmpty(authCode))
    {
        var code = context.Request.Query["code"].FirstOrDefault();
        if (context.Request.Path.StartsWithSegments("/public"))
        {
            // Allow public files
        }
        else if (string.IsNullOrEmpty(code) || !code.Equals(authCode, StringComparison.Ordinal))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }
    }
    else
    {
        context.RequestServices.GetRequiredService<ILogger<Program>>()
            .LogError("!!! No auth code has been configured, skipping authentication !!!");
    }
    await next();
});


app.UseWebSockets();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/api/user/ws", async (
        [FromServices] WebSocketManager ws,
        [FromServices] CallManager cm,
        HttpContext context
    )
    => await ws.UpgradeAsync(context, (ws, ct) => cm.ConnectUserAsync(ws, ct))
);
app.MapGet("/api/agent/ws", async (
        [FromServices] WebSocketManager ws,
        [FromServices] CallManager cm,
        HttpContext context
    )
    => await ws.UpgradeAsync(context, (ws, ct) => cm.ConnectAgentAsync(ws, ct))
);
app.MapGet("/", () => "Ok.");
var tokenWarmer = Task.Run(async () => await app.Services.GetRequiredService<CognitiveServicesAuth>().KeepWarmAsync());

app.Run();

public enum CallState
{
    UserConnected,
    CallEstablished,
    Disconnected
}


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