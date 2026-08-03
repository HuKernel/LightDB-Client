using DbLiteDesktop.Models;
using DbLiteDesktop.Providers;
using DbLiteDesktop.Services;

namespace DbLiteDesktop.Forms;

public partial class ConnectionForm : Form
{
    private readonly PasswordEncryptService _passwordEncryptService = new();

    public DbConnectionConfig ConnectionConfig { get; private set; }
    public bool ConnectAfterSave { get; private set; }

    public ConnectionForm(DbConnectionConfig? existing = null)
    {
        InitializeComponent();
        ConnectionConfig = existing is null
            ? new DbConnectionConfig { DbType = "sqlite" }
            : Clone(existing);
        LoadFromModel();
        ApplyTheme();
    }

    private static DbConnectionConfig Clone(DbConnectionConfig config)
    {
        return new DbConnectionConfig
        {
            Id = config.Id,
            Name = config.Name,
            DbType = config.DbType,
            Host = config.Host,
            Port = config.Port,
            DatabaseName = config.DatabaseName,
            Username = config.Username,
            PasswordEncrypted = config.PasswordEncrypted,
            FilePath = config.FilePath,
            ConnectionTimeoutSec = config.ConnectionTimeoutSec,
            CommandTimeoutSec = config.CommandTimeoutSec,
            CreatedAt = config.CreatedAt,
            UpdatedAt = config.UpdatedAt
        };
    }

    private void LoadFromModel()
    {
        cboDbType.Items.Clear();
        cboDbType.Items.AddRange(["sqlite", "mysql", "postgresql"]);
        cboDbType.SelectedItem = string.IsNullOrWhiteSpace(ConnectionConfig.DbType)
            ? "sqlite"
            : ConnectionConfig.DbType.ToLowerInvariant();

        txtName.Text = ConnectionConfig.Name;
        txtHost.Text = ConnectionConfig.Host ?? string.Empty;
        numPort.Value = ConnectionConfig.Port is > 0 ? ConnectionConfig.Port.Value : 3306;
        txtDatabaseName.Text = ConnectionConfig.DatabaseName ?? string.Empty;
        txtUsername.Text = ConnectionConfig.Username ?? string.Empty;
        txtFilePath.Text = ConnectionConfig.FilePath ?? string.Empty;
        numConnectionTimeout.Value = ConnectionConfig.ConnectionTimeoutSec is > 0 ? ConnectionConfig.ConnectionTimeoutSec.Value : 10;
        numCommandTimeout.Value = ConnectionConfig.CommandTimeoutSec is > 0 ? ConnectionConfig.CommandTimeoutSec.Value : 30;

        if (!string.IsNullOrWhiteSpace(ConnectionConfig.PasswordEncrypted) &&
            !string.Equals(ConnectionConfig.DbType, "sqlite", StringComparison.OrdinalIgnoreCase))
        {
            txtPassword.Text = _passwordEncryptService.Decrypt(ConnectionConfig.PasswordEncrypted);
        }

        UpdateFieldVisibility();
    }

    private void UpdateFieldVisibility()
    {
        var isSqlite = SelectedDbType == "sqlite";

        lblHost.Visible = txtHost.Visible = !isSqlite;
        lblPort.Visible = numPort.Visible = !isSqlite;
        lblDatabaseName.Visible = txtDatabaseName.Visible = !isSqlite;
        lblUsername.Visible = txtUsername.Visible = !isSqlite;
        lblPassword.Visible = txtPassword.Visible = !isSqlite;

        lblFilePath.Visible = txtFilePath.Visible = btnBrowse.Visible = isSqlite;

        // 超时设置对 MySQL/PostgreSQL 可见
        lblConnectionTimeout.Visible = numConnectionTimeout.Visible = !isSqlite;
        lblCommandTimeout.Visible = numCommandTimeout.Visible = !isSqlite;

        // 根据数据库类型切换默认端口
        if (!isSqlite && numPort.Value == 3306 && SelectedDbType == "postgresql")
        {
            numPort.Value = 5432;
        }
        else if (!isSqlite && numPort.Value == 5432 && SelectedDbType == "mysql")
        {
            numPort.Value = 3306;
        }
    }

    private string SelectedDbType =>
        (cboDbType.SelectedItem?.ToString() ?? "sqlite").ToLowerInvariant();

    private DbConnectionConfig BuildConfigFromInputs()
    {
        var dbType = SelectedDbType;

        return new DbConnectionConfig
        {
            Id = ConnectionConfig.Id,
            Name = txtName.Text.Trim(),
            DbType = dbType,
            Host = dbType != "sqlite" ? txtHost.Text.Trim() : null,
            Port = dbType != "sqlite" ? (int)numPort.Value : null,
            DatabaseName = dbType != "sqlite" ? txtDatabaseName.Text.Trim() : null,
            Username = dbType != "sqlite" ? txtUsername.Text.Trim() : null,
            PasswordEncrypted = dbType != "sqlite"
                ? _passwordEncryptService.Encrypt(txtPassword.Text)
                : null,
            FilePath = dbType == "sqlite" ? txtFilePath.Text.Trim() : null,
            ConnectionTimeoutSec = dbType != "sqlite" ? (int)numConnectionTimeout.Value : null,
            CommandTimeoutSec = dbType != "sqlite" ? (int)numCommandTimeout.Value : null,
            CreatedAt = ConnectionConfig.CreatedAt,
            UpdatedAt = ConnectionConfig.UpdatedAt
        };
    }

    private bool ValidateInputs(out string message)
    {
        if (string.IsNullOrWhiteSpace(txtName.Text))
        {
            message = "请输入连接名称。";
            return false;
        }

        if (SelectedDbType == "sqlite")
        {
            if (string.IsNullOrWhiteSpace(txtFilePath.Text))
            {
                message = "请选择 SQLite 数据库文件。";
                return false;
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(txtHost.Text) ||
                string.IsNullOrWhiteSpace(txtDatabaseName.Text) ||
                string.IsNullOrWhiteSpace(txtUsername.Text))
            {
                message = "请填写主机地址、数据库名称和用户名。";
                return false;
            }
        }

        message = string.Empty;
        return true;
    }

    private void SaveAndClose(bool connectAfterSave)
    {
        if (!ValidateInputs(out var message))
        {
            MessageBox.Show(message, "提示");
            return;
        }

        ConnectionConfig = BuildConfigFromInputs();
        ConnectAfterSave = connectAfterSave;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void TestCurrentConnection()
    {
        if (!ValidateInputs(out var message))
        {
            MessageBox.Show(message, "提示");
            return;
        }

        var config = BuildConfigFromInputs();
        var provider = DatabaseProviderFactory.Create(config.DbType);
        var password = config.DbType == "sqlite" ? string.Empty : txtPassword.Text;

        provider.TestConnection(config, password);
        MessageBox.Show("连接测试成功。", "提示");
    }

    private void ApplyTheme()
    {
        var accentColor = Color.FromArgb(15, 118, 110);
        var accentHoverColor = Color.FromArgb(17, 94, 89);
        var borderColor = Color.FromArgb(226, 232, 240);
        var textColor = Color.FromArgb(15, 23, 42);
        var subtleTextColor = Color.FromArgb(100, 116, 139);

        BackColor = Color.FromArgb(248, 250, 252);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        foreach (var button in new[] { btnTest, btnSave, btnConnect })
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = accentColor;
            button.ForeColor = Color.White;
            button.Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
            button.Padding = new Padding(14, 7, 14, 7);
            button.MinimumSize = new Size(0, 36);
            button.Cursor = Cursors.Hand;
            button.MouseEnter += (_, _) => button.BackColor = accentHoverColor;
            button.MouseLeave += (_, _) => button.BackColor = accentColor;
        }

        foreach (var button in new[] { btnCancel, btnBrowse })
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = borderColor;
            button.FlatAppearance.BorderSize = 1;
            button.BackColor = Color.White;
            button.ForeColor = subtleTextColor;
            button.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            button.Padding = new Padding(14, 7, 14, 7);
            button.MinimumSize = new Size(0, 35);
            button.Cursor = Cursors.Hand;
        }

        foreach (var textBox in new[] { txtName, txtHost, txtDatabaseName, txtUsername, txtPassword, txtFilePath })
        {
            textBox.BorderStyle = BorderStyle.FixedSingle;
            textBox.BackColor = Color.White;
            textBox.ForeColor = textColor;
            textBox.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        }

        foreach (var comboBox in new[] { cboDbType })
        {
            comboBox.FlatStyle = FlatStyle.Flat;
            comboBox.BackColor = Color.White;
            comboBox.ForeColor = textColor;
            comboBox.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        }

        numPort.BackColor = Color.White;
        numPort.ForeColor = textColor;
    }

    private void cboDbType_SelectedIndexChanged(object? sender, EventArgs e)
    {
        UpdateFieldVisibility();
    }

    private void btnBrowse_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "SQLite Database|*.db;*.sqlite;*.sqlite3|All Files|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            txtFilePath.Text = dialog.FileName;
        }
    }

    private void btnTest_Click(object? sender, EventArgs e)
    {
        try
        {
            TestCurrentConnection();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "连接测试失败");
        }
    }

    private void btnSave_Click(object? sender, EventArgs e)
    {
        SaveAndClose(false);
    }

    private void btnConnect_Click(object? sender, EventArgs e)
    {
        SaveAndClose(true);
    }

    private void btnCancel_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }
}
