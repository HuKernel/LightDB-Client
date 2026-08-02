using System.Data;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

namespace DbLiteDesktop.Controls;

public class SqlEditorTextBox : RichTextBox
{
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "AND", "OR", "NOT", "NULL", "IS", "IN", "LIKE",
        "BETWEEN", "ORDER", "BY", "GROUP", "HAVING", "LIMIT", "OFFSET", "AS",
        "JOIN", "LEFT", "RIGHT", "INNER", "OUTER", "FULL", "CROSS", "ON", "USING",
        "UNION", "ALL", "DISTINCT", "CASE", "WHEN", "THEN", "ELSE", "END",
        "COUNT", "SUM", "AVG", "MIN", "MAX", "CAST", "CONVERT",
        "SHOW", "TABLES", "DESC", "DESCRIBE", "EXPLAIN", "PRAGMA",
        "WITH", "INSERT", "UPDATE", "DELETE", "CREATE", "ALTER", "DROP",
        "TABLE", "INDEX", "VIEW", "INTO", "VALUES", "SET",
        "ASC", "DESC"
    };

    private static readonly Color KeywordColor = Color.FromArgb(59, 130, 246);
    private static readonly Color StringColor = Color.FromArgb(22, 163, 74);
    private static readonly Color CommentColor = Color.FromArgb(100, 116, 139);
    private static readonly Color NumberColor = Color.FromArgb(217, 119, 6);

    private bool _suppressHighlight;
    private SqlCompletionPopup? _completionPopup;

    private static readonly Regex TokenRegex = new(
        @"('[^']*')|(--[^\r\n]*)|(/\*[\s\S]*?\*/)|(\b\d+\.?\d*\b)|(\w+)",
        RegexOptions.Compiled
    );

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, [MarshalAs(UnmanagedType.LPWStr)] string lParam);

    private const int EM_SETCUEBANNER = 0x1501;
    private const int EM_GETCUEBANNER = 0x1502;

    public Func<List<string>>? CompletionProvider { get; set; }

    public string PlaceholderText
    {
        get => _placeholderText;
        set
        {
            _placeholderText = value;
            if (IsHandleCreated)
            {
                SendMessage(Handle, EM_SETCUEBANNER, 1, value ?? string.Empty);
            }
        }
    }

    private string _placeholderText = string.Empty;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!string.IsNullOrEmpty(_placeholderText))
        {
            SendMessage(Handle, EM_SETCUEBANNER, 1, _placeholderText);
        }
        EnsurePopup();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (IsHandleCreated)
        {
            EnsurePopup();
        }
    }

    private void EnsurePopup()
    {
        if (_completionPopup is not null || Parent is null || Disposing || IsDisposed)
        {
            return;
        }
        _completionPopup = new SqlCompletionPopup(Parent);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);

        if (_suppressHighlight)
        {
            return;
        }

        HighlightSyntax();
        UpdateCompletion();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_completionPopup is { Visible: true })
        {
            switch (e.KeyCode)
            {
                case Keys.Down:
                    _completionPopup.MoveDown();
                    e.Handled = true;
                    return;
                case Keys.Up:
                    _completionPopup.MoveUp();
                    e.Handled = true;
                    return;
                case Keys.Tab:
                case Keys.Enter:
                    _completionPopup.ConfirmSelection();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                case Keys.Escape:
                    _completionPopup.Hide();
                    e.Handled = true;
                    return;
            }
        }
        base.OnKeyDown(e);
    }

    protected override void OnLeave(EventArgs e)
    {
        _completionPopup?.Hide();
        base.OnLeave(e);
    }

    private void HighlightSyntax()
    {
        if (string.IsNullOrEmpty(Text))
        {
            return;
        }

        _suppressHighlight = true;
        try
        {
            var selStart = SelectionStart;
            var selLength = SelectionLength;

            SelectAll();
            SelectionColor = ForeColor;

            foreach (Match match in TokenRegex.Matches(Text))
            {
                var color = ForeColor;

                if (match.Groups[1].Success)
                {
                    color = StringColor;
                }
                else if (match.Groups[2].Success || match.Groups[3].Success)
                {
                    color = CommentColor;
                }
                else if (match.Groups[4].Success)
                {
                    color = NumberColor;
                }
                else if (match.Groups[5].Success && Keywords.Contains(match.Groups[5].Value))
                {
                    color = KeywordColor;
                }
                else
                {
                    continue;
                }

                Select(match.Index, match.Length);
                SelectionColor = color;
            }

            Select(selStart, selLength);
            SelectionColor = ForeColor;
        }
        finally
        {
            _suppressHighlight = false;
        }
    }

    private void UpdateCompletion()
    {
        EnsurePopup();
        if (_completionPopup is null || CompletionProvider is null)
        {
            return;
        }

        var prefix = GetCurrentWordPrefix();
        if (prefix.Length < 2)
        {
            _completionPopup.Hide();
            return;
        }

        var candidates = GetCompletionItems()
            .Where(c => c.Length > prefix.Length
                && c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count == 0)
        {
            _completionPopup.Hide();
            return;
        }

        var caretPos = GetPositionFromCharIndex(SelectionStart);
        var lineHeight = TextRenderer.MeasureText("Ag", Font).Height;
        caretPos.Offset(0, lineHeight + 2);

        var screenPos = PointToScreen(caretPos);
        _completionPopup.Show(candidates, screenPos, ReplaceCurrentWord);
    }

    private List<string> GetCompletionItems()
    {
        var items = new List<string>(Keywords);
        if (CompletionProvider is not null)
        {
            items.AddRange(CompletionProvider());
        }
        return items;
    }

    private string GetCurrentWordPrefix()
    {
        var text = Text;
        var caret = SelectionStart;
        if (caret <= 0)
        {
            return string.Empty;
        }

        var start = caret - 1;
        while (start >= 0 && (char.IsLetterOrDigit(text[start]) || text[start] == '_'))
        {
            start--;
        }
        return text.Substring(start + 1, caret - start - 1);
    }

    private void ReplaceCurrentWord(string replacement)
    {
        var text = Text;
        var caret = SelectionStart;
        var start = caret - 1;
        while (start >= 0 && (char.IsLetterOrDigit(text[start]) || text[start] == '_'))
        {
            start--;
        }
        var realStart = start + 1;

        _suppressHighlight = true;
        try
        {
            Select(realStart, caret - realStart);
            SelectedText = replacement;
        }
        finally
        {
            _suppressHighlight = false;
        }

        SelectionStart = realStart + replacement.Length;
        SelectionLength = 0;
        HighlightSyntax();
        _completionPopup?.Hide();
    }

    public void ApplyTheme()
    {
        BorderStyle = BorderStyle.FixedSingle;
        BackColor = Color.White;
        ForeColor = Color.FromArgb(71, 85, 105);
        Font = new Font("Consolas", 10F, FontStyle.Regular, GraphicsUnit.Point);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _completionPopup?.Dispose();
            _completionPopup = null;
        }
        base.Dispose(disposing);
    }
}
