public record CallOptions(
    string UserLanguage,
    string AgentLanguage,
    CallAudioOptions? AgentAudioOptions,
    string? TranslatorMode = null
);

public record CallAudioOptions(
    bool UserOriginalAudio,
    bool UserTranslatedAudio,
    bool AgentOriginalAudio,
    bool AgentTranslatedAudio
);