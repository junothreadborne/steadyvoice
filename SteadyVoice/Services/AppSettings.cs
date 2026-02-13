using System.IO;
using System.Text.Json;

namespace SteadyVoice.Services;

public class AppSettings {
    public string ApiUrl { get; set; } = "http://localhost:8880";
    public string Voice { get; set; } = "af_heart";

    // Reader View settings
    public bool ShowReaderWindow { get; set; } = true;
    public bool ReaderAutoPlay { get; set; } = true;
    public bool ReaderCloseOnFocusLoss { get; set; }
    public string ReaderFontFamily { get; set; } = "Segoe UI";
    public int ReaderFontSize { get; set; } = 18;
    public bool ReaderDarkMode { get; set; }

    // General
    public string LogLevel { get; set; } = "Info";

    private static readonly string SettingsPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true
    };

    public static AppSettings Load() {
        try {
            if (File.Exists(SettingsPath)) {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new();
            }
        } catch (Exception ex) {
            Log.Error("Failed to load settings", ex);
        }
        return new();
    }

    public void Save() {
        try {
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        } catch (Exception ex) {
            Log.Error("Failed to save settings", ex);
        }
    }
}
