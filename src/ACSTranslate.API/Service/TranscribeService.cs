using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Tasks.Dataflow;
using Microsoft.EntityFrameworkCore;

namespace ACSTranslate;
public class TranscribeService(
    IDbContextFactory<OrchestratorContext> _dbFactory
)
{
    private readonly ConcurrentDictionary<Guid, TranscribeStreamer> _streamers = new();
    public TranscribeStreamer GetStreamer(Guid callId)
        => _streamers.GetOrAdd(callId, _ => new TranscribeStreamer(
            async record => await SaveTranscriptAsync(callId, record)
        ));

    public async Task SaveTranscriptAsync(Guid callId, TranscribeRecord transcriptRecord)
    {
        try
        {
            using var db = _dbFactory.CreateDbContext();
            var transcript = new Transcription
            {
                CallId = callId,
                SentAt = transcriptRecord.SentAt,
                FinalizedAt = transcriptRecord.FinalizedAt ?? transcriptRecord.SentAt,
                User = transcriptRecord.User.ToString(),
                NativeText = transcriptRecord.NativeText,
                TranslatedText = transcriptRecord.TranslatedText
            };
            db.Transcriptions.Add(transcript);
            await db.SaveChangesAsync();
        }
        catch { } // Todo: Log error
    }
}
public class TranscribeUpdater(
    Dictionary<Guid, TranscribeRecord> _messages,
    BroadcastBlock<TranscribeRecord> _broadcastBlock,
    Func<TranscribeRecord, Task> _transcribeSaveCallback,
    Guid _id) : ITranscribeUpdater
{
    public Guid UpdateText(string? nativeText = null, string? translatedText = null)
    {
        _messages[_id] = _messages[_id] with
        {
            NativeText = nativeText ?? _messages[_id].NativeText,
            TranslatedText = translatedText ?? _messages[_id].TranslatedText
        };
        _broadcastBlock.Post(_messages[_id]);
        return _id;
    }
    public Guid FinalizeText(string? nativeText = null, string? translatedText = null)
    {
        _messages[_id] = _messages[_id] with
        {
            FinalizedAt = DateTimeOffset.Now,
            NativeText = nativeText ?? _messages[_id].NativeText,
            TranslatedText = translatedText ?? _messages[_id].TranslatedText
        };
        _broadcastBlock.Post(_messages[_id]);
        _ = _transcribeSaveCallback(_messages[_id]);
        var oldId = _id;
        var newId = Guid.NewGuid();
        _messages.Add(newId, new TranscribeRecord(newId, DateTimeOffset.Now, null, _messages[_id].User, "", ""));
        _id = newId;
        return oldId;
    }
}
public class TranscribeStreamer(
    Func<TranscribeRecord, Task> _transcribeSaveCallback
)
{
    private readonly Dictionary<Guid, TranscribeRecord> _messages = [];
    private readonly BroadcastBlock<TranscribeRecord> _broadcastBlock = new(x => x);
    public TranscribeUpdater StartTranscribe(TranscribeUser user)
    {
        var id = Guid.NewGuid();
        _messages.Add(id, new TranscribeRecord(id, DateTimeOffset.Now, null, user, "", ""));
        return new TranscribeUpdater(_messages, _broadcastBlock, _transcribeSaveCallback, id);
    }
    public async IAsyncEnumerable<TranscribeRecord> GetMessagesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var bufferBlock = new BufferBlock<TranscribeRecord>();
        using var link = _broadcastBlock.LinkTo(bufferBlock);
        // Replay messages before we link
        foreach (var message in _messages.Values.OrderBy(x => x.SentAt))
        {
            yield return message;
        }
        await foreach (var message in bufferBlock.ReceiveAllAsync(cancellationToken))
        {
            yield return message;
        }
    }
}
public record TranscribeRecord(Guid ID, DateTimeOffset SentAt, DateTimeOffset? FinalizedAt, TranscribeUser User, string NativeText, string TranslatedText);
public enum TranscribeUser
{
    User,
    Agent
}