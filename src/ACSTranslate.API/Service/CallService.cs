using System.Text.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ACSTranslate;

public class CallService
{
    private readonly IDbContextFactory<OrchestratorContext> _dbFactory;
    private readonly ACSService _acsService;
    private readonly InboundConfig _config;
    private readonly ILogger<CallService> _logger;

    public CallService(
        IDbContextFactory<OrchestratorContext> dbFactory,
        ACSService acsService,
        InboundConfig config,
        ILogger<CallService> logger)
    {
        _dbFactory = dbFactory;
        _acsService = acsService;
        _config = config;
        _logger = logger;
    }

    public async Task<Call> CreateCallAsync(string incomingCallContext, string callerId, string language)
    {
        using var db = _dbFactory.CreateDbContext();
        var call = new Call
        {
            Id = Guid.NewGuid(),
            Status = CallStatus.New,
            CallerId = callerId,
            CallReceived = DateTimeOffset.UtcNow,
            UserLanguage = language,
            IncomingCallContext = incomingCallContext
        };
        db.Calls.Add(call);
        await db.SaveChangesAsync();

        // Answer the call
        var callbackEndpoint = new Uri(_config.BaseUri, $"/api/calls/{call.Id}/callback");
        await _acsService.AnswerCallAsync(incomingCallContext, callbackEndpoint);

        await SetCallStatusAsync(call.Id, CallStatus.Waiting);
        
        _logger.LogInformation("Created and answered call {CallId} from {CallerId}", call.Id, callerId);
        return call;
    }

    public async Task<Call?> GetCallAsync(Guid callId)
    {
        using var db = _dbFactory.CreateDbContext();
        return await db.Calls.FindAsync(callId);
    }

    public async Task<IEnumerable<Call>> GetWaitingCallsAsync()
    {
        using var db = _dbFactory.CreateDbContext();
        return await db.Calls
            .Where(c => c.Status == CallStatus.Waiting)
            .OrderBy(c => c.CallReceived)
            .ToListAsync();
    }

    public async Task SetCallStatusAsync(Guid callId, CallStatus status)
    {
        using var db = _dbFactory.CreateDbContext();
        var call = await db.Calls.FindAsync(callId);
        if (call != null)
        {
            call.Status = status;
            await db.SaveChangesAsync();
            _logger.LogInformation("Call {CallId} status changed to {Status}", callId, status);
        }
    }
}
