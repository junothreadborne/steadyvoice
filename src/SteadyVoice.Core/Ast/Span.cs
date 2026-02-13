namespace SteadyVoice.Core.Ast;

/// <summary>
/// A half-open character range [Start, End) within the canonical Markdown string.
/// </summary>
public readonly record struct Span(int Start, int End)
{
    public int Length => End - Start;

    public bool IsEmpty => Start == End;

    public bool Contains(int offset) => offset >= Start && offset < End;

    public bool Contains(Span other) => other.Start >= Start && other.End <= End;

    public bool Overlaps(Span other) => Start < other.End && other.Start < End;

    public string SliceFrom(string source) => source[Start..End];

    public static Span Empty => new(0, 0);
}
