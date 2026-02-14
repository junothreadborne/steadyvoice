using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace SteadyVoice.Core.Ast;

/// <summary>
/// Parses a canonical Markdown string into a SteadyVoice AST.
/// Uses Markdig internally and maps to our own node types.
/// </summary>
public static class MarkdownParser
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePreciseSourceLocation()
        .Build();

    /// <summary>
    /// Parse a canonical Markdown string into a <see cref="DocumentNode"/>.
    /// </summary>
    public static DocumentNode Parse(string markdown)
    {
        var markdigDoc = Markdown.Parse(markdown, Pipeline);
        var doc = new DocumentNode
        {
            Source = markdown,
            SourceSpan = ToSpan(markdigDoc.Span, markdown),
        };

        foreach (var block in markdigDoc)
            ConvertBlock(block, doc, markdown);

        return doc;
    }

    private static void ConvertBlock(Block block, AstNode parent, string source)
    {
        switch (block)
        {
            case HeadingBlock heading:
                var headingNode = new HeadingNode
                {
                    Level = heading.Level,
                    SourceSpan = ToSpan(heading.Span, source),
                };
                ConvertInlines(heading.Inline, headingNode, source);
                parent.Children.Add(headingNode);
                break;

            case ParagraphBlock paragraph:
                var paragraphNode = new ParagraphNode
                {
                    SourceSpan = ToSpan(paragraph.Span, source),
                };
                ConvertInlines(paragraph.Inline, paragraphNode, source);
                parent.Children.Add(paragraphNode);
                break;

            case ListBlock list:
                var listNode = new ListNode
                {
                    IsOrdered = list.IsOrdered,
                    SourceSpan = ToSpan(list.Span, source),
                };
                foreach (var item in list)
                    ConvertBlock(item, listNode, source);
                parent.Children.Add(listNode);
                break;

            case ListItemBlock listItem:
                var listItemNode = new ListItemNode
                {
                    SourceSpan = ToSpan(listItem.Span, source),
                };
                foreach (var child in listItem)
                    ConvertBlock(child, listItemNode, source);
                parent.Children.Add(listItemNode);
                break;

            case QuoteBlock quote:
                var quoteNode = new QuoteBlockNode
                {
                    SourceSpan = ToSpan(quote.Span, source),
                };
                foreach (var child in quote)
                    ConvertBlock(child, quoteNode, source);
                parent.Children.Add(quoteNode);
                break;

            case FencedCodeBlock fenced:
                var fencedNode = new CodeBlockNode
                {
                    Language = string.IsNullOrWhiteSpace(fenced.Info) ? null : fenced.Info,
                    Code = ExtractCodeBlockText(fenced),
                    SourceSpan = ToSpan(fenced.Span, source),
                };
                parent.Children.Add(fencedNode);
                break;

            case CodeBlock code:
                var codeNode = new CodeBlockNode
                {
                    Code = ExtractCodeBlockText(code),
                    SourceSpan = ToSpan(code.Span, source),
                };
                parent.Children.Add(codeNode);
                break;

            case ThematicBreakBlock:
                parent.Children.Add(new ThematicBreakNode
                {
                    SourceSpan = ToSpan(block.Span, source),
                });
                break;

            // Unsupported block types (tables, HTML, link references, etc.)
            // are silently skipped per roadmap §2.
        }
    }

    private static void ConvertInlines(ContainerInline? container, AstNode parent, string source)
    {
        if (container is null) return;

        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    parent.Children.Add(new TextNode
                    {
                        Text = literal.Content.ToString(),
                        SourceSpan = ToInlineSpan(literal),
                    });
                    break;

                case CodeInline code:
                    parent.Children.Add(new TextNode
                    {
                        Text = code.Content,
                        SourceSpan = ToInlineSpan(code),
                    });
                    break;

                case LinkInline link:
                    // Flatten link: extract text content, discard URL.
                    if (link.FirstChild is not null)
                        ConvertInlines(link, parent, source);
                    break;

                case EmphasisInline emphasis:
                    // Flatten emphasis: extract text content, discard styling.
                    ConvertInlines(emphasis, parent, source);
                    break;

                case LineBreakInline:
                    // Treat as whitespace.
                    break;

                // Other inline types are silently skipped.
            }
        }
    }

    private static string ExtractCodeBlockText(CodeBlock code)
    {
        var lines = code.Lines;
        if (lines.Count == 0) return string.Empty;

        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(lines.Lines[i].Slice);
        }
        return sb.ToString();
    }

    private static Span ToSpan(Markdig.Syntax.SourceSpan markdigSpan, string source)
    {
        var start = Math.Max(0, markdigSpan.Start);
        // Markdig's End is inclusive; ours is exclusive.
        var end = Math.Min(source.Length, markdigSpan.End + 1);
        return new Span(start, end);
    }

    private static Span ToInlineSpan(Inline inline)
    {
        var start = Math.Max(0, inline.Span.Start);
        // Markdig's End is inclusive; ours is exclusive.
        var end = inline.Span.End + 1;
        return new Span(start, end);
    }
}
