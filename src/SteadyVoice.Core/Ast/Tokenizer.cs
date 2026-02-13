using System.Text.RegularExpressions;

namespace SteadyVoice.Core.Ast;

/// <summary>
/// Produces a flat sequence of <see cref="Token"/>s from a parsed AST.
/// Walks all <see cref="TextNode"/>s depth-first and classifies each
/// character run by kind. Source spans reference the canonical Markdown string.
/// </summary>
public static partial class Tokenizer
{
    // URL pattern: http(s)://... until whitespace
    [GeneratedRegex(@"https?://\S+", RegexOptions.Compiled)]
    private static partial Regex UrlPattern();

    // Common abbreviations that end with a period but aren't sentence endings.
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "Mr.", "Mrs.", "Ms.", "Dr.", "Prof.", "Sr.", "Jr.",
        "St.", "Ave.", "Blvd.", "Dept.", "Est.",
        "vs.", "etc.", "approx.", "govt.",
        "e.g.", "i.e.", "al.", "fig.", "vol.", "no.",
    };

    /// <summary>
    /// Tokenize all inline text in the document.
    /// </summary>
    public static List<Token> Tokenize(DocumentNode document)
    {
        var tokens = new List<Token>();

        foreach (var textNode in document.DescendantsOfType<TextNode>())
            TokenizeTextNode(textNode, tokens);

        return tokens;
    }

    private static void TokenizeTextNode(TextNode node, List<Token> tokens)
    {
        var text = node.Text;
        var baseOffset = node.SourceSpan.Start;
        var i = 0;

        while (i < text.Length)
        {
            // Try URL first (longest match wins)
            var urlMatch = UrlPattern().Match(text, i);
            if (urlMatch.Success && urlMatch.Index == i)
            {
                tokens.Add(new Token
                {
                    Text = urlMatch.Value,
                    SourceSpan = new Span(baseOffset + i, baseOffset + i + urlMatch.Length),
                    Kind = TokenKind.Url,
                });
                i += urlMatch.Length;
                continue;
            }

            // Whitespace run
            if (char.IsWhiteSpace(text[i]))
            {
                var start = i;
                while (i < text.Length && char.IsWhiteSpace(text[i]))
                    i++;
                tokens.Add(new Token
                {
                    Text = text[start..i],
                    SourceSpan = new Span(baseOffset + start, baseOffset + i),
                    Kind = TokenKind.Whitespace,
                });
                continue;
            }

            // Word or number: letters, digits, apostrophes, hyphens within words
            if (char.IsLetterOrDigit(text[i]))
            {
                var start = i;
                while (i < text.Length && IsWordChar(text, i))
                    i++;

                // Absorb a trailing period if this forms a known abbreviation
                var word = text[start..i];
                if (i < text.Length && text[i] == '.' && Abbreviations.Contains(word + "."))
                {
                    i++;
                    word = text[start..i];
                    tokens.Add(new Token
                    {
                        Text = word,
                        SourceSpan = new Span(baseOffset + start, baseOffset + i),
                        Kind = TokenKind.Abbreviation,
                    });
                    continue;
                }

                // Classify as Number if all digits (with optional decimal point)
                var kind = IsNumber(word) ? TokenKind.Number : TokenKind.Word;
                tokens.Add(new Token
                {
                    Text = word,
                    SourceSpan = new Span(baseOffset + start, baseOffset + i),
                    Kind = kind,
                });
                continue;
            }

            // Everything else is punctuation (one character at a time)
            tokens.Add(new Token
            {
                Text = text[i].ToString(),
                SourceSpan = new Span(baseOffset + i, baseOffset + i + 1),
                Kind = TokenKind.Punctuation,
            });
            i++;
        }
    }

    private static bool IsWordChar(string text, int i)
    {
        var c = text[i];
        if (char.IsLetterOrDigit(c))
            return true;

        // Apostrophes and hyphens are word-internal if surrounded by letters/digits
        if (c is '\'' or '\u2019' or '-')
            return i > 0 && i < text.Length - 1
                && char.IsLetterOrDigit(text[i - 1])
                && char.IsLetterOrDigit(text[i + 1]);

        // Periods are word-internal when between letters/digits (e.g., "3.14", "e.g")
        if (c == '.')
            return i > 0 && i < text.Length - 1
                && char.IsLetterOrDigit(text[i - 1])
                && char.IsLetterOrDigit(text[i + 1]);

        return false;
    }

    private static bool IsNumber(string word)
    {
        var hasDigit = false;
        foreach (var c in word)
        {
            if (char.IsDigit(c))
                hasDigit = true;
            else if (c != '.')
                return false;
        }
        return hasDigit && !word.StartsWith('.') && !word.EndsWith('.');
    }
}
