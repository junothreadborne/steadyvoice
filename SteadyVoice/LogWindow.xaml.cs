using System.IO;
using System.Windows;
using SteadyVoice.Services;

namespace SteadyVoice;

public partial class LogWindow : Window {
    public LogWindow() {
        InitializeComponent();

        // Load existing log file
        var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "steadyvoice.log");
        try {
            if (File.Exists(logPath)) {
                LogText.Text = File.ReadAllText(logPath);
            }
        } catch { }

        LogText.ScrollToEnd();

        Log.LineWritten += OnLineWritten;
        Closed += (_, _) => Log.LineWritten -= OnLineWritten;
    }

    private void OnLineWritten(string line) => Dispatcher.BeginInvoke(() => {
        LogText.AppendText(line + Environment.NewLine);
        LogText.ScrollToEnd();
    });

    private void OnClear(object sender, RoutedEventArgs e) => LogText.Clear();
}
