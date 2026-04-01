using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ACSTranslate;
using ACSTranslate.Genesys;
using ACSTranslate.Translation;

var builder = WebApplication.CreateBuilder(args);

// Add Application Insights
builder.Services.AddApplicationInsightsTelemetry();

builder.Services.AddControllers();
builder.Services.AddConfig().MapConfigPart(x => x.AzureAISpeech);

builder.Services.AddSingleton<WebSocketManager>();
builder.Services.AddSingleton<CallManager>();
builder.Services.AddSingleton<CognitiveServicesAuth>();

// Register AI Speech translator
builder.Services.AddSingleton<ITranslatorFactory>(sp =>
{
    var cogAuth = sp.GetRequiredService<CognitiveServicesAuth>();
    sp.GetRequiredService<ILogger<Program>>().LogInformation("Using AI Speech translator (3-stage: STT → Translate → TTS)");
    return new AISpeechTranslatorFactory(cogAuth);
});

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

builder.Services.AddSingleton<ArmClient>(sp =>
{
    var credential = sp.GetRequiredService<TokenCredential>();
    return new ArmClient(credential);
});

// ACS Configuration
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<Config>();
    return config.ACS ?? new ACSConfig();
});

builder.Services.BindConfiguration<InboundConfig>("Inbound");
builder.Services.BindConfiguration<EventGridConfig>("EventGrid");

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
builder.Services.AddSingleton<ACSCallBridgeManager>();
builder.Services.AddSingleton<ACSWebSocketHandler>();

// Genesys AudioHook v2 services
builder.Services.AddSingleton<GenesysCallBridgeManager>();
builder.Services.AddSingleton<GenesysWebSocketHandler>();
builder.Services.AddSingleton<InboundCallHandler>();
builder.Services.AddSingleton<EventGridSubscriptionManager>();
builder.Services.AddSingleton<IEnumerable<IEventGridHandler>>(services => 
    [services.GetRequiredService<InboundCallHandler>()]);

var app = builder.Build();



Console.WriteLine("=== APPLICATION STARTING - CONSOLE TEST ===");
Console.WriteLine($"Environment: {builder.Environment.EnvironmentName}");

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

// Genesys AudioHook v2 - WebSocket endpoint
app.MapGet("/ws/genesys", async (
    [FromServices] GenesysWebSocketHandler handler,
    HttpContext context
) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    var sessionId = context.Request.Headers["audiohook-session-id"].FirstOrDefault()
                    ?? context.Request.Query["sessionId"].FirstOrDefault();
    if (string.IsNullOrEmpty(sessionId))
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsync("Missing audiohook-session-id header");
        return;
    }

    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
    await handler.HandleGenesysStreamAsync(sessionId, webSocket, context.RequestAborted);
});

app.MapGet("/api/languages", () => Results.Ok(LanguageConfig.ListLanguages()));

app.MapGet("/", () => "Ok.");
var tokenWarmer = Task.Run(async () => await app.Services.GetRequiredService<CognitiveServicesAuth>().KeepWarmAsync());

// Auto-configure EventGrid subscription after app starts
var eventGridTask = Task.Run(async () =>
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    try
    {
        Console.WriteLine("[EventGrid] Auto-config task started");
        logger.LogInformation("EventGrid subscription configuration task started. Waiting 5 seconds...");
        
        // Log all configuration for debugging
        var inboundConfig = app.Services.GetService<InboundConfig>();
        var eventGridConfig = app.Services.GetService<EventGridConfig>();
        logger.LogInformation("Configuration check:");
        logger.LogInformation("  InboundConfig: {HasConfig}", inboundConfig != null ? $"Hostname={inboundConfig.Hostname}, EventsUri={inboundConfig.EventsUri}" : "NULL");
        logger.LogInformation("  EventGridConfig: {HasConfig}", eventGridConfig != null ? $"TopicResourceID={eventGridConfig.TopicResourceID}" : "NULL");
        Console.WriteLine($"[EventGrid] InboundConfig={(inboundConfig != null ? inboundConfig.Hostname : "NULL")}, EventGridTopic={(eventGridConfig != null ? eventGridConfig.TopicResourceID : "NULL")}");
        
        // Wait 5 seconds before trying to auto-configure the Event Grid subscription
        // as we need the endpoint to be available to configure the subscription
        await Task.Delay(5_000);
        logger.LogInformation("Calling TryAutoConfigureEventGridSubscriptionAsync...");
        Console.WriteLine("[EventGrid] Calling TryAutoConfigureEventGridSubscriptionAsync");
        await app.TryAutoConfigureEventGridSubscriptionAsync();
        logger.LogInformation("EventGrid subscription configuration task completed.");
        Console.WriteLine("[EventGrid] Auto-config task completed");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "EventGrid subscription configuration task failed: {Message}", ex.Message);
        Console.WriteLine($"[EventGrid] Auto-config task failed: {ex.Message}");
    }
});

// Log immediately before starting to verify app is running
var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
startupLogger.LogWarning("=== APPLICATION IS STARTING - LOGGING TEST ===");
var testInbound = app.Services.GetService<InboundConfig>();
var testEventGrid = app.Services.GetService<EventGridConfig>();
startupLogger.LogWarning("Config loaded: Inbound={Inbound}, EventGrid={EventGrid}", 
    testInbound != null ? $"YES (Hostname={testInbound.Hostname})" : "NO", 
    testEventGrid != null ? $"YES (TopicResourceID={testEventGrid.TopicResourceID})" : "NO");

app.Run();