using Azure.Core;
using Azure.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ACSTranslate;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddConfig().MapConfigPart(x => x.AzureAISpeech);

builder.Services.AddSingleton<WebSocketManager>();
builder.Services.AddSingleton<CallManager>();
builder.Services.AddSingleton<CognitiveServicesAuth>();

builder.Services.AddSingleton<TokenCredential>(context =>
{
    var config = context.GetRequiredService<Config>();
    var options = new DefaultAzureCredentialOptions();
    if (!string.IsNullOrEmpty(config.AzureTenantId))
    {
        options.TenantId = config.AzureTenantId;
    }
    return new DefaultAzureCredential(options);
});

// ACS Configuration
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<Config>();
    return config.ACS ?? new ACSConfig();
});

builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<Config>();
    return config.Inbound ?? new InboundConfig();
});

// ACS Services
builder.Services.AddSingleton<ACSService>(sp =>
{
    var acsConfig = sp.GetRequiredService<ACSConfig>();
    var credential = acsConfig.IsConfigured ? sp.GetRequiredService<TokenCredential>() : null;
    var logger = sp.GetRequiredService<ILogger<ACSService>>();
    return new ACSService(acsConfig, credential, logger);
});

builder.Services.AddDbContextFactory<OrchestratorContext>((services, options) =>
{
    options.UseInMemoryDatabase("Orchestrator");
});

builder.Services.AddSingleton<CallService>();
builder.Services.AddSingleton<ACSWebSocketHandler>();
builder.Services.AddSingleton<InboundCallHandler>();
builder.Services.AddSingleton<IEnumerable<IEventGridHandler>>(services => 
    [services.GetRequiredService<InboundCallHandler>()]);

var app = builder.Build();

// Basic auth using query string code
app.Use(async (context, next) =>
{
    var authCode = context.RequestServices.GetRequiredService<Config>().AuthCode;
    if (!string.IsNullOrEmpty(authCode))
    {
        var code = context.Request.Query["code"].FirstOrDefault();
        if (context.Request.Path.StartsWithSegments("/public") ||
            context.Request.Path.StartsWithSegments("/api/eventgrid") ||
            context.Request.Path.StartsWithSegments("/ws/acs") ||
            context.Request.Path.StartsWithSegments("/api/calls/acs/config"))
        {
            // Allow public files, event grid webhooks, ACS websockets, and ACS config
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
app.MapControllers();

// Default mode - Direct WebSocket connections
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

// ACS mode - WebSocket for ACS media streaming
app.MapGet("/ws/acs/{callId:guid}", async (
    Guid callId,
    [FromServices] ACSWebSocketHandler handler,
    HttpContext context
) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
    await handler.HandleACSStreamAsync(callId, webSocket, context.RequestAborted);
});

app.MapGet("/", () => "Ok.");
var tokenWarmer = Task.Run(async () => await app.Services.GetRequiredService<CognitiveServicesAuth>().KeepWarmAsync());

app.Run();