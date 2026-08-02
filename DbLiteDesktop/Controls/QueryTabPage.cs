using System.Windows.Forms;

namespace DbLiteDesktop.Controls;

public class QueryTabPage : UserControl
{
    public SqlEditorTextBox TxtSql { get; } = new();
    public DataGridView GridResults { get; } = new();
    public Label LblStatus { get; } = new();
    public Button BtnRunSql { get; } = new();
    public Button BtnClearSql { get; } = new();
    public Button BtnCopySql { get; } = new();
    public Button BtnFormatSql { get; } = new();
    public Button BtnExportResults { get; } = new();

    private readonly TableLayoutPanel _layout = new();
    private readonly FlowLayoutPanel _buttonPanel = new();

    public string Title { get; set; }

    public FlowLayoutPanel ButtonPanel => _buttonPanel;

    public event EventHandler? RunSqlRequested;
    public event EventHandler? ClearSqlRequested;
    public event EventHandler? CopySqlRequested;
    public event EventHandler? FormatSqlRequested;
    public event EventHandler? ExportResultsRequested;

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
        _layout.Controls.Add(TxtSql, 0, 0);
        _layout.Controls.Add(_buttonPanel, 0, 1);
        _layout.Controls.Add(GridResults, 0, 2);
        _layout.Controls.Add(LblStatus, 0, 3);
        Controls.Add(_layout);

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
        GridResults.Dock = DockStyle.Fill;
        GridResults.ReadOnly = true;
        GridResults.RowHeadersVisible = false;
        GridResults.AllowUserToOrderColumns = true;

        LblStatus.Dock = DockStyle.Fill;
        LblStatus.Text = "未连接";
        LblStatus.TextAlign = ContentAlignment.MiddleLeft;
    }

    private void HookEvents()
    {
        BtnRunSql.Click += (_, _) => RunSqlRequested?.Invoke(this, EventArgs.Empty);
        BtnFormatSql.Click += (_, _) => FormatSqlRequested?.Invoke(this, EventArgs.Empty);
        BtnClearSql.Click += (_, _) => ClearSqlRequested?.Invoke(this, EventArgs.Empty);
        BtnCopySql.Click += (_, _) => CopySqlRequested?.Invoke(this, EventArgs.Empty);
        BtnExportResults.Click += (_, _) => ExportResultsRequested?.Invoke(this, EventArgs.Empty);
    }
}

