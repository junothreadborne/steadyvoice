# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
dotnet run --project src/SteadyVoice          # Build and run (Debug)
dotnet build                                  # Build entire solution
dotnet build src/SteadyVoice -c Release       # Build app (Release)
dotnet test                                   # Run all tests
dotnet test tests/SteadyVoice.Core.Tests      # Run core tests only
```

Requires .NET 10 SDK. A Kokoro TTS API must be running and reachable at the configured API URL (default `http://localhost:8880`).

## What This Is

SteadyVoice is a Windows-only WPF system tray app that reads highlighted text aloud using Kokoro TTS. User highlights text anywhere, presses Ctrl+Shift+R, and the app captures the text, sends it to a Kokoro API at a configurable URL, and plays back the audio with optional word-by-word highlighting in a reader window.

## Project Structure

```
src/
  SteadyVoice/              # WPF app (Windows-only, system tray)
  SteadyVoice.Core/         # Platform-agnostic core library
    Ast/                     # Canonical document AST, tokenizer, and chunker
tests/
  SteadyVoice.Core.Tests/   # xUnit tests for Core
```

`SteadyVoice.Core` contains the document model and Markdown parser (via Markdig). The WPF app references Core. Tests reference Core.

## Architecture

**Entry point**: `App.xaml.cs` — `OnStartup()` loads settings, initializes all services, and registers the global hotkey. `OnExit()` tears everything down.

**Core flow**:
```
Hotkey (Ctrl+Shift+R)
  → TextCaptureService (UI Automation, clipboard fallback)
  → TextProcessor.Clean()
  → MarkdownParser.Parse() → DocumentNode AST
  → Tokenizer.Tokenize() → flat token list
  → AstChunker.Chunk() → split into ~200-word chunks at block boundaries
  → For each chunk:
      → TtsService → HTTP POST to Kokoro API (localhost:8880/dev/captioned_speech)
      → AudioPlayerService.AddChunkAsync() (decode MP3→PCM, stream into BufferedWaveProvider)
      → ReaderWindow.AppendTimestamps() (incremental highlight mapping)
  → AudioPlayerService.FinishStreaming()
```

**Services** (`src/SteadyVoice/Services/`):
- **AppSettings** — JSON persistence of `settings.json` next to executable, defaults on missing/corrupt; includes configurable `ApiUrl`
- **HotkeyService** — Global hotkey via P/Invoke to `user32.dll`
- **TextCaptureService** — Tier 1: UI Automation (non-destructive), Tier 2: simulated Ctrl+C with modifier release polling
- **TextProcessor** — Encoding normalization, spacing, punctuation cleanup
- **TtsService** — HTTP client to Kokoro; returns NDJSON stream with audio chunks + word timestamps; called per-chunk during streaming
- **AudioPlayerService** — Two modes: classic single-shot `Play(byte[])` and streaming `AddChunkAsync()`/`FinishStreaming()`. Streaming mode decodes MP3 chunks to PCM and feeds a `BufferedWaveProvider` for gapless playback with backpressure (waits for buffer space). Position tracked via `WaveOutEvent.GetPosition()`
- **Log** — Static, thread-safe file logger with 5MB rotation and real-time subscription for LogWindow
- **CursorIndicator** — Shows hourglass cursor during TTS generation

**Windows** (WPF):
- **ReaderWindow** — Styled text with click-to-play-from-word; maintains parallel `_wordRuns`/`_wordTokens` lists for word-level indexing; supports incremental timestamp appending via `StartStreamingHighlight()`/`AppendTimestamps()` with resumable `BuildTimestampRunMap`; light/dark theming via Window.Resources
- **SettingsWindow** — Dialog for API URL, voice, reader prefs, log level; returns `DialogResult`
- **LogWindow** — Real-time log viewer, singleton pattern

## Key Patterns

- **Chunked TTS streaming** — `AstChunker` (in Core) splits text at top-level AST block boundaries (paragraphs, headings, lists) targeting ~200 words per chunk. Chunks are sent to the TTS API sequentially; audio playback starts after the first chunk returns. `AudioPlayerService` streams decoded PCM into a `BufferedWaveProvider` with backpressure. Timestamps are offset by cumulative chunk duration and appended incrementally to `ReaderWindow`
- **Async/CancellationToken throughout** — TTS operations are cancellable; pressing hotkey during playback cancels in-flight requests and stops streaming
- **NDJSON streaming** — Kokoro API returns newline-delimited JSON objects with audio chunks and `WordTimestamp` data; audio chunks are concatenated per-request, timestamps collected for synchronized highlighting
- **Timestamp alignment** — Greedy forward-only token matching between API timestamps and displayed words; `BuildTimestampRunMap` returns next token index so mapping resumes correctly across chunk boundaries; 120ms default device latency offset
- **P/Invoke** — Used for hotkey registration, clipboard simulation, and cursor management
- **Settings hot-reload** — No restart needed; new `TtsService` instance created after settings save

## Dependencies

- `Hardcodet.NotifyIcon.Wpf` v2.0.1 — System tray icon (SteadyVoice)
- `NAudio` v2.2.1 — Audio playback (SteadyVoice)
- `Markdig` v0.45.0 — Markdown parsing to AST (SteadyVoice.Core)
