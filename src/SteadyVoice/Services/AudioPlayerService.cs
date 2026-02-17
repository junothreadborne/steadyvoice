using System.IO;
using NAudio.Wave;

namespace SteadyVoice.Services;

public sealed class AudioPlayerService : IDisposable {
    private WaveOutEvent? _waveOut;
    private WaveStream? _waveStream;

    // Streaming mode state
    private BufferedWaveProvider? _bufferedProvider;
    private long _totalPcmBytesWritten;
    private WaveFormat? _streamingFormat;

    private const int DesiredLatencyMs = 120;
    private const int NumberOfBuffers = 2;
    private const int StreamingBufferSeconds = 60;

    public double OutputLatencySeconds => (_waveOut?.DesiredLatency ?? DesiredLatencyMs) / 1000.0;

    public bool IsPlaying => _waveOut?.PlaybackState == PlaybackState.Playing;

    public bool IsStreaming => _bufferedProvider != null;

    public double DurationSeconds => _waveStream?.TotalTime.TotalSeconds ?? 0;

    /// <summary>
    /// Cumulative duration of all PCM data fed via AddChunk, in seconds.
    /// </summary>
    public double TotalStreamedDurationSeconds {
        get {
            if (_streamingFormat == null) return 0;
            return (double)_totalPcmBytesWritten / _streamingFormat.AverageBytesPerSecond;
        }
    }

    public double CurrentPositionSeconds {
        get {
            if (_waveOut == null) return 0;

            if (_bufferedProvider != null) {
                // Streaming mode: position from device bytes played
                var bytesPlayed = _waveOut.GetPosition();
                return (double)bytesPlayed / _bufferedProvider.WaveFormat.AverageBytesPerSecond;
            }

            if (_waveStream != null) {
                // Classic mode: position from device bytes played
                var bytesPlayed = _waveOut.GetPosition();
                var bytesPerSecond = _waveStream.WaveFormat.AverageBytesPerSecond;
                return (double)bytesPlayed / bytesPerSecond;
            }

            return 0;
        }
    }

    public event Action? PlaybackStopped;

    /// <summary>
    /// Classic single-shot playback of a complete MP3 byte array.
    /// </summary>
    public void Play(byte[] audioData) {
        Stop();

        var ms = new MemoryStream(audioData);
        _waveStream = new Mp3FileReader(ms);
        _waveOut = new WaveOutEvent {
            DesiredLatency = DesiredLatencyMs,
            NumberOfBuffers = NumberOfBuffers
        };
        _waveOut.Init(_waveStream);
        _waveOut.PlaybackStopped += OnPlaybackStopped;
        _waveOut.Play();
    }

    /// <summary>
    /// Feed an MP3 chunk into the streaming player. On the first call,
    /// initializes the audio output and starts playback immediately.
    /// Waits for buffer space if the buffer is near capacity.
    /// </summary>
    public async Task AddChunkAsync(byte[] mp3Data, CancellationToken ct = default) {
        // Decode MP3 to PCM
        using var ms = new MemoryStream(mp3Data);
        using var reader = new Mp3FileReader(ms);
        var pcmFormat = reader.WaveFormat;

        if (_bufferedProvider == null) {
            // First chunk: initialize streaming playback
            Stop(); // clean up any previous playback
            _streamingFormat = pcmFormat;
            _bufferedProvider = new BufferedWaveProvider(pcmFormat) {
                BufferDuration = TimeSpan.FromSeconds(StreamingBufferSeconds),
                ReadFully = true // output silence on underrun instead of stopping
            };
            _totalPcmBytesWritten = 0;

            _waveOut = new WaveOutEvent {
                DesiredLatency = DesiredLatencyMs,
                NumberOfBuffers = NumberOfBuffers
            };
            _waveOut.Init(_bufferedProvider);
            _waveOut.PlaybackStopped += OnStreamingPlaybackStopped;
            _waveOut.Play();
        }

        // Read PCM samples and write to buffer, waiting for space when needed
        var buffer = new byte[4096];
        int bytesRead;
        while ((bytesRead = reader.Read(buffer, 0, buffer.Length)) > 0) {
            // Wait until there's enough room in the buffer
            while (_bufferedProvider.BufferLength - _bufferedProvider.BufferedBytes < bytesRead) {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(50, ct);
            }

            _bufferedProvider.AddSamples(buffer, 0, bytesRead);
            _totalPcmBytesWritten += bytesRead;
        }
    }

    /// <summary>
    /// Signal that no more chunks will be added. Playback will stop
    /// naturally once the buffer drains.
    /// </summary>
    public void FinishStreaming() {
        if (_bufferedProvider != null) {
            _bufferedProvider.ReadFully = false;
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e) {
        PlaybackStopped?.Invoke();
        Cleanup();
    }

    private void OnStreamingPlaybackStopped(object? sender, StoppedEventArgs e) {
        PlaybackStopped?.Invoke();
        CleanupStreaming();
    }

    public void Stop() {
        if (_waveOut != null) {
            if (_bufferedProvider != null) {
                _waveOut.Stop();
                CleanupStreaming();
            } else {
                _waveOut.Stop();
                Cleanup();
            }
        }
    }

    private void Cleanup() {
        if (_waveOut != null) {
            _waveOut.PlaybackStopped -= OnPlaybackStopped;
            _waveOut.Dispose();
            _waveOut = null;
        }

        _waveStream?.Dispose();
        _waveStream = null;
    }

    private void CleanupStreaming() {
        if (_waveOut != null) {
            _waveOut.PlaybackStopped -= OnStreamingPlaybackStopped;
            _waveOut.Dispose();
            _waveOut = null;
        }

        _bufferedProvider = null;
        _streamingFormat = null;
        _totalPcmBytesWritten = 0;
    }

    public void Dispose() => Stop();
}
