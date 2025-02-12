namespace ACSTranslate;

public class NullAudioStorage : IAudioStorage
{
    public void StoreAudioEnqueue(Guid callId, Guid id, AudioType audioType, byte[] data)
    {
    }

    public Task<Stream> GetAudioStreamAsync(Guid callId, Guid id, AudioType audioType)
        => Task.FromResult((Stream)new MemoryStream([]));
}