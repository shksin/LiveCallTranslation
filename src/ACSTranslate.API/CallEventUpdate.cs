using System.Text.Json.Serialization;

public record CallEventUpdate(
    [property: JsonPropertyName("id")]
    Guid Id,
    [property: JsonPropertyName("state")]
    string State
);