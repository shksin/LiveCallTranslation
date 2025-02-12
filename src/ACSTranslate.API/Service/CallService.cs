using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using Microsoft.EntityFrameworkCore;

namespace ACSTranslate;

public class CallService(
    IDbContextFactory<OrchestratorContext> _dbFactory,
    ACSService _acsService,
    InboundConfig _config,
    ILogger<CallService> _logger
)
{
    public async Task<IEnumerable<CallViewModel>> GetAvailableCallsAsync()
    {
        using var db = _dbFactory.CreateDbContext();
        return await db.Calls
                .Where(x => x.Status == CallStatus.Waiting)
                .Select(x => new CallViewModel(x))
                .ToListAsync();
    }

    public async Task<CallViewModel> GetCallAsync(Guid callId)
    {
        using var db = _dbFactory.CreateDbContext();
        return new CallViewModel(
                await db.Calls.FindAsync(callId)
                ?? throw new KeyNotFoundException()
            );
    }

    public async Task<CallViewModel> CreateCallAsync(string incomingCallContext, string callerId, TranslationConfig config)
    {
        using var db = _dbFactory.CreateDbContext();
        var call = new Call
        {
            Id = Guid.NewGuid(),
            Status = CallStatus.New,
            CallerId = callerId,
            CallReceived = DateTimeOffset.UtcNow,
            UserLanguage = config.UserLanguage
        };
        db.Calls.Add(call);
        await db.SaveChangesAsync();

        var callbackEndpoint = new Uri(_config.BaseUri, $"/api/calls/{call.Id}/log");
        var websocketEndpoint = new Uri(_config.BaseWsUri, $"/ws/audio/{call.Id}/user");
        await _acsService.AnswerCallAsync(incomingCallContext, callbackEndpoint, websocketEndpoint);

        var callModel = new CallViewModel(call);
        RaiseCallChangeEvent(callModel);
        return callModel;
    }

    public async Task<CallViewModel> ConnectCallAsync(Guid callId, string incomingCallContext)
    {
        var call = await SetCallToConnecting(callId);

        var callbackEndpoint = new Uri(_config.BaseUri, $"/api/calls/{call.Id}/log");
        var websocketEndpoint = new Uri(_config.BaseWsUri, $"/ws/audio/{call.Id}/agent");

        await _acsService.AnswerCallAsync(incomingCallContext, callbackEndpoint, websocketEndpoint);

        return call;
    }

    public async Task<CallViewModel> SetCallToWaiting(Guid callId)
        => await SetCallState(callId, CallStatus.New, CallStatus.Waiting);

    public async Task<CallViewModel> SetCallToConnecting(Guid callId)
        => await SetCallState(callId, CallStatus.Waiting, CallStatus.Connecting);
    public async Task<CallViewModel> SetCallToAnswered(Guid callId)
        => await SetCallState(callId, CallStatus.Connecting, CallStatus.Answered);

    private async Task<CallViewModel> SetCallState(Guid callId, CallStatus expectedStatus, CallStatus newStatus)
    {
        using var db = _dbFactory.CreateDbContext();
        var call = await db.Calls.FindAsync(callId) ?? throw new KeyNotFoundException();

        if (call.Status == newStatus) return new CallViewModel(call);
        if (call.Status != expectedStatus) throw new Exception($"Call can not be set to {newStatus}");

        _logger.LogInformation("Setting call {CallId} from {OldStatus} to {NewStatus}", callId, call.Status, newStatus);

        call.Status = newStatus;
        await db.SaveChangesAsync();

        var callModel = new CallViewModel(call);
        RaiseCallChangeEvent(callModel);
        return callModel;
    }

    // TODO: Refactor into an eventing service, also, fix lack of cloning
    private readonly BroadcastBlock<CallViewModel> _callEventBlock = new(x => x);
    // TODO: Do we need to filter to only available calls?
    private void RaiseCallChangeEvent(CallViewModel call)
        => _callEventBlock.Post(call);

    public async IAsyncEnumerable<CallViewModel> GetCallEventsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var bufferBlock = new BufferBlock<CallViewModel>();
        using var link = _callEventBlock.LinkTo(bufferBlock);
        // TODO: Do we need to send all calls or just available calls?
        foreach (var call in await GetAvailableCallsAsync())
        {
            yield return call;
        }
        await foreach (var call in bufferBlock.ReceiveAllAsync(cancellationToken))
        {
            yield return call;
        }
    }
}

public record CallViewModel(Guid Id, string Status, string CallerId, DateTimeOffset CallReceived, string? UserLanguage = null)
{
    public CallViewModel(Call call)
        : this(call.Id, call.Status.ToString(), MaskPhoneNumber(call.CallerId) ?? "Unknown", call.CallReceived, call.UserLanguage)
    {
    }
    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);
    public string ToJson()
        => JsonSerializer.Serialize(this, _options);

    private static string? MaskPhoneNumber(string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber) || !phoneNumber.Trim().Trim('+').All(char.IsNumber)) return phoneNumber;
        return phoneNumber?.Length > 6 ? $"{phoneNumber[..4]}{new string('*', phoneNumber.Length - 7)}{phoneNumber[^3..]}" : phoneNumber;
    }
}