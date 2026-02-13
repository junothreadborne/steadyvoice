using SteadyVoice.Core.Ast;

namespace SteadyVoice.Core.Tests.Ast;

public class SpanTests
{
    [Fact]
    public void Length_ReturnsCorrectValue()
    {
        var span = new Span(3, 10);
        Assert.Equal(7, span.Length);
    }

    [Fact]
    public void Empty_HasZeroLength()
    {
        var span = Span.Empty;
        Assert.Equal(0, span.Length);
        Assert.True(span.IsEmpty);
    }

    [Fact]
    public void IsEmpty_FalseForNonEmptySpan()
    {
        var span = new Span(0, 5);
        Assert.False(span.IsEmpty);
    }

    [Theory]
    [InlineData(5, true)]   // inside
    [InlineData(3, true)]   // at start (inclusive)
    [InlineData(9, true)]   // just before end
    [InlineData(10, false)] // at end (exclusive)
    [InlineData(2, false)]  // before start
    [InlineData(11, false)] // after end
    public void Contains_Offset(int offset, bool expected)
    {
        var span = new Span(3, 10);
        Assert.Equal(expected, span.Contains(offset));
    }

    [Fact]
    public void Contains_Span_TrueWhenFullyEnclosed()
    {
        var outer = new Span(0, 20);
        var inner = new Span(5, 15);
        Assert.True(outer.Contains(inner));
    }

    [Fact]
    public void Contains_Span_FalseWhenPartialOverlap()
    {
        var a = new Span(0, 10);
        var b = new Span(5, 15);
        Assert.False(a.Contains(b));
    }

    [Fact]
    public void Contains_Span_SelfContainsSelf()
    {
        var span = new Span(3, 7);
        Assert.True(span.Contains(span));
    }

    [Fact]
    public void Overlaps_TrueForPartialOverlap()
    {
        var a = new Span(0, 10);
        var b = new Span(5, 15);
        Assert.True(a.Overlaps(b));
        Assert.True(b.Overlaps(a));
    }

    [Fact]
    public void Overlaps_FalseForAdjacentSpans()
    {
        var a = new Span(0, 5);
        var b = new Span(5, 10);
        Assert.False(a.Overlaps(b));
    }

    [Fact]
    public void Overlaps_FalseForDisjointSpans()
    {
        var a = new Span(0, 3);
        var b = new Span(7, 10);
        Assert.False(a.Overlaps(b));
    }

    [Fact]
    public void SliceFrom_ExtractsCorrectSubstring()
    {
        var source = "Hello, world!";
        var span = new Span(7, 12);
        Assert.Equal("world", span.SliceFrom(source));
    }

    [Fact]
    public void Equality_ValueSemantics()
    {
        var a = new Span(3, 10);
        var b = new Span(3, 10);
        var c = new Span(3, 11);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.True(a == b);
        Assert.True(a != c);
    }
}
