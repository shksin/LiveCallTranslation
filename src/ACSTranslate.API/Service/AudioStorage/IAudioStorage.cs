namespace ACSTranslate;

public interface IAudioStorage
{
    void StoreAudioEnqueue(Guid callId, Guid id, AudioType audioType, byte[] data);
    Task<Stream> GetAudioStreamAsync(Guid callId, Guid id, AudioType audioType);
}
public enum AudioType
{
    Native,
    Translated
}
public static class AudioStorageExtensions
{
    public static Action<Guid, AudioType, byte[]> ForCall(this IAudioStorage storage, Guid callId)
        => (id, audioType, data) => storage.StoreAudioEnqueue(callId, id, audioType, data);
}