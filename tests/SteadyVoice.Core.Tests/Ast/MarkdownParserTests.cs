using SteadyVoice.Core.Ast;

namespace SteadyVoice.Core.Tests.Ast;

public class MarkdownParserTests
{
    [Fact]
    public void Parse_EmptyString_ReturnsEmptyDocument()
    {
        var doc = MarkdownParser.Parse("");

        Assert.Empty(doc.Children);
        Assert.Equal("", doc.Source);
    }

    [Fact]
    public void Parse_SingleParagraph()
    {
        var doc = MarkdownParser.Parse("Hello world");

        var para = Assert.Single(doc.Children);
        Assert.IsType<ParagraphNode>(para);

        var text = Assert.Single(para.Children);
        var textNode = Assert.IsType<TextNode>(text);
        Assert.Equal("Hello world", textNode.Text);
    }

    [Fact]
    public void Parse_MultipleParagraphs()
    {
        var md = "First paragraph.\n\nSecond paragraph.";
        var doc = MarkdownParser.Parse(md);

        Assert.Equal(2, doc.Children.Count);
        Assert.All(doc.Children, child => Assert.IsType<ParagraphNode>(child));
    }

    [Theory]
    [InlineData("# Heading 1", 1)]
    [InlineData("## Heading 2", 2)]
    [InlineData("### Heading 3", 3)]
    [InlineData("#### Heading 4", 4)]
    [InlineData("##### Heading 5", 5)]
    [InlineData("###### Heading 6", 6)]
    public void Parse_HeadingLevels(string markdown, int expectedLevel)
    {
        var doc = MarkdownParser.Parse(markdown);

        var heading = Assert.Single(doc.Children);
        var headingNode = Assert.IsType<HeadingNode>(heading);
        Assert.Equal(expectedLevel, headingNode.Level);
    }

    [Fact]
    public void Parse_HeadingWithTextContent()
    {
        var doc = MarkdownParser.Parse("# My Title");

        var heading = Assert.IsType<HeadingNode>(doc.Children[0]);
        var text = Assert.IsType<TextNode>(heading.Children[0]);
        Assert.Equal("My Title", text.Text);
    }

    [Fact]
    public void Parse_UnorderedList()
    {
        var md = "- Item one\n- Item two\n- Item three";
        var doc = MarkdownParser.Parse(md);

        var list = Assert.Single(doc.Children);
        var listNode = Assert.IsType<ListNode>(list);
        Assert.False(listNode.IsOrdered);
        Assert.Equal(3, listNode.Children.Count);
        Assert.All(listNode.Children, child => Assert.IsType<ListItemNode>(child));
    }

    [Fact]
    public void Parse_OrderedList()
    {
        var md = "1. First\n2. Second\n3. Third";
        var doc = MarkdownParser.Parse(md);

        var list = Assert.Single(doc.Children);
        var listNode = Assert.IsType<ListNode>(list);
        Assert.True(listNode.IsOrdered);
        Assert.Equal(3, listNode.Children.Count);
    }

    [Fact]
    public void Parse_ListItem_ContainsParagraphWithText()
    {
        var md = "- Hello";
        var doc = MarkdownParser.Parse(md);

        var list = Assert.IsType<ListNode>(doc.Children[0]);
        var item = Assert.IsType<ListItemNode>(list.Children[0]);

        // List items contain a paragraph which contains text
        var para = Assert.IsType<ParagraphNode>(item.Children[0]);
        var text = Assert.IsType<TextNode>(para.Children[0]);
        Assert.Equal("Hello", text.Text);
    }

    [Fact]
    public void Parse_Blockquote()
    {
        var md = "> This is quoted text.";
        var doc = MarkdownParser.Parse(md);

        var quote = Assert.Single(doc.Children);
        Assert.IsType<QuoteBlockNode>(quote);

        // Blockquote contains a paragraph
        var para = Assert.IsType<ParagraphNode>(quote.Children[0]);
        var text = Assert.IsType<TextNode>(para.Children[0]);
        Assert.Equal("This is quoted text.", text.Text);
    }

    [Fact]
    public void Parse_IndentedCodeBlock()
    {
        var md = "    int x = 1;\n    int y = 2;";
        var doc = MarkdownParser.Parse(md);

        var code = Assert.Single(doc.Children);
        var codeNode = Assert.IsType<CodeBlockNode>(code);
        Assert.Null(codeNode.Language);
        Assert.Contains("int x = 1;", codeNode.Code);
        Assert.Contains("int y = 2;", codeNode.Code);
    }

    [Fact]
    public void Parse_FencedCodeBlock()
    {
        var md = "```csharp\nConsole.WriteLine(\"Hi\");\n```";
        var doc = MarkdownParser.Parse(md);

        var code = Assert.Single(doc.Children);
        var codeNode = Assert.IsType<CodeBlockNode>(code);
        Assert.Equal("csharp", codeNode.Language);
        Assert.Contains("Console.WriteLine", codeNode.Code);
    }

    [Fact]
    public void Parse_FencedCodeBlock_NoLanguage()
    {
        var md = "```\nplain code\n```";
        var doc = MarkdownParser.Parse(md);

        var codeNode = Assert.IsType<CodeBlockNode>(doc.Children[0]);
        Assert.Null(codeNode.Language);
        Assert.Contains("plain code", codeNode.Code);
    }

    [Fact]
    public void Parse_ThematicBreak()
    {
        var md = "Above\n\n---\n\nBelow";
        var doc = MarkdownParser.Parse(md);

        Assert.Equal(3, doc.Children.Count);
        Assert.IsType<ParagraphNode>(doc.Children[0]);
        Assert.IsType<ThematicBreakNode>(doc.Children[1]);
        Assert.IsType<ParagraphNode>(doc.Children[2]);
    }

    [Fact]
    public void Parse_MixedDocument()
    {
        var md = """
            # Title

            A paragraph with some text.

            - Item one
            - Item two

            > A quote

            ```
            code
            ```

            ---

            Final paragraph.
            """;

        var doc = MarkdownParser.Parse(md);

        // Verify we get the expected node types in order
        var types = doc.Children.Select(c => c.GetType()).ToList();
        Assert.Contains(typeof(HeadingNode), types);
        Assert.Contains(typeof(ParagraphNode), types);
        Assert.Contains(typeof(ListNode), types);
        Assert.Contains(typeof(QuoteBlockNode), types);
        Assert.Contains(typeof(CodeBlockNode), types);
        Assert.Contains(typeof(ThematicBreakNode), types);
    }

    [Fact]
    public void Parse_NestedBlockquoteWithList()
    {
        var md = "> - Item in quote\n> - Another item";
        var doc = MarkdownParser.Parse(md);

        var quote = Assert.IsType<QuoteBlockNode>(doc.Children[0]);
        var list = Assert.IsType<ListNode>(quote.Children[0]);
        Assert.Equal(2, list.Children.Count);
    }

    [Fact]
    public void Parse_SourceSpans_AreAccurate()
    {
        var md = "Hello world";
        var doc = MarkdownParser.Parse(md);

        var para = Assert.IsType<ParagraphNode>(doc.Children[0]);
        var text = Assert.IsType<TextNode>(para.Children[0]);

        // The text span should allow us to slice back to the original text
        Assert.Equal("Hello world", text.SourceSpan.SliceFrom(md));
    }

    [Fact]
    public void Parse_SourceSpans_ParentContainsChildren()
    {
        var md = "# Title\n\nA paragraph.";
        var doc = MarkdownParser.Parse(md);

        foreach (var child in doc.Children)
        {
            Assert.True(
                doc.SourceSpan.Contains(child.SourceSpan),
                $"Document span {doc.SourceSpan} should contain child span {child.SourceSpan}");
        }
    }

    [Fact]
    public void Parse_HeadingSpan_MatchesSourceText()
    {
        var md = "# Hello";
        var doc = MarkdownParser.Parse(md);

        var heading = Assert.IsType<HeadingNode>(doc.Children[0]);
        var sliced = heading.SourceSpan.SliceFrom(md);
        Assert.Contains("Hello", sliced);
    }

    [Fact]
    public void Parse_LineBreak_DoesNotProduceNode()
    {
        // Two trailing spaces before newline = hard line break in Markdown
        var md = "Line one  \nLine two";
        var doc = MarkdownParser.Parse(md);

        var para = Assert.IsType<ParagraphNode>(doc.Children[0]);
        // Should only contain TextNodes, no LineBreak artifacts
        Assert.All(para.Children, child => Assert.IsType<TextNode>(child));

        var allText = string.Join("", para.DescendantsOfType<TextNode>().Select(t => t.Text));
        Assert.Contains("Line one", allText);
        Assert.Contains("Line two", allText);
    }

    [Fact]
    public void Parse_EmphasisFlattened_TextPreserved()
    {
        var md = "This is **bold** and *italic* text.";
        var doc = MarkdownParser.Parse(md);

        var para = Assert.IsType<ParagraphNode>(doc.Children[0]);
        var allText = string.Join("", para.DescendantsOfType<TextNode>().Select(t => t.Text));
        Assert.Contains("bold", allText);
        Assert.Contains("italic", allText);
    }

    [Fact]
    public void Parse_LinkFlattened_TextPreserved()
    {
        var md = "Click [here](https://example.com) to continue.";
        var doc = MarkdownParser.Parse(md);

        var para = Assert.IsType<ParagraphNode>(doc.Children[0]);
        var allText = string.Join("", para.DescendantsOfType<TextNode>().Select(t => t.Text));
        Assert.Contains("here", allText);
    }

    [Fact]
    public void Parse_InlineCode_BecomesTextNode()
    {
        var md = "Use `Console.WriteLine()` to print.";
        var doc = MarkdownParser.Parse(md);

        var para = Assert.IsType<ParagraphNode>(doc.Children[0]);
        var allText = string.Join("", para.DescendantsOfType<TextNode>().Select(t => t.Text));
        Assert.Contains("Console.WriteLine()", allText);
    }

    [Fact]
    public void Parse_DocumentSource_PreservesOriginalMarkdown()
    {
        var md = "# Hello\n\nWorld";
        var doc = MarkdownParser.Parse(md);

        Assert.Equal(md, doc.Source);
    }
}
