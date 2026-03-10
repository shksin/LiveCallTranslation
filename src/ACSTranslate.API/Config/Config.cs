namespace ACSTranslate;

/// <summary>
/// Translator type: AISpeech (3-stage: STT → Translate → TTS) or VoiceLive (single model end-to-end)
/// </summary>
public enum Translator
{
    /// <summary>
    /// Uses Azure Speech SDK: Speech Recognition → Translation → Text-to-Speech (higher latency, more control)
    /// </summary>
    AISpeech,
    
    /// <summary>
    /// Uses GPT-4o Realtime API (Voice Live API): End-to-end audio translation with server-side VAD (lower latency)
    /// </summary>
    VoiceLive
}

public record Config (
    AzureAISpeechConfig AzureAISpeech,
    ACSConfig? ACS = null,
    InboundConfig? Inbound = null,
    EventGridConfig? EventGrid = null,
    AzureOpenAIConfig? AzureOpenAI = null,
    Translator Translator = Translator.AISpeech,
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

public record AzureOpenAIConfig(
    string? Endpoint = null,
    string? DeploymentName = null,
    string? ApiKey = null,
    string? ApiVersion = "2025-05-01-preview",
    bool UseAzureSpeechVoices = true,
    bool UseTelephonyResampling = false
)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(DeploymentName);
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