using System.Collections.Concurrent;
using ACSTranslate.Core;

namespace ACSTranslate;

public class InMemoryAudioStorage : IAudioStorage
{
    ConcurrentDictionary<Guid, Dictionary<Guid, byte[]>> _storage = [];
    // TODO: Hacky, we need to store each audio type separately
    public void StoreAudioEnqueue(Guid callId, Guid id, AudioType audioType, byte[] data)
    {
        var encoded = AudioEncoder.EncodeToWav(AudioFormatConfig.Current.GlobalAudioFormat, data);

        _storage.GetOrAdd(callId, _ => [])[id] = encoded;
    }

    public Task<Stream> GetAudioStreamAsync(Guid callId, Guid id, AudioType audioType)
        => Task.FromResult((Stream)new MemoryStream(_storage[callId][id]));
}