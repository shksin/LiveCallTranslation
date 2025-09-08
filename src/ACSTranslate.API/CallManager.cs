using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks.Dataflow;
using ACSTranslate;

public class CallManager(
    CognitiveServicesAuth _cogAuth,
    ILogger<CallManager> _logger
)
{
    // Todo: race conditions here! Refactor to use ConcurrentDictionary or similar
    private readonly Dictionary<Guid, (WebSocket? WebSocket, CallState CallState)> _calls = [];
    private readonly BroadcastBlock<(Guid CallId, CallState CallState)> _callEvents = new(x => x);
    public async Task ConnectUserAsync(WebSocket ws, CancellationToken ct)
    {
        // TODO: Implement receive logic prior to agent taking connection
        Guid callId = Guid.NewGuid();
        _calls[callId] = (ws, CallState.UserConnected);
        TriggerUpdate(callId);
        _logger.LogInformation("User connected: {CallId}", callId);
        try
        {
            // In this demo version we just leave the user connection open indefinitely
            // The agent will control when audio is enabled and when the call ends
            await Task.Delay(-1, ct);
        }
        finally
        {
            // Force an update to disconnected when the user disconnects
            _calls[callId] = (null, CallState.Disconnected);
            TriggerUpdate(callId);
            _logger.LogInformation("User disconnected: {CallId}", callId);
        }
    }

    private void TriggerUpdate(Guid callId)
        => _callEvents.Post((callId, _calls[callId].CallState));

    private async Task CallEventsLoop(WebSocket ws, CancellationToken ct)
    {
        var bufferBlock = new BufferBlock<(Guid CallId, CallState CallState)>();
        using var link = _callEvents.LinkTo(bufferBlock);
        await foreach (var callEvent in bufferBlock.ReceiveAllAsync(ct))
        {
            if (ws.State != WebSocketState.Open) break;
            await ws.SendAsync(new
            {
                type = "update",
                updates = new[]
                {
                    new
                    {

                        id = callEvent.CallId.ToString(),
                        state = callEvent.CallState.ToString()
                    }
                }
            }, ct);
        }
    }

    private async Task SendAllCallStatus(WebSocket ws, CancellationToken ct)
    {
        var callUpdates = _calls.Where(x => x.Value.CallState != CallState.Disconnected).Select(x => new
        {

            id = x.Key.ToString(),
            state = x.Value.CallState.ToString()
        }).ToArray();
        await ws.SendAsync(new
        {
            type = "update",
            updates = callUpdates
        }, ct);
    }

    public async Task ConnectAgentAsync(WebSocket ws, CancellationToken ct)
    {
        var agentId = Guid.NewGuid();

        try
        {
            // Send available languages ASAP
            await ws.SendAsync(new
            {
                type = "languages",
                languages = LanguageConfig.ListLanguages()
            }, ct);

            // First up, configure subscriptions to call events
            var callEventUpdater = Task.Run(async () => await CallEventsLoop(ws, ct), ct);

            // Then send a update event with all current calls
            await SendAllCallStatus(ws, ct);

            _logger.LogInformation("Agent connected: {AgentId}", agentId);

            // Finally start the agent receive loop, which will allow the agent to connect to a user
            var buffer = new byte[64 * 1024];
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var json = await ws.ReceiveStringAsync(buffer, ct);
                if (json == null) break;

                _logger.LogInformation("Agent {AgentId} sent message: {Message}", agentId, json);
                var request = JsonNode.Parse(json);

                if (request?["type"]?.GetValue<string>() == "connect" &&
                    Guid.TryParse(request?["callId"]?.GetValue<string>(), out var callId))
                {
                    _logger.LogInformation("Agent {AgentId} connecting to call {CallId}", agentId, callId);

                    // First up we claim the call if it is available
                    if (!_calls.TryGetValue(callId, out var call) || call.CallState != CallState.UserConnected || call.WebSocket == null)
                    {
                        _logger.LogWarning("Agent {AgentId} attempted to connect to invalid call {CallId}", agentId, callId);
                        await SendAllCallStatus(ws, ct);
                    }
                    else
                    {
                        // Parse our call options
                        CallOptions? callOptions = null;
                        try
                        {
                            var optionsNode = request?["options"];
                            if (optionsNode is not null)
                            {
                                callOptions = optionsNode.Deserialize<CallOptions>();
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to deserialize call options for call {CallId}", callId);
                        }

                        if (callOptions == null)
                        {
                            await ws.SendAsync(new
                            {
                                type = "error",
                                message = "Invalid call options"
                            }, ct);
                            _logger.LogWarning("Agent {AgentId} sent invalid call options for call {CallId}", agentId, callId);
                            await SendAllCallStatus(ws, ct);
                            continue;
                        }

                        // Validate our target languages
                        var userLanguageConfig = LanguageConfig.GetLanguageConfig(callOptions?.UserLanguage);
                        var agentLanguageConfig = LanguageConfig.GetLanguageConfig(callOptions?.AgentLanguage);
                        var agentAudioOptions = callOptions?.AgentAudioOptions ?? new CallAudioOptions(false, true, false, false);

                        if (userLanguageConfig == null || agentLanguageConfig == null)
                        {
                            await ws.SendAsync(new
                            {
                                type = "error",
                                message = "Invalid language options"
                            }, ct);
                            _logger.LogWarning("Agent {AgentId} sent invalid language options for call {CallId}", agentId, callId);
                            await SendAllCallStatus(ws, ct);
                            continue;
                        }

                        // Update the call state to established
                        _calls[callId] = (null, CallState.CallEstablished);
                        TriggerUpdate(callId);

                        // Start the call
                        try
                        {
                            await RunCallAsync(
                                call.WebSocket,
                                userLanguageConfig,
                                ws,
                                agentLanguageConfig,
                                agentAudioOptions,
                                ct
                            );
                        }
                        catch (Exception e)
                        {
                            _logger.LogError(e, "Error during call {CallId}", callId);
                        }
                        finally
                        {
                            // When the call ends, update the state
                            // This is done in 2 places to cover both user and agent disconnects
                            _calls[callId] = (null, CallState.Disconnected);
                            TriggerUpdate(callId);
                            _logger.LogInformation("Agent {AgentId} ended call: {CallId}", agentId, callId);
                            await call.WebSocket.BestEffortSendAsync(new { type = "disconnect" }, ct);
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("Agent {AgentId} sent unknown message: {Message}", agentId, request?.ToJsonString());
                }
            }
        }
        finally
        {
            _logger.LogInformation("Agent disconnected: {AgentId}", agentId);
        }
    }

    private async Task RunCallAsync(
        WebSocket userWs,
        LanguageConfig userLanguage,
        WebSocket agentWs,
        LanguageConfig agentLanguage,
        CallAudioOptions agentAudioOptions,
        CancellationToken ct)
    {
        // We have 2 receive loops running in parallel, if either ends the call we end the other leg
        bool enableAudio = false;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Func<byte[], Task>? userReceiveCallback = null;
        var userReceive = ReceiveLoopAsync(userWs, async (data) =>
        {
            if (!enableAudio) return;
            await (userReceiveCallback?.Invoke(data) ?? Task.CompletedTask);
        }, cts.Token);
        Func<byte[], Task>? agentReceiveCallback = null;
        var agentReceive = ReceiveLoopAsync(agentWs, async (data) =>
        {
            if (!enableAudio) return;
            await (agentReceiveCallback?.Invoke(data) ?? Task.CompletedTask);
        }, cts.Token);

        // Tell our clients to both start sending audio so we are ready to go
        await userWs.SendAsync(new { type = "enable" }, ct);
        await agentWs.SendAsync(new { type = "enable" }, ct);

        // Set up our 2 translators
        using var userToAgentTranslator = await TranslatorInstance.CreateAsync(
            userLanguage,
            agentLanguage,
            _cogAuth
        );
        userToAgentTranslator.AttachDebugLogging(_logger);
        userToAgentTranslator.AttachTranscribeOutput(async (originalText, translatedText, isFinal) =>
        {
            await agentWs.SendAsync(new
            {
                type = "transcription",
                source = "user",
                originalText,
                translatedText,
                isFinal
            }, ct);
        });
        userToAgentTranslator.AttachSpeechOutput(async (data) =>
        {
            await agentWs.SendAsync(new
            {
                type = "audio",
                data = Convert.ToBase64String(data)
            }, ct);
        });
        userReceiveCallback = (data) =>
        {
            userToAgentTranslator.SendData(data);
            return Task.CompletedTask;
        };

        using var agentToUserTranslator = await TranslatorInstance.CreateAsync(
            agentLanguage,
            userLanguage,
            _cogAuth
        );
        agentToUserTranslator.AttachDebugLogging(_logger);
        agentToUserTranslator.AttachTranscribeOutput(async (originalText, translatedText, isFinal) =>
        {
            await agentWs.SendAsync(new
            {
                type = "transcription",
                source = "agent",
                originalText,
                translatedText,
                isFinal
            }, ct);
        });
        agentToUserTranslator.AttachSpeechOutput(async (data) =>
        {
            await userWs.SendAsync(new
            {
                type = "audio",
                data = Convert.ToBase64String(data)
            }, ct);
        });
        agentReceiveCallback = (data) =>
        {
            agentToUserTranslator.SendData(data);
            return Task.CompletedTask;
        };

        // Send any welcome messages
        SendConnected(userLanguage, agentToUserTranslator, userWs);
        SendConnected(agentLanguage, userToAgentTranslator, agentWs);

        // Finally allow audio to flow to the translators
        enableAudio = true;

        // Wait for either receive loop to complete
        await Task.WhenAny(userReceive, agentReceive);
        cts.Cancel();

        // Finally we force state with disconnect messages to both sides if they are still connected
        await userWs.BestEffortSendAsync(new { type = "disconnect" }, ct);
        await agentWs.BestEffortSendAsync(new { type = "disconnect" }, ct);
    }

    // We just throw this on a separate thread to avoid blocking the main call logic
    private void SendConnected(LanguageConfig languageConfig, TranslatorInstance ts, WebSocket ws) => Task.Run(async () =>
    {
        if (string.IsNullOrWhiteSpace(languageConfig.ConnectedMessage)) return;
        await ts.SpeakAsync(languageConfig.ConnectedMessage, async (data) =>
        {
            await ws.SendAsync(new
            {
                type = "audio",
                data = Convert.ToBase64String(data)
            }, CancellationToken.None);
        });
    });

    private async Task ReceiveLoopAsync(WebSocket ws, Action<byte[]> audioCallback, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var json = await ws.ReceiveStringAsync(buffer, ct);
            if (json == null) break;

            var request = JsonNode.Parse(json);
            var requestType = request?["type"]?.GetValue<string>();

            if (requestType == "disconnect") break;
            else if (requestType == "audio")
            {
                var audioDataString = request?["data"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(audioDataString))
                {
                    var audioData = Convert.FromBase64String(audioDataString);
                    audioCallback(audioData);
                }
            }
            else
            {
                _logger.LogWarning("Received unknown message: {Message}", request?.ToJsonString());
            }
        }
    }
}
