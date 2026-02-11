using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace LocalTTS.Services;

public record WordTimestamp(string Word, double StartTime, double EndTime);

public record TtsResult(byte[] Audio, List<WordTimestamp>? Timestamps = null);

public sealed class TtsService(AppSettings settings) : IDisposable {
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly AppSettings _settings = settings;

    public async Task<byte[]> SynthesizeAsync(string text, CancellationToken ct = default) {
        var result = await SynthesizeWithTimestampsAsync(text, includeTimestamps: false, ct);
        return result.Audio;
    }

    public async Task<TtsResult> SynthesizeWithTimestampsAsync(string text, bool includeTimestamps = true, CancellationToken ct = default) {
        if (!includeTimestamps) {
            // Use standard endpoint
            var payload = new {
                model = "kokoro",
                voice = _settings.Voice,
                input = text
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var url = $"http://localhost:{_settings.Port}/v1/audio/speech";
            var response = await _client.PostAsync(url, content, ct);
            response.EnsureSuccessStatusCode();

            var audio = await response.Content.ReadAsByteArrayAsync(ct);
            return new TtsResult(audio);
        } else {
            // Use captioned speech endpoint for timestamps
            var payload = new {
                model = "kokoro",
                voice = _settings.Voice,
                input = text,
                response_format = "mp3",
                normalization_options = new {
                    normalize = false
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var url = $"http://localhost:{_settings.Port}/dev/captioned_speech";
            var response = await _client.PostAsync(url, content, ct);
            response.EnsureSuccessStatusCode();

            var responseText = await response.Content.ReadAsStringAsync(ct);

            // API may return NDJSON (multiple JSON objects separated by newlines)
            // We need to parse each line and combine results
            var lines = responseText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            var audioChunks = new List<byte[]>();
            var timestamps = new List<WordTimestamp>();

            foreach (var line in lines) {
                if (string.IsNullOrWhiteSpace(line)) {
                    continue;
                }

                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                // Collect all audio chunks (base64 encoded)
                if (root.TryGetProperty("audio", out var audioElement)) {
                    var audioBase64 = audioElement.GetString();
                    if (!string.IsNullOrEmpty(audioBase64)) {
                        audioChunks.Add(Convert.FromBase64String(audioBase64));
                    }
                }

                // Collect timestamps from all chunks
                if (root.TryGetProperty("timestamps", out var timestampsElement)) {
                    foreach (var ts in timestampsElement.EnumerateArray()) {
                        var word = ts.GetProperty("word").GetString()!;
                        var startTime = ts.GetProperty("start_time").GetDouble();
                        var endTime = ts.GetProperty("end_time").GetDouble();
                        timestamps.Add(new WordTimestamp(word, startTime, endTime));
                    }
                }
            }

            if (audioChunks.Count == 0) {
                throw new InvalidOperationException("No audio data received from captioned speech endpoint");
            }

            // Concatenate all audio chunks
            var totalLength = audioChunks.Sum(c => c.Length);
            var audio = new byte[totalLength];
            var offset = 0;
            foreach (var chunk in audioChunks) {
                Buffer.BlockCopy(chunk, 0, audio, offset, chunk.Length);
                offset += chunk.Length;
            }

            Log.Debug($"Captioned speech: {audioChunks.Count} chunks, {audio.Length} bytes, {timestamps.Count} timestamps");
            return new TtsResult(audio, timestamps);
        }
    }

    public void Dispose() => _client.Dispose();
}
