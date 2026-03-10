using Microsoft.EntityFrameworkCore;

namespace ACSTranslate;

public class OrchestratorContext : DbContext
{
    public OrchestratorContext(DbContextOptions<OrchestratorContext> options)
        : base(options)
    {
    }

    public DbSet<Call> Calls { get; set; } = null!;
}

public class Call
{
    public Guid Id { get; set; }
    public CallStatus Status { get; set; }
    public string? CallerId { get; set; }
    public DateTimeOffset CallReceived { get; set; }
    public string UserLanguage { get; set; } = "en-US";
    public string? IncomingCallContext { get; set; }
    public string? CallConnectionId { get; set; }
}

public enum CallStatus
{
    New = 0,
    Waiting = 1,
    Answered = 2,
    Ended = 3
}
