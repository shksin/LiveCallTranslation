namespace ACSTranslate;

public record WebHookRequest(string WebHookRequestOrigin, string? WebHookRequestCallback, string? WebHookRequestRate)
{
    public static WebHookRequest FromHeaders(HttpRequest request)
    {
        // Todo: Throw on empty
        var webhookRequestOrigin = request.Headers["WebHook-Request-Origin"].FirstOrDefault();
        ArgumentException.ThrowIfNullOrEmpty(webhookRequestOrigin, "WebHook-Request-Origin header is required.");
        var webhookRequestCallback = request.Headers["WebHook-Request-Callback"];
        var webhookRequestRate = request.Headers["WebHook-Request-Rate"];
        return new WebHookRequest(webhookRequestOrigin, webhookRequestCallback, webhookRequestRate);
    }
    public WebHookResponse ToResponse()
    {
        return new WebHookResponse(WebHookRequestRate ?? "0", WebHookRequestOrigin);
    }
}
