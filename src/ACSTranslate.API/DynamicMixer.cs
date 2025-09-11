using System.Net.WebSockets;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

public class DynamicMixer
{
    private readonly BufferedWaveProvider _userOriginalAudioBuffer;
    private readonly BufferedWaveProvider _userTranslatedAudioBuffer;
    private readonly BufferedWaveProvider _agentOriginalAudioBuffer;
    private readonly BufferedWaveProvider _agentTranslatedAudioBuffer;

    private readonly VolumeSampleProvider _userOriginalVolume;
    private readonly VolumeSampleProvider _userTranslatedVolume;
    private readonly VolumeSampleProvider _agentOriginalVolume;
    private readonly VolumeSampleProvider _agentTranslatedVolume;

    private readonly MixingSampleProvider _mixer;
    private readonly int _frameMs;
    private readonly WaveFormat _waveFormat;

    public DynamicMixer(WaveFormat waveFormat, int bufferSeconds = 120, int frameMs = 50)
    {
        _userOriginalAudioBuffer = new BufferedWaveProvider(waveFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferLength = waveFormat.AverageBytesPerSecond * bufferSeconds
        };
        _userTranslatedAudioBuffer = new BufferedWaveProvider(waveFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferLength = waveFormat.AverageBytesPerSecond * bufferSeconds
        };
        _agentOriginalAudioBuffer = new BufferedWaveProvider(waveFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferLength = waveFormat.AverageBytesPerSecond * bufferSeconds
        };
        _agentTranslatedAudioBuffer = new BufferedWaveProvider(waveFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferLength = waveFormat.AverageBytesPerSecond * bufferSeconds
        };

        _userOriginalVolume = new VolumeSampleProvider(_userOriginalAudioBuffer.ToSampleProvider())
        {
            Volume = 0.0f
        };
        _userTranslatedVolume = new VolumeSampleProvider(_userTranslatedAudioBuffer.ToSampleProvider())
        {
            Volume = 1.0f
        };
        _agentOriginalVolume = new VolumeSampleProvider(_agentOriginalAudioBuffer.ToSampleProvider())
        {
            Volume = 0.0f
        };
        _agentTranslatedVolume = new VolumeSampleProvider(_agentTranslatedAudioBuffer.ToSampleProvider())
        {
            Volume = 0.0f
        };

        _mixer = new MixingSampleProvider(
        [
            _userOriginalVolume,
            _userTranslatedVolume,
            _agentOriginalVolume,
            _agentTranslatedVolume
        ])
        {
            ReadFully = true
        };

        _frameMs = frameMs;
        _waveFormat = waveFormat;
    }

    public void AddUserOriginalAudio(byte[] data)
        => _userOriginalAudioBuffer.AddSamples(data, 0, data.Length);

    public void AddUserTranslatedAudio(byte[] data)
        => _userTranslatedAudioBuffer.AddSamples(data, 0, data.Length);

    public void AddAgentOriginalAudio(byte[] data)
        => _agentOriginalAudioBuffer.AddSamples(data, 0, data.Length);

    public void AddAgentTranslatedAudio(byte[] data)
        => _agentTranslatedAudioBuffer.AddSamples(data, 0, data.Length);

    public void SetAudioOptions(CallAudioOptions options)
    {
        _userOriginalVolume.Volume = options.UserOriginalAudio ? 0.8f : 0.0f;
        _userTranslatedVolume.Volume = options.UserTranslatedAudio ? 1.0f : 0.0f;
        _agentOriginalVolume.Volume = options.AgentOriginalAudio ? 0.5f : 0.0f;
        _agentTranslatedVolume.Volume = options.AgentTranslatedAudio ? 0.8f : 0.0f;
    }

    public async Task SendMixedAudioAsync(AudioWebSocket ws, CancellationToken ct)
    {
        var mixerOutput = _mixer.ToWaveProvider16();

        using var mixerTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(_frameMs));
        byte[] mixerBuffer = new byte[_waveFormat.AverageBytesPerSecond / (1000 / _frameMs)];
        while (!ct.IsCancellationRequested)
        {
            // Only skip on the realtime buffers, not the translated ones, as they will have larger buffers
            while (_userOriginalAudioBuffer.BufferedDuration.TotalMilliseconds > 200 ||
                   _agentOriginalAudioBuffer.BufferedDuration.TotalMilliseconds > 200)
            {
                Console.WriteLine($"{_userOriginalAudioBuffer.BufferedDuration.TotalMilliseconds}ms " +
                    $"{_agentOriginalAudioBuffer.BufferedDuration.TotalMilliseconds}ms");
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