using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace ACSTranslate;

[Route("api/calls")]
[ApiController]
public class CallsController(
    CallService _callService,
    ACSService _acsService,
    TranscribeService _transcribeService,
    IAudioStorage _audioStorage,
    ILogger<CallsController> _logger
) : ControllerBase
{
    [HttpPost("{callId}/log")]
    public async Task<ActionResult> Log(Guid callId)
    {
        using StreamReader bodyReader = new(Request.Body);
        var body = await bodyReader.ReadToEndAsync();
        _logger.LogInformation("Call {CallId} log: {Log}", callId, body);
        return Ok();
    }
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CallViewModel>>> GetAvailableCalls()
    {
        return Ok(await _callService.GetAvailableCallsAsync());
    }

    [HttpGet("events")]
    public async Task GetCallEvents(CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        await foreach (var callEvent in _callService.GetCallEventsAsync(cancellationToken))
        {
            await Response.WriteAsync($"data: {callEvent.ToJson()}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }

    [HttpGet("{callId:guid}")]
    public async Task<ActionResult<CallViewModel>> GetCall(Guid callId)
    {
        return Ok(await _callService.GetCallAsync(callId));
    }

    [HttpGet("{callId:guid}/transcription")]
    public async Task GetCallTranscription(Guid callId, CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        await foreach (var message in _transcribeService.GetStreamer(callId).GetMessagesAsync(cancellationToken))
        {
            await Response.WriteAsync($"data: {TranscriptionEventModel.FromTranscribeRecord(message).ToJson()}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }

    [HttpGet("{callId:guid}/transcription/{transcriptionId:guid}/audio/translated")]
    public async Task<IActionResult> GetTranslatedTranscriptionAudio(Guid callId, Guid transcriptionId)
    {
        var audioStream = await _audioStorage.GetAudioStreamAsync(callId, transcriptionId, AudioType.Translated);
        return File(audioStream, "audio/x-wav", $"{callId}_{transcriptionId}.wav");
    }
    [HttpGet("{callId:guid}/transcription/{transcriptionId:guid}/audio/native")]
    public Task<IActionResult> GetNativeTranscriptionAudio(Guid callId, Guid transcriptionId)
    {
        throw new NotImplementedException();
    }

    [HttpGet("acsauth")]
    public async Task<ActionResult<AcsAuth>> GetAcsAuth()
    {
        var serverId = await _acsService.GetServerApplicationACSIdentity();
        var token = await _acsService.GetACSToken();
        return Ok(new AcsAuth(token.Token, token.ExpiresOn, serverId));
    }

    [HttpGet("user/voip/auth")]
    public async Task<ActionResult<UserVoipAuth>> GetUserVoipAuth()
    {
        var userApp = await _acsService.GetUserApplicationACSIdentity();
        var token = await _acsService.GetACSToken();
        return Ok(new UserVoipAuth(token.Token, token.ExpiresOn, userApp));
    }
}

public record AcsAuth(string Token, DateTimeOffset ExpiresOn, string ServerId);
public record UserVoipAuth(string Token, DateTimeOffset ExpiresOn, string EndpointToDial);
public record TranscriptionEventModel(
    string Id,
    DateTimeOffset SentAt,
    DateTimeOffset? FinalizedAt,
    string User,
    string NativeText,
    string TranslatedText
)
{
    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);
    public static TranscriptionEventModel FromTranscribeRecord(TranscribeRecord record)
        => new(
            record.ID.ToString(),
            record.SentAt,
            record.FinalizedAt,
            record.User.ToString(),
            record.NativeText,
            record.TranslatedText
        );
    public string ToJson()
        => JsonSerializer.Serialize(this, _options);
}