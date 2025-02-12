namespace ACSTranslate;

public record InboundConfig(string? Hostname)
{
    private string GetHost() {
        var websiteHostname = Environment.GetEnvironmentVariable("WEBSITE_DEFAULT_HOSTNAME");
        if (!string.IsNullOrWhiteSpace(websiteHostname))
        {
            return websiteHostname;
        }
        if (!string.IsNullOrWhiteSpace(Hostname))
        {
            return Hostname;
        }
        throw new InvalidOperationException("Hostname not set");
    }
    public Uri BaseUri => new($"https://{GetHost()}");
    public Uri BaseWsUri => new($"wss://{GetHost()}");
    public Uri EventsUri => new(BaseUri, EventGridController.EventGridEndpoint);
}