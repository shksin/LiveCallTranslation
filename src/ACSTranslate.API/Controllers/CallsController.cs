using Microsoft.AspNetCore.Mvc;

namespace ACSTranslate;

[Route("api/calls")]
[ApiController]
public class CallsController : ControllerBase
{
    private readonly CallService _callService;
    private readonly ACSService _acsService;
    private readonly ILogger<CallsController> _logger;

    public CallsController(
        CallService callService,
        ACSService acsService,
        ILogger<CallsController> logger)
    {
        _callService = callService;
        _acsService = acsService;
        _logger = logger;
    }

    [HttpGet("waiting")]
    public async Task<ActionResult<IEnumerable<object>>> GetWaitingCalls()
    {
        var calls = await _callService.GetWaitingCallsAsync();
        return Ok(calls.Select(c => new
        {
            c.Id,
            c.CallerId,
            c.CallReceived,
            c.UserLanguage
        }));
    }

    [HttpPost("{callId}/callback")]
    public async Task<IActionResult> CallCallback(Guid callId)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        _logger.LogInformation("Call {CallId} callback: {Body}", callId, body);
        return Ok();
    }

    [HttpGet("acs/config")]
    public IActionResult GetACSConfig()
    {
        return Ok(new
        {
            isConfigured = _acsService.IsConfigured,
            inboundNumber = _acsService.IsConfigured ? _acsService.InboundNumber : null
        });
    }

    [HttpGet("acs/token")]
    public async Task<ActionResult> GetACSToken()
    {
        if (!_acsService.IsConfigured)
        {
            return BadRequest(new { error = "ACS is not configured" });
        }

        try
        {
            var token = await _acsService.GetACSTokenAsync();
            return Ok(new
            {
                token = token.Token,
                expiresOn = token.ExpiresOn
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get ACS token");
            return StatusCode(500, new { error = "Failed to get ACS token" });
        }
    }
}
