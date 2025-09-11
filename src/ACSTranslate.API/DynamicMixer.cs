using NAudio.Wave;
using NAudio.Wave.SampleProviders;

public class DynamicMixer<T> where T : Enum
{
    private readonly Dictionary<T, BufferedWaveProvider> _audioBuffers = [];
    private readonly Dictionary<T, VolumeSampleProvider> _volumeProviders = [];
    private readonly Dictionary<T, bool> _monitorLatency = [];
    private readonly MixingSampleProvider _mixer;
    private readonly int _frameMs;
    private readonly WaveFormat _waveFormat;
    private readonly int _maxMonitoredBufferMs;

    public DynamicMixer(WaveFormat waveFormat, int bufferSeconds = 120, int frameMs = 50, int maxMonitoredBufferMs = 200)
    {
        foreach (T key in Enum.GetValues(typeof(T)))
        {
            _audioBuffers[key] = new BufferedWaveProvider(waveFormat)
            {
                DiscardOnBufferOverflow = true,
                BufferLength = waveFormat.AverageBytesPerSecond * bufferSeconds
            };
            _volumeProviders[key] = new VolumeSampleProvider(_audioBuffers[key].ToSampleProvider())
            {
                Volume = 0.0f
            };
        }

        _mixer = new MixingSampleProvider(_volumeProviders.Values)
        {
            ReadFully = true
        };

        _frameMs = frameMs;
        _maxMonitoredBufferMs = maxMonitoredBufferMs;
        _waveFormat = waveFormat;
    }

    public void AddAudio(T key, byte[] data)
        => _audioBuffers[key].AddSamples(data, 0, data.Length);

    public void SetAudioOptions(T key, float volume)
        => _volumeProviders[key].Volume = Math.Clamp(volume, 0.0f, 1.0f);

    public void MonitorLatency(T key)
        => _monitorLatency[key] = true;

    public async Task SendMixedAudioAsync(AudioWebSocket ws, CancellationToken ct)
    {
        var mixerOutput = _mixer.ToWaveProvider16();

        using var mixerTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(_frameMs));
        byte[] mixerBuffer = new byte[_waveFormat.AverageBytesPerSecond / (1000 / _frameMs)];
        while (!ct.IsCancellationRequested)
        {
            // Skip audio if any monitored buffer is too large
            while (_monitorLatency.Keys.ToList().Select(k => _audioBuffers[k].BufferedDuration.TotalMilliseconds).Max() > _maxMonitoredBufferMs)
            {
                mixerOutput.Read(mixerBuffer, 0, mixerBuffer.Length);
            }

            int read = mixerOutput.Read(mixerBuffer, 0, mixerBuffer.Length);
            if (read > 0)
            {
                await ws.SendAudioAsync(mixerBuffer, 0, read, ct);
            }

            try { await mixerTimer.WaitForNextTickAsync(ct); }
            catch (OperationCanceledException) { break; }
        }
    }
}