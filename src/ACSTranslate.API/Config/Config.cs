public record Config (
    AzureAISpeechConfig AzureAISpeech,
    string? AzureTenantId = null,
    string? AuthCode = null
);
public record AzureAISpeechConfig (
    string ResourceID,
    string Region,
    string? Endpoint = null
);
public static class ConfigExtensions
{
    public static IServiceCollection AddConfig(this IServiceCollection services)
        => services.AddTransient(context
            => context.GetRequiredService<IConfiguration>().Get<Config>()
                ?? throw new Exception("Unable to bind config"));
    public static IServiceCollection MapConfigPart<T>(this IServiceCollection services, Func<Config, T> map) where T : class
        => services.AddTransient(context => map(context.GetRequiredService<Config>()));
}