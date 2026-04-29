using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry().UseAzureMonitor();
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
            .LogWarning("!!! No auth code has been configured, skipping authentication !!!");
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
var auth = app.Services.GetRequiredService<CognitiveServicesAuth>();
await auth.ValidateConnectivityAsync();
await auth.ValidateAuthTokenAsync();
var tokenWarmer = Task.Run(async () => await auth.KeepWarmAsync());

app.Run();