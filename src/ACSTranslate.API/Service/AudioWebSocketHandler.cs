using System.Net.WebSockets;
using System.Text;
using Azure.Communication.CallAutomation;

namespace ACSTranslate;

public class AudioWebSocketHandler(
    AzureAISpeechTranslatorFactory _translatorFactory,
    TranslationConfig _translationConfig,
    Action<Guid, AudioType, byte[]>? _storeAudio,
    ILogger _logger
)
{
    private AzureAISpeechInput? _userSend;
    private AzureAISpeechInput? _agentSend;
    private readonly CancellationTokenSource _cts = new();
    public async Task ConnectUser(
        WebSocket ws,
        TranscribeStreamer transcriptionStreamer,
        string welcomeMessage,
        string connectedMessage,
        CancellationToken ct
    )
    {
        var combined = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token).Token;

        var translatorOptions = _translationConfig.UserOptions;

        // This is inverted as it is the output we are capturing
        // TODO: Refactor where this sits
        var transcription = transcriptionStreamer.StartTranscribe(TranscribeUser.Agent);

        _userSend = await _translatorFactory.CreateTranslatorAsync(translatorOptions, transcription, async (id, data) =>
        {
            _storeAudio?.Invoke(id, AudioType.Translated, data);
            if (combined.IsCancellationRequested) return;
            var audioData = OutStreamingData.GetAudioDataForOutbound(data);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(audioData);
            await ws.SendAsync(jsonBytes, WebSocketMessageType.Text, endOfMessage: true, combined);
        });

        // Send a welcome message to the user in the background
        await _userSend.SendOneOffMessage(welcomeMessage);
        // Wait for us to be connected and send a connected message
        _ = Task.Run(async () =>
        {
            while (_agentSend == null) await Task.Delay(250, combined);
            if (combined.IsCancellationRequested) return;
            await _userSend.SendOneOffMessage(connectedMessage);
        }, combined);

        ArraySegment<byte> buffer = new byte[4096];
        try
        {
            while (!combined.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(buffer, combined);
                if (_agentSend == null) continue;

                string data = Encoding.UTF8.GetString(buffer.Slice(0, result.Count));
                var input = StreamingData.Parse(data);
                if (input is AudioData audioData)
                {
                    await _agentSend.SendDataAsync(audioData.Data);
                }
            }
        }
        finally
        {
            _logger.LogInformation("Closing user connection");
            _cts.Cancel();
            var userSend = _userSend;
            _userSend = null;
            userSend.Dispose();
        }
        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
    }
    public async Task ConnectAgent(WebSocket ws, TranscribeStreamer transcriptionStreamer, string connectedMessage, CancellationToken ct)
    {
        var combined = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token).Token;

        var translatorOptions = _translationConfig.AgentOptions;

        // This is inverted as it is the output we are capturing
        // TODO: Refactor where this sits
        var transcription = transcriptionStreamer.StartTranscribe(TranscribeUser.User);

        _agentSend = await _translatorFactory.CreateTranslatorAsync(translatorOptions, transcription, async (id, data) =>
        {
            _storeAudio?.Invoke(id, AudioType.Translated, data);
            if (combined.IsCancellationRequested) return;
            var audioData = OutStreamingData.GetAudioDataForOutbound(data);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(audioData);
            await ws.SendAsync(jsonBytes, WebSocketMessageType.Text, endOfMessage: true, combined);
        });

        // Wait for us to be connected and send a connected message
        _ = Task.Run(async () =>
        {
            while (_userSend == null) await Task.Delay(250, combined);
            if (combined.IsCancellationRequested) return;
            await _agentSend.SendOneOffMessage(connectedMessage);
        }, combined);

        ArraySegment<byte> buffer = new byte[4096];
        try
        {
            while (!combined.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(buffer, combined);
                if (_userSend == null) continue;

                string data = Encoding.UTF8.GetString(buffer.Slice(0, result.Count));
                var input = StreamingData.Parse(data);
                if (input is AudioData audioData)
                {
                    await _userSend.SendDataAsync(audioData.Data);
                }
            }
        }
        finally
        {
            _logger.LogInformation("Closing agent connection");
            _cts.Cancel();
            var agentSend = _agentSend;
            _agentSend = null;
            agentSend.Dispose();
        }
        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
    }
}
