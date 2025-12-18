namespace ACSTranslate;

public record Config (
    AzureAISpeechConfig AzureAISpeech,
    ACSConfig? ACS = null,
    InboundConfig? Inbound = null,
    EventGridConfig? EventGrid = null,
    string? AzureTenantId = null,
    string? AuthCode = null
);
public record AzureAISpeechConfig (
    string ResourceID,
    string Region
);
public record ACSConfig(
    string? Endpoint = null,
    string? InboundNumber = null
)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(InboundNumber);
};
public record InboundConfig(string? Hostname)
{
    private string GetHost() {
        var websiteHostname = Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME");
        if (!string.IsNullOrWhiteSpace(websiteHostname))
        {
            return websiteHostname;
        }
        if (!string.IsNullOrWhiteSpace(Hostname))
        {
            return Hostname;
        }
        return "localhost:5000";
    }
    public Uri BaseUri => new($"https://{GetHost()}");
    public Uri BaseWsUri => new($"wss://{GetHost()}");
    public Uri EventsUri => new(BaseUri, EventGridController.EventGridEndpoint);
};
public static class ConfigExtensions
{
    public static IServiceCollection AddConfig(this IServiceCollection services)
        => services.AddTransient(context
            => context.GetRequiredService<IConfiguration>().Get<Config>()
                ?? throw new Exception("Unable to bind config"));
    public static IServiceCollection MapConfigPart<T>(this IServiceCollection services, Func<Config, T> map) where T : class
        => services.AddTransient(context => map(context.GetRequiredService<Config>()));
}