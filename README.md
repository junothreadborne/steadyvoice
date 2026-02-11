# LocalTTS

A Windows system tray app that reads highlighted text aloud using [Kokoro TTS](https://github.com/remsky/Kokoro-FastAPI) running locally via Docker.

## How It Works

1. Highlight text in any application
2. Press **Ctrl+Shift+R**
3. If Reader View is enabled (default), the reader window opens and can auto-play TTS
4. If Reader View is disabled, the text is read aloud immediately without the reader window
5. Press **Ctrl+Shift+R** again while audio is playing to stop playback

### Reader View

When Reader View is enabled, pressing **Ctrl+Shift+R** opens a clean reading window with the selected text. The window:
- Click any word to start reading from that point

## Requirements

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- For GPU acceleration: NVIDIA GPU with [NVIDIA Container Toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/install-guide.html)

## Setup

```
git clone https://github.com/everybody-art/localtts.git
cd localtts
dotnet run --project LocalTTS
```

On first launch the app will pull and start the Kokoro TTS Docker container. This may take a few minutes the first time. A tray icon will appear and show "Ready" when the TTS engine is available.

## Architecture

```
Global hotkey (Ctrl+Shift+R)
  -> Reads selected text via UI Automation (clipboard fallback)
  -> If Reader View is enabled: open reader window
  -> POST to Kokoro-FastAPI (localhost:8880)
  -> If Reader View is open: use captioned speech for word timestamps
  -> Plays audio via NAudio
```

The app automatically manages the Docker container lifecycle — starting it on launch and stopping it on exit.

## Settings

Right-click the tray icon → **Settings...** to configure:

| Setting | Default | Description |
|---------|---------|-------------|
| Docker Image | `ghcr.io/remsky/kokoro-fastapi-cpu:latest` | Use `ghcr.io/remsky/kokoro-fastapi-gpu:latest` for GPU acceleration |
| Port | `8880` | Local port for the Kokoro API |
| Container Name | `localtts-kokoro` | Docker container name |
| Voice | `af_heart` | Kokoro voice ID (selectable list with preview) |
| Log Level | Info | Minimum log level to record |
| Auto-start container | On | Start the Docker container on app launch |
| Auto-stop container | On | Stop the Docker container on app exit |
| Enable Reader View | On | Show reader window on hotkey (when off, hotkey plays audio only) |
| Reader Auto-play | On | Auto-play TTS when reader opens |
| Reader Dark Mode | Off | Use dark theme for reader window |
| Reader Font | Segoe UI | Font family for reader text |
| Reader Font Size | 18 | Font size for reader text (10-36) |

Settings are saved to `settings.json` next to the executable.

## Troubleshooting

Right-click the tray icon → **View Log...** to see real-time log output. Logs are also written to `localtts.log` in the application's output directory.

Common issues:
- **"Starting Kokoro..." stays forever**: Check that Docker Desktop is running and `docker ps` works from your terminal
- **"No text selected"**: Make sure you have text highlighted before pressing the hotkey
- **No tray icon visible**: Check the system tray overflow area (^ arrow in taskbar)

## Contributing

Contributions are welcome! One of the biggest goals for this project is making it easier to set up and use for everyone — if that's something you're interested in helping with, we'd love the input.

Feel free to open an issue to report bugs or suggest features, or submit a pull request if you'd like to contribute directly. All skill levels are welcome.
