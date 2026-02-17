using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using SteadyVoice.Core.Ast;
using SteadyVoice.Services;

namespace SteadyVoice;

public partial class App : Application, IDisposable {
    private TaskbarIcon? _trayIcon;
    private HotkeyService? _hotkeyService;
    private AudioPlayerService? _audioPlayer;
    private TtsService? _ttsService;
    private AppSettings _settings = new();
    private CancellationTokenSource? _ttsCts;

    // Shared parsed document state
    private DocumentNode? _currentDoc;
    private List<Token>? _currentTokens;
    private ReaderWindow? _readerWindow;

    protected override void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);
        Log.Info("App starting...");

        _settings = AppSettings.Load();
        ApplyLogLevel();
        Log.RotateIfNeeded();
        _audioPlayer = new AudioPlayerService();
        _audioPlayer.PlaybackStopped += OnPlaybackStopped;
        _ttsService = new TtsService(_settings);

        _trayIcon = new TaskbarIcon {
            ToolTipText = "SteadyVoice - Ready (Ctrl+Shift+R)",
            MenuActivation = PopupActivationMode.RightClick,
            ContextMenu = CreateContextMenu()
        };

        // Try to load icon
        try {
            var iconUri = new Uri("pack://application:,,,/Resources/icon.ico");
            var iconStream = GetResourceStream(iconUri)?.Stream;
            if (iconStream != null) {
                _trayIcon.Icon = new System.Drawing.Icon(iconStream);
            }
        } catch {
            _trayIcon.Icon = System.Drawing.SystemIcons.Application;
        }

        _hotkeyService = new HotkeyService();
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;
        _hotkeyService.Register();

        _trayIcon.ShowBalloonTip("SteadyVoice", "Ready! Highlight text and press Ctrl+Shift+R", BalloonIcon.Info);
        Log.Info("Startup complete - ready");
    }

    private System.Windows.Controls.ContextMenu CreateContextMenu() {
        var menu = new System.Windows.Controls.ContextMenu();

        var settingsItem = new System.Windows.Controls.MenuItem { Header = "Settings..." };
        settingsItem.Click += (_, _) => OpenSettings();
        menu.Items.Add(settingsItem);

        var logItem = new System.Windows.Controls.MenuItem { Header = "View Log..." };
        logItem.Click += (_, _) => OpenLog();
        menu.Items.Add(logItem);

        var stopItem = new System.Windows.Controls.MenuItem { Header = "Stop Playback" };
        stopItem.Click += (_, _) => _audioPlayer?.Stop();
        menu.Items.Add(stopItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(exitItem);

        return menu;
    }

    private LogWindow? _logWindow;

    private void OpenLog() {
        if (_logWindow is { IsLoaded: true }) {
            _logWindow.Activate();
            return;
        }
        _logWindow = new LogWindow();
        _logWindow.Show();
    }

    private void OpenSettings() {
        var window = new SettingsWindow(_settings);
        if (window.ShowDialog() == true) {
            _ttsService = new TtsService(_settings);
            ApplyLogLevel();
            Log.Info("Settings updated");
        }
    }

    private void ApplyLogLevel() {
        if (Enum.TryParse<LogLevel>(_settings.LogLevel, ignoreCase: true, out var level)) {
            Log.MinLevel = level;
        }
    }

    private (string CleanedText, DocumentNode Doc, List<Token> Tokens)? CaptureAndParse() {
        var text = TextCaptureService.CaptureSelectedText();
        if (string.IsNullOrWhiteSpace(text)) {
            _trayIcon?.ShowBalloonTip("SteadyVoice", "No text selected", BalloonIcon.Warning);
            return null;
        }

        var cleanedText = TextProcessor.Clean(text);
        _currentDoc = MarkdownParser.Parse(cleanedText);
        _currentTokens = Tokenizer.Tokenize(_currentDoc);
        return (cleanedText, _currentDoc, _currentTokens);
    }

    private async void OnHotkeyPressed() {
        // If audio is playing, stop it regardless of double-press
        if (_audioPlayer?.IsPlaying == true) {
            _ttsCts?.Cancel();
            _audioPlayer.Stop();
            return;
        }

        Log.Debug($"Hotkey pressed. ShowReaderWindow: {_settings.ShowReaderWindow}");

        if (_settings.ShowReaderWindow) {
            // Open reader window with highlighting
            OpenReaderView();
        } else {
            // TTS without window
            Log.Debug("Reader window disabled - TTS only");
            await PerformChunkedTts();
        }
    }

    private async Task PerformChunkedTts() {
        _ttsCts?.Cancel();
        _ttsCts = new CancellationTokenSource();
        var ct = _ttsCts.Token;

        try {
            var parsed = CaptureAndParse();
            if (parsed == null) {
                return;
            }

            var (_, doc, tokens) = parsed.Value;
            var chunks = AstChunker.Chunk(doc, tokens, 100);
            Log.Debug($"Chunked TTS: {chunks.Count} chunks");

            _trayIcon!.ToolTipText = "SteadyVoice - Generating...";
            CursorIndicator.ShowBusy();

            var firstChunk = true;
            foreach (var chunk in chunks) {
                ct.ThrowIfCancellationRequested();

                var result = await _ttsService!.SynthesizeAsync(chunk.Text, ct);

                if (firstChunk) {
                    CursorIndicator.Restore();
                    firstChunk = false;
                }

                await _audioPlayer!.AddChunkAsync(result, ct);
            }

            _audioPlayer!.FinishStreaming();
            _trayIcon.ToolTipText = "SteadyVoice - Ready (Ctrl+Shift+R)";
        } catch (OperationCanceledException) {
            CursorIndicator.Restore();
        } catch (Exception ex) {
            CursorIndicator.Restore();
            _audioPlayer?.Stop();
            _trayIcon?.ShowBalloonTip("SteadyVoice", $"TTS error: {ex.Message}", BalloonIcon.Error);
            _trayIcon!.ToolTipText = "SteadyVoice - Ready (Ctrl+Shift+R)";
        }
    }

    private void OpenReaderView() {
        Log.Debug("OpenReaderView called");
        var parsed = CaptureAndParse();
        if (parsed == null) {
            Log.Info("No text selected for reader");
            return;
        }

        var (cleanedText, doc, tokens) = parsed.Value;
        Log.Debug($"Reader text captured: {cleanedText.Length} chars");

        // Reuse or create window
        if (_readerWindow is { IsLoaded: true }) {
            Log.Debug("Reusing existing reader window");
            _readerWindow.UpdateContent(cleanedText, doc, tokens);
            _readerWindow.Activate();
        } else {
            Log.Debug("Creating new reader window");
            _readerWindow = new ReaderWindow(cleanedText, doc, tokens, _settings, OnReaderPlayRequested, OnReaderClosed);
            _readerWindow.Show();
            Log.Debug("Reader window shown");
        }

        // Auto-play if enabled
        if (_settings.ReaderAutoPlay) {
            Log.Debug("Auto-playing TTS with highlighting");
            _audioPlayer?.Stop();
            _ = PerformChunkedTtsWithHighlighting(0);
        }
    }

    private void OnReaderPlayRequested(string text, int startWordIndex) {
        _audioPlayer?.Stop();
        _ = PerformChunkedTtsWithHighlighting(startWordIndex);
    }

    private void OnReaderClosed() {
        _ttsCts?.Cancel();
        _audioPlayer?.Stop();
    }

    private void OnPlaybackStopped() =>
        // Stop highlighting when playback ends
        _readerWindow?.StopHighlighting();

    private async Task PerformChunkedTtsWithHighlighting(int startWordIndex) {
        _ttsCts?.Cancel();
        _ttsCts = new CancellationTokenSource();
        var ct = _ttsCts.Token;

        try {
            var doc = _currentDoc;
            var tokens = _currentTokens;
            if (doc == null || tokens == null)
                return;

            var chunks = AstChunker.Chunk(doc, tokens);
            Log.Debug($"Chunked TTS with highlighting: {chunks.Count} chunks, startWord={startWordIndex}");

            // Find the first chunk that contains words at or after startWordIndex
            var startChunkIdx = 0;
            for (var i = 0; i < chunks.Count; i++) {
                if (chunks[i].StartWordIndex + chunks[i].WordCount > startWordIndex) {
                    startChunkIdx = i;
                    break;
                }
            }

            _trayIcon!.ToolTipText = "SteadyVoice - Generating...";
            CursorIndicator.ShowBusy();

            var firstChunk = true;
            var latency = _audioPlayer!.OutputLatencySeconds;

            for (var i = startChunkIdx; i < chunks.Count; i++) {
                ct.ThrowIfCancellationRequested();

                var chunk = chunks[i];
                var result = await _ttsService!.SynthesizeWithTimestampsAsync(chunk.Text, includeTimestamps: true, ct);

                // Get duration offset BEFORE adding this chunk's audio
                var timeOffset = _audioPlayer.TotalStreamedDurationSeconds;

                // Feed audio to streaming player (waits for buffer space)
                await _audioPlayer.AddChunkAsync(result.Audio, ct);

                if (firstChunk) {
                    CursorIndicator.Restore();

                    // Initialize streaming highlight on the reader window
                    if (_readerWindow is { IsLoaded: true }) {
                        _readerWindow.StartStreamingHighlight(() => {
                            var pos = _audioPlayer?.CurrentPositionSeconds ?? 0;
                            var adjusted = pos - latency;
                            return adjusted > 0 ? adjusted : 0;
                        }, startWordIndex);
                    }

                    firstChunk = false;
                }

                // Append offset timestamps for this chunk
                if (result.Timestamps != null && _readerWindow is { IsLoaded: true }) {
                    var offsetTimestamps = result.Timestamps
                        .Select(ts => new WordTimestamp(ts.Word, ts.StartTime + timeOffset, ts.EndTime + timeOffset))
                        .ToList();

                    Log.Debug($"Chunk {i}: {offsetTimestamps.Count} timestamps, offset={timeOffset:0.###}s");
                    _readerWindow.AppendTimestamps(offsetTimestamps);
                }
            }

            _audioPlayer.FinishStreaming();
            _trayIcon.ToolTipText = "SteadyVoice - Ready (Ctrl+Shift+R)";
        } catch (OperationCanceledException) {
            CursorIndicator.Restore();
            _audioPlayer?.Stop();
        } catch (Exception ex) {
            CursorIndicator.Restore();
            _audioPlayer?.Stop();
            Log.Error("Chunked TTS with highlighting failed", ex);
            _trayIcon?.ShowBalloonTip("SteadyVoice", $"TTS error: {ex.Message}", BalloonIcon.Error);
            _trayIcon!.ToolTipText = "SteadyVoice - Ready (Ctrl+Shift+R)";
        }
    }

    protected override void OnExit(ExitEventArgs e) {
        _ttsCts?.Cancel();
        _hotkeyService?.Unregister();
        _audioPlayer?.Stop();
        _trayIcon?.Dispose();

        base.OnExit(e);
    }

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing) {
        if (disposing) {
            _ttsCts?.Cancel();
            _ttsCts?.Dispose();
            _audioPlayer?.Dispose();
            _ttsService?.Dispose();
            _trayIcon?.Dispose();
        }
    }
}
