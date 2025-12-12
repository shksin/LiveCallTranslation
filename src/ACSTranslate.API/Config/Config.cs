public record Config (
    AzureAISpeechConfig AzureAISpeech,
    ACSConfig? ACS = null,
    InboundConfig? Inbound = null,
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
public record InboundConfig(
    string? BaseUrl = null,
    string? BaseWsUrl = null
)
{
    public Uri BaseUri => new Uri(BaseUrl ?? "http://localhost:5000");
    public Uri BaseWsUri => new Uri(BaseWsUrl ?? "ws://localhost:5000");
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