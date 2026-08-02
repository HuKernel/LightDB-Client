namespace DbLiteDesktop.Controls;

public sealed class SqlCompletionPopup : IDisposable
{
    private readonly ListBox _listBox;
    private Action<string>? _onSelected;
    private bool _disposed;

    public bool Visible => _listBox.Visible;

    public SqlCompletionPopup(Control anchor)
    {
        _listBox = new ListBox
        {
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
            SelectionMode = SelectionMode.One,
            Visible = false,
            Size = new Size(260, 160),
        };
        _listBox.Click += (_, _) => ConfirmSelection();

        var host = anchor.FindForm() ?? anchor.Parent ?? anchor;
        host.Controls.Add(_listBox);
        _listBox.BringToFront();
    }

    public void Show(IEnumerable<string> items, Point screenLocation, Action<string> onSelected)
    {
        var list = items
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(i => i, StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToList();

        if (list.Count == 0)
        {
            Hide();
            return;
        }

        _listBox.BeginUpdate();
        try
        {
            _listBox.Items.Clear();
            foreach (var item in list)
            {
                _listBox.Items.Add(item);
            }
            _listBox.SelectedIndex = 0;
        }
        finally
        {
            _listBox.EndUpdate();
        }

        _onSelected = onSelected;
        var preferredHeight = _listBox.PreferredHeight + 8;
        _listBox.Size = new Size(260, Math.Min(220, preferredHeight));

        var host = _listBox.Parent;
        if (host is null)
        {
            return;
        }
        var local = host.PointToClient(screenLocation);
        var maxX = host.Width - _listBox.Width - 10;
        var maxY = host.Height - _listBox.Height - 10;
        if (local.X > maxX) local.X = Math.Max(0, maxX);
        if (local.Y > maxY) local.Y = Math.Max(0, maxY);

        _listBox.Location = local;
        _listBox.Visible = true;
        _listBox.BringToFront();
    }

    public void Hide()
    {
        if (_listBox.Visible)
        {
            _listBox.Visible = false;
        }
        _onSelected = null;
    }

    public void MoveUp()
    {
        if (_listBox.SelectedIndex > 0)
        {
            _listBox.SelectedIndex--;
        }
    }

    public void MoveDown()
    {
        if (_listBox.SelectedIndex >= 0 && _listBox.SelectedIndex < _listBox.Items.Count - 1)
        {
            _listBox.SelectedIndex++;
        }
    }

    public void ConfirmSelection()
    {
        if (_listBox.SelectedItem is string item)
        {
            _onSelected?.Invoke(item);
        }
        Hide();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _listBox.Dispose();
        _disposed = true;
    }
}
