using System.Text.RegularExpressions;

namespace ACSTranslate;

public record ACSConfig(Uri Endpoint, string InboundNumber)
{
    private static string FormatRawId(string phoneNumber) 
        => $"4:{Regex.Replace(phoneNumber, @"[^0-9+]", string.Empty).Trim()}";
    private readonly string _inboundNumberRawId = FormatRawId(InboundNumber);

    public bool MatchesInboundNumber(string rawId)
    {
        if ("*" == InboundNumber && rawId.StartsWith("4:"))
        {
            return true;
        }
        else
        {
            return _inboundNumberRawId.Equals(rawId);
        }
    }
}