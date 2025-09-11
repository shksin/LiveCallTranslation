using System.Collections.ObjectModel;
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
    private readonly Dictionary<Guid, (UserWebSocket? WebSocket, CallState CallState)> _calls = [];
    private readonly BroadcastBlock<(Guid CallId, CallState CallState)> _callEvents = new(x => x);
    public async Task ConnectUserAsync(UserWebSocket ws, CancellationToken ct)
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

    private async Task CallEventsLoop(AgentWebSocket ws, CancellationToken ct)
    {
        var bufferBlock = new BufferBlock<(Guid CallId, CallState CallState)>();
        using var link = _callEvents.LinkTo(bufferBlock);
        await foreach (var callEvent in bufferBlock.ReceiveAllAsync(ct))
        {
            if (!ws.Connected) break;
            await ws.SendEventUpdates(new CallEventUpdate(callEvent.CallId, callEvent.CallState.ToString()), ct);
        }
    }

    private async Task SendAllCallStatus(AgentWebSocket ws, CancellationToken ct)
        => await ws.SendEventUpdates(_calls
            .Where(x => x.Value.CallState != CallState.Disconnected)
            .Select(x => new CallEventUpdate(x.Key, x.Value.CallState.ToString())), ct);

    public async Task ConnectAgentAsync(AgentWebSocket ws, CancellationToken ct)
    {
        var agentId = Guid.NewGuid();

        try
        {
            // Send available languages ASAP
            await ws.SendLanguagesAsync(LanguageConfig.ListLanguages(), ct);

            // First up, configure subscriptions to call events
            var callEventUpdater = Task.Run(async () => await CallEventsLoop(ws, ct), ct);

            // Then send a update event with all current calls
            await SendAllCallStatus(ws, ct);

            _logger.LogInformation("Agent connected: {AgentId}", agentId);

            // Finally start the agent receive loop, which will allow the agent to connect to a user
            var buffer = new byte[64 * 1024];
            while (ws.Connected && !ct.IsCancellationRequested)
            {
                var json = await ws.ReceiveStringAsync(buffer, ct);
                if (json == null) break;

                var request = JsonNode.Parse(json);

                if (request?["type"]?.GetValue<string>() == "ping" ||
                    request?["type"]?.GetValue<string>() == "disconnect" ||
                    request?["type"]?.GetValue<string>() == "audio" ||
                    request?["type"]?.GetValue<string>() == "audioOptions")
                {
                    // We can safely ignore these events!
                }
                else if (request?["type"]?.GetValue<string>() == "connect" &&
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
                            await ws.SendErrorAsync("Invalid call options", ct);
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
                            await ws.SendErrorAsync("Invalid language options", ct);
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
                            await call.WebSocket.SendDisconnectAsync(ct);
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

    private static void SetAudioValuesFromOptions(DynamicMixer<AudioChannels> mixer, CallAudioOptions options)
    {
        mixer.SetAudioOptions(AudioChannels.UserOriginal, options.UserOriginalAudio ? 0.8f : 0.0f);
        mixer.SetAudioOptions(AudioChannels.UserTranslated, options.UserTranslatedAudio ? 1.0f : 0.0f);
        mixer.SetAudioOptions(AudioChannels.AgentOriginal, options.AgentOriginalAudio ? 0.5f : 0.0f);
        mixer.SetAudioOptions(AudioChannels.AgentTranslated, options.AgentTranslatedAudio ? 0.8f : 0.0f);
    }

    private async Task RunCallAsync(
        UserWebSocket userWs,
        LanguageConfig userLanguage,
        AgentWebSocket agentWs,
        LanguageConfig agentLanguage,
        CallAudioOptions agentAudioOptions,
        CancellationToken ct)
    {
        bool enableAudio = false;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Audio mixing - TODO: Document, these magic numbers shouldn't be here!
        DynamicMixer<AudioChannels> mixer = new(new(16000, 16, 1), 120, 50);
        var mixerLoop = Task.Run(async () => await mixer.SendMixedAudioAsync(agentWs, cts.Token), cts.Token);
        SetAudioValuesFromOptions(mixer, agentAudioOptions);

        // Watch the user original and agent original channels for latency and skip audio if needed
        mixer.MonitorLatency(AudioChannels.UserOriginal);
        mixer.MonitorLatency(AudioChannels.AgentOriginal);

        // We have 2 receive loops running in parallel, if either ends the call we end the other leg
        Action<byte[]>? userReceiveCallback = null;
        var userReceive = userWs.ReceiveLoopAsync(new Dictionary<string, Func<JsonNode, bool>>()
            {
                {"disconnect", (_) => false},
                {"audio", (request) =>
                    {
                        if (!enableAudio) return true;
                        var audioDataString = request?["data"]?.GetValue<string>();
                        if (string.IsNullOrEmpty(audioDataString)) return true;
                        var data = Convert.FromBase64String(audioDataString);
                        mixer.AddAudio(AudioChannels.UserOriginal, data);
                        userReceiveCallback?.Invoke(data);
                        return true;
                    }
                }
            },
            cts.Token
        );
        Action<byte[]>? agentReceiveCallback = null;
        var agentReceive = agentWs.ReceiveLoopAsync(
            new Dictionary<string, Func<JsonNode, bool>>()
            {
                {"disconnect", (_) => false},
                {"audio", (request) =>
                    {
                        if (!enableAudio) return true;
                        var audioDataString = request?["data"]?.GetValue<string>();
                        if (string.IsNullOrEmpty(audioDataString)) return true;
                        var data = Convert.FromBase64String(audioDataString);
                        mixer.AddAudio(AudioChannels.AgentOriginal, data);
                        agentReceiveCallback?.Invoke(data);
                        return true;
                    }
                },
                {"audioOptions", (request) =>
                    {
                        var options = request?["options"]?.Deserialize<CallAudioOptions>();
                        if (options != null)
                        {
                            SetAudioValuesFromOptions(mixer, options);
                        }
                        return true;
                    }
                },
            },
            cts.Token
        );

        // Tell our clients to both start sending audio so we are ready to go
        await userWs.SendEnableAsync(cts.Token);
        await agentWs.SendEnableAsync(cts.Token);

        // Set up our 2 translators
        using var userToAgentTranslator = await TranslatorInstance.CreateAsync(
            userLanguage,
            agentLanguage,
            _cogAuth
        );
        userToAgentTranslator.AttachDebugLogging(_logger);
        userToAgentTranslator.AttachTranscribeOutput(async (originalText, translatedText, isFinal)
            => await agentWs.SendTranscriptionAsync("user", originalText, translatedText, isFinal, cts.Token)
        );
        userToAgentTranslator.AttachSpeechOutput((data) =>
        {
            mixer.AddAudio(AudioChannels.UserTranslated, data);
            return Task.CompletedTask;
        });
        userReceiveCallback = userToAgentTranslator.SendData;

        using var agentToUserTranslator = await TranslatorInstance.CreateAsync(
            agentLanguage,
            userLanguage,
            _cogAuth
        );
        agentToUserTranslator.AttachDebugLogging(_logger);
        agentToUserTranslator.AttachTranscribeOutput(async (originalText, translatedText, isFinal)
            => await agentWs.SendTranscriptionAsync("agent", originalText, translatedText, isFinal, cts.Token)
        );
        agentToUserTranslator.AttachSpeechOutput(async (data) =>
        {
            mixer.AddAudio(AudioChannels.AgentTranslated, data);
            await userWs.SendAudioAsync(data, cts.Token);
        });
        agentReceiveCallback = agentToUserTranslator.SendData;

        // Send any welcome messages
        SendConnected(userLanguage, agentToUserTranslator, userWs);
        SendConnected(agentLanguage, userToAgentTranslator, agentWs);

        // Finally allow audio to flow to the translators
        enableAudio = true;

        // Wait for either receive loop to complete
        await Task.WhenAny(userReceive, agentReceive);
        cts.Cancel();

        // Finally we force state with disconnect messages to both sides if they are still connected
        await userWs.SendDisconnectAsync(ct);
        await agentWs.SendDisconnectAsync(ct);
    }

    // We just throw this on a separate thread to avoid blocking the main call logic
    private void SendConnected(LanguageConfig languageConfig, TranslatorInstance ts, AudioWebSocket ws) => Task.Run(async () =>
    {
        if (string.IsNullOrWhiteSpace(languageConfig.ConnectedMessage)) return;
        await ts.SpeakAsync(languageConfig.ConnectedMessage, async (data)
            => await ws.SendAudioAsync(data, CancellationToken.None)
        );
    });
}
public enum AudioChannels
{
    UserOriginal,
    UserTranslated,
    AgentOriginal,
    AgentTranslated
}