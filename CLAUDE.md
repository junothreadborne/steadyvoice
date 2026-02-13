# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
dotnet run --project SteadyVoice          # Build and run (Debug)
dotnet build SteadyVoice                  # Build only (Debug)
dotnet build SteadyVoice -c Release       # Build (Release)
```

Requires .NET 10 SDK. A Kokoro TTS API must be running and reachable at the configured API URL (default `http://localhost:8880`).

There is no test suite.

## What This Is

SteadyVoice is a Windows-only WPF system tray app that reads highlighted text aloud using Kokoro TTS. User highlights text anywhere, presses Ctrl+Shift+R, and the app captures the text, sends it to a Kokoro API at a configurable URL, and plays back the audio with optional word-by-word highlighting in a reader window.

## Architecture

**Entry point**: `App.xaml.cs` — `OnStartup()` loads settings, initializes all services, and registers the global hotkey. `OnExit()` tears everything down.

**Core flow**:
```
Hotkey (Ctrl+Shift+R)
  → TextCaptureService (UI Automation, clipboard fallback)
  → TextProcessor.Clean()
  → TtsService → HTTP POST to Kokoro API (localhost:8880/dev/captioned_speech)
  → AudioPlayerService (NAudio MP3 playback)
  → ReaderWindow (word-by-word highlighting synced via timestamps)
```

**Services** (`SteadyVoice/Services/`):
- **AppSettings** — JSON persistence of `settings.json` next to executable, defaults on missing/corrupt; includes configurable `ApiUrl`
- **HotkeyService** — Global hotkey via P/Invoke to `user32.dll`
- **TextCaptureService** — Tier 1: UI Automation (non-destructive), Tier 2: simulated Ctrl+C with modifier release polling
- **TextProcessor** — Encoding normalization, spacing, punctuation cleanup
- **TtsService** — HTTP client to Kokoro; returns NDJSON stream with audio chunks + word timestamps
- **AudioPlayerService** — NAudio `WaveOutEvent` playback with latency-compensated position tracking
- **Log** — Static, thread-safe file logger with 5MB rotation and real-time subscription for LogWindow
- **CursorIndicator** — Shows hourglass cursor during TTS generation

**Windows** (WPF):
- **ReaderWindow** — Styled text with click-to-play-from-word; maintains parallel `_wordRuns`/`_wordStartIndices` lists for word-level indexing; light/dark theming via Window.Resources
- **SettingsWindow** — Dialog for API URL, voice, reader prefs, log level; returns `DialogResult`
- **LogWindow** — Real-time log viewer, singleton pattern

## Key Patterns

- **Async/CancellationToken throughout** — TTS operations are cancellable; pressing hotkey during playback stops it
- **NDJSON streaming** — Kokoro API returns newline-delimited JSON objects with audio chunks and `WordTimestamp` data; audio chunks are concatenated, timestamps collected for synchronized highlighting
- **Timestamp alignment** — Greedy forward-only token matching between API timestamps and displayed words; adjustable timescale compensates for audio latency (120ms default device latency)
- **P/Invoke** — Used for hotkey registration, clipboard simulation, and cursor management
- **Settings hot-reload** — No restart needed; new `TtsService` instance created after settings save

## Dependencies

- `Hardcodet.NotifyIcon.Wpf` v2.0.1 — System tray icon
- `NAudio` v2.2.1 — Audio playback
