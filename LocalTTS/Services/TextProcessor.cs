using System.Text.RegularExpressions;

namespace LocalTTS.Services;

public partial class TextProcessor {
    public static string Clean(string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return string.Empty;
        }

        // Normalize line endings to LF
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");

        // Fix common encoding issues
        text = FixEncodingIssues(text);

        // Collapse multiple spaces into single space
        text = MultipleSpaces().Replace(text, " ");

        // Normalize multiple newlines into paragraph breaks (double newline)
        text = MultipleNewlines().Replace(text, "\n\n");

        // Trim whitespace from each line
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++) {
            lines[i] = lines[i].Trim();
        }
        text = string.Join("\n", lines);

        return text.Trim();
    }

    private static string FixEncodingIssues(string text) {
        // Smart quotes to regular quotes
        text = text.Replace('\u2018', '\''); // Left single quote
        text = text.Replace('\u2019', '\''); // Right single quote
        text = text.Replace('\u201C', '"');  // Left double quote
        text = text.Replace('\u201D', '"');  // Right double quote

        // Em-dash and en-dash to regular dash
        text = text.Replace('\u2014', '-');  // Em-dash
        text = text.Replace('\u2013', '-');  // En-dash

        // Ellipsis to three dots
        text = text.Replace("\u2026", "...");

        // Non-breaking space to regular space
        text = text.Replace('\u00A0', ' ');

        return text;
    }

    [GeneratedRegex(@" {2,}")]
    private static partial Regex MultipleSpaces();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultipleNewlines();
}
