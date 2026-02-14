namespace SteadyVoice.Core.Ast;

/// <summary>
/// Base class for all AST nodes. Every node tracks its source span
/// within the canonical Markdown string.
/// </summary>
public abstract class AstNode
{
    public Span SourceSpan { get; init; }
    public List<AstNode> Children { get; } = [];

    /// <summary>
    /// Walk all descendants depth-first.
    /// </summary>
    public IEnumerable<AstNode> DescendantsDepthFirst()
    {
        var stack = new Stack<AstNode>(Children.AsEnumerable().Reverse());
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            for (var i = node.Children.Count - 1; i >= 0; i--)
                stack.Push(node.Children[i]);
        }
    }

    /// <summary>
    /// Walk all descendants of a specific type.
    /// </summary>
    public IEnumerable<T> DescendantsOfType<T>() where T : AstNode
        => DescendantsDepthFirst().OfType<T>();
}

/// <summary>
/// Root node. Holds the canonical Markdown source and all top-level blocks.
/// </summary>
public class DocumentNode : AstNode
{
    public required string Source { get; init; }
}

/// <summary>
/// A heading (# through ######).
/// </summary>
public class HeadingNode : AstNode
{
    public required int Level { get; init; }
}

/// <summary>
/// A paragraph of inline content.
/// </summary>
public class ParagraphNode : AstNode;

/// <summary>
/// An ordered or unordered list.
/// </summary>
public class ListNode : AstNode
{
    public required bool IsOrdered { get; init; }
}

/// <summary>
/// A single item within a list.
/// </summary>
public class ListItemNode : AstNode;

/// <summary>
/// A blockquote (> prefixed lines).
/// </summary>
public class QuoteBlockNode : AstNode;

/// <summary>
/// A fenced or indented code block.
/// </summary>
public class CodeBlockNode : AstNode
{
    public string? Language { get; init; }
    public required string Code { get; init; }
}

/// <summary>
/// A thematic break (---, ***, ___).
/// </summary>
public class ThematicBreakNode : AstNode;

/// <summary>
/// Inline text content within a block node (paragraph, heading, list item).
/// Leaf node — no children.
/// </summary>
public class TextNode : AstNode
{
    public required string Text { get; init; }
}
