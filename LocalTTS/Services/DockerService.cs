using System.Diagnostics;
using System.Net.Http;

namespace LocalTTS.Services;

public class DockerService(AppSettings settings) {
    private readonly AppSettings _settings = settings;

    public async Task EnsureRunningAsync() {
        if (!_settings.AutoStartContainer) {
            Log.Info("Auto-start disabled, skipping container management");
            return;
        }

        Log.Info("Checking container status...");
        var (exitCode, output) = await RunDockerAsync($"inspect -f \"{{{{.State.Running}}}}\" {_settings.ContainerName}");
        Log.Debug($"inspect exit={exitCode}, output={output.Trim()}");

        if (exitCode == 0 && output.Trim() == "true") {
            Log.Info("Container already running");
            return;
        }

        if (exitCode == 0) {
            Log.Info("Starting existing container...");
            var (startExit, startOut) = await RunDockerAsync($"start {_settings.ContainerName}");
            Log.Debug($"start exit={startExit}, output={startOut.Trim()}");
        } else {
            Log.Info("Creating new container...");
            var gpuFlag = _settings.DockerImage.Contains("gpu", StringComparison.OrdinalIgnoreCase)
                ? "--gpus all " : "";
            var (runExit, runOut) = await RunDockerAsync(
                $"run -d {gpuFlag}--name {_settings.ContainerName} -p {_settings.Port}:8880 {_settings.DockerImage}");
            Log.Debug($"run exit={runExit}, output={runOut.Trim()}");
        }

        Log.Info("Waiting for API to be ready...");
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        for (var i = 0; i < 60; i++) {
            try {
                var response = await client.GetAsync($"http://localhost:{_settings.Port}/v1/models");
                Log.Debug($"Health check {i + 1}: {response.StatusCode}");
                if (response.IsSuccessStatusCode) {
                    Log.Info("API is ready!");
                    return;
                }
            } catch (Exception ex) {
                Log.Debug($"Health check {i + 1}: {ex.GetType().Name} - {ex.Message}");
            }
            await Task.Delay(2000);
        }

        throw new Exception("Kokoro API did not become ready in time");
    }

    public async Task StopAsync() {
        if (!_settings.AutoStopContainer) {
            Log.Info("Auto-stop disabled, leaving container running");
            return;
        }

        Log.Info("Stopping container...");
        await RunDockerAsync($"stop {_settings.ContainerName}");
    }

    private static async Task<(int ExitCode, string Output)> RunDockerAsync(string arguments) {
        Log.Debug($"Running: docker {arguments}");
        var psi = new ProcessStartInfo {
            FileName = "docker",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new Exception("Failed to start docker");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (!string.IsNullOrWhiteSpace(stderr)) {
            Log.Debug($"stderr: {stderr.Trim()}");
        }

        return (process.ExitCode, stdout);
    }
}
