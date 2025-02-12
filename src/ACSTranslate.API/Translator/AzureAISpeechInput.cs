using ACSTranslate.Core;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.Translation;
using Microsoft.Extensions.Logging;

namespace ACSTranslate;

public class AzureAISpeechInput
{
    private readonly SpeechSynthesizer _speechSynthesizer;
    private readonly TranslationRecognizer _recognizer;
    private readonly PushAudioInputStream _inputStream;
    private readonly TranslatorOutput _receiveData;
    private bool _started;
    // Todo: Refactor, as this does not guarantee the last transcript id is the last one
    private Guid _lastTranscriptId;
    public AzureAISpeechInput(
        SpeechTranslationConfig config,
        SpeechConfig speechOutConfig,
        ITranscribeUpdater transcription,
        TranslatorOutput receiveData,
        ILogger logger)
    {
        _speechSynthesizer = new SpeechSynthesizer(speechOutConfig, null);


        _inputStream = new PushAudioInputStream();
        var audioInput = AudioConfig.FromStreamInput(_inputStream);
        _recognizer = new TranslationRecognizer(config, audioInput);

        // Debug

        _recognizer.Canceled += (s, e) => logger.LogInformation("Canceled: {Reason}, {Details}, {Object}", e.Reason, e.ErrorDetails, e);


        // Use new method for stop detection
        var targets = new List<string> { ". ", "? ", "。", "？" };
        int segmentNumber = 0;

        _recognizer.Recognized += async (s, e) =>
        {

            _lastTranscriptId = transcription.FinalizeText(e.Result.Text, e.Result.Translations[config.TargetLanguages[0]]);

            logger.LogTrace("Recognized: {Text}", e.Result.Text);
            foreach (var translation in e.Result.Translations)
            {
                logger.LogTrace("Translation: {Language}, {Text}", translation.Key, translation.Value);
            }

            if (e.Result.Reason == ResultReason.TranslatedSpeech)
            {
                var transText = e.Result.Translations[config.TargetLanguages[0]];
                if (!string.IsNullOrEmpty(e.Result.Text))
                {
                    var transToSynth = CopyStringToLastOccurrence(transText, targets, segmentNumber, true);
                    segmentNumber = 0;
                    //Output TTS here
                    logger.LogTrace("Speaking: {Text}", transToSynth);
                    await Task.Run(() => SendSynthesizedOutput(_lastTranscriptId, transToSynth));
                }
            }
        };
        _recognizer.Recognizing += async (s, e) =>
        {
            transcription.UpdateText(e.Result.Text, e.Result.Translations[config.TargetLanguages[0]]);

            logger.LogTrace("Recognizing: {Text}", e.Result.Text);
            foreach (var translation in e.Result.Translations)
            {
                logger.LogTrace("Translation: {Language}, {Text}", translation.Key, translation.Value);
            }

            var transText = e.Result.Translations[config.TargetLanguages[0]];
            int count = StringCount(transText, targets);
            if (count > segmentNumber)
            {
                //Console.WriteLine("Intermediate Sent to Synth");
                var transToSynth = CopyStringToLastOccurrence(transText, targets, segmentNumber, false);
                segmentNumber = count;

                logger.LogTrace("Speaking: {Text}", transToSynth);
                //Output TTS here
                await Task.Run(() => SendSynthesizedOutput(_lastTranscriptId, transToSynth));
            }
        };
        _recognizer.SessionStarted += (s, e) => logger.LogTrace("SessionStarted");
        _recognizer.SessionStopped += (s, e) => logger.LogTrace("SessionStopped");

        _receiveData = receiveData;
        _recognizer.Synthesizing += (s, e) =>
        {
            //var audio = e.Result.GetAudio();
            //await SendOutputAsync(_lastTranscriptId, audio);
        };
    }
    public async Task SendOneOffMessage(string message)
    {
        await SendSynthesizedOutput(_lastTranscriptId, message);
    }
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

    private async Task SendSynthesizedOutput(Guid translationId, string transToSynth)
    {
        using var result = await _speechSynthesizer.StartSpeakingTextAsync(transToSynth);
        using var audioDataStream = AudioDataStream.FromResult(result);
        byte[] audio = new byte[1600];
        int filledSize = 0;
        while ((filledSize = (int)audioDataStream.ReadData(audio)) > 0)
        {
            await SendOutputAsync(translationId, audio[..filledSize]);
        }
    }
    private async Task SendOutputAsync(Guid translationId, byte[] data)
    {
        await _receiveData(translationId, data);
    }
    public async Task SendDataAsync(byte[] data)
    {
        if (!_started)
        {
            _started = true;
            await _recognizer.StartContinuousRecognitionAsync();
        }
        _inputStream.Write(data);
    }
    public void Dispose()
    {
        try
        {
            _recognizer.StopContinuousRecognitionAsync();
        }
        finally
        {
            _recognizer.Dispose();
        }
    }
}

