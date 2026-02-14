using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SteadyVoice.Services;
using SteadyVoice.Core.Ast;
using System.Text;

namespace SteadyVoice;

public partial class ReaderWindow : Window {
    private const int MinFontSize = 10;
    private const int MaxFontSize = 36;
    private const int HighlightIntervalMs = 50;
    private const int ParagraphSpacing = 12;

    private readonly AppSettings _settings;
    private readonly Action<string, int>? _onPlayRequested;
    private readonly Action? _onClosed;
    private string _currentText = string.Empty;
    private int _fontSize;
    private bool _hasBeenActivated;
    private bool _isClosing;

    // AST-backed word data
    private readonly List<Run> _wordRuns = [];
    private readonly List<Token> _wordTokens = [];
    private List<WordTimestamp>? _timestamps;
    private Func<double>? _getPlaybackPosition;
    private DispatcherTimer? _highlightTimer;
    private int _currentHighlightIndex = -1;
    private int _selectedWordIndex = -1;
    private SolidColorBrush? _highlightBrush;
    private SolidColorBrush? _selectionBrush;
    private List<int>? _timestampRunMap;

    public ReaderWindow(string text, DocumentNode doc, List<Token> tokens, AppSettings settings, Action<string, int>? onPlayRequested = null, Action? onClosed = null) {
        InitializeComponent();
        _settings = settings;
        _onPlayRequested = onPlayRequested;
        _onClosed = onClosed;
        _fontSize = settings.ReaderFontSize;

        ApplyTheme();
        UpdateContent(text, doc, tokens);

        Activated += OnActivated;
        Deactivated += OnDeactivated;
        Closed += OnWindowClosed;
        KeyDown += OnKeyDown;
    }

    private void OnActivated(object? sender, EventArgs e) => _hasBeenActivated = true;

    private void OnDeactivated(object? sender, EventArgs e) {
        if (_settings.ReaderCloseOnFocusLoss && _hasBeenActivated && !_isClosing) {
            _isClosing = true;
            Close();
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e) {
        StopHighlighting();
        _onClosed?.Invoke();
    }

    private void OnToolbarMouseDown(object sender, MouseButtonEventArgs e) {
        if (e.ChangedButton == MouseButton.Left) {
            DragMove();
        }
    }

    public void UpdateContent(string text, DocumentNode doc, List<Token> allTokens) {
        StopHighlighting();
        _currentText = text;
        _wordRuns.Clear();
        _wordTokens.Clear();
        ClearSelectionHighlight();
        _selectedWordIndex = -1;
        Document.Blocks.Clear();

        // Group tokens by their containing block node for paragraph layout
        foreach (var blockNode in doc.Children) {
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, ParagraphSpacing) };
            var blockTokens = allTokens.Where(t => blockNode.SourceSpan.Contains(t.SourceSpan)).ToList();
            var needsSpace = false;

            foreach (var token in blockTokens) {
                if (token.Kind == TokenKind.Whitespace) {
                    needsSpace = true;
                    continue;
                }

                if (token.Kind == TokenKind.Word || token.Kind == TokenKind.Number
                    || token.Kind == TokenKind.Url || token.Kind == TokenKind.Abbreviation) {
                    if (needsSpace) {
                        paragraph.Inlines.Add(new Run(" "));
                        needsSpace = false;
                    }

                    var run = new Run(token.Text);
                    run.Tag = _wordRuns.Count;
                    run.Cursor = Cursors.Hand;
                    run.MouseDown += OnWordClicked;
                    _wordRuns.Add(run);
                    _wordTokens.Add(token);
                    paragraph.Inlines.Add(run);
                } else {
                    // Punctuation: append inline without making it a clickable word run
                    paragraph.Inlines.Add(new Run(token.Text));
                    needsSpace = false;
                }
            }

            if (paragraph.Inlines.Count > 0) {
                Document.Blocks.Add(paragraph);
            }
        }

        ApplyFontSettings();
    }

    public void StartHighlighting(List<WordTimestamp> timestamps, Func<double> getPlaybackPosition) {
        StartHighlighting(timestamps, getPlaybackPosition, 0);
    }

    public void StartHighlighting(List<WordTimestamp> timestamps, Func<double> getPlaybackPosition, int startWordIndex) {
        StopHighlighting();
        _timestamps = timestamps;
        _getPlaybackPosition = getPlaybackPosition;
        _currentHighlightIndex = -1;
        ClearSelectionHighlight();
        _selectedWordIndex = -1;
        _timestampRunMap = BuildTimestampRunMap(timestamps, _wordTokens, startWordIndex);

        _highlightTimer = new DispatcherTimer {
            Interval = TimeSpan.FromMilliseconds(HighlightIntervalMs)
        };
        _highlightTimer.Tick += OnHighlightTick;
        _highlightTimer.Start();
    }

    public void StopHighlighting() {
        _highlightTimer?.Stop();
        _highlightTimer = null;
        _timestamps = null;
        _getPlaybackPosition = null;
        _timestampRunMap = null;
        ClearHighlight();
    }

    private void OnHighlightTick(object? sender, EventArgs e) {
        if (_timestamps == null || _getPlaybackPosition == null || _wordRuns.Count == 0) {
            return;
        }

        var position = _getPlaybackPosition();

        // Find current word based on position
        var newIndex = -1;
        for (var i = 0; i < _timestamps.Count; i++) {
            if (position >= _timestamps[i].StartTime && position < _timestamps[i].EndTime) {
                newIndex = i;
                break;
            }
        }

        if (newIndex == -1) {
            ClearHighlight();
            return;
        }

        var mappedIndex = newIndex;
        if (_timestampRunMap != null && newIndex >= 0 && newIndex < _timestampRunMap.Count) {
            mappedIndex = _timestampRunMap[newIndex];
        }

        if (mappedIndex == -1) {
            // Punctuation or unaligned token: keep current highlight
            return;
        }

        if (mappedIndex != _currentHighlightIndex) {
            // Clear previous highlight
            if (_currentHighlightIndex >= 0 && _currentHighlightIndex < _wordRuns.Count) {
                _wordRuns[_currentHighlightIndex].Background = Brushes.Transparent;
            }

            // Apply new highlight
            if (mappedIndex >= 0 && mappedIndex < _wordRuns.Count) {
                _wordRuns[mappedIndex].Background = _highlightBrush;
            }

            _currentHighlightIndex = mappedIndex;
        }
    }

    private void ClearHighlight() {
        if (_currentHighlightIndex >= 0 && _currentHighlightIndex < _wordRuns.Count) {
            _wordRuns[_currentHighlightIndex].Background = Brushes.Transparent;
        }
        _currentHighlightIndex = -1;
    }

    private void ClearSelectionHighlight() {
        if (_selectedWordIndex >= 0 && _selectedWordIndex < _wordRuns.Count && _selectedWordIndex != _currentHighlightIndex) {
            _wordRuns[_selectedWordIndex].Background = Brushes.Transparent;
        }
    }

    private void SetSelectedWord(int index) {
        if (index < 0 || index >= _wordRuns.Count) {
            return;
        }

        ClearSelectionHighlight();
        _selectedWordIndex = index;

        if (_highlightTimer == null && _selectionBrush != null) {
            _wordRuns[index].Background = _selectionBrush;
        }
    }

    private void ApplyTheme() {
        if (_settings.ReaderDarkMode) {
            Resources["BackgroundBrush"] = new SolidColorBrush(Color.FromRgb(30, 30, 30));
            Resources["TextBrush"] = new SolidColorBrush(Color.FromRgb(230, 230, 230));
            Resources["ToolbarBrush"] = new SolidColorBrush(Color.FromRgb(45, 45, 45));
            Resources["ButtonHoverBrush"] = new SolidColorBrush(Color.FromRgb(60, 60, 60));
            _highlightBrush = new SolidColorBrush(Color.FromRgb(70, 100, 150));
            _selectionBrush = new SolidColorBrush(Color.FromRgb(60, 60, 90));
        } else {
            Resources["BackgroundBrush"] = new SolidColorBrush(Color.FromRgb(250, 250, 250));
            Resources["TextBrush"] = new SolidColorBrush(Color.FromRgb(26, 26, 26));
            Resources["ToolbarBrush"] = new SolidColorBrush(Color.FromRgb(240, 240, 240));
            Resources["ButtonHoverBrush"] = new SolidColorBrush(Color.FromRgb(224, 224, 224));
            _highlightBrush = new SolidColorBrush(Color.FromRgb(255, 235, 156)); // Yellow highlight
            _selectionBrush = new SolidColorBrush(Color.FromRgb(200, 220, 255));
        }
    }

    private void ApplyFontSettings() {
        Document.FontFamily = new FontFamily(_settings.ReaderFontFamily);
        Document.FontSize = _fontSize;
        FontSizeDisplay.Text = _fontSize.ToString(CultureInfo.InvariantCulture);
    }

    private void OnReadAloud(object sender, RoutedEventArgs e) => _onPlayRequested?.Invoke(_currentText, 0);

    private void OnFontDecrease(object sender, RoutedEventArgs e) {
        if (_fontSize > MinFontSize) {
            _fontSize -= 2;
            ApplyFontSettings();
        }
    }

    private void OnFontIncrease(object sender, RoutedEventArgs e) {
        if (_fontSize < MaxFontSize) {
            _fontSize += 2;
            ApplyFontSettings();
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) {
        if (!_isClosing) {
            _isClosing = true;
            Close();
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e) {
        if (e.Key == Key.Escape && !_isClosing) {
            _isClosing = true;
            Close();
        }
    }

    private void OnWordClicked(object sender, MouseButtonEventArgs e) {
        if (e.ChangedButton != MouseButton.Left) {
            return;
        }

        if (sender is not Run run || run.Tag is not int wordIndex) {
            return;
        }

        SetSelectedWord(wordIndex);
        var textToPlay = GetTextFromWordIndex(wordIndex);
        _onPlayRequested?.Invoke(textToPlay, wordIndex);
        e.Handled = true;
    }

    private string GetTextFromWordIndex(int wordIndex) {
        if (wordIndex < 0 || wordIndex >= _wordTokens.Count) {
            return _currentText;
        }

        var startIndex = _wordTokens[wordIndex].SourceSpan.Start;
        if (startIndex < 0 || startIndex >= _currentText.Length) {
            return _currentText;
        }

        return _currentText[startIndex..];
    }

    // Greedy, forward-only token alignment between timestamps and word tokens.
    private static List<int> BuildTimestampRunMap(List<WordTimestamp> timestamps, List<Token> tokens, int startWordIndex) {
        var map = new List<int>(timestamps.Count);
        var tokenIndex = Math.Clamp(startWordIndex, 0, tokens.Count);

        for (var i = 0; i < timestamps.Count; i++) {
            var tsNorm = NormalizeToken(timestamps[i].Word);
            if (string.IsNullOrEmpty(tsNorm)) {
                map.Add(-1);
                continue;
            }

            var matched = -1;
            while (tokenIndex < tokens.Count) {
                var tokNorm = NormalizeToken(tokens[tokenIndex].Text);
                if (!string.IsNullOrEmpty(tokNorm) && tokNorm == tsNorm) {
                    matched = tokenIndex;
                    tokenIndex++;
                    break;
                }
                tokenIndex++;
            }

            map.Add(matched);
        }

        return map;
    }

    private static string NormalizeToken(string token) {
        if (string.IsNullOrWhiteSpace(token)) {
            return string.Empty;
        }

        var sb = new StringBuilder(token.Length);
        foreach (var ch in token) {
            var normalized = ch == '\u2019' ? '\'' : ch;
            if (char.IsLetterOrDigit(normalized) || normalized == '\'' || normalized == '-') {
                sb.Append(char.ToLowerInvariant(normalized));
            }
        }

        return sb.ToString();
    }
}
