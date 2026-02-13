using SteadyVoice.Core.Ast;

namespace SteadyVoice.Core.Tests.Ast;

public class TokenTests
{
    [Fact]
    public void Token_StoresAllProperties()
    {
        var token = new Token
        {
            Text = "Hello",
            SourceSpan = new Span(0, 5),
            Kind = TokenKind.Word,
            NormalizedText = "hello",
        };

        Assert.Equal("Hello", token.Text);
        Assert.Equal(new Span(0, 5), token.SourceSpan);
        Assert.Equal(TokenKind.Word, token.Kind);
        Assert.Equal("hello", token.NormalizedText);
    }

    [Fact]
    public void Token_NormalizedTextDefaultsToNull()
    {
        var token = new Token
        {
            Text = "...",
            SourceSpan = new Span(5, 8),
            Kind = TokenKind.Punctuation,
        };

        Assert.Null(token.NormalizedText);
    }

    [Theory]
    [InlineData(TokenKind.Word)]
    [InlineData(TokenKind.Punctuation)]
    [InlineData(TokenKind.Whitespace)]
    [InlineData(TokenKind.Url)]
    [InlineData(TokenKind.Number)]
    [InlineData(TokenKind.Abbreviation)]
    public void TokenKind_AllVariantsExist(TokenKind kind)
    {
        var token = new Token
        {
            Text = "test",
            SourceSpan = Span.Empty,
            Kind = kind,
        };

        Assert.Equal(kind, token.Kind);
    }
}
