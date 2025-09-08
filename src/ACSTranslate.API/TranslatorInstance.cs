using ACSTranslate;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.Translation;

public class TranslatorInstance : IDisposable
{
    // Todo: Refactor out the audio format selection
    private static readonly SpeechSynthesisOutputFormat _audioFormat = SpeechSynthesisOutputFormat.Raw16Khz16BitMonoPcm;
    private readonly SpeechSynthesizer _speechSynthesizer;
    private readonly TranslationRecognizer _recognizer;
    private readonly PushAudioInputStream _inputStream;
    private readonly SpeechTranslationConfig _translationConfig;

    public TranslatorInstance(
        LanguageConfig inputLanguage,
        LanguageConfig outputLanguage,
        string authToken,
        string region
    )
    {
        // Set up translator
        _translationConfig = SpeechTranslationConfig.FromAuthorizationToken(authToken, region);
        _translationConfig.SpeechRecognitionLanguage = inputLanguage.Code;
        _translationConfig.AddTargetLanguage(outputLanguage.ShortCode);
        _translationConfig.VoiceName = outputLanguage.Voice;
        _translationConfig.SetSpeechSynthesisOutputFormat(_audioFormat);
        _translationConfig.SetProperty(PropertyId.SpeechServiceResponse_TranslationRequestStablePartialResult, "true");

        _inputStream = new PushAudioInputStream();
        var audioInput = AudioConfig.FromStreamInput(_inputStream);
        _recognizer = new TranslationRecognizer(_translationConfig, audioInput);

        // Set up speech to text
        var speechOutputConfig = SpeechConfig.FromAuthorizationToken(authToken, region);
        speechOutputConfig.SpeechSynthesisVoiceName = outputLanguage.Voice;
        speechOutputConfig.SetSpeechSynthesisOutputFormat(_audioFormat);

        _speechSynthesizer = new SpeechSynthesizer(speechOutputConfig, null);
    }

    public static async Task<TranslatorInstance> CreateAsync(
        LanguageConfig inputLanguage,
        LanguageConfig outputLanguage,
        CognitiveServicesAuth auth
    )
    {
        var authToken = await auth.GetAuthTokenAsync();
        var instance = new TranslatorInstance(inputLanguage, outputLanguage, authToken, auth.Region);
        await instance.StartAsync();
        return instance;
    }

    // Todo, refactor
    public async Task StartAsync()
    {
        await _recognizer.StartContinuousRecognitionAsync();
    }
    public void SendData(byte[] data)
    {
        _inputStream.Write(data);
    } 
    public void AttachTranscribeOutput(
        Func<string, string, bool, Task> callback,
        string? translationShortCode = null
    )
    {
        if (translationShortCode is null || !_translationConfig.TargetLanguages.Contains(translationShortCode))
            translationShortCode = _translationConfig.TargetLanguages[0];

        _recognizer.Recognizing += async (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.Result.Text) &&
                e.Result.Translations.TryGetValue(translationShortCode, out var translatedText) &&
                !string.IsNullOrEmpty(translatedText))
            await callback(e.Result.Text, translatedText, false);
        };
        _recognizer.Recognized += async (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.Result.Text) &&
                e.Result.Translations.TryGetValue(translationShortCode, out var translatedText) &&
                !string.IsNullOrEmpty(translatedText))
            await callback(e.Result.Text, translatedText, true);
        };
    }
    public void AttachSpeechOutput(
        Func<byte[], Task> callback,
        string? translationShortCode = null
    )
    {
        if (translationShortCode is null || !_translationConfig.TargetLanguages.Contains(translationShortCode))
            translationShortCode = _translationConfig.TargetLanguages[0];

        var targets = new List<string> { ". ", "? ", "。", "？" };
        int segmentNumber = 0;

        _recognizer.Recognizing += async (s, e) =>
        {
            var transText = e.Result.Translations[translationShortCode];
            int count = StringCount(transText, targets);
            if (count > segmentNumber)
            {
                var transToSynth = CopyStringToLastOccurrence(transText, targets, segmentNumber, false);
                segmentNumber = count;
                await Task.Run(() => SpeakAsync(transToSynth, callback));
            }
        };
        _recognizer.Recognized += async (s, e) =>
        {
            if (e.Result.Reason != ResultReason.TranslatedSpeech) return;
            if (string.IsNullOrEmpty(e.Result.Text)) return;

            var transText = e.Result.Translations[translationShortCode];
            var transToSynth = CopyStringToLastOccurrence(transText, targets, segmentNumber, true);
            segmentNumber = 0;
            await Task.Run(() => SpeakAsync(transToSynth, callback));
        };
    }
    public void AttachDebugLogging(ILogger logger)
    {
        _recognizer.Recognizing += (s, e) =>
        {
            logger.LogTrace("Recognizing: {Text}", e.Result.Text);
            foreach (var translation in e.Result.Translations)
            {
                logger.LogTrace("Translation: {Language}, {Text}", translation.Key, translation.Value);
            }
        };
        _recognizer.Recognized += (s, e) =>
        {
            logger.LogTrace("Recognized: {Text}", e.Result.Text);
            foreach (var translation in e.Result.Translations)
            {
                logger.LogTrace("Translation: {Language}, {Text}", translation.Key, translation.Value);
            }
        };
        _recognizer.SessionStarted += (s, e) => logger.LogTrace("SessionStarted");
        _recognizer.SessionStopped += (s, e) => logger.LogTrace("SessionStopped");
    }

    public async Task SpeakAsync(string text, Func<byte[], Task> callback)
    {
        await foreach (var audio in TextToSpeech(text))
        {
            await callback(audio);
        }
    }
    private async IAsyncEnumerable<byte[]> TextToSpeech(string text)
    {
        using var result = await _speechSynthesizer.StartSpeakingTextAsync(text);
        using var audioDataStream = AudioDataStream.FromResult(result);
        byte[] audio = new byte[1600];
        int filledSize = 0;
        while ((filledSize = (int)audioDataStream.ReadData(audio)) > 0)
        {
            yield return audio[..filledSize];
        }
    }


    public void Dispose()
    {
        try
        {
            _recognizer?.StopContinuousRecognitionAsync();
        }
        finally
        {
            _speechSynthesizer?.Dispose();
            _recognizer?.Dispose();
            _inputStream?.Dispose();
        }    
    }
    
    // Helpers
    private int StringCount(string str, List<string> targets)
    {
        int count = 0;
        foreach (var target in targets)
        {
            count += str.Split(new string[] { target }, StringSplitOptions.None).Length - 1;
        }
        return count;
    }

    private string CopyStringToLastOccurrence(string str, List<string> targets, int lastCount, bool isRecognized)
    {
        str += " ";
        int count = StringCount(str, targets);

        int index = -1;
        if (!isRecognized)//Find the last occurrence of any of the target strings
        {
            foreach (var target in targets)
            {
                int idx = str.LastIndexOf(target) + target.Length;
                if (idx > index)
                {
                    index = idx;
                }
            }
        }

        //Find the occurrences of the target strings we have already processed
        if (lastCount > 0)
        {
            string strTemp = str;
            //If this string comes from a recognized event then take the who string, otherwise just take the string up to the last target string occurance
            if (!isRecognized)
            {
                strTemp = str.Substring(0, index);
            }
            for (int i = 0; i < lastCount; i++)
            {
                int index_ = int.MaxValue;
                foreach (var target in targets)
                {
                    int idx_ = strTemp.IndexOf(target);
                    if (idx_ > -1)
                    {
                        idx_ = idx_ + target.Length;
                        if (idx_ < index_)
                        {
                            index_ = idx_;
                        }
                    }
                }
                strTemp = strTemp.Substring(index_);
            }
            return strTemp;
        }
        else
        {
            string strTemp = str;
            //If this string comes from a recognized event then take the who string, otherwise just take the string up to the last target string occurance
            if (!isRecognized)
            {
                strTemp = str.Substring(0, index);
            }
            return strTemp;
        }
    }
}
