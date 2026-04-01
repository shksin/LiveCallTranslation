public record CallOptions(
    string UserLanguage,
    string AgentLanguage,
    CallAudioOptions? AgentAudioOptions
);

public record CallAudioOptions(
    bool UserOriginalAudio,
    bool UserTranslatedAudio,
    bool AgentOriginalAudio,
    bool AgentTranslatedAudio
);