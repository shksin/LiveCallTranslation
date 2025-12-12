using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ACSTranslate;

public class ACSWebSocketHandler
{
    private readonly CallService _callService;
    private readonly CognitiveServicesAuth _cogAuth;
    private readonly ILogger<ACSWebSocketHandler> _logger;

    public ACSWebSocketHandler(
        CallService callService,
        CognitiveServicesAuth cogAuth,
        ILogger<ACSWebSocketHandler> logger)
    {
        _callService = callService;
        _cogAuth = cogAuth;
        _logger = logger;
    }

    public async Task HandleACSStreamAsync(Guid callId, WebSocket webSocket, CancellationToken cancellationToken)
    {
        var call = await _callService.GetCallAsync(callId);
        if (call == null)
        {
            _logger.LogWarning("Call {CallId} not found", callId);
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Call not found", cancellationToken);
            return;
        }

        _logger.LogInformation("ACS WebSocket connection established for call {CallId}", callId);

        // Get language configuration
        var userLanguage = LanguageConfig.GetLanguageConfig(call.UserLanguage);
        var agentLanguage = LanguageConfig.GetLanguageConfig("en-US"); // Default agent language

        if (userLanguage == null || agentLanguage == null)
        {
            _logger.LogError("Invalid language configuration for call {CallId}", callId);
            await webSocket.CloseAsync(WebSocketCloseStatus.InvalidPayloadData, "Invalid language", cancellationToken);
            return;
        }

        // Create bidirectional translator
        var translator = await TranslatorInstance.CreateAsync(userLanguage, agentLanguage, _cogAuth);
        translator.AttachDebugLogging(_logger);

        var buffer = new byte[64 * 1024];
        var outputBuffer = new List<byte>();

        try
        {
            while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    await ProcessACSMessageAsync(json, translator, webSocket, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling ACS WebSocket for call {CallId}", callId);
        }
        finally
        {
            translator.Dispose();
            await _callService.SetCallStatusAsync(callId, CallStatus.Ended);
            _logger.LogInformation("ACS WebSocket connection closed for call {CallId}", callId);
        }
    }

    private async Task ProcessACSMessageAsync(string json, TranslatorInstance translator, WebSocket webSocket, CancellationToken cancellationToken)
    {
        try
        {
            var message = JsonSerializer.Deserialize<ACSStreamingMessage>(json);
            
            if (message?.kind == "AudioData" && message.audioData?.data != null)
            {
                var audioBytes = Convert.FromBase64String(message.audioData.data);
                translator.SendData(audioBytes);
                
                // Set up speech output to send back to ACS
                translator.AttachSpeechOutput(async (translatedAudio) =>
                {
                    var response = new
                    {
                        kind = "AudioData",
                        audioData = new
                        {
                            data = Convert.ToBase64String(translatedAudio)
                        }
                    };
                    
                    var responseJson = JsonSerializer.Serialize(response);
                    var responseBytes = Encoding.UTF8.GetBytes(responseJson);
                    
                    if (webSocket.State == WebSocketState.Open)
                    {
                        await webSocket.SendAsync(
                            new ArraySegment<byte>(responseBytes),
                            WebSocketMessageType.Text,
                            true,
                            cancellationToken);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process ACS message: {Json}", json);
        }
    }

    private class ACSStreamingMessage
    {
        public string? kind { get; set; }
        public AudioData? audioData { get; set; }
    }

    private class AudioData
    {
        public string? data { get; set; }
    }
}
