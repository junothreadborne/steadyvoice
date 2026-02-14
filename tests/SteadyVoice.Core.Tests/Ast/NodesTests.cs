using SteadyVoice.Core.Ast;

namespace SteadyVoice.Core.Tests.Ast;

public class NodesTests
{
    [Fact]
    public void DocumentNode_HoldsSourceString()
    {
        var doc = new DocumentNode
        {
            Source = "# Hello",
            SourceSpan = new Span(0, 7),
        };

        Assert.Equal("# Hello", doc.Source);
    }

    [Fact]
    public void Children_DefaultsToEmptyList()
    {
        var node = new ParagraphNode { SourceSpan = Span.Empty };
        Assert.Empty(node.Children);
    }

    [Fact]
    public void DescendantsDepthFirst_WalksTree()
    {
        var doc = new DocumentNode
        {
            Source = "",
            SourceSpan = Span.Empty,
        };
        var heading = new HeadingNode { Level = 1, SourceSpan = Span.Empty };
        var text1 = new TextNode { Text = "Title", SourceSpan = Span.Empty };
        heading.Children.Add(text1);

        var para = new ParagraphNode { SourceSpan = Span.Empty };
        var text2 = new TextNode { Text = "Body", SourceSpan = Span.Empty };
        para.Children.Add(text2);

        doc.Children.Add(heading);
        doc.Children.Add(para);

        var descendants = doc.DescendantsDepthFirst().ToList();

        Assert.Equal(4, descendants.Count);
        Assert.Same(heading, descendants[0]);
        Assert.Same(text1, descendants[1]);
        Assert.Same(para, descendants[2]);
        Assert.Same(text2, descendants[3]);
    }

    [Fact]
    public void DescendantsOfType_FiltersCorrectly()
    {
        var doc = new DocumentNode
        {
            Source = "",
            SourceSpan = Span.Empty,
        };
        var heading = new HeadingNode { Level = 1, SourceSpan = Span.Empty };
        var para = new ParagraphNode { SourceSpan = Span.Empty };
        var text = new TextNode { Text = "Hello", SourceSpan = Span.Empty };
        para.Children.Add(text);

        doc.Children.Add(heading);
        doc.Children.Add(para);

        var textNodes = doc.DescendantsOfType<TextNode>().ToList();
        Assert.Single(textNodes);
        Assert.Same(text, textNodes[0]);
    }

    [Fact]
    public void DescendantsDepthFirst_EmptyForLeafNode()
    {
        var node = new ThematicBreakNode { SourceSpan = Span.Empty };
        Assert.Empty(node.DescendantsDepthFirst());
    }

    [Fact]
    public void HeadingNode_StoresLevel()
    {
        var h3 = new HeadingNode { Level = 3, SourceSpan = Span.Empty };
        Assert.Equal(3, h3.Level);
    }

    [Fact]
    public void ListNode_StoresOrderedFlag()
    {
        var ordered = new ListNode { IsOrdered = true, SourceSpan = Span.Empty };
        var unordered = new ListNode { IsOrdered = false, SourceSpan = Span.Empty };

        Assert.True(ordered.IsOrdered);
        Assert.False(unordered.IsOrdered);
    }

    [Fact]
    public void CodeBlockNode_StoresLanguageAndCode()
    {
        var block = new CodeBlockNode
        {
            Language = "csharp",
            Code = "Console.WriteLine();",
            SourceSpan = Span.Empty,
        };

        Assert.Equal("csharp", block.Language);
        Assert.Equal("Console.WriteLine();", block.Code);
    }

    [Fact]
    public void CodeBlockNode_LanguageCanBeNull()
    {
        var block = new CodeBlockNode
        {
            Code = "some code",
            SourceSpan = Span.Empty,
        };

        Assert.Null(block.Language);
    }

    [Fact]
    public void ParentSpanContainsChildSpans()
    {
        var doc = new DocumentNode
        {
            Source = "# Hi\n\nWorld",
            SourceSpan = new Span(0, 11),
        };
        var heading = new HeadingNode { Level = 1, SourceSpan = new Span(0, 4) };
        var para = new ParagraphNode { SourceSpan = new Span(6, 11) };

        doc.Children.Add(heading);
        doc.Children.Add(para);

        foreach (var child in doc.Children)
            Assert.True(doc.SourceSpan.Contains(child.SourceSpan));
    }
}
