using ACSTranslate.Core;
using Azure.Core;
using Microsoft.CognitiveServices.Speech;

namespace ACSTranslate;

public class AzureAISpeechTranslatorFactory(
    AzureAISpeechConfig _config,
    TokenCredential _tokenCredential,
    ILogger<AzureAISpeechTranslatorFactory> _logger
)
{
    public async Task<AzureAISpeechInput> CreateTranslatorAsync(
        TranslatorOptions options,
        ITranscribeUpdater transcription,
        TranslatorOutput receiveData
    )
    {
        var config = await GetSpeechTranslationConfigAsync();

        config.SpeechRecognitionLanguage = options.InputLanguage;
        config.AddTargetLanguage(options.OutputLanguageCode);
        config.VoiceName = options.OutputVoice;
        config.SetSpeechSynthesisOutputFormat(AudioFormatConfig.Current.ToSpeechSynthesisOutputFormat());
        config.SetProperty(PropertyId.SpeechServiceResponse_TranslationRequestStablePartialResult, "true");

        _logger.LogInformation("Created Azure AI Speech Translation with options: {Options}", options);

        var speechOutConfig = await GetSpeechConfigAsync();
        speechOutConfig.SpeechSynthesisVoiceName = options.OutputVoice;
        speechOutConfig.SetSpeechSynthesisOutputFormat(AudioFormatConfig.Current.ToSpeechSynthesisOutputFormat());

        return new AzureAISpeechInput(config, speechOutConfig, transcription, receiveData, _logger);
    }

    private async Task<SpeechTranslationConfig> GetSpeechTranslationConfigAsync(CancellationToken cancellationToken = default)
    {
        var authToken = await GetAISpeechAuthTokenAsync(cancellationToken);

        return SpeechTranslationConfig.FromAuthorizationToken(authToken, _config.Region);
    }
    private async Task<SpeechConfig> GetSpeechConfigAsync(CancellationToken cancellationToken = default)
    {
        var authToken = await GetAISpeechAuthTokenAsync(cancellationToken);

        return SpeechConfig.FromAuthorizationToken(authToken, _config.Region);
    }
    private async Task<string> GetAISpeechAuthTokenAsync(CancellationToken cancellationToken = default)
    {
        var entraAuth = await _tokenCredential.GetTokenAsync(new TokenRequestContext([
            "https://cognitiveservices.azure.com/.default"
        ]), cancellationToken);

        return $"aad#{_config.ResourceID}#{entraAuth.Token}";
    }



    public async Task<byte[]> GetOneShotAudioTranslation(TranslatorOptions options, string text)
    {
        var config = await GetSpeechConfigAsync();
        config.SpeechSynthesisLanguage = options.OutputLanguage;
        config.SpeechSynthesisVoiceName = options.OutputVoice;
        config.SetSpeechSynthesisOutputFormat(AudioFormatConfig.Current.ToSpeechSynthesisOutputFormat());

        using var synthesizer = new SpeechSynthesizer(config, null);

        var result = await synthesizer.SpeakTextAsync(text);
        return result.AudioData;
    }
}
