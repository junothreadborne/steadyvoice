namespace SteadyVoice.Core.Ast;

public enum TokenKind
{
    Word,
    Punctuation,
    Whitespace,
    Url,
    Number,
    Abbreviation,
}

/// <summary>
/// A linguistic token derived from the AST. Tokens form a flat sequence
/// independent of the block-level tree structure.
/// </summary>
public class Token
{
    public required string Text { get; init; }
    public required Span SourceSpan { get; init; }
    public required TokenKind Kind { get; init; }
    public string? NormalizedText { get; init; }
}
