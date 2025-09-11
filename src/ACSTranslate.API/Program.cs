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
    => await ws.UpgradeAsync(context, (ws, ct) => cm.ConnectUserAsync(new UserWebSocket(ws), ct))
);
app.MapGet("/api/agent/ws", async (
        [FromServices] WebSocketManager ws,
        [FromServices] CallManager cm,
        HttpContext context
    )
    => await ws.UpgradeAsync(context, (ws, ct) => cm.ConnectAgentAsync(new AgentWebSocket(ws), ct))
);
app.MapGet("/", () => "Ok.");
var tokenWarmer = Task.Run(async () => await app.Services.GetRequiredService<CognitiveServicesAuth>().KeepWarmAsync());

app.Run();