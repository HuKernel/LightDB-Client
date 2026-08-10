using DbLiteDesktop.Models;

namespace DbLiteDesktop.Forms;

public sealed class VectorSearchForm : Form
{
    private readonly TableColumnInfo _column;
    private readonly TextBox txtVector = new();
    private readonly ComboBox cboMetric = new();
    private readonly NumericUpDown numTopK = new();
    private readonly Button btnGenerate = new();
    private readonly Button btnCancel = new();
    private readonly Label lblTableColumn = new();
    private readonly Label lblVector = new();
    private readonly Label lblMetric = new();
    private readonly Label lblTopK = new();
    private readonly Label lblHint = new();

    private const string MetricCosine = "余弦距离 <=>";
    private const string MetricL2 = "欧氏距离 <->";
    private const string MetricInner = "内积 <#>";

    public string GeneratedSql { get; private set; } = string.Empty;

    public VectorSearchForm(string tableName, TableColumnInfo column)
    {
        _column = column;
        BuildLayout(tableName);
        ApplyTheme();
    }

    private void BuildLayout(string tableName)
    {
        Text = "向量相似搜索";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(520, 400);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        lblTableColumn.Text = $"表:{tableName}    向量列:{_column.Name}  ({_column.Type})";
        lblTableColumn.Location = new Point(20, 18);
        lblTableColumn.AutoSize = true;
        lblTableColumn.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);

        lblVector.Text = "查询向量(逗号分隔的数字,维度需与列一致):";
        lblVector.Location = new Point(20, 52);
        lblVector.AutoSize = true;

        txtVector.Multiline = true;
        txtVector.ScrollBars = ScrollBars.Vertical;
        txtVector.Location = new Point(20, 78);
        txtVector.Size = new Size(480, 120);
        txtVector.Font = new Font("Consolas", 9.5F, GraphicsUnit.Point);

        lblMetric.Text = "距离度量:";
        lblMetric.Location = new Point(20, 216);
        lblMetric.AutoSize = true;

        cboMetric.Items.AddRange([MetricCosine, MetricL2, MetricInner]);
        cboMetric.DropDownStyle = ComboBoxStyle.DropDownList;
        cboMetric.SelectedIndex = 0;
        cboMetric.Location = new Point(96, 212);
        cboMetric.Size = new Size(180, 28);

        lblTopK.Text = "Top K:";
        lblTopK.Location = new Point(310, 216);
        lblTopK.AutoSize = true;

        numTopK.Minimum = 1;
        numTopK.Maximum = 1000;
        numTopK.Value = 10;
        numTopK.Location = new Point(360, 212);
        numTopK.Size = new Size(140, 28);

        lblHint.Text = _column.VectorDimension > 0
            ? $"💡 该列维度为 {_column.VectorDimension},粘贴向量后将自动校验维度"
            : "💡 该列未声明固定维度";
        lblHint.Location = new Point(20, 256);
        lblHint.AutoSize = true;
        lblHint.ForeColor = Color.FromArgb(100, 116, 139);

        btnGenerate.Text = "生成并执行";
        btnGenerate.Location = new Point(240, 320);
        btnGenerate.AutoSize = true;
        btnGenerate.Click += (_, _) => TryGenerate(tableName);

        btnCancel.Text = "取消";
        btnCancel.Location = new Point(380, 320);
        btnCancel.AutoSize = true;
        btnCancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        AcceptButton = btnGenerate;
        CancelButton = btnCancel;

        Controls.AddRange([lblTableColumn, lblVector, txtVector, lblMetric, cboMetric,
            lblTopK, numTopK, lblHint, btnGenerate, btnCancel]);
    }

    private void TryGenerate(string tableName)
    {
        var raw = txtVector.Text.Trim().Trim('[', ']');
        if (string.IsNullOrWhiteSpace(raw))
        {
            MessageBox.Show(this, "请输入查询向量。", "提示");
            return;
        }

        var parts = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var numbers = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            if (!double.TryParse(part, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                MessageBox.Show(this, $"向量包含非数字内容:{part}", "提示");
                return;
            }
            numbers.Add(part);
        }

        if (_column.VectorDimension > 0 && numbers.Count != _column.VectorDimension)
        {
            MessageBox.Show(this,
                $"向量维度不匹配:列 {_column.Name} 需要 {_column.VectorDimension} 维,实际输入 {numbers.Count} 维。",
                "提示");
            return;
        }

        var operator_ = cboMetric.SelectedIndex switch
        {
            1 => "<->",
            2 => "<#>",
            _ => "<=>"
        };

        Func<string, string> quote = DbLiteDesktop.Utils.IdentifierQuoteHelper.QuotePostgres;
        var vectorLiteral = $"[{string.Join(",", numbers)}]";
        var topK = (int)numTopK.Value;

        GeneratedSql =
            $"SELECT *, {quote(_column.Name)} {operator_} '{vectorLiteral}'::vector AS vector_distance\n" +
            $"FROM {quote(tableName)}\n" +
            $"WHERE {quote(_column.Name)} IS NOT NULL\n" +
            $"ORDER BY {quote(_column.Name)} {operator_} '{vectorLiteral}'::vector\n" +
            $"LIMIT {topK};";

        DialogResult = DialogResult.OK;
        Close();
    }

    private void ApplyTheme()
    {
        var accentColor = Color.FromArgb(15, 118, 110);
        var accentHoverColor = Color.FromArgb(17, 94, 89);
        var borderColor = Color.FromArgb(226, 232, 240);
        var textColor = Color.FromArgb(15, 23, 42);

        BackColor = Color.FromArgb(248, 250, 252);
        lblTableColumn.ForeColor = accentColor;

        txtVector.BorderStyle = BorderStyle.FixedSingle;
        txtVector.BackColor = Color.White;
        txtVector.ForeColor = textColor;

        cboMetric.FlatStyle = FlatStyle.Flat;
        cboMetric.BackColor = Color.White;
        cboMetric.ForeColor = textColor;

        numTopK.BackColor = Color.White;
        numTopK.ForeColor = textColor;

        btnGenerate.FlatStyle = FlatStyle.Flat;
        btnGenerate.FlatAppearance.BorderSize = 0;
        btnGenerate.BackColor = accentColor;
        btnGenerate.ForeColor = Color.White;
        btnGenerate.Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
        btnGenerate.Padding = new Padding(16, 8, 16, 8);
        btnGenerate.Cursor = Cursors.Hand;
        btnGenerate.MouseEnter += (_, _) => btnGenerate.BackColor = accentHoverColor;
        btnGenerate.MouseLeave += (_, _) => btnGenerate.BackColor = accentColor;

        btnCancel.FlatStyle = FlatStyle.Flat;
        btnCancel.FlatAppearance.BorderColor = borderColor;
        btnCancel.FlatAppearance.BorderSize = 1;
        btnCancel.BackColor = Color.White;
        btnCancel.ForeColor = Color.FromArgb(100, 116, 139);
        btnCancel.Padding = new Padding(16, 8, 16, 8);
        btnCancel.Cursor = Cursors.Hand;
    }
}
