# SteadyVoice Roadmap — Canonical Text & Reading Modes Architecture

## Purpose

SteadyVoice currently captures highlighted text via a global hotkey and reads it aloud using a local Kokoro TTS container, with optional word-by-word highlighting.

This document outlines the next architectural evolution:

* Introduce a **canonical document layer (Markdown-based)**
* Formalize **Reading Modes** as policy-driven behaviors
* Refactor text handling into a clean **Document → Plan → Speech** pipeline
* Keep the product hotkey-first while expanding flexibility

The goal is to make SteadyVoice:

* Structurally robust
* Mode-aware (word / normal / zen)
* Configurable without becoming chaotic
* Prepared for large text input without sacrificing latency or alignment precision

---

# Current State (Simplified)

```
Hotkey
  → TextCaptureService
  → TextProcessor.Clean()
  → TtsService (Kokoro)
  → AudioPlayerService
  → ReaderWindow (word highlight)
```

This is effective but linear and tightly coupled.

Text is treated as a cleaned string.
Structure is implicit.
Modes are not first-class.
Chunking is implicit (handled by Kokoro stream).

To support larger inputs and multiple reading styles, text must become structured.

---

# Strategic Shift: Canonical Document Layer

## Core Principle

SteadyVoice should not adapt to arbitrary formats directly.

Instead:

> All captured text is normalized into **canonical Markdown**.
> All further processing operates exclusively on that Markdown.

This creates a single structural contract for the entire system.

---

# New Pipeline (High-Level)

```
Hotkey
  → CaptureEvent
  → Canonicalization (→ SteadyMarkdown)
  → Markdown → AST
  → ReadingModePolicy
  → SpeechPlan
  → TtsService
  → Audio Stitching
  → Playback + Highlight
```

Each stage becomes independent and testable.

---

# 1. Capture Layer (Hotkey-First Identity)

SteadyVoice remains hotkey-driven.

Capture produces:

```csharp
class CaptureEvent {
    string RawText;
    string? HtmlFragment;      // if available via clipboard
    string SourceApp;
    string? WindowTitle;
    DateTime Timestamp;
}
```

### Design Intent

* Keep hotkey-only interaction for v1.
* Maintain hybrid capture strategy:

  * UI Automation first
  * Clipboard fallback second (preserve clipboard)
* Avoid file upload or manual document selection in v1.

This keeps the product lightweight and context-driven.

---

# 2. Canonicalization → “SteadyMarkdown”

All raw input is converted into a strict Markdown subset.

## SteadyMarkdown Dialect

Supported structures:

* Headings (`#`)
* Paragraphs (blank line separated)
* Ordered/unordered lists
* Blockquotes
* Fenced code blocks
* Inline code
* Links
* Horizontal rules

Intentionally unsupported or flattened:

* Complex tables → converted to lists
* Images → replaced with descriptive placeholders
* Styling → discarded
* Layout elements → removed

### Why Markdown?

* Clear structural boundaries
* Easy to parse into AST
* Human-readable (optional source view later)
* Stable chunking anchors

---

# 3. AST Layer

Markdown is parsed into a structured AST:

## Block Nodes

* Document
* Heading
* Paragraph
* List
* ListItem
* QuoteBlock
* CodeBlock
* ThematicBreak

Each node contains:

```csharp
struct Span {
    int Start;
    int End; // relative to canonical Markdown string
}
```

Spans always reference canonical Markdown.

This allows:

* Resume from position
* Click-to-play
* Word highlighting
* Deterministic navigation

---

# 4. Derived Linguistic Layers

AST remains structural.

Additional layers are derived:

## Token Index

* Word
* Punctuation
* Whitespace
* URL
* Number
* Abbreviation

Each token stores:

* Text
* Span
* Normalized text (optional)
* Flags

## Sentence Boundaries

Sentence spans are calculated algorithmically.
This supports:

* Sentence-based synthesis
* Word-by-word highlighting
* Natural chunk splitting

These layers are independent of the AST.

---

# 5. Reading Modes (Policy-Driven Behavior)

Reading modes are not UI states.
They are **policy bundles**.

Each mode defines:

* UnitOfNavigation
* UnitOfSynthesis
* MaxUtteranceSize
* HighlightGranularity
* PrefetchDepth
* NormalizationProfile
* ProsodyProfile
* StitchProfile

---

## Mode 1: Word Mode

Purpose: Precision reading, assistive use

* Navigation: Word
* Synthesis: Sentence-level
* Highlight: Word
* Prefetch: 3–6 sentences
* Prosody: Flat
* Stitch: Minimal padding

Latency and alignment are critical.

---

## Mode 2: Normal Mode

Purpose: Daily use

* Navigation: Paragraph
* Synthesis: Paragraph (adaptive)
* Highlight: Sentence or paragraph
* Prefetch: 1–3 paragraphs
* Prosody: Structured
* Stitch: Padded + normalized

Balanced behavior.

---

## Mode 3: Zen Mode

Purpose: Long-form listening

* Navigation: Section / time-based
* Synthesis: Adaptive chunking
* Highlight: Optional
* Prefetch: Continuous
* Prosody: Expressive
* Stitch: Padded + loudness normalization (+ optional crossfade)

Seamless flow is prioritized over granular alignment.

---

# 6. Speech Planning Layer

ReadingModePolicy + AST produces:

```csharp
class Utterance {
    string SpokenText;
    Span SourceSpan;
    UtteranceType Type;
    PauseProfile Pause;
}
```

Utterances are the atomic TTS requests.

This decouples:

* Structural segmentation
* Speech formatting
* Engine behavior

---

# 7. TTS Streaming and Stitching

Current Kokoro integration already streams NDJSON:

* Audio chunks
* Word timestamps

Future architecture should:

* Generate utterances in batches
* Preserve timestamp alignment
* Apply:

  * Inter-chunk padding
  * Loudness normalization
  * Optional crossfade (Zen mode)

Word timestamps remain mapped back to canonical spans.

---

# 8. Why This Architecture Matters

This refactor enables:

* Massive text support without instability
* Clear separation of concerns
* Predictable behavior across modes
* Easy future engine swaps
* Cleaner debugging
* Potential multi-language expansion later

It also prevents the TTS engine from becoming the structural authority.

---

# What Is Explicitly Not Included (V1 Scope)

* File upload
* EPUB/PDF parsing
* Cross-platform support
* Full browser integration
* Advanced SSML tuning
* Multi-language support

Those can be added later via new CaptureProfiles or extended Normalization layers.

---

# Migration Plan (Incremental)

1. Introduce Canonicalization step (still output string)
2. Parse Markdown → AST (internal only)
3. Implement ReadingModePolicy (default = current behavior)
4. Convert current TTS call to operate on Utterances
5. Add Normal Mode
6. Add Zen Mode
7. Refine Word Mode alignment using token index

No need to rewrite everything at once.

---

# Long-Term Vision

SteadyVoice becomes:

> A hotkey-triggered, structure-aware reading engine that turns anything into a clean, speakable document.

Not just a “highlight and read” tool.
