using ACSTranslate;
using ACSTranslate.Core;
using Azure.Communication.CallAutomation;
using Azure.Communication.Identity;
using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Azure.ResourceManager;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()); // Configure JSON options
    });

// Only enable when running in Azure
if (builder.Environment.IsProduction())
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
}

builder.Services.AddSingleton<ACSService>();
builder.Services.AddSingleton<AudioWebSocketService>();
builder.Services.AddSingleton<CallService>();
builder.Services.AddSingleton<EventGridSubscriptionManager>();
builder.Services.AddSingleton<InboundCallHandler>();
builder.Services.BindConfiguration<InboundConfig>("Inbound");
builder.Services.BindConfiguration<ACSConfig>("ACS");
builder.Services.BindConfiguration<EventGridConfig>("EventGrid");
builder.Services.AddSingleton<TranscribeService>();
builder.Services.AddSingleton<IAudioStorage, NullAudioStorage>();

builder.Services.BindConfiguration<AzureAISpeechConfig>("AzureAISpeech");
builder.Services.AddSingleton<AzureAISpeechTranslatorFactory>();

builder.Services.AddSingleton<IEnumerable<IEventGridHandler>>(services => [
    services.GetRequiredService<InboundCallHandler>()
]);
builder.Services.AddSingleton<TokenCredential>(services =>
{
    var tenantId = services.GetRequiredService<IConfiguration>().GetValue<string>("AzureTenantId");
    var managedIdentityObjectId = services.GetRequiredService<IConfiguration>().GetValue<string>("ManagedIdentityClientId");

    if (string.IsNullOrWhiteSpace(managedIdentityObjectId))
    {
        var options = new AzureCliCredentialOptions();
        if (!string.IsNullOrEmpty(tenantId)) options.TenantId = tenantId;
        return new AzureCliCredential(options);
    }
    else
    {
        DefaultAzureCredentialOptions options = new()
        {
            ManagedIdentityClientId = managedIdentityObjectId,
            WorkloadIdentityClientId = managedIdentityObjectId
        };
        return new DefaultAzureCredential(options);
    }
});
builder.Services.AddSingleton<ArmClient>(services => new ArmClient(services.GetRequiredService<TokenCredential>()));
builder.Services.AddSingleton<CommunicationIdentityClient>(services =>
{
    var endpoint = services.GetRequiredService<ACSConfig>().Endpoint;
    var credential = services.GetRequiredService<TokenCredential>();
    return new CommunicationIdentityClient(endpoint, credential);
});
builder.Services.AddSingleton<CallAutomationClient>(services =>
{
    var endpoint = services.GetRequiredService<ACSConfig>().Endpoint;
    var credential = services.GetRequiredService<TokenCredential>();
    return new CallAutomationClient(endpoint, credential);
});

builder.Services.AddDbContextFactory<OrchestratorContext>((services, options) =>
{
    var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("SQLDB");
    if (string.IsNullOrEmpty(connectionString))
    {
        options.UseInMemoryDatabase("Orchestrator");
    }
    else
    {
        options.UseAzureSql(connectionString);
    }
});

// Add CORS services
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

var app = builder.Build();

await app.EnsureDbCreatedAsync<OrchestratorContext>();
_ = Task.Run(async () =>
{
    // Wait 5 seconds before trying to auto-configure the Event Grid subscription
    //  as we need the endpoint to be available to configure the subscription
    await Task.Delay(5_000);
    await app.TryAutoConfigureEventGridSubscriptionAsync();
});


app.MapOpenApi();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseWebSockets();
app.UseCors(); // Use CORS middleware
app.MapControllers();

app.Run();