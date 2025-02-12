namespace ACSTranslate;

public record WebHookResponse(string WebHookAllowedRate, string WebHookAllowedOrigin)
{
    public void AppendToHeaders(HttpResponse response)
    {
        response.Headers.Append("WebHook-Allowed-Rate", WebHookAllowedRate);
        response.Headers.Append("WebHook-Allowed-Origin", WebHookAllowedOrigin);
    }
}
