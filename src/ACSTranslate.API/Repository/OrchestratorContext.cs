using Microsoft.EntityFrameworkCore;

namespace ACSTranslate;

public class OrchestratorContext(DbContextOptions<OrchestratorContext> options)
    : DbContext(options)
{
    public DbSet<Call> Calls { get; set; } = null!;
    public DbSet<Transcription> Transcriptions { get; set; } = null!;
}

public class Call
{
    public Guid Id { get; set; }
    public CallStatus Status { get; set; }
    public string? CallerId { get; set; }
    public DateTimeOffset CallReceived { get; set; }
    public string UserLanguage { get; set; } = null!;
}

public enum CallStatus
{
    New = 0,
    Waiting = 1,
    Connecting = 3,
    Answered = 4,
    Ended = 5,
    Abandoned = 6,
}

public class Transcription
{
    public Guid Id { get; set; }
    public Guid CallId { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public DateTimeOffset FinalizedAt { get; set; }
    public string User { get; set; } = null!;
    public string NativeText { get; set; } = null!;
    public string TranslatedText { get; set; } = null!;
}