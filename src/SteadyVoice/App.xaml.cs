using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using SteadyVoice.Core.Ast;
using SteadyVoice.Services;

namespace SteadyVoice;

public partial class App : Application {
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
            await PerformTts();
        }
    }

    private async Task PerformTts() {
        _ttsCts?.Cancel();
        _ttsCts = new CancellationTokenSource();
        var ct = _ttsCts.Token;

        try {
            var parsed = CaptureAndParse();
            if (parsed == null) {
                return;
            }

            _trayIcon!.ToolTipText = "SteadyVoice - Generating...";
            CursorIndicator.ShowBusy();
            var audioData = await _ttsService!.SynthesizeAsync(parsed.Value.CleanedText, ct);
            CursorIndicator.Restore();
            _audioPlayer!.Play(audioData);
            _trayIcon.ToolTipText = "SteadyVoice - Ready (Ctrl+Shift+R)";
        } catch (OperationCanceledException) {
            CursorIndicator.Restore();
        } catch (Exception ex) {
            CursorIndicator.Restore();
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
            _ = PerformTtsWithHighlighting(cleanedText, 0);
        }
    }

    private void OnReaderPlayRequested(string text, int startWordIndex) {
        _audioPlayer?.Stop();
        _ = PerformTtsWithHighlighting(text, startWordIndex);
    }

    private void OnReaderClosed() => _audioPlayer?.Stop();

    private void OnPlaybackStopped() =>
        // Stop highlighting when playback ends
        _readerWindow?.StopHighlighting();

    private async Task PerformTtsWithHighlighting(string text, int startWordIndex) {
        _ttsCts?.Cancel();
        _ttsCts = new CancellationTokenSource();
        var ct = _ttsCts.Token;

        try {
            _trayIcon!.ToolTipText = "SteadyVoice - Generating...";
            CursorIndicator.ShowBusy();

            // Get audio with timestamps for highlighting
            var result = await _ttsService!.SynthesizeWithTimestampsAsync(text, includeTimestamps: true, ct);
            CursorIndicator.Restore();

            // Start highlighting if we have timestamps and window is open
            _audioPlayer!.Play(result.Audio);

            if (result.Timestamps != null && _readerWindow is { IsLoaded: true }) {
                var latency = _audioPlayer.OutputLatencySeconds;
                var duration = _audioPlayer.DurationSeconds;
                var lastTimestamp = result.Timestamps.Count > 0 ? result.Timestamps[^1].EndTime : 0;
                var timeScale = (duration > 0 && lastTimestamp > 0) ? lastTimestamp / duration : 1.0;
                if (timeScale < 0.5) {
                    timeScale = 0.5;
                }

                if (timeScale > 2.0) {
                    timeScale = 2.0;
                }

                Log.Debug($"Starting highlighting with {result.Timestamps.Count} timestamps (latency {latency:0.###}s, scale {timeScale:0.###})");
                _readerWindow.StartHighlighting(result.Timestamps, () => {
                    var pos = _audioPlayer?.CurrentPositionSeconds ?? 0;
                    var adjusted = (pos - latency) * timeScale;
                    return adjusted > 0 ? adjusted : 0;
                }, startWordIndex);
            }
            _trayIcon.ToolTipText = "SteadyVoice - Ready (Ctrl+Shift+R)";
        } catch (OperationCanceledException) {
            CursorIndicator.Restore();
        } catch (Exception ex) {
            CursorIndicator.Restore();
            Log.Error("TTS with highlighting failed", ex);
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
}
