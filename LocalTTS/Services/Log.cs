using System.IO;

namespace LocalTTS.Services;

public enum LogLevel { Debug, Info, Warn, Error }

public static class Log {
    private static readonly string LogPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "localtts.log");

    private static readonly object Lock = new();

    public static LogLevel MinLevel { get; set; } = LogLevel.Info;

    public static event Action<string>? LineWritten;

    public static void RotateIfNeeded() {
        try {
            if (!File.Exists(LogPath)) {
                return;
            }

            var info = new FileInfo(LogPath);
            if (info.Length <= 5 * 1024 * 1024) {
                return;
            }

            var oldPath = LogPath + ".old";
            File.Copy(LogPath, oldPath, overwrite: true);
            File.WriteAllText(LogPath, "");
        } catch { }
    }

    public static void Debug(string message) => Write(LogLevel.Debug, "DBG", message);

    public static void Info(string message) => Write(LogLevel.Info, "INF", message);

    public static void Warn(string message, Exception? ex = null) =>
        Write(LogLevel.Warn, "WRN", FormatWithException(message, ex));

    public static void Error(string message, Exception? ex = null) =>
        Write(LogLevel.Error, "ERR", FormatWithException(message, ex));

    private static string FormatWithException(string message, Exception? ex) =>
        ex != null ? $"{message} - {ex.GetType().Name}: {ex.Message}" : message;

    private static void Write(LogLevel level, string tag, string message) {
        if (level < MinLevel) {
            return;
        }

        var line = $"[{DateTime.Now:HH:mm:ss}] [{tag}] {message}";
        lock (Lock) {
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        LineWritten?.Invoke(line);
    }
}
