using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LocalTTS.Services;

namespace LocalTTS;

public partial class SettingsWindow : Window {
    private readonly AppSettings _settings;
    private readonly AudioPlayerService _previewPlayer = new();
    private TtsService? _previewTts;
    private CancellationTokenSource? _previewCts;

    private const string PreviewText = "The quick brown fox jumps over the lazy dog.";

    public SettingsWindow(AppSettings settings) {
        InitializeComponent();
        _settings = settings;

        Loaded += OnLoaded;
        Closed += OnClosed;

        // Docker settings
        DockerImageBox.Text = settings.DockerImage;
        PortBox.Text = settings.Port.ToString(CultureInfo.InvariantCulture);
        ContainerNameBox.Text = settings.ContainerName;
        VoiceComboBox.Text = settings.Voice;
        AutoStartBox.IsChecked = settings.AutoStartContainer;
        AutoStopBox.IsChecked = settings.AutoStopContainer;

        // Reader View settings
        ShowReaderWindowBox.IsChecked = settings.ShowReaderWindow;
        ReaderAutoPlayBox.IsChecked = settings.ReaderAutoPlay;
        ReaderCloseOnFocusLossBox.IsChecked = settings.ReaderCloseOnFocusLoss;
        ReaderDarkModeBox.IsChecked = settings.ReaderDarkMode;
        ReaderFontSizeBox.Text = settings.ReaderFontSize.ToString(CultureInfo.InvariantCulture);

        // Set font family selection
        foreach (ComboBoxItem item in ReaderFontBox.Items) {
            if (item.Content.ToString() == settings.ReaderFontFamily) {
                ReaderFontBox.SelectedItem = item;
                break;
            }
        }
        if (ReaderFontBox.SelectedItem == null) {
            ReaderFontBox.SelectedIndex = 0;
        }

        // General settings
        foreach (ComboBoxItem item in LogLevelBox.Items) {
            if (item.Content.ToString() == settings.LogLevel) {
                LogLevelBox.SelectedItem = item;
                break;
            }
        }
        if (LogLevelBox.SelectedItem == null) {
            LogLevelBox.SelectedIndex = 1; // Info
        }
    }

    private void OnSave(object sender, RoutedEventArgs e) {
        if (!int.TryParse(PortBox.Text, out var port) || port < 1 || port > 65535) {
            MessageBox.Show("Port must be a number between 1 and 65535.", "Invalid Port",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(ReaderFontSizeBox.Text, out var fontSize) || fontSize < 10 || fontSize > 36) {
            MessageBox.Show("Font size must be a number between 10 and 36.", "Invalid Font Size",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Docker settings
        _settings.DockerImage = DockerImageBox.Text.Trim();
        _settings.Port = port;
        _settings.ContainerName = ContainerNameBox.Text.Trim();
        _settings.Voice = VoiceComboBox.Text.Trim();
        _settings.AutoStartContainer = AutoStartBox.IsChecked == true;
        _settings.AutoStopContainer = AutoStopBox.IsChecked == true;

        // Reader View settings
        _settings.ShowReaderWindow = ShowReaderWindowBox.IsChecked == true;
        _settings.ReaderAutoPlay = ReaderAutoPlayBox.IsChecked == true;
        _settings.ReaderCloseOnFocusLoss = ReaderCloseOnFocusLossBox.IsChecked == true;
        _settings.ReaderDarkMode = ReaderDarkModeBox.IsChecked == true;
        _settings.ReaderFontSize = fontSize;
        _settings.ReaderFontFamily = (ReaderFontBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Segoe UI";

        // General settings
        _settings.LogLevel = (LogLevelBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Info";

        _settings.Save();

        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private async void OnLoaded(object sender, RoutedEventArgs e) => await PopulateVoiceListAsync();

    private void OnClosed(object? sender, EventArgs e) {
        _previewCts?.Cancel();
        _previewPlayer.Stop();
        _previewPlayer.Dispose();
        _previewTts?.Dispose();
        _previewTts = null;
    }

    private async Task PopulateVoiceListAsync() {
        var currentVoice = VoiceComboBox.Text.Trim();
        var voices = new List<string>();

        if (int.TryParse(PortBox.Text, out var port) && port is >= 1 and <= 65535) {
            voices = await TryFetchVoicesAsync(port);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        VoiceComboBox.Items.Clear();

        void AddVoice(string? voice) {
            if (string.IsNullOrWhiteSpace(voice)) {
                return;
            }
            var trimmed = voice.Trim();
            if (seen.Add(trimmed)) {
                VoiceComboBox.Items.Add(trimmed);
            }
        }

        foreach (var voice in voices) {
            AddVoice(voice);
        }

        AddVoice(currentVoice);
        AddVoice(_settings.Voice);

        if (string.IsNullOrWhiteSpace(VoiceComboBox.Text)) {
            VoiceComboBox.Text = _settings.Voice;
        }

        var target = VoiceComboBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(target)) {
            foreach (var item in VoiceComboBox.Items) {
                if (item is string value && value.Equals(target, StringComparison.OrdinalIgnoreCase)) {
                    VoiceComboBox.SelectedItem = item;
                    break;
                }
            }
        }
    }

    private static async Task<List<string>> TryFetchVoicesAsync(int port) {
        var endpoints = new[] {
            $"http://localhost:{port}/v1/audio/voices",
            $"http://localhost:{port}/v1/voices"
        };

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        foreach (var endpoint in endpoints) {
            try {
                var response = await client.GetAsync(endpoint);
                if (!response.IsSuccessStatusCode) {
                    continue;
                }

                var json = await response.Content.ReadAsStringAsync();
                var voices = ParseVoiceList(json);
                if (voices.Count > 0) {
                    return voices;
                }
            } catch {
                // Ignore and try the next endpoint.
            }
        }

        return new List<string>();
    }

    private static List<string> ParseVoiceList(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            return ExtractVoicesFromJson(doc.RootElement);
        } catch {
            return new List<string>();
        }
    }

    private static List<string> ExtractVoicesFromJson(JsonElement root) {
        var voices = new List<string>();

        void AddVoice(string? voice) {
            if (!string.IsNullOrWhiteSpace(voice)) {
                voices.Add(voice);
            }
        }

        if (root.ValueKind == JsonValueKind.Array) {
            AddVoicesFromArray(root, voices);
            return voices;
        }

        if (root.ValueKind == JsonValueKind.Object) {
            if (root.TryGetProperty("voices", out var voicesElement) && voicesElement.ValueKind == JsonValueKind.Array) {
                AddVoicesFromArray(voicesElement, voices);
                return voices;
            }

            if (root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Array) {
                AddVoicesFromArray(dataElement, voices);
            }
        }

        return voices;
    }

    private static void AddVoicesFromArray(JsonElement array, List<string> voices) {
        foreach (var item in array.EnumerateArray()) {
            switch (item.ValueKind) {
                case JsonValueKind.String:
                    voices.Add(item.GetString() ?? string.Empty);
                    break;
                case JsonValueKind.Object:
                    if (item.TryGetProperty("id", out var idElement)) {
                        voices.Add(idElement.GetString() ?? string.Empty);
                    } else if (item.TryGetProperty("name", out var nameElement)) {
                        voices.Add(nameElement.GetString() ?? string.Empty);
                    } else if (item.TryGetProperty("voice", out var voiceElement)) {
                        voices.Add(voiceElement.GetString() ?? string.Empty);
                    }
                    break;
            }
        }
    }

    private async void OnPreviewVoice(object sender, RoutedEventArgs e) {
        if (_previewPlayer.IsPlaying) {
            _previewPlayer.Stop();
            return;
        }

        var voice = VoiceComboBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(voice)) {
            MessageBox.Show("Select a voice to preview.", "Voice Preview",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!int.TryParse(PortBox.Text, out var port) || port < 1 || port > 65535) {
            MessageBox.Show("Port must be a number between 1 and 65535.", "Invalid Port",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var previousContent = VoicePreviewButton.Content;
        VoicePreviewButton.IsEnabled = false;
        VoicePreviewButton.Content = "Previewing...";

        try {
            _previewCts?.Cancel();
            _previewCts = new CancellationTokenSource();

            var previewSettings = new AppSettings {
                Port = port,
                Voice = voice
            };

            _previewTts?.Dispose();
            _previewTts = new TtsService(previewSettings);

            var audio = await _previewTts.SynthesizeAsync(PreviewText, _previewCts.Token);
            _previewPlayer.Play(audio);
        } catch (Exception ex) {
            MessageBox.Show($"Preview failed: {ex.Message}", "Voice Preview",
                MessageBoxButton.OK, MessageBoxImage.Error);
        } finally {
            VoicePreviewButton.Content = previousContent;
            VoicePreviewButton.IsEnabled = true;
        }
    }
}
