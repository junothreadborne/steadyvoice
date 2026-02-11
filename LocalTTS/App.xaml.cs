using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using LocalTTS.Services;

namespace LocalTTS;

public partial class App : Application {
    private TaskbarIcon? _trayIcon;
    private HotkeyService? _hotkeyService;
    private AudioPlayerService? _audioPlayer;
    private TtsService? _ttsService;
    private DockerService? _dockerService;
    private AppSettings _settings = new();
    private CancellationTokenSource? _ttsCts;

    // Reader View state
    private ReaderWindow? _readerWindow;

    protected override async void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);
        Log.Info("App starting...");

        _settings = AppSettings.Load();
        ApplyLogLevel();
        Log.RotateIfNeeded();
        _audioPlayer = new AudioPlayerService();
        _audioPlayer.PlaybackStopped += OnPlaybackStopped;
        _ttsService = new TtsService(_settings);
        _dockerService = new DockerService(_settings);

        _trayIcon = new TaskbarIcon {
            ToolTipText = "LocalTTS - Starting...",
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

        _trayIcon.ToolTipText = "LocalTTS - Starting Kokoro...";
        try {
            await _dockerService.EnsureRunningAsync();
            _trayIcon.ToolTipText = "LocalTTS - Ready (Ctrl+Shift+R)";
            _trayIcon.ShowBalloonTip("LocalTTS", "Ready! Highlight text and press Ctrl+Shift+R", BalloonIcon.Info);
            Log.Info("Startup complete - ready");
        } catch (Exception ex) {
            Log.Error("Docker startup failed", ex);
            _trayIcon.ToolTipText = "LocalTTS - Docker error";
            _trayIcon.ShowBalloonTip("LocalTTS", $"Docker error: {ex.Message}", BalloonIcon.Error);
        }
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
            _dockerService = new DockerService(_settings);
            ApplyLogLevel();
            Log.Info("Settings updated");
        }
    }

    private void ApplyLogLevel() {
        if (Enum.TryParse<LogLevel>(_settings.LogLevel, ignoreCase: true, out var level)) {
            Log.MinLevel = level;
        }
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
            var text = TextCaptureService.CaptureSelectedText();
            if (string.IsNullOrWhiteSpace(text)) {
                _trayIcon?.ShowBalloonTip("LocalTTS", "No text selected", BalloonIcon.Warning);
                return;
            }

            _trayIcon!.ToolTipText = "LocalTTS - Generating...";
            CursorIndicator.ShowBusy();
            var audioData = await _ttsService!.SynthesizeAsync(text, ct);
            CursorIndicator.Restore();
            _audioPlayer!.Play(audioData);
            _trayIcon.ToolTipText = "LocalTTS - Ready (Ctrl+Shift+R)";
        } catch (OperationCanceledException) {
            CursorIndicator.Restore();
        } catch (Exception ex) {
            CursorIndicator.Restore();
            _trayIcon?.ShowBalloonTip("LocalTTS", $"TTS error: {ex.Message}", BalloonIcon.Error);
            _trayIcon!.ToolTipText = "LocalTTS - Ready (Ctrl+Shift+R)";
        }
    }

    private void OpenReaderView() {
        Log.Debug("OpenReaderView called");
        var text = TextCaptureService.CaptureSelectedText();
        if (string.IsNullOrWhiteSpace(text)) {
            Log.Info("No text selected for reader");
            _trayIcon?.ShowBalloonTip("LocalTTS", "No text selected", BalloonIcon.Warning);
            return;
        }

        Log.Debug($"Reader text captured: {text.Length} chars");
        var cleanedText = TextProcessor.Clean(text);

        // Reuse or create window
        if (_readerWindow is { IsLoaded: true }) {
            Log.Debug("Reusing existing reader window");
            _readerWindow.UpdateText(cleanedText);
            _readerWindow.Activate();
        } else {
            Log.Debug("Creating new reader window");
            _readerWindow = new ReaderWindow(cleanedText, _settings, OnReaderPlayRequested, OnReaderClosed);
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
            _trayIcon!.ToolTipText = "LocalTTS - Generating...";
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
            _trayIcon.ToolTipText = "LocalTTS - Ready (Ctrl+Shift+R)";
        } catch (OperationCanceledException) {
            CursorIndicator.Restore();
        } catch (Exception ex) {
            CursorIndicator.Restore();
            Log.Error("TTS with highlighting failed", ex);
            _trayIcon?.ShowBalloonTip("LocalTTS", $"TTS error: {ex.Message}", BalloonIcon.Error);
            _trayIcon!.ToolTipText = "LocalTTS - Ready (Ctrl+Shift+R)";
        }
    }

    protected override async void OnExit(ExitEventArgs e) {
        _ttsCts?.Cancel();
        _hotkeyService?.Unregister();
        _audioPlayer?.Stop();
        _trayIcon?.Dispose();

        if (_dockerService != null) {
            try { await _dockerService.StopAsync(); } catch { }
        }

        base.OnExit(e);
    }
}
