# Plan: feature/ast — AST Model + Project Restructure

## Goal
Define the canonical AST node types from the architecture roadmap, restructure the solution for open-source multi-project layout, and build first-pass unit tests for the AST layer. No changes to existing app behavior.

---

## 1. Restructure solution layout

Move from flat layout to `src/` + `tests/`:

```
steadyvoice/
├── src/
│   ├── SteadyVoice/                  # Existing WPF app (moved from root)
│   └── SteadyVoice.Core/            # NEW: platform-agnostic core library
│       ├── SteadyVoice.Core.csproj
│       └── Ast/
│           ├── Nodes.cs              # Block node types
│           ├── Span.cs               # Source span tracking
│           ├── Token.cs              # Linguistic token types
│           └── MarkdownParser.cs     # Markdig → SteadyVoice AST
├── tests/
│   └── SteadyVoice.Core.Tests/      # NEW: xUnit test project
│       ├── SteadyVoice.Core.Tests.csproj
│       └── Ast/
│           ├── NodesTests.cs
│           ├── SpanTests.cs
│           ├── TokenTests.cs
│           └── MarkdownParserTests.cs
├── SteadyVoice.sln                   # Updated with new projects
├── README.md
├── CLAUDE.md
├── ARCHITECTURE_ROADMAP.md
└── LICENSE
```

**Steps:**
- Create `src/` and `tests/` directories
- Move `SteadyVoice/` → `src/SteadyVoice/`
- Create `src/SteadyVoice.Core/` class library (net10.0, no WPF)
- Create `tests/SteadyVoice.Core.Tests/` xUnit project
- Update `SteadyVoice.sln` to reference all three projects at new paths
- Add project reference: `SteadyVoice` → `SteadyVoice.Core`
- Add project reference: `SteadyVoice.Core.Tests` → `SteadyVoice.Core`
- Verify `dotnet build` still works

---

## 2. Define AST node types (`SteadyVoice.Core/Ast/`)

### Span (source position tracking)
```csharp
public readonly record struct Span(int Start, int End);
// Refers to character offsets in the canonical Markdown string
```

### Block nodes (from roadmap §3)
```csharp
public abstract class AstNode {
    Span SourceSpan;
    List<AstNode> Children;
}

// Concrete types:
// - DocumentNode (root, contains blocks)
// - HeadingNode (level 1-6, contains inlines)
// - ParagraphNode (contains inlines)
// - ListNode (ordered/unordered, contains ListItemNodes)
// - ListItemNode (contains blocks)
// - QuoteBlockNode (contains blocks)
// - CodeBlockNode (language?, raw text)
// - ThematicBreakNode (no children)
```

### Token types (from roadmap §4)
```csharp
public enum TokenKind { Word, Punctuation, Whitespace, Url, Number, Abbreviation }

public class Token {
    string Text;
    Span SourceSpan;
    string? NormalizedText;
    TokenKind Kind;
}
```

Design notes:
- Nodes are classes (tree with children), Span is a value type
- DocumentNode holds the canonical Markdown source string
- Each node's Span is relative to that source string
- Tokens are a flat derived layer, not part of the tree

---

## 3. Implement Markdig → AST parser (`MarkdownParser.cs`)

- Parse canonical Markdown string with Markdig
- Walk Markdig's AST and map to SteadyVoice node types
- Preserve accurate source spans (Markdig tracks these)
- Unsupported Markdig node types (tables, images) → flatten or skip per roadmap §2
- Add Markdig NuGet package to `SteadyVoice.Core`

---

## 4. Write unit tests

Test coverage for:

**Span tests:**
- Construction, equality, contains/overlaps logic

**Node tests:**
- Tree construction and child traversal
- Span consistency (parent spans contain child spans)

**Token tests:**
- TokenKind classification
- NormalizedText behavior

**MarkdownParser tests:**
- Plain paragraph → single ParagraphNode
- Multiple paragraphs → multiple ParagraphNodes under DocumentNode
- Headings at different levels
- Ordered and unordered lists
- Nested structures (list inside blockquote)
- Code blocks (fenced)
- Thematic breaks
- Mixed document with all supported types
- Unsupported elements (tables, images) are handled gracefully
- Source spans are accurate (round-trip: span substring == node text)

---

## 5. Update CLAUDE.md

Add test commands and new project structure info:
```bash
dotnet test                                          # Run all tests
dotnet test tests/SteadyVoice.Core.Tests             # Run core tests only
```

---

## What this does NOT include
- Wiring AST into the live app pipeline (no changes to App.xaml.cs, TtsService, etc.)
- ReadingModePolicy, Utterances, or SpeechPlan
- Canonicalization step (raw text → Markdown normalization) — that's a separate concern
- Token index / sentence boundary detection (roadmap §4) — can follow in a later PR
