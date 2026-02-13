using SteadyVoice.Core.Ast;

namespace SteadyVoice.Core.Tests.Ast;

public class TokenizerTests
{
    private static List<Token> TokenizeMarkdown(string md)
    {
        var doc = MarkdownParser.Parse(md);
        return Tokenizer.Tokenize(doc);
    }

    [Fact]
    public void Tokenize_EmptyDocument_ReturnsNoTokens()
    {
        var tokens = TokenizeMarkdown("");
        Assert.Empty(tokens);
    }

    [Fact]
    public void Tokenize_SingleWord()
    {
        var tokens = TokenizeMarkdown("Hello");
        var t = Assert.Single(tokens);
        Assert.Equal("Hello", t.Text);
        Assert.Equal(TokenKind.Word, t.Kind);
    }

    [Fact]
    public void Tokenize_TwoWords_IncludesWhitespace()
    {
        var tokens = TokenizeMarkdown("Hello world");

        Assert.Equal(3, tokens.Count);
        Assert.Equal(TokenKind.Word, tokens[0].Kind);
        Assert.Equal("Hello", tokens[0].Text);
        Assert.Equal(TokenKind.Whitespace, tokens[1].Kind);
        Assert.Equal(" ", tokens[1].Text);
        Assert.Equal(TokenKind.Word, tokens[2].Kind);
        Assert.Equal("world", tokens[2].Text);
    }

    [Fact]
    public void Tokenize_Punctuation_SeparateTokens()
    {
        var tokens = TokenizeMarkdown("Hello, world!");

        var kinds = tokens.Select(t => t.Kind).ToList();
        Assert.Contains(TokenKind.Punctuation, kinds);

        var comma = tokens.First(t => t.Text == ",");
        Assert.Equal(TokenKind.Punctuation, comma.Kind);

        var excl = tokens.First(t => t.Text == "!");
        Assert.Equal(TokenKind.Punctuation, excl.Kind);
    }

    [Fact]
    public void Tokenize_Number()
    {
        var tokens = TokenizeMarkdown("There are 42 items");

        var num = tokens.First(t => t.Text == "42");
        Assert.Equal(TokenKind.Number, num.Kind);
    }

    [Fact]
    public void Tokenize_DecimalNumber()
    {
        var tokens = TokenizeMarkdown("Price is 3.14 dollars");

        var num = tokens.First(t => t.Text == "3.14");
        Assert.Equal(TokenKind.Number, num.Kind);
    }

    [Fact]
    public void Tokenize_MixedAlphaNumeric_IsWord()
    {
        var tokens = TokenizeMarkdown("Use v2beta3 API");

        var mixed = tokens.First(t => t.Text == "v2beta3");
        Assert.Equal(TokenKind.Word, mixed.Kind);
    }

    [Fact]
    public void Tokenize_Url()
    {
        var tokens = TokenizeMarkdown("Visit https://example.com/path today");

        var url = tokens.First(t => t.Kind == TokenKind.Url);
        Assert.Equal("https://example.com/path", url.Text);
    }

    [Fact]
    public void Tokenize_HttpUrl()
    {
        var tokens = TokenizeMarkdown("See http://example.org for info");

        var url = tokens.First(t => t.Kind == TokenKind.Url);
        Assert.StartsWith("http://example.org", url.Text);
    }

    [Fact]
    public void Tokenize_Abbreviation()
    {
        var tokens = TokenizeMarkdown("Dr. Smith is here");

        var abbr = tokens.First(t => t.Text == "Dr.");
        Assert.Equal(TokenKind.Abbreviation, abbr.Kind);
    }

    [Fact]
    public void Tokenize_MultipleAbbreviations()
    {
        var tokens = TokenizeMarkdown("e.g. Mr. Jones vs. Mrs. Jones");

        var abbreviations = tokens.Where(t => t.Kind == TokenKind.Abbreviation).ToList();
        Assert.Contains(abbreviations, a => a.Text == "e.g.");
        Assert.Contains(abbreviations, a => a.Text == "Mr.");
        Assert.Contains(abbreviations, a => a.Text == "vs.");
        Assert.Contains(abbreviations, a => a.Text == "Mrs.");
    }

    [Fact]
    public void Tokenize_Contraction_StaysAsOneWord()
    {
        var tokens = TokenizeMarkdown("Don't stop");

        var contraction = tokens.First(t => t.Text.Contains("Don"));
        Assert.Equal(TokenKind.Word, contraction.Kind);
        Assert.Equal("Don't", contraction.Text);
    }

    [Fact]
    public void Tokenize_SmartQuoteContraction_StaysAsOneWord()
    {
        // U+2019 RIGHT SINGLE QUOTATION MARK — common in text copied from Word/browsers
        var doc = new DocumentNode
        {
            Source = "don\u2019t",
            SourceSpan = new Span(0, 5),
        };
        var para = new ParagraphNode { SourceSpan = new Span(0, 5) };
        var text = new TextNode { Text = "don\u2019t", SourceSpan = new Span(0, 5) };
        para.Children.Add(text);
        doc.Children.Add(para);

        var tokens = Tokenizer.Tokenize(doc);
        var word = Assert.Single(tokens);
        Assert.Equal(TokenKind.Word, word.Kind);
        Assert.Equal("don\u2019t", word.Text);
    }

    [Fact]
    public void Tokenize_LeadingHyphen_IsPunctuation()
    {
        var tokens = TokenizeMarkdown("-start");

        Assert.Equal(TokenKind.Punctuation, tokens[0].Kind);
        Assert.Equal("-", tokens[0].Text);
        Assert.Equal(TokenKind.Word, tokens[1].Kind);
        Assert.Equal("start", tokens[1].Text);
    }

    [Fact]
    public void Tokenize_TrailingHyphen_IsPunctuation()
    {
        var tokens = TokenizeMarkdown("end-");

        var word = tokens.First(t => t.Kind == TokenKind.Word);
        Assert.Equal("end", word.Text);
        var punct = tokens.Last(t => t.Kind == TokenKind.Punctuation);
        Assert.Equal("-", punct.Text);
    }

    [Fact]
    public void Tokenize_HyphenatedWord()
    {
        var tokens = TokenizeMarkdown("well-known fact");

        var hyphenated = tokens.First(t => t.Text.Contains("well"));
        Assert.Equal(TokenKind.Word, hyphenated.Kind);
        Assert.Equal("well-known", hyphenated.Text);
    }

    [Fact]
    public void Tokenize_SourceSpans_MatchCanonicalMarkdown()
    {
        var md = "Hello world";
        var doc = MarkdownParser.Parse(md);
        var tokens = Tokenizer.Tokenize(doc);

        foreach (var token in tokens)
        {
            var sliced = token.SourceSpan.SliceFrom(md);
            Assert.Equal(token.Text, sliced);
        }
    }

    [Fact]
    public void Tokenize_MultipleParagraphs_TokensFromBoth()
    {
        var tokens = TokenizeMarkdown("First para.\n\nSecond para.");

        var words = tokens.Where(t => t.Kind == TokenKind.Word).Select(t => t.Text).ToList();
        Assert.Contains("First", words);
        Assert.Contains("Second", words);
    }

    [Fact]
    public void Tokenize_Heading_TokenizesInlineText()
    {
        var tokens = TokenizeMarkdown("# My Title");

        var words = tokens.Where(t => t.Kind == TokenKind.Word).Select(t => t.Text).ToList();
        Assert.Contains("My", words);
        Assert.Contains("Title", words);
    }

    [Fact]
    public void Tokenize_ListItems()
    {
        var tokens = TokenizeMarkdown("- Alpha\n- Beta");

        var words = tokens.Where(t => t.Kind == TokenKind.Word).Select(t => t.Text).ToList();
        Assert.Contains("Alpha", words);
        Assert.Contains("Beta", words);
    }

    [Fact]
    public void Tokenize_CodeBlock_SkippedNoTextNodes()
    {
        // Code blocks produce CodeBlockNode, not TextNode children,
        // so the tokenizer should not produce tokens from code blocks.
        var tokens = TokenizeMarkdown("```\nsome code\n```");

        var words = tokens.Where(t => t.Kind == TokenKind.Word).ToList();
        Assert.Empty(words);
    }

    [Fact]
    public void Tokenize_CoverageIsComplete_NoGaps()
    {
        var md = "Hello, world! Visit https://example.com today.";
        var doc = MarkdownParser.Parse(md);
        var tokens = Tokenizer.Tokenize(doc);

        // Concatenated token text should reconstruct the original inline text
        var reconstructed = string.Join("", tokens.Select(t => t.Text));
        Assert.Equal("Hello, world! Visit https://example.com today.", reconstructed);
    }

    [Fact]
    public void Tokenize_SpansAreContiguous()
    {
        var md = "One two three";
        var doc = MarkdownParser.Parse(md);
        var tokens = Tokenizer.Tokenize(doc);

        for (var i = 1; i < tokens.Count; i++)
        {
            Assert.True(
                tokens[i - 1].SourceSpan.End == tokens[i].SourceSpan.Start,
                $"Gap between token '{tokens[i - 1].Text}' and '{tokens[i].Text}'");
        }
    }

    [Fact]
    public void Tokenize_PeriodAfterNonAbbreviation_IsPunctuation()
    {
        var tokens = TokenizeMarkdown("End.");

        var period = tokens.First(t => t.Text == ".");
        Assert.Equal(TokenKind.Punctuation, period.Kind);
    }

    [Fact]
    public void Tokenize_EllipsisDots_ArePunctuation()
    {
        var tokens = TokenizeMarkdown("Wait...");

        var dots = tokens.Where(t => t.Kind == TokenKind.Punctuation).ToList();
        Assert.True(dots.Count >= 1);
    }

    [Fact]
    public void Tokenize_MultipleWhitespace_SingleToken()
    {
        // TextProcessor.Clean would normally collapse these,
        // but the tokenizer should handle them regardless.
        var doc = new DocumentNode
        {
            Source = "a   b",
            SourceSpan = new Span(0, 5),
        };
        var para = new ParagraphNode { SourceSpan = new Span(0, 5) };
        var text = new TextNode { Text = "a   b", SourceSpan = new Span(0, 5) };
        para.Children.Add(text);
        doc.Children.Add(para);

        var tokens = Tokenizer.Tokenize(doc);
        var ws = tokens.First(t => t.Kind == TokenKind.Whitespace);
        Assert.Equal("   ", ws.Text);
    }
}
