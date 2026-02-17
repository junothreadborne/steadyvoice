namespace SteadyVoice.Core.Ast;

/// <summary>
/// A chunk of text extracted from the AST at block boundaries,
/// ready to be sent to the TTS engine.
/// </summary>
public record TextChunk(
    string Text,
    int StartBlockIndex,
    int EndBlockIndex,
    int StartWordIndex,
    int WordCount
);

/// <summary>
/// Splits a document into chunks at top-level block boundaries,
/// targeting a configurable word count per chunk. Never splits mid-block.
/// </summary>
public static class AstChunker
{
    private static bool IsWordToken(TokenKind kind) =>
        kind is TokenKind.Word or TokenKind.Number or TokenKind.Url or TokenKind.Abbreviation;

    public static List<TextChunk> Chunk(DocumentNode doc, List<Token> tokens, int targetWordCount = 200)
    {
        var chunks = new List<TextChunk>();
        var blocks = doc.Children;

        if (blocks.Count == 0)
            return chunks;

        // Build per-block word counts by scanning tokens once
        var blockWordCounts = new int[blocks.Count];
        var blockIndex = 0;

        foreach (var token in tokens)
        {
            if (!IsWordToken(token.Kind))
                continue;

            // Advance block index until we find the block containing this token
            while (blockIndex < blocks.Count &&
                   !blocks[blockIndex].SourceSpan.Contains(token.SourceSpan))
            {
                blockIndex++;
            }

            if (blockIndex < blocks.Count)
                blockWordCounts[blockIndex]++;
        }

        // Group blocks into chunks
        var chunkStartBlock = 0;
        var chunkWordCount = 0;
        var globalWordIndex = 0;
        var chunkStartWordIndex = 0;

        for (var i = 0; i < blocks.Count; i++)
        {
            var blockWords = blockWordCounts[i];

            // Skip blocks with no speakable words (code blocks, thematic breaks)
            if (blockWords == 0)
                continue;

            if (chunkWordCount > 0 && chunkWordCount + blockWords > targetWordCount)
            {
                // Finalize current chunk before adding this block
                chunks.Add(CreateChunk(doc, blocks, chunkStartBlock, i, chunkStartWordIndex, chunkWordCount));
                chunkStartBlock = i;
                chunkStartWordIndex = globalWordIndex;
                chunkWordCount = 0;
            }

            if (chunkWordCount == 0)
                chunkStartBlock = i;

            chunkWordCount += blockWords;
            globalWordIndex += blockWords;
        }

        // Finalize last chunk
        if (chunkWordCount > 0)
        {
            chunks.Add(CreateChunk(doc, blocks, chunkStartBlock, blocks.Count, chunkStartWordIndex, chunkWordCount));
        }

        return chunks;
    }

    private static TextChunk CreateChunk(
        DocumentNode doc, List<AstNode> blocks,
        int startBlock, int endBlock,
        int startWordIndex, int wordCount)
    {
        // Find the actual span covering all blocks in this chunk
        var spanStart = blocks[startBlock].SourceSpan.Start;
        var spanEnd = blocks[endBlock - 1].SourceSpan.End;
        var text = doc.Source[spanStart..spanEnd];

        return new TextChunk(text, startBlock, endBlock, startWordIndex, wordCount);
    }
}
