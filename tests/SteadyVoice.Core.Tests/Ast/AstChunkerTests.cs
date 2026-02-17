using SteadyVoice.Core.Ast;

namespace SteadyVoice.Core.Tests.Ast;

public class AstChunkerTests
{
    private static (DocumentNode Doc, List<Token> Tokens) Parse(string md)
    {
        var doc = MarkdownParser.Parse(md);
        var tokens = Tokenizer.Tokenize(doc);
        return (doc, tokens);
    }

    [Fact]
    public void Chunk_EmptyDocument_ReturnsEmpty()
    {
        var (doc, tokens) = Parse("");
        var chunks = AstChunker.Chunk(doc, tokens);
        Assert.Empty(chunks);
    }

    [Fact]
    public void Chunk_SingleParagraphUnderTarget_ReturnsSingleChunk()
    {
        var (doc, tokens) = Parse("Hello world, this is a test.");
        var chunks = AstChunker.Chunk(doc, tokens, targetWordCount: 200);

        Assert.Single(chunks);
        Assert.Equal(0, chunks[0].StartBlockIndex);
        Assert.Equal(0, chunks[0].StartWordIndex);
        Assert.Equal(6, chunks[0].WordCount); // Hello world this is a test
    }

    [Fact]
    public void Chunk_SingleParagraphOverTarget_KeptAsOneChunk()
    {
        // A single paragraph should never be split, even if it exceeds target
        var words = string.Join(" ", Enumerable.Range(1, 50).Select(i => $"word{i}"));
        var (doc, tokens) = Parse(words);
        var chunks = AstChunker.Chunk(doc, tokens, targetWordCount: 10);

        Assert.Single(chunks);
        Assert.Equal(50, chunks[0].WordCount);
    }

    [Fact]
    public void Chunk_MultipleParagraphs_SplitsAtBoundary()
    {
        var para1 = string.Join(" ", Enumerable.Range(1, 10).Select(i => $"alpha{i}"));
        var para2 = string.Join(" ", Enumerable.Range(1, 10).Select(i => $"beta{i}"));
        var para3 = string.Join(" ", Enumerable.Range(1, 10).Select(i => $"gamma{i}"));
        var md = $"{para1}\n\n{para2}\n\n{para3}";

        var (doc, tokens) = Parse(md);
        var chunks = AstChunker.Chunk(doc, tokens, targetWordCount: 15);

        Assert.Equal(3, chunks.Count);
        // Each paragraph is 10 words; adding any two exceeds 15
        Assert.Equal(10, chunks[0].WordCount);
        Assert.Equal(0, chunks[0].StartWordIndex);
        Assert.Equal(10, chunks[1].WordCount);
        Assert.Equal(10, chunks[1].StartWordIndex);
        Assert.Equal(10, chunks[2].WordCount);
        Assert.Equal(20, chunks[2].StartWordIndex);
    }

    [Fact]
    public void Chunk_HeadingsActAsSeparateBlocks()
    {
        var md = "# Title\n\nParagraph one.\n\n## Subtitle\n\nParagraph two.";
        var (doc, tokens) = Parse(md);
        var chunks = AstChunker.Chunk(doc, tokens, targetWordCount: 3);

        // Title (1 word) → chunk 1
        // "Paragraph one" (2 words) → fits with Title? 1+2=3, exactly at target → stays in chunk 1
        // Subtitle (1 word) → chunk 2 start
        // "Paragraph two" (2 words) → fits with Subtitle → chunk 2
        Assert.Equal(2, chunks.Count);
    }

    [Fact]
    public void Chunk_CodeBlocksSkipped()
    {
        var md = "Some text.\n\n```csharp\nvar x = 1;\n```\n\nMore text.";
        var (doc, tokens) = Parse(md);
        var chunks = AstChunker.Chunk(doc, tokens, targetWordCount: 200);

        // Code blocks have no word tokens, so they're skipped
        // "Some text" and "More text" should be in the chunks
        Assert.Single(chunks);
        Assert.Equal(4, chunks[0].WordCount); // Some text More text
    }

    [Fact]
    public void Chunk_TextExtractedCorrectly()
    {
        var md = "First paragraph.\n\nSecond paragraph.\n\nThird paragraph.";
        var (doc, tokens) = Parse(md);
        var chunks = AstChunker.Chunk(doc, tokens, targetWordCount: 2);

        Assert.Equal(3, chunks.Count);
        Assert.Contains("First", chunks[0].Text);
        Assert.Contains("Second", chunks[1].Text);
        Assert.Contains("Third", chunks[2].Text);
    }

    [Fact]
    public void Chunk_StartWordIndexesAreCorrect()
    {
        var md = "One two three.\n\nFour five six.\n\nSeven eight nine.";
        var (doc, tokens) = Parse(md);
        var chunks = AstChunker.Chunk(doc, tokens, targetWordCount: 3);

        // Each paragraph has 3 words, target is 3, so each gets its own chunk
        Assert.Equal(3, chunks.Count);
        Assert.Equal(0, chunks[0].StartWordIndex);
        Assert.Equal(3, chunks[1].StartWordIndex);
        Assert.Equal(6, chunks[2].StartWordIndex);
    }

    [Fact]
    public void Chunk_ListTreatedAsSingleBlock()
    {
        var md = "Intro.\n\n- Item one\n- Item two\n- Item three\n\nConclusion.";
        var (doc, tokens) = Parse(md);
        var chunks = AstChunker.Chunk(doc, tokens, targetWordCount: 3);

        // Intro (1 word) → chunk by itself when list won't fit
        // List items (6 words) → single block, kept together
        // Conclusion (1 word)
        Assert.True(chunks.Count >= 2);

        // The list's words should all be in the same chunk
        var listChunk = chunks.First(c => c.WordCount >= 6);
        Assert.Equal(6, listChunk.WordCount);
    }

    [Fact]
    public void Chunk_AdvancesPastMultipleNonTextBlocks()
    {
        // Text, then code block + thematic break (two consecutive non-text blocks), then text.
        // Forces blockIndex to advance past multiple blocks in the while loop.
        var md = "Before.\n\n```\ncode\n```\n\n---\n\nAfter.";
        var (doc, tokens) = Parse(md);
        var chunks = AstChunker.Chunk(doc, tokens, targetWordCount: 200);

        Assert.Single(chunks);
        Assert.Equal(2, chunks[0].WordCount); // Before + After
    }

    [Fact]
    public void Chunk_DocumentEndingWithNonTextBlocks()
    {
        // Text paragraph followed by a code block at the end.
        var md = "Some words here.\n\n```\ncode block at end\n```";
        var (doc, tokens) = Parse(md);
        var chunks = AstChunker.Chunk(doc, tokens, targetWordCount: 200);

        Assert.Single(chunks);
        Assert.Equal(3, chunks[0].WordCount); // Some words here
    }

    [Fact]
    public void Chunk_TokenOutsideAllBlocks_IsIgnored()
    {
        // Synthetic test: construct a token whose span falls outside any block.
        // This exercises the blockIndex < blocks.Count guard (evaluating to false).
        var source = "Hello world";
        var doc = new DocumentNode { Source = source, SourceSpan = new Span(0, source.Length) };
        var para = new ParagraphNode { SourceSpan = new Span(0, 5) }; // covers "Hello" only
        para.Children.Add(new TextNode { Text = "Hello", SourceSpan = new Span(0, 5) });
        doc.Children.Add(para);

        // Two word tokens: "Hello" inside the block, "world" outside all blocks
        var tokens = new List<Token>
        {
            new() { Text = "Hello", Kind = TokenKind.Word, SourceSpan = new Span(0, 5) },
            new() { Text = "world", Kind = TokenKind.Word, SourceSpan = new Span(6, 11) },
        };

        var chunks = AstChunker.Chunk(doc, tokens, targetWordCount: 200);

        // "Hello" is counted in the paragraph; "world" is orphaned and ignored
        Assert.Single(chunks);
        Assert.Equal(1, chunks[0].WordCount);
    }

    [Fact]
    public void Chunk_DefaultTargetIs200()
    {
        // Verify default parameter works
        var words = string.Join(" ", Enumerable.Range(1, 100).Select(i => $"word{i}"));
        var (doc, tokens) = Parse(words);
        var chunks = AstChunker.Chunk(doc, tokens);

        // 100 words in a single paragraph, default target 200 → one chunk
        Assert.Single(chunks);
    }
}
