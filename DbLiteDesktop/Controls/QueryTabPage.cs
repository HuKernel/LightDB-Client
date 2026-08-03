using System.Windows.Forms;

namespace DbLiteDesktop.Controls;

public class QueryTabPage : UserControl
{
    public SqlEditorTextBox TxtSql { get; } = new();
    public QueryResultsContainer Results { get; } = new();
    public DataGridView GridResults => Results.ActiveGrid;
    public Label LblStatus { get; } = new();
    public Label LblRowCount { get; } = new();
    public Button BtnRunSql { get; } = new();
    public Button BtnClearSql { get; } = new();
    public Button BtnCopySql { get; } = new();
    public Button BtnFormatSql { get; } = new();
    public Button BtnExportResults { get; } = new();

    private readonly TableLayoutPanel _layout = new();
    private readonly FlowLayoutPanel _buttonPanel = new();
    private readonly Panel _sqlHost = new();
    private readonly SqlLineNumbersGutter _lineNumbers = new();
    private readonly Panel _statusHost = new();

    public string Title { get; set; }

    public FlowLayoutPanel ButtonPanel => _buttonPanel;

    public event EventHandler? RunSqlRequested;
    public event EventHandler? StopSqlRequested;
    public event EventHandler? ClearSqlRequested;
    public event EventHandler? CopySqlRequested;
    public event EventHandler? FormatSqlRequested;
    public event EventHandler? ExportResultsRequested;

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (_isRunning == value)
            {
                return;
            }
            _isRunning = value;
            BtnRunSql.Text = value ? "停止" : "执行";
            BtnRunSql.BackColor = value
                ? Color.FromArgb(220, 90, 80)
                : _runButtonColor;
            BtnRunSql.ForeColor = Color.White;
        }
    }

    private Color _runButtonColor = Color.FromArgb(22, 163, 74);
    public Color RunButtonColor
    {
        get => _runButtonColor;
        set
        {
            _runButtonColor = value;
            if (!_isRunning)
            {
                BtnRunSql.BackColor = value;
            }
        }
    }

    public bool IsStopping;

    public QueryTabPage(string title)
    {
        Title = title;
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        BuildLayout();
        HookEvents();
    }

    public string GetEffectiveSql()
    {
        return TxtSql.SelectionLength > 0
            ? TxtSql.SelectedText.Trim()
            : TxtSql.Text.Trim();
    }

    private void BuildLayout()
    {
        _layout.ColumnCount = 1;
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _layout.Dock = DockStyle.Fill;
        _layout.RowCount = 4;
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 132F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _layout.Controls.Add(_sqlHost, 0, 0);
        _layout.Controls.Add(_buttonPanel, 0, 1);
        _layout.Controls.Add(Results, 0, 2);
        _layout.Controls.Add(_statusHost, 0, 3);
        Controls.Add(_layout);

        _sqlHost.Dock = DockStyle.Fill;
        _sqlHost.BackColor = Color.White;
        _sqlHost.Controls.Add(_lineNumbers);
        _sqlHost.Controls.Add(TxtSql);
        TxtSql.SendToBack();
        _lineNumbers.BringToFront();
        _lineNumbers.Attach(TxtSql);

        TxtSql.Dock = DockStyle.Fill;
        TxtSql.Multiline = true;
        TxtSql.ScrollBars = RichTextBoxScrollBars.Both;
        TxtSql.WordWrap = false;

        _buttonPanel.Controls.Add(BtnRunSql);
        _buttonPanel.Controls.Add(BtnFormatSql);
        _buttonPanel.Controls.Add(BtnClearSql);
        _buttonPanel.Controls.Add(BtnCopySql);
        _buttonPanel.Controls.Add(BtnExportResults);
        _buttonPanel.Dock = DockStyle.Fill;
        _buttonPanel.FlowDirection = FlowDirection.LeftToRight;
        _buttonPanel.Padding = new Padding(12, 8, 12, 0);

        BtnRunSql.AutoSize = true;
        BtnRunSql.Text = "执行";
        BtnFormatSql.AutoSize = true;
        BtnFormatSql.Text = "格式化";
        BtnClearSql.AutoSize = true;
        BtnClearSql.Text = "清空";
        BtnCopySql.AutoSize = true;
        BtnCopySql.Text = "复制 SQL";
        BtnExportResults.AutoSize = true;
        BtnExportResults.Text = "导出结果";

        GridResults.AllowUserToAddRows = false;
        GridResults.AllowUserToDeleteRows = false;
        GridResults.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        GridResults.ReadOnly = true;
        GridResults.RowHeadersVisible = false;
        GridResults.AllowUserToOrderColumns = true;

        LblStatus.Dock = DockStyle.Fill;
        LblStatus.Text = "未连接";
        LblStatus.TextAlign = ContentAlignment.MiddleLeft;

        _statusHost.Dock = DockStyle.Fill;
        _statusHost.BackColor = Color.FromArgb(241, 245, 249);
        _statusHost.Controls.Add(LblStatus);
        _statusHost.Controls.Add(LblRowCount);

        LblRowCount.Dock = DockStyle.Right;
        LblRowCount.TextAlign = ContentAlignment.MiddleRight;
        LblRowCount.Padding = new Padding(0, 0, 16, 0);
        LblRowCount.Text = string.Empty;
        LblRowCount.AutoSize = false;
        LblRowCount.Width = 160;
    }

    private void HookEvents()
    {
        BtnRunSql.Click += (_, _) =>
        {
            if (_isRunning)
            {
                StopSqlRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                RunSqlRequested?.Invoke(this, EventArgs.Empty);
            }
        };
        BtnFormatSql.Click += (_, _) => FormatSqlRequested?.Invoke(this, EventArgs.Empty);
        BtnClearSql.Click += (_, _) => ClearSqlRequested?.Invoke(this, EventArgs.Empty);
        BtnCopySql.Click += (_, _) => CopySqlRequested?.Invoke(this, EventArgs.Empty);
        BtnExportResults.Click += (_, _) => ExportResultsRequested?.Invoke(this, EventArgs.Empty);
    }
}

