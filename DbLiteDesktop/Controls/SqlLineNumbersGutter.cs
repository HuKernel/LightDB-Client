using System.Runtime.InteropServices;

namespace DbLiteDesktop.Controls;

public sealed class SqlLineNumbersGutter : Control
{
    private RichTextBox? _editor;

    public SqlLineNumbersGutter()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);
        BackColor = Color.FromArgb(248, 250, 252);
        ForeColor = Color.FromArgb(148, 163, 184);
        Font = new Font("Consolas", 9F, GraphicsUnit.Point);
        Width = 48;
        Dock = DockStyle.Left;
    }

    public void Attach(RichTextBox editor)
    {
        _editor = editor;
        editor.TextChanged += (_, _) => Invalidate();
        editor.VScroll += (_, _) => Invalidate();
        editor.HScroll += (_, _) => Invalidate();
        editor.Resize += (_, _) => Invalidate();
        editor.FontChanged += (_, _) => Invalidate();
        editor.ClientSizeChanged += (_, _) => Invalidate();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_editor is null)
        {
            return;
        }

        e.Graphics.Clear(BackColor);

        var lineHeight = _editor.Font.Height;
        if (lineHeight <= 0)
        {
            return;
        }

        var totalLines = _editor.GetLineFromCharIndex(_editor.TextLength) + 1;
        var firstVisible = GetFirstVisibleLine(_editor);
        // 上下多绘制 2 行以平滑滚动时不闪
        var startLine = Math.Max(0, firstVisible - 2);
        var endLine = Math.Min(totalLines - 1, firstVisible + Height / lineHeight + 2);

        using var brush = new SolidBrush(ForeColor);
        var format = new StringFormat
        {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces
        };

        for (var i = startLine; i <= endLine; i++)
        {
            var charIdx = _editor.GetFirstCharIndexFromLine(i);
            if (charIdx < 0)
            {
                continue;
            }
            var pos = _editor.GetPositionFromCharIndex(charIdx);
            var y = pos.Y;
            var rect = new Rectangle(2, y, Width - 10, lineHeight);
            e.Graphics.DrawString((i + 1).ToString(), Font, brush, rect, format);
        }

        using var pen = new Pen(Color.FromArgb(226, 232, 240), 1);
        e.Graphics.DrawLine(pen, Width - 1, 0, Width - 1, Height);
        format.Dispose();
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    private const int EM_GETFIRSTVISIBLELINE = 0xCE;

    private static int GetFirstVisibleLine(RichTextBox editor)
    {
        return SendMessage(editor.Handle, EM_GETFIRSTVISIBLELINE, 0, 0);
    }
}
