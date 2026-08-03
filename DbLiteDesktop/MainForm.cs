using System.Data;
using System.Drawing.Drawing2D;
using DbLiteDesktop.Forms;
using DbLiteDesktop.Models;
using DbLiteDesktop.Providers;
using DbLiteDesktop.Services;
using DbLiteDesktop.Utils;

namespace DbLiteDesktop;

public partial class MainForm : Form
{
    private const int PreviewPageSize = 100;
    private const string AllFieldsOption = "全部字段";

    private readonly ConfigService _configService = new();
    private readonly PasswordEncryptService _passwordEncryptService = new();
    // SqlGuardService 改为静态类，无需实例化
    private readonly DataExportService _dataExportService = new();
    private readonly ContextMenuStrip _previewCopyMenu = new();
    private readonly ContextMenuStrip _tableCopyMenu = new();
    private QueryHistoryService _queryHistoryService = null!;
    private DbConnectionConfig? _currentConfig;
    private IDatabaseProvider? _currentProvider;
    private string? _currentPreviewTableName;
    private int _currentPreviewPage = 1;
    private List<string> _currentPreviewColumns = [];
    private List<string> _allTables = [];
    private string _previewCopyText = string.Empty;
    private bool _isLoading;
    private CancellationTokenSource? _rowCountCts;
    private int _queryTabCounter;
    private int _hoveredCloseIndex = -1;
    private int _contextMenuQueryTabIndex = -1;

    public MainForm()
    {
        InitializeComponent();
        InitializeServices();
        ApplyTheme();
        tabMain.DrawItem += tabMain_DrawItem;
        Shown += (_, _) =>
        {
            ApplyDefaultSplitterDistance();
            if (queryTabs.TabPages.Count == 0)
            {
                AddQueryTab();
            }
        };
    }

    private void InitializeServices()
    {
        _configService.Initialize();
        _queryHistoryService = new QueryHistoryService(_configService.DatabasePath);
        _queryHistoryService.Initialize();
        InitializePreviewSearch();
        InitializePreviewCopyMenu();
        InitializeTableCopyMenu();
        LoadConnections();
        LoadHistory();
    }

    private void InitializePreviewSearch()
    {
        cboPreviewMatch.Items.Clear();
        cboPreviewMatch.Items.AddRange(["包含", "精确"]);
        cboPreviewMatch.SelectedIndex = 0;

        cboPreviewField.Items.Clear();
        cboPreviewField.Items.Add(AllFieldsOption);
        cboPreviewField.SelectedIndex = 0;
    }

    private void InitializePreviewCopyMenu()
    {
        _previewCopyMenu.BackColor = Color.White;
        _previewCopyMenu.ForeColor = Color.FromArgb(15, 23, 42);
        _previewCopyMenu.Font = new Font("Segoe UI", 9F, GraphicsUnit.Point);
        _previewCopyMenu.ShowImageMargin = false;
        _previewCopyMenu.RenderMode = ToolStripRenderMode.ManagerRenderMode;
        _previewCopyMenu.Renderer = new LightMenuRenderer();

        var copyItem = new ToolStripMenuItem("复制");
        copyItem.BackColor = Color.White;
        copyItem.ForeColor = Color.FromArgb(15, 23, 42);
        copyItem.Font = new Font("Segoe UI", 9F, GraphicsUnit.Point);
        copyItem.Margin = new Padding(8, 6, 8, 6);
        copyItem.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_previewCopyText))
            {
                Clipboard.SetText(_previewCopyText);
            }
        };

        _previewCopyMenu.Items.Clear();
        _previewCopyMenu.Items.Add(copyItem);
        gridPreview.MouseDown += gridPreview_MouseDown;
    }

    private void InitializeTableCopyMenu()
    {
        _tableCopyMenu.BackColor = Color.White;
        _tableCopyMenu.ForeColor = Color.FromArgb(15, 23, 42);
        _tableCopyMenu.Font = new Font("Segoe UI", 9F, GraphicsUnit.Point);
        _tableCopyMenu.ShowImageMargin = false;
        _tableCopyMenu.RenderMode = ToolStripRenderMode.ManagerRenderMode;
        _tableCopyMenu.Renderer = new LightMenuRenderer();

        var copyNameItem = new ToolStripMenuItem("复制表名");
        copyNameItem.BackColor = Color.White;
        copyNameItem.ForeColor = Color.FromArgb(15, 23, 42);
        copyNameItem.Font = new Font("Segoe UI", 9F, GraphicsUnit.Point);
        copyNameItem.Margin = new Padding(8, 6, 8, 6);
        copyNameItem.Click += (_, _) =>
        {
            var node = treeTables.SelectedNode;
            if (node is not null && !string.IsNullOrEmpty(node.Text))
            {
                Clipboard.SetText(node.Text);
            }
        };

        _tableCopyMenu.Items.Clear();
        _tableCopyMenu.Items.Add(copyNameItem);

        treeTables.MouseDown += treeTables_MouseDown;
    }

    private void treeTables_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
        {
            return;
        }
        var hit = treeTables.GetNodeAt(e.Location);
        if (hit is null || string.IsNullOrEmpty(hit.Text))
        {
            return;
        }
        treeTables.SelectedNode = hit;
        _tableCopyMenu.Show(treeTables, e.Location);
    }

    private void LoadConnections()
    {
        var selectedId = GetSelectedConnection()?.Id;
        var items = _configService.GetConnections();

        cboConnections.Items.Clear();
        foreach (var item in items)
        {
            cboConnections.Items.Add(item);
        }

        if (selectedId.HasValue)
        {
            SelectConnection(selectedId.Value);
        }
        else if (cboConnections.Items.Count > 0)
        {
            cboConnections.SelectedIndex = 0;
        }
    }

    private void LoadHistory()
    {
        gridHistory.DataSource = null;
        gridHistory.DataSource = _queryHistoryService.GetRecent();
    }

    private DbConnectionConfig? GetSelectedConnection()
    {
        return cboConnections.SelectedItem as DbConnectionConfig;
    }

    private void SelectConnection(int connectionId)
    {
        for (var i = 0; i < cboConnections.Items.Count; i++)
        {
            if (cboConnections.Items[i] is DbConnectionConfig item && item.Id == connectionId)
            {
                cboConnections.SelectedIndex = i;
                return;
            }
        }
    }

    private void OpenConnectionForm(DbConnectionConfig? config = null)
    {
        using var form = new ConnectionForm(config);
        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _configService.SaveConnection(form.ConnectionConfig);
        LoadConnections();
        SelectConnection(form.ConnectionConfig.Id);

        if (form.ConnectAfterSave)
        {
            ConnectSelected();
        }
    }

    private void TestSelectedConnection()
    {
        var config = GetSelectedConnection();
        if (config is null)
        {
            MessageBox.Show("请先选择连接。", "提示");
            return;
        }

        try
        {
            var provider = DatabaseProviderFactory.Create(config.DbType);
            provider.TestConnection(config, GetPassword(config));
            MessageBox.Show("连接测试成功。", "提示");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "连接测试失败");
        }
    }

    private void ConnectSelected()
    {
        var config = GetSelectedConnection();
        if (config is null)
        {
            MessageBox.Show("请先选择连接。", "提示");
            return;
        }

        try
        {
            var provider = DatabaseProviderFactory.Create(config.DbType);
            provider.TestConnection(config, GetPassword(config));
            _currentConfig = config;
            _currentProvider = provider;
            LoadTables();
            lblStatus.Text = $"已连接：{config.Name}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "连接失败");
        }
    }

    private void LoadTables()
    {
        if (_currentConfig is null || _currentProvider is null)
        {
            return;
        }

        SetLoading(true);
        Task.Run(() =>
        {
            try
            {
                var tables = _currentProvider.GetTables(_currentConfig, GetPassword(_currentConfig));
                Invoke(() =>
                {
                    _allTables = tables.ToList();
                    txtTableSearch.Clear();
                    ApplyTableFilter();
                    SetLoading(false);
                });
            }
            catch (Exception ex)
            {
                Invoke(() =>
                {
                    SetLoading(false);
                    MessageBox.Show(ex.Message, "读取表列表失败");
                });
            }
        });
    }

    private void SetLoading(bool loading)
    {
        _isLoading = loading;
        lblStatus.Text = loading ? "加载中..." : lblStatus.Text;
        Cursor = loading ? Cursors.WaitCursor : Cursors.Default;
        UseWaitCursor = loading;
    }

    private void LoadColumnsForTable(string tableName)
    {
        if (_currentConfig is null || _currentProvider is null)
        {
            return;
        }

        SetLoading(true);
        Task.Run(() =>
        {
            try
            {
                var columns = _currentProvider.GetColumns(_currentConfig, GetPassword(_currentConfig), tableName);
                Invoke(() =>
                {
                    gridColumns.DataSource = null;
                    gridColumns.DataSource = columns;
                    _currentPreviewColumns = columns.Select(column => column.Name).Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
                    BindPreviewFields();

                    // 给当前 active 查询页设置补全 provider(不覆盖用户输入)
                    var activePage = GetActiveQueryPage();
                    if (activePage is not null)
                    {
                        activePage.TxtSql.CompletionProvider = BuildCompletionItems;
                    }

                    // 切表 → 新开查询标签页,带该表的预览 SQL(不覆盖任何已有页内容)
                    AddQueryTab(_currentProvider.BuildPreviewSql(tableName, 100));

                    _currentPreviewTableName = tableName;
                    _currentPreviewPage = 1;
                    txtPreviewKeyword.Clear();
                    cboPreviewMatch.SelectedIndex = 0;
                    LoadPreviewPage();
                    SetLoading(false);
                    UpdateRowCountAsync(tableName);
                });
            }
            catch (Exception ex)
            {
                Invoke(() =>
                {
                    SetLoading(false);
                    MessageBox.Show(ex.Message, "读取字段失败");
                });
            }
        });
    }

    private void BindPreviewFields()
    {
        cboPreviewField.Items.Clear();
        cboPreviewField.Items.Add(AllFieldsOption);

        foreach (var column in _currentPreviewColumns)
        {
            cboPreviewField.Items.Add(column);
        }

        cboPreviewField.SelectedIndex = 0;
    }

    private void LoadPreviewPage()
    {
        if (_currentConfig is null || _currentProvider is null || string.IsNullOrWhiteSpace(_currentPreviewTableName))
        {
            return;
        }

        SetLoading(true);
        Task.Run(() =>
        {
            try
            {
                var sql = string.Empty;
                Invoke(() => { sql = BuildPreviewSql(_currentProvider); });

                var result = _currentProvider.ExecuteQuery(_currentConfig, GetPassword(_currentConfig), sql, PreviewPageSize);

                Invoke(() =>
                {
                    gridPreview.SuspendLayout();
                    try
                    {
                        gridPreview.DataSource = null;
                        gridPreview.DataSource = result;
                        gridPreview.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);
                    }
                    finally
                    {
                        gridPreview.ResumeLayout();
                    }

                    lblPreviewPage.Text = $"第 {_currentPreviewPage} 页";
                    btnPrevPage.Enabled = _currentPreviewPage > 1;
                    btnNextPage.Enabled = result.Rows.Count >= PreviewPageSize;
                    tabMain.SelectedTab = tabPreview;
                    SetLoading(false);
                });
            }
            catch (Exception ex)
            {
                Invoke(() =>
                {
                    SetLoading(false);
                    MessageBox.Show(ex.Message, "加载数据预览失败");
                });
            }
        });
    }

    private string BuildPreviewSql(IDatabaseProvider provider)
    {
        if (string.IsNullOrWhiteSpace(_currentPreviewTableName))
        {
            return string.Empty;
        }

        var selectedColumn = cboPreviewField.SelectedItem?.ToString();
        if (string.Equals(selectedColumn, AllFieldsOption, StringComparison.Ordinal))
        {
            selectedColumn = null;
        }

        var parseResult = PreviewSearchInputParser.Parse(
            txtPreviewKeyword.Text,
            _currentPreviewColumns,
            selectedColumn,
            string.Equals(cboPreviewMatch.SelectedItem?.ToString(), "精确", StringComparison.Ordinal)
        );

        if (!parseResult.Success)
        {
            throw new InvalidOperationException(parseResult.ErrorMessage);
        }

        if (string.IsNullOrWhiteSpace(parseResult.Keyword))
        {
            return provider.BuildPagedPreviewSql(_currentPreviewTableName, _currentPreviewPage, PreviewPageSize);
        }

        var exactMatch = parseResult.FieldName is not null
            ? true
            : parseResult.ExactMatch;

        return provider.BuildFilteredPreviewSql(
            _currentPreviewTableName,
            _currentPreviewColumns,
            parseResult.FieldName,
            parseResult.Keyword,
            exactMatch,
            _currentPreviewPage,
            PreviewPageSize
        );
    }

    private void RunSqlFor(DbLiteDesktop.Controls.QueryTabPage page, bool preferSelection = false)
    {
        if (_currentConfig is null || _currentProvider is null)
        {
            MessageBox.Show("请先连接数据库。", "提示");
            return;
        }

        var sql = preferSelection
            ? page.GetEffectiveSql()
            : page.TxtSql.Text.Trim();

        if (string.IsNullOrWhiteSpace(sql))
        {
            return;
        }

        if (!SqlGuardService.IsReadonlySql(sql))
        {
            MessageBox.Show("当前工具只允许执行只读 SQL。", "提示");
            return;
        }

        SetLoading(true);
        var startedAt = DateTime.UtcNow;
        var config = _currentConfig;
        var provider = _currentProvider;
        var password = GetPassword(config);

        Task.Run(() =>
        {
            try
            {
                var result = provider.ExecuteQuery(config, password, sql, 1000);
                var duration = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;

                Invoke(() =>
                {
                    page.GridResults.SuspendLayout();
                    try
                    {
                        page.GridResults.DataSource = null;
                        page.GridResults.DataSource = result;
                        page.GridResults.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);
                    }
                    finally
                    {
                        page.GridResults.ResumeLayout();
                    }

                    page.LblStatus.Text = $"查询成功，返回 {result.Rows.Count} 行，耗时 {duration} ms";

                    _queryHistoryService.Add(new QueryHistoryItem
                    {
                        ConnectionId = config.Id,
                        DbType = config.DbType,
                        DatabaseName = GetDisplayDatabaseName(config),
                        SqlText = sql,
                        Success = true,
                        DurationMs = duration,
                        RowCount = result.Rows.Count
                    });

                    LoadHistory();
                    tabMain.SelectedTab = tabSql;
                    SetLoading(false);
                });
            }
            catch (Exception ex)
            {
                var duration = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                Invoke(() =>
                {
                    page.LblStatus.Text = $"查询失败：{ex.Message}";

                    _queryHistoryService.Add(new QueryHistoryItem
                    {
                        ConnectionId = config.Id,
                        DbType = config.DbType,
                        DatabaseName = GetDisplayDatabaseName(config),
                        SqlText = sql,
                        Success = false,
                        ErrorMessage = ex.Message,
                        DurationMs = duration,
                        RowCount = 0
                    });

                    LoadHistory();
                    SetLoading(false);
                    MessageBox.Show(ex.Message, "查询失败");
                });
            }
        });
    }

    private string GetPassword(DbConnectionConfig config)
    {
        return string.Equals(config.DbType, "sqlite", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : _passwordEncryptService.Decrypt(config.PasswordEncrypted ?? string.Empty);
    }

    private static string GetDisplayDatabaseName(DbConnectionConfig config)
    {
        return config.DatabaseName ?? config.FilePath ?? config.Name;
    }

    private void Disconnect()
    {
        _currentConfig = null;
        _currentProvider = null;
        _currentPreviewTableName = null;
        _currentPreviewPage = 1;
        _currentPreviewColumns = [];
        CancelRowCount();
        lblRowCount.Text = string.Empty;
        treeTables.Nodes.Clear();
        _allTables = [];
        txtTableSearch.Clear();
        gridColumns.DataSource = null;
        gridPreview.DataSource = null;
        lblPreviewPage.Text = "第 1 页";
        lblStatus.Text = "未连接";
        txtPreviewKeyword.Clear();
        cboPreviewField.Items.Clear();
        cboPreviewField.Items.Add(AllFieldsOption);
        cboPreviewField.SelectedIndex = 0;

        foreach (var page in EnumerateQueryPages())
        {
            page.GridResults.DataSource = null;
            page.LblStatus.Text = "未连接";
            page.TxtSql.CompletionProvider = null;
        }
    }

    private bool _themeApplied;

    private void ApplyTheme()
    {
        if (_themeApplied)
        {
            return;
        }

        _themeApplied = true;

        // 方案 C — TablePlus 开发者风(Teal + Run Green + Link Sky)
        var pageBackColor = Color.FromArgb(248, 250, 252);     // #f8fafc Slate-50
        var cardBackColor = Color.White;                       // 卡片纯白
        var chromeBackColor = Color.FromArgb(241, 245, 249);   // #f1f5f9 Slate-100
        var inputBackColor = Color.FromArgb(248, 250, 252);    // #f8fafc
        var accentColor = Color.FromArgb(15, 118, 110);        // #0f766e Teal-700 主色
        var accentHoverColor = Color.FromArgb(17, 94, 89);     // #115e59 Teal-800 hover
        var accentLight = Color.FromArgb(204, 251, 241);       // #ccfbf1 Teal-100 选中
        var runColor = Color.FromArgb(22, 163, 74);            // #16a34a Green-600 执行
        var runHoverColor = Color.FromArgb(21, 128, 61);       // #15803d Green-700
        var linkColor = Color.FromArgb(2, 132, 199);           // #0284c7 Sky-600 链接/计数
        var borderColor = Color.FromArgb(226, 232, 240);       // #e2e8f0 Slate-200
        var textColor = Color.FromArgb(15, 23, 42);            // #0f172a Slate-900 主文字
        var subtleTextColor = Color.FromArgb(100, 116, 139);   // #64748b Slate-500 次级
        var rowAltColor = Color.FromArgb(248, 250, 252);       // #f8fafc 交替行

        BackColor = pageBackColor;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        txtPreviewKeyword.PlaceholderText = "输入值，或直接输入 字段名=数据";

        lblAppTitle.Font = new Font("Segoe UI", 15F, FontStyle.Bold, GraphicsUnit.Point);
        lblAppTitle.ForeColor = accentColor;
        lblAppTitle.Text = "🗄️ DB Lite Desktop";
        lblAppTitle.AutoSize = false;
        lblAppTitle.Dock = DockStyle.Fill;
        lblAppTitle.TextAlign = ContentAlignment.MiddleLeft;
        lblAppSubtitle.Font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        lblAppSubtitle.ForeColor = subtleTextColor;
        lblAppSubtitle.Visible = true;
        lblAppSubtitle.AutoSize = true;
        lblAppSubtitle.Location = new Point(0, 38);
        lblAppSubtitle.Margin = new Padding(0);

        lblTablesTitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Point);
        lblTablesTitle.ForeColor = textColor;
        lblTablesTitle.Text = "📊 数据表导航";
        lblTablesSubtitle.Font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        lblTablesSubtitle.ForeColor = subtleTextColor;
        lblTablesSubtitle.Text = "连接成功后显示当前数据库中的表";

        ApplyPanelChrome(headerPanel, cardBackColor, borderColor);
        ApplyPanelChrome(navigationPanel, cardBackColor, borderColor);
        ApplyPanelChrome(workspacePanel, cardBackColor, borderColor);
        ApplyPanelChrome(headerActionsPanel, cardBackColor, Color.Transparent);
        ApplyPanelChrome(previewSearchPanel, chromeBackColor, borderColor);
        ApplyPanelChrome(previewButtonPanel, chromeBackColor, borderColor);
        ApplyPanelChrome(buttonsPanel, chromeBackColor, Color.Transparent);
        ApplyPanelChrome(sqlToolbar, chromeBackColor, borderColor);

        lblConnection.ForeColor = subtleTextColor;
        lblConnection.TextAlign = ContentAlignment.MiddleLeft;
        StyleComboBox(cboConnections);
        cboConnections.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        StyleHeaderButton(btnNewConnection, false);
        StyleHeaderButton(btnEditConnection, false);
        StyleHeaderButton(btnDeleteConnection, false);
        StyleHeaderButton(btnTestConnection, false);
        StyleHeaderButton(btnRefresh, false);
        StyleHeaderButton(btnConnect, true, accentColor);
        StyleHeaderButton(btnDisconnect, false);

        splitContainer.BackColor = pageBackColor;

        treeTables.BackColor = chromeBackColor;
        treeTables.BorderStyle = BorderStyle.None;
        treeTables.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        treeTables.ForeColor = textColor;
        treeTables.FullRowSelect = true;
        treeTables.HotTracking = true;
        treeTables.Indent = 20;
        treeTables.ItemHeight = 32;
        treeTables.ShowLines = false;
        treeTables.ShowPlusMinus = true;
        treeTables.ShowRootLines = false;
        treeTables.HideSelection = false;
        treeTables.DrawMode = TreeViewDrawMode.OwnerDrawText;
        treeTables.DrawNode += (sender, e) => {
            if (e.Node == null) return;

            var bounds = e.Bounds;
            var isSelected = (e.State & TreeNodeStates.Selected) != 0;
            var isHot = (e.State & TreeNodeStates.Hot) != 0;

            var rowRect = new Rectangle(0, bounds.Top, treeTables.Width, bounds.Height);
            if (isSelected) {
                // 选中:不透明 Seafoam 背景 + Teal 左侧指示条
                using var selBrush = new SolidBrush(Color.FromArgb(204, 251, 241));
                e.Graphics.FillRectangle(selBrush, rowRect);
                using var accentPen = new Pen(accentColor, 3);
                e.Graphics.DrawLine(accentPen, 0, bounds.Top, 0, bounds.Bottom - 1);
            } else if (isHot) {
                using var hotBrush = new SolidBrush(Color.FromArgb(241, 245, 249));
                e.Graphics.FillRectangle(hotBrush, rowRect);
            }

            using var boldFont = isSelected || isHot
                ? new Font(treeTables.Font, FontStyle.Bold)
                : null;

            TextRenderer.DrawText(
                e.Graphics,
                e.Node.Text,
                boldFont ?? treeTables.Font,
                new Rectangle(bounds.X + 4, bounds.Y, bounds.Width, bounds.Height),
                isSelected ? textColor : subtleTextColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            );
        };

        tabMain.Appearance = TabAppearance.Normal;
        tabMain.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabMain.Padding = new Point(16, 10);
        tabMain.SizeMode = TabSizeMode.Fixed;
        tabMain.ItemSize = new Size(120, 36);

        queryTabs.SizeMode = TabSizeMode.Fixed;
        queryTabs.ItemSize = new Size(180, 36);
        queryTabs.DrawItem += QueryTabs_DrawItem;
        queryTabs.MouseClick += QueryTabs_MouseClick;
        queryTabs.MouseMove += QueryTabs_MouseMove;
        queryTabs.MouseLeave += QueryTabs_MouseLeave;
        queryTabs.DoubleClick += QueryTabs_DoubleClick;

        StyleGrid(gridColumns);
        StyleGrid(gridHistory);
        StyleGrid(gridPreview);

        StyleGhostButton(btnPrevPage);
        StyleGhostButton(btnNextPage);
        StyleActionButton(btnApplyPreviewFilter, accentColor);
        StyleActionButton(btnAddQueryTab, accentColor);
        StyleGhostButton(btnResetPreviewFilter);
        StyleGhostButton(btnExportPreview);

        lblStatus.BackColor = chromeBackColor;
        lblStatus.ForeColor = subtleTextColor;
        lblStatus.Font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        lblStatus.Padding = new Padding(16, 0, 0, 0);
        lblStatus.BorderStyle = BorderStyle.None;
        lblPreviewPage.ForeColor = textColor;
        lblPreviewPage.Font = new Font("Segoe UI", 8.75F, FontStyle.Bold, GraphicsUnit.Point);
        lblPreviewTip.ForeColor = subtleTextColor;
        lblPreviewTip.Font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        lblPreviewTip.Text = "💡 支持 字段名=数据 快捷搜索";
        lblRowCount.BackColor = chromeBackColor;
        lblRowCount.ForeColor = linkColor;
        lblRowCount.Font = new Font("Segoe UI", 8.75F, FontStyle.Bold, GraphicsUnit.Point);

        previewSearchPanel.BackColor = chromeBackColor;
        previewSearchPanel.Margin = new Padding(0);
        previewButtonPanel.BackColor = chromeBackColor;
        sqlToolbar.BackColor = chromeBackColor;
        sqlLayout.BackColor = cardBackColor;
        previewLayout.BackColor = cardBackColor;
        tabMain.BackColor = cardBackColor;
        headerPanel.BackColor = cardBackColor;
        navigationPanel.BackColor = cardBackColor;
        workspacePanel.BackColor = cardBackColor;

        // 显式设置 4 个 TabPage 浅色背景(避免系统默认灰色)
        tabColumns.BackColor = cardBackColor;
        tabPreview.BackColor = cardBackColor;
        tabSql.BackColor = cardBackColor;
        tabHistory.BackColor = cardBackColor;
        queryTabs.BackColor = cardBackColor;

        lblPreviewField.ForeColor = subtleTextColor;
        lblPreviewMatch.ForeColor = subtleTextColor;
        lblPreviewKeyword.ForeColor = subtleTextColor;
        lblPreviewField.TextAlign = ContentAlignment.MiddleLeft;
        lblPreviewMatch.TextAlign = ContentAlignment.MiddleLeft;
        lblPreviewKeyword.TextAlign = ContentAlignment.MiddleLeft;
        lblPreviewField.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold, GraphicsUnit.Point);
        lblPreviewMatch.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold, GraphicsUnit.Point);
        lblPreviewKeyword.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold, GraphicsUnit.Point);

        StyleComboBox(cboPreviewField);
        StyleComboBox(cboPreviewMatch);
        StyleTextInput(txtPreviewKeyword);
        StyleTextInput(txtTableSearch);
        txtTableSearch.PlaceholderText = "搜索表名,实时过滤";

        AlignPreviewSearchControls();
    }

    private void StyleGrid(DataGridView grid)
    {
        EnableDoubleBuffering(grid);

        // Ocean Depths 浅色版 - 表格
        var gridBackColor = Color.White;                              // 卡片背景
        var gridHeaderBackColor = Color.FromArgb(241, 245, 249);      // Cream tint 表头
        var gridRowAltColor = Color.FromArgb(248, 250, 252);          // 极淡交替行
        var gridSelectionColor = Color.FromArgb(236, 253, 245);    // #ecfdf5 Green-50 选中行
        var gridTextColor = Color.FromArgb(15, 23, 42);               // 主文字 Deep Navy
        var gridSubtleColor = Color.FromArgb(100, 116, 139);           // 表头次级灰蓝
        var gridLineColor = Color.FromArgb(226, 232, 240);            // 极淡分隔线

        grid.BackgroundColor = gridBackColor;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.EnableHeadersVisualStyles = false;

        grid.RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.DisableResizing;
        grid.RowHeadersDefaultCellStyle.BackColor = gridHeaderBackColor;
        grid.RowHeadersDefaultCellStyle.ForeColor = gridSubtleColor;
        grid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;

        grid.GridColor = gridLineColor;
        grid.ColumnHeadersDefaultCellStyle.BackColor = gridHeaderBackColor;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(15, 23, 42);
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(12, 14, 12, 14);
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = gridHeaderBackColor;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.FromArgb(15, 23, 42);
        grid.ColumnHeadersHeight = 48;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.DefaultCellStyle.SelectionBackColor = gridSelectionColor;
        grid.DefaultCellStyle.SelectionForeColor = gridTextColor;
        grid.DefaultCellStyle.BackColor = gridBackColor;
        grid.DefaultCellStyle.ForeColor = gridTextColor;
        grid.AlternatingRowsDefaultCellStyle.BackColor = gridRowAltColor;
        grid.AlternatingRowsDefaultCellStyle.ForeColor = gridTextColor;
        grid.DefaultCellStyle.Padding = new Padding(10, 10, 10, 10);
        grid.RowTemplate.Height = 40;
        grid.RowTemplate.Resizable = DataGridViewTriState.False;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        grid.ScrollBars = ScrollBars.Both;
        grid.AllowUserToResizeRows = false;
        grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;

        if (ReferenceEquals(grid, gridPreview) || IsQueryResultsGrid(grid))
        {
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        }
        else
        {
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        }
    }

    private static bool IsQueryResultsGrid(DataGridView grid)
    {
        Control? current = grid;
        while (current is not null)
        {
            if (current is DbLiteDesktop.Controls.QueryTabPage)
            {
                return true;
            }
            current = current.Parent;
        }
        return false;
    }

    private static void StyleActionButton(Button button, Color? accentColor = null)
    {
        var baseColor = accentColor ?? Color.FromArgb(15, 118, 110);
        var hoverColor = ControlPaint.Light(baseColor, 0.12F);
        var pressedColor = ControlPaint.Dark(baseColor, 0.08F);

        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = baseColor;
        button.FlatAppearance.MouseDownBackColor = pressedColor;
        button.BackColor = baseColor;
        button.ForeColor = Color.White;
        button.Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
        button.Padding = new Padding(16, 8, 16, 8);
        button.Margin = new Padding(0, 0, 10, 0);
        button.MinimumSize = new Size(0, 36);
        button.Cursor = Cursors.Hand;

        ApplyRoundedRegion(button, 7);

        var isHover = false;
        var isPressed = false;
        button.Paint += (sender, args) =>
        {
            var btn = (Button)sender!;
            var g = args.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(0, 0, btn.Width - 1, btn.Height - 1);
            var topColor = isPressed ? pressedColor : (isHover ? hoverColor : baseColor);
            var bottomColor = ControlPaint.Dark(topColor, 0.14F);

            using var path = CreateRoundedRectPath(rect, 7);
            using var brush = new LinearGradientBrush(
                rect, topColor, bottomColor, LinearGradientMode.Vertical);
            g.FillPath(brush, path);

            TextRenderer.DrawText(
                g, btn.Text, btn.Font, rect, btn.ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
        button.MouseDown += (_, _) => { isPressed = true; button.Invalidate(); };
        button.MouseUp += (_, _) => { isPressed = false; button.Invalidate(); };
        button.MouseEnter += (_, _) => { isHover = true; button.Invalidate(); };
        button.MouseLeave += (_, _) => { isHover = false; isPressed = false; button.Invalidate(); };
    }

    private static void StyleGhostButton(Button button)
    {
        var backColor = Color.White;
        var hoverColor = Color.FromArgb(241, 245, 249);
        var borderColor = Color.FromArgb(226, 232, 240);
        var hoverBorder = Color.FromArgb(15, 118, 110);
        var textColor = Color.FromArgb(15, 23, 42);
        var hoverText = Color.FromArgb(15, 118, 110);

        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = backColor;
        button.ForeColor = textColor;
        button.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        button.Padding = new Padding(14, 7, 14, 7);
        button.Margin = new Padding(0, 0, 10, 0);
        button.MinimumSize = new Size(0, 35);
        button.Cursor = Cursors.Hand;

        ApplyRoundedRegion(button, 6);

        var isHover = false;
        button.Paint += (sender, args) =>
        {
            var btn = (Button)sender!;
            var g = args.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(0, 0, btn.Width - 1, btn.Height - 1);
            using var path = CreateRoundedRectPath(rect, 6);

            using var bgBrush = new SolidBrush(isHover ? hoverColor : backColor);
            g.FillPath(bgBrush, path);

            using var pen = new Pen(isHover ? hoverBorder : borderColor, isHover ? 1.6F : 1F);
            g.DrawPath(pen, path);

            TextRenderer.DrawText(
                g, btn.Text, btn.Font, rect, isHover ? hoverText : textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
        button.MouseEnter += (_, _) => { isHover = true; button.Invalidate(); };
        button.MouseLeave += (_, _) => { isHover = false; button.Invalidate(); };
    }

    private static void StyleHeaderButton(Button button, bool emphasize, Color? accentColor = null)
    {
        var baseColor = accentColor ?? Color.FromArgb(15, 118, 110);
        var hoverColor = ControlPaint.Light(baseColor, 0.12F);

        var backColor = Color.White;
        var hoverBackColor = Color.FromArgb(241, 245, 249);
        var borderColor = Color.FromArgb(226, 232, 240);
        var textColor = Color.FromArgb(15, 23, 42);
        var subtleText = Color.FromArgb(100, 116, 139);

        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.Margin = new Padding(3, 6, 3, 6);
        button.Padding = new Padding(12, 6, 12, 6);
        button.MinimumSize = new Size(0, 36);
        button.Font = new Font("Segoe UI", 8.75F, emphasize ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
        button.ForeColor = emphasize ? Color.White : subtleText;
        button.BackColor = emphasize ? baseColor : backColor;
        button.Cursor = Cursors.Hand;

        ApplyRoundedRegion(button, emphasize ? 7 : 6);

        var isHover = false;
        button.Paint += (sender, args) =>
        {
            var btn = (Button)sender!;
            var g = args.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var radius = emphasize ? 7 : 6;
            var rect = new Rectangle(0, 0, btn.Width - 1, btn.Height - 1);
            using var path = CreateRoundedRectPath(rect, radius);

            if (emphasize)
            {
                var top = isHover ? hoverColor : baseColor;
                var bottom = ControlPaint.Dark(top, 0.14F);
                using var grad = new LinearGradientBrush(rect, top, bottom, LinearGradientMode.Vertical);
                g.FillPath(grad, path);
            }
            else
            {
                using var bg = new SolidBrush(isHover ? hoverBackColor : backColor);
                g.FillPath(bg, path);
                using var pen = new Pen(isHover ? baseColor : borderColor, 1);
                g.DrawPath(pen, path);
            }

            TextRenderer.DrawText(
                g, btn.Text, btn.Font, rect,
                emphasize ? Color.White : (isHover ? textColor : subtleText),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
        button.MouseEnter += (_, _) => { isHover = true; button.Invalidate(); };
        button.MouseLeave += (_, _) => { isHover = false; button.Invalidate(); };
    }

    private static void ApplyPanelChrome(Panel panel, Color backColor, Color borderColor)
    {
        panel.BackColor = backColor;
        EnableDoubleBuffering(panel);
        if (borderColor == Color.Transparent)
        {
            return;
        }

        panel.Paint += (_, args) =>
        {
            var g = args.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(0, 0, panel.Width - 1, panel.Height - 1);

            // 1px 描边
            using var pen = new Pen(borderColor, 1);
            g.DrawRectangle(pen, rect);

            // 底部 1px 极淡投影,模拟卡片浮起
            using var shadowPen = new Pen(Color.FromArgb(22, 100, 116, 139), 1);
            g.DrawLine(shadowPen, 1, panel.Height, panel.Width - 2, panel.Height);
        };
    }

    private static void StyleComboBox(ComboBox comboBox)
    {
        comboBox.FlatStyle = FlatStyle.Flat;
        comboBox.BackColor = Color.White;
        comboBox.ForeColor = Color.FromArgb(15, 23, 42);
        comboBox.Font = new Font("Segoe UI", 9F, GraphicsUnit.Point);
        comboBox.IntegralHeight = false;
        comboBox.DrawMode = DrawMode.OwnerDrawFixed;
        comboBox.ItemHeight = 26;
        comboBox.DrawItem += (sender, e) =>
        {
            if (e.Index < 0)
            {
                return;
            }

            var cb = (ComboBox)sender!;
            var isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            var bg = isSelected ? Color.FromArgb(204, 251, 241) : Color.White;
            var fg = Color.FromArgb(15, 23, 42);

            using var bgBrush = new SolidBrush(bg);
            e.Graphics.FillRectangle(bgBrush, e.Bounds);

            // 顶部主框显示时也走这里,用控件背景统一
            TextRenderer.DrawText(
                e.Graphics,
                cb.Items[e.Index]?.ToString() ?? string.Empty,
                e.Font,
                new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height),
                fg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        };
    }

    private static void StyleTextInput(TextBox textBox)
    {
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.BackColor = Color.FromArgb(248, 250, 252);
        textBox.ForeColor = Color.FromArgb(15, 23, 42);
        textBox.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
    }

    private static void EnableDoubleBuffering(Control control)
    {
        typeof(Control).InvokeMember(
            "DoubleBuffered",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.SetProperty,
            null,
            control,
            new object[] { true }
        );
    }

    private static GraphicsPath CreateRoundedRectPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return path;
        }

        var d = Math.Max(1, radius * 2);
        var arcW = Math.Min(d, rect.Width);
        var arcH = Math.Min(d, rect.Height);

        path.AddArc(rect.X, rect.Y, arcW, arcH, 180, 90);
        path.AddArc(rect.Right - arcW, rect.Y, arcW, arcH, 270, 90);
        path.AddArc(rect.Right - arcW, rect.Bottom - arcH, arcW, arcH, 0, 90);
        path.AddArc(rect.X, rect.Bottom - arcH, arcW, arcH, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void ApplyRoundedRegion(Control control, int radius)
    {
        void UpdateRegion()
        {
            if (control.Width <= 0 || control.Height <= 0)
            {
                return;
            }
            using var path = CreateRoundedRectPath(new Rectangle(0, 0, control.Width, control.Height), radius);
            control.Region = new Region(path);
        }

        UpdateRegion();
        control.SizeChanged += (_, _) => UpdateRegion();
    }

    private void AlignPreviewSearchControls()
    {
        cboConnections.Height = 36;

        cboPreviewField.Height = 36;
        cboPreviewMatch.Height = 36;
        txtPreviewKeyword.Height = 36;

        btnApplyPreviewFilter.AutoSize = false;
        btnApplyPreviewFilter.Height = 36;

        btnResetPreviewFilter.AutoSize = false;
        btnResetPreviewFilter.Height = 36;
    }

    private void ApplyDefaultSplitterDistance()
    {
        if (splitContainer.Width <= 0)
        {
            return;
        }

        splitContainer.Panel1MinSize = 220;
        splitContainer.Panel2MinSize = Math.Min(760, Math.Max(560, splitContainer.Width / 2));

        var target = Math.Min(240, Math.Max(220, splitContainer.Width / 4));
        var maxAllowed = splitContainer.Width - splitContainer.Panel2MinSize - splitContainer.SplitterWidth;
        if (maxAllowed < splitContainer.Panel1MinSize)
        {
            splitContainer.Panel2MinSize = Math.Max(420, splitContainer.Width - splitContainer.Panel1MinSize - splitContainer.SplitterWidth);
            maxAllowed = splitContainer.Width - splitContainer.Panel2MinSize - splitContainer.SplitterWidth;
        }

        splitContainer.SplitterDistance = Math.Max(splitContainer.Panel1MinSize, Math.Min(target, maxAllowed));
    }

    private void tabMain_DrawItem(object? sender, DrawItemEventArgs e)
    {
        var tabPage = tabMain.TabPages[e.Index];
        var bounds = e.Bounds;
        var selected = e.Index == tabMain.SelectedIndex;
        var hot = e.State == DrawItemState.HotLight;

        // Ocean Depths 浅色
        var backgroundColor = selected
            ? Color.White
            : hot
                ? Color.FromArgb(241, 245, 249)
                : Color.FromArgb(241, 245, 249);
        var textColor = selected ? Color.FromArgb(15, 118, 110) : Color.FromArgb(100, 116, 139);
        var accentColor = Color.FromArgb(15, 118, 110);

        using var background = new SolidBrush(backgroundColor);
        e.Graphics.FillRectangle(background, bounds);

        if (selected)
        {
            var accentBounds = new Rectangle(bounds.Left, bounds.Bottom - 3, bounds.Width, 3);
            using var accentBrush = new SolidBrush(accentColor);
            e.Graphics.FillRectangle(accentBrush, accentBounds);
        }

        using var selectedFont = selected ? new Font(tabMain.Font, FontStyle.Bold) : null;
        TextRenderer.DrawText(
            e.Graphics,
            tabPage.Text,
            selectedFont ?? tabMain.Font,
            bounds,
            hot && !selected ? Color.FromArgb(15, 23, 42) : textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
        );
    }

    private void btnNewConnection_Click(object? sender, EventArgs e)
    {
        OpenConnectionForm();
    }

    private void btnEditConnection_Click(object? sender, EventArgs e)
    {
        var selected = GetSelectedConnection();
        if (selected is null)
        {
            MessageBox.Show("请先选择连接。", "提示");
            return;
        }

        OpenConnectionForm(selected);
    }

    private void btnDeleteConnection_Click(object? sender, EventArgs e)
    {
        var selected = GetSelectedConnection();
        if (selected is null)
        {
            MessageBox.Show("请先选择连接。", "提示");
            return;
        }

        var result = MessageBox.Show(
            $"确定删除连接“{selected.Name}”吗？",
            "确认删除",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning
        );

        if (result != DialogResult.Yes)
        {
            return;
        }

        _configService.DeleteConnection(selected.Id);
        if (_currentConfig?.Id == selected.Id)
        {
            Disconnect();
        }

        LoadConnections();
    }

    private void btnTestConnection_Click(object? sender, EventArgs e)
    {
        TestSelectedConnection();
    }

    private void btnConnect_Click(object? sender, EventArgs e)
    {
        ConnectSelected();
    }

    private void btnRefresh_Click(object? sender, EventArgs e)
    {
        LoadConnections();
        LoadHistory();

        if (_currentConfig is not null)
        {
            SelectConnection(_currentConfig.Id);
            LoadTables();
        }
    }

    private void btnDisconnect_Click(object? sender, EventArgs e)
    {
        Disconnect();
    }

    private void treeTables_AfterSelect(object? sender, TreeViewEventArgs e)
    {
        if (e.Node is not null)
        {
            LoadColumnsForTable(e.Node.Text);
        }
    }

    private void btnRunSql_Click(object? sender, EventArgs e)
    {
        // 按钮已迁移至每个 QueryTabPage，此占位避免设计器订阅失败。
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Ctrl+Enter: 执行全部 SQL
        if (keyData == (Keys.Control | Keys.Enter))
        {
            var page = GetActiveQueryPage();
            if (page is not null)
            {
                RunSqlFor(page, preferSelection: false);
            }
            return true;
        }

        // Ctrl+E: 执行选中 SQL(无选中则执行全部)
        if (keyData == (Keys.Control | Keys.E))
        {
            var page = GetActiveQueryPage();
            if (page is not null)
            {
                RunSqlFor(page, preferSelection: true);
            }
            return true;
        }

        // Ctrl+Shift+F: 格式化当前查询页 SQL
        if (keyData == (Keys.Control | Keys.Shift | Keys.F))
        {
            var page = GetActiveQueryPage();
            if (page is not null)
            {
                FormatSqlFor(page);
            }
            return true;
        }

        // F5: 刷新表列表
        if (keyData == Keys.F5)
        {
            if (_currentConfig is not null)
            {
                LoadTables();
            }
            else
            {
                LoadConnections();
                LoadHistory();
            }
            return true;
        }

        // Escape: 清空搜索框
        if (keyData == Keys.Escape)
        {
            if (tabMain.SelectedTab == tabPreview)
            {
                txtPreviewKeyword.Clear();
                cboPreviewField.SelectedIndex = 0;
                cboPreviewMatch.SelectedIndex = 0;
            }
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void btnClearSql_Click(object? sender, EventArgs e)
    {
        // 按钮已迁移至每个 QueryTabPage。
    }

    private void btnCopySql_Click(object? sender, EventArgs e)
    {
        // 按钮已迁移至每个 QueryTabPage。
    }

    private void btnPrevPage_Click(object? sender, EventArgs e)
    {
        if (_currentPreviewPage <= 1)
        {
            return;
        }

        _currentPreviewPage--;
        LoadPreviewPage();
    }

    private void btnNextPage_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentPreviewTableName))
        {
            return;
        }

        _currentPreviewPage++;
        LoadPreviewPage();
    }

    private void btnApplyPreviewFilter_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentPreviewTableName))
        {
            MessageBox.Show("请先选择表。", "提示");
            return;
        }

        _currentPreviewPage = 1;
        LoadPreviewPage();
    }

    private void btnResetPreviewFilter_Click(object? sender, EventArgs e)
    {
        txtPreviewKeyword.Clear();
        cboPreviewField.SelectedIndex = 0;
        cboPreviewMatch.SelectedIndex = 0;
        _currentPreviewPage = 1;
        LoadPreviewPage();
    }

    private void gridHistory_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0)
        {
            return;
        }

        if (gridHistory.Rows[e.RowIndex].DataBoundItem is QueryHistoryItem item)
        {
            var page = GetActiveQueryPage() ?? AddQueryTab();
            page.TxtSql.Text = item.SqlText;
            tabMain.SelectedTab = tabSql;
        }
    }

    private void gridPreview_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
        {
            return;
        }

        var hit = gridPreview.HitTest(e.X, e.Y);

        if (hit.Type == DataGridViewHitTestType.ColumnHeader && hit.ColumnIndex >= 0)
        {
            _previewCopyText = gridPreview.Columns[hit.ColumnIndex].HeaderText;
            _previewCopyMenu.Show(gridPreview, new Point(e.X, e.Y));
            return;
        }

        if (hit.Type == DataGridViewHitTestType.Cell && hit.RowIndex >= 0 && hit.ColumnIndex >= 0)
        {
            var value = gridPreview.Rows[hit.RowIndex].Cells[hit.ColumnIndex].Value;
            _previewCopyText = value?.ToString() ?? string.Empty;
            _previewCopyMenu.Show(gridPreview, new Point(e.X, e.Y));
        }
    }

    private void btnExportResults_Click(object? sender, EventArgs e)
    {
        // 按钮已迁移至每个 QueryTabPage。
    }

    private void GridResults_SortCompare(object? sender, DataGridViewSortCompareEventArgs e)
    {
        if (decimal.TryParse(e.CellValue1?.ToString(), out var n1) &&
            decimal.TryParse(e.CellValue2?.ToString(), out var n2))
        {
            e.SortResult = n1.CompareTo(n2);
        }
        else
        {
            e.SortResult = string.Compare(
                e.CellValue1?.ToString(),
                e.CellValue2?.ToString(),
                StringComparison.Ordinal
            );
        }
        e.Handled = true;
    }

    private void btnExportPreview_Click(object? sender, EventArgs e)
    {
        ExportGridData(gridPreview, "数据预览");
    }

    private void ExportGridData(DataGridView grid, string defaultFileName)
    {
        if (grid.DataSource is not DataTable table || table.Rows.Count == 0)
        {
            MessageBox.Show("没有可导出的数据。", "提示");
            return;
        }

        using var dialog = new SaveFileDialog
        {
            FileName = $"{defaultFileName}_{DateTime.Now:yyyyMMdd_HHmmss}",
            Filter = "CSV 文件|*.csv|JSON 文件|*.json",
            DefaultExt = "csv"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var format = dialog.FilterIndex == 2
                ? DataExportService.ExportFormat.Json
                : DataExportService.ExportFormat.Csv;

            _dataExportService.Export(table, dialog.FileName, format);
            MessageBox.Show($"导出成功：{dialog.FileName}", "提示");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "导出失败");
        }
    }

    private void UpdateRowCountAsync(string tableName)
    {
        if (_currentConfig is null || _currentProvider is null)
        {
            return;
        }

        CancelRowCount();
        var cts = new CancellationTokenSource();
        _rowCountCts = cts;
        var token = cts.Token;
        var config = _currentConfig;
        var provider = _currentProvider;
        var password = GetPassword(config);

        lblRowCount.Text = "正在统计行数…";
        lblRowCount.ForeColor = SystemColors.ControlDark;

        Task.Run(() =>
        {
            long count;
            try
            {
                count = provider.GetRowCount(config, password, tableName);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Invoke(() =>
                {
                    if (token.IsCancellationRequested || _currentPreviewTableName != tableName)
                    {
                        return;
                    }

                    lblRowCount.Text = "行数统计失败";
                    lblRowCount.ForeColor = Color.Firebrick;
                    toolTip.SetToolTip(lblRowCount, ex.Message);
                });
                return;
            }

            Invoke(() =>
            {
                if (token.IsCancellationRequested || _currentPreviewTableName != tableName)
                {
                    return;
                }

                lblRowCount.Text = $"共 {count:N0} 行";
                lblRowCount.ForeColor = Color.FromArgb(2, 132, 199);  // Sky-600 link color
                toolTip.SetToolTip(lblRowCount, string.Empty);
            });
        }, token);
    }

    private void CancelRowCount()
    {
        if (_rowCountCts is null)
        {
            return;
        }

        _rowCountCts.Cancel();
        _rowCountCts.Dispose();
        _rowCountCts = null;
    }

    private DbLiteDesktop.Controls.QueryTabPage AddQueryTab(string? sql = null)
    {
        _queryTabCounter++;
        var page = new DbLiteDesktop.Controls.QueryTabPage($"查询 {_queryTabCounter}");

        page.RunSqlRequested += (_, _) => RunSqlFor(page, preferSelection: true);
        page.ClearSqlRequested += (_, _) => page.TxtSql.Clear();
        page.CopySqlRequested += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(page.TxtSql.Text))
            {
                Clipboard.SetText(page.TxtSql.Text);
            }
        };
        page.FormatSqlRequested += (_, _) => FormatSqlFor(page);
        page.ExportResultsRequested += (_, _) => ExportGridData(page.GridResults, "查询结果");
        page.GridResults.SortCompare += GridResults_SortCompare;

        var tabPage = new TabPage(page.Title);
        tabPage.UseVisualStyleBackColor = true;
        tabPage.Controls.Add(page);
        queryTabs.TabPages.Add(tabPage);

        StyleQueryTabPage(page);

        page.TxtSql.CompletionProvider = BuildCompletionItems;
        page.TxtSql.PlaceholderText = "请输入只读 SQL，例如：SELECT * FROM your_table LIMIT 100";

        if (!string.IsNullOrEmpty(sql))
        {
            page.TxtSql.Text = sql;
        }

        queryTabs.SelectedTab = tabPage;
        _ = page.TxtSql.Focus();
        return page;
    }

    private void StyleQueryTabPage(DbLiteDesktop.Controls.QueryTabPage page)
    {
        if (!_themeApplied)
        {
            return;
        }

        var chromeBackColor = Color.FromArgb(241, 245, 249);
        var borderColor = Color.FromArgb(226, 232, 240);
        var accentColor = Color.FromArgb(15, 118, 110);
        var runColor = Color.FromArgb(22, 163, 74);  // Green-600 执行按钮

        page.TxtSql.ApplyTheme();
        page.TxtSql.PlaceholderText = "请输入只读 SQL，例如：SELECT * FROM your_table LIMIT 100";
        StyleGrid(page.GridResults);
        StyleActionButton(page.BtnRunSql, runColor);  // 执行用绿色,跟 Teal 主按钮形成分工
        StyleGhostButton(page.BtnFormatSql);
        StyleGhostButton(page.BtnClearSql);
        StyleGhostButton(page.BtnCopySql);
        StyleGhostButton(page.BtnExportResults);
        ApplyPanelChrome(page.ButtonPanel, chromeBackColor, borderColor);

        page.BackColor = Color.White;
        page.LblStatus.BackColor = chromeBackColor;
        page.LblStatus.ForeColor = Color.FromArgb(100, 116, 139);
        page.LblStatus.Font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        page.LblStatus.Padding = new Padding(16, 0, 0, 0);
        page.LblStatus.BorderStyle = BorderStyle.None;
    }

    private DbLiteDesktop.Controls.QueryTabPage? GetQueryPage(TabPage? tabPage)
    {
        return tabPage?.Controls.OfType<DbLiteDesktop.Controls.QueryTabPage>().FirstOrDefault();
    }

    private DbLiteDesktop.Controls.QueryTabPage? GetActiveQueryPage()
    {
        return GetQueryPage(queryTabs.SelectedTab);
    }

    private IEnumerable<DbLiteDesktop.Controls.QueryTabPage> EnumerateQueryPages()
    {
        foreach (TabPage tabPage in queryTabs.TabPages)
        {
            var page = GetQueryPage(tabPage);
            if (page is not null)
            {
                yield return page;
            }
        }
    }

    private List<string> BuildCompletionItems()
    {
        var items = new List<string>();
        foreach (TreeNode node in treeTables.Nodes)
        {
            items.Add(node.Text);
        }
        items.AddRange(_currentPreviewColumns);
        return items;
    }

    private void btnAddQueryTab_Click(object? sender, EventArgs e)
    {
        AddQueryTab();
    }

    private void ApplyTableFilter()
    {
        var keyword = txtTableSearch.Text.Trim();
        treeTables.BeginUpdate();
        try
        {
            treeTables.Nodes.Clear();
            foreach (var table in _allTables)
            {
                if (string.IsNullOrEmpty(keyword)
                    || table.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    treeTables.Nodes.Add(table);
                }
            }
        }
        finally
        {
            treeTables.EndUpdate();
        }
    }

    private void txtTableSearch_TextChanged(object? sender, EventArgs e)
    {
        ApplyTableFilter();
    }

    private void FormatSqlFor(DbLiteDesktop.Controls.QueryTabPage page)
    {
        if (string.IsNullOrWhiteSpace(page.TxtSql.Text))
        {
            return;
        }

        var source = page.TxtSql.SelectionLength > 0
            ? page.TxtSql.SelectedText
            : page.TxtSql.Text;

        var formatted = DbLiteDesktop.Utils.SqlFormatter.Format(source);
        if (string.IsNullOrEmpty(formatted))
        {
            return;
        }

        if (page.TxtSql.SelectionLength > 0)
        {
            page.TxtSql.SelectedText = formatted;
        }
        else
        {
            page.TxtSql.Text = formatted;
        }
    }

    private const int TabCloseButtonSize = 16;
    private const int TabCloseButtonRightMargin = 8;

    private static Rectangle GetTabCloseButtonRect(Rectangle tabBounds)
    {
        var x = tabBounds.Right - TabCloseButtonSize - TabCloseButtonRightMargin;
        var y = tabBounds.Y + (tabBounds.Height - TabCloseButtonSize) / 2;
        return new Rectangle(x, y, TabCloseButtonSize, TabCloseButtonSize);
    }

    private void QueryTabs_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= queryTabs.TabPages.Count)
        {
            return;
        }

        var tabPage = queryTabs.TabPages[e.Index];
        var bounds = e.Bounds;
        var selected = e.Index == queryTabs.SelectedIndex;

        // Ocean Depths 浅色
        var bgColor = selected
            ? Color.White
            : Color.FromArgb(241, 245, 249);
        using var bg = new SolidBrush(bgColor);
        e.Graphics.FillRectangle(bg, bounds);

        if (selected)
        {
            var accentBounds = new Rectangle(bounds.Left, bounds.Bottom - 3, bounds.Width, 3);
            using var accentBrush = new SolidBrush(Color.FromArgb(15, 118, 110));
            e.Graphics.FillRectangle(accentBrush, accentBounds);
        }

        var textColor = selected ? Color.FromArgb(15, 23, 42) : Color.FromArgb(100, 116, 139);
        using var font = new Font("Segoe UI", 9F, selected ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
        var textRect = new Rectangle(bounds.X + 12, bounds.Y, bounds.Width - TabCloseButtonSize - TabCloseButtonRightMargin - 12, bounds.Height);
        TextRenderer.DrawText(e.Graphics, tabPage.Text, font, textRect, textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        var isLastTab = queryTabs.TabPages.Count <= 1;
        var closeRect = GetTabCloseButtonRect(bounds);
        var isHot = _hoveredCloseIndex == e.Index && !isLastTab;

        if (isHot)
        {
            using var hoverBg = new SolidBrush(Color.FromArgb(255, 235, 235));
            e.Graphics.FillRectangle(hoverBg, closeRect);
        }

        var closeColor = isLastTab
            ? Color.FromArgb(200, 210, 220)
            : isHot
                ? Color.FromArgb(220, 60, 60)
                : Color.FromArgb(120, 135, 150);

        using var closeFont = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Point);
        TextRenderer.DrawText(e.Graphics, "×", closeFont, closeRect, closeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void QueryTabs_MouseClick(object? sender, MouseEventArgs e)
    {
        for (var i = 0; i < queryTabs.TabPages.Count; i++)
        {
            var rect = queryTabs.GetTabRect(i);
            if (!rect.Contains(e.Location))
            {
                continue;
            }

            if (e.Button == MouseButtons.Middle)
            {
                CloseQueryTab(i);
                return;
            }

            if (e.Button == MouseButtons.Left && GetTabCloseButtonRect(rect).Contains(e.Location))
            {
                CloseQueryTab(i);
                return;
            }

            if (e.Button == MouseButtons.Right)
            {
                ShowQueryTabContextMenu(i, e.Location);
                return;
            }

            return;
        }
    }

    private void QueryTabs_MouseMove(object? sender, MouseEventArgs e)
    {
        var newIndex = -1;
        for (var i = 0; i < queryTabs.TabPages.Count; i++)
        {
            var rect = queryTabs.GetTabRect(i);
            if (rect.Contains(e.Location) && GetTabCloseButtonRect(rect).Contains(e.Location))
            {
                newIndex = i;
                break;
            }
        }

        if (_hoveredCloseIndex != newIndex)
        {
            _hoveredCloseIndex = newIndex;
            queryTabs.Invalidate();
        }

        queryTabs.Cursor = newIndex >= 0 && queryTabs.TabPages.Count > 1 ? Cursors.Hand : Cursors.Default;
    }

    private void QueryTabs_MouseLeave(object? sender, EventArgs e)
    {
        if (_hoveredCloseIndex != -1)
        {
            _hoveredCloseIndex = -1;
            queryTabs.Invalidate();
        }
        queryTabs.Cursor = Cursors.Default;
    }

    private void QueryTabs_DoubleClick(object? sender, EventArgs e)
    {
        var pos = queryTabs.PointToClient(Cursor.Position);
        for (var i = 0; i < queryTabs.TabPages.Count; i++)
        {
            var rect = queryTabs.GetTabRect(i);
            if (rect.Contains(pos) && !GetTabCloseButtonRect(rect).Contains(pos))
            {
                CloseQueryTab(i);
                return;
            }
        }
    }

    private void CloseQueryTab(int index)
    {
        if (index < 0 || index >= queryTabs.TabPages.Count)
        {
            return;
        }

        if (queryTabs.TabPages.Count <= 1)
        {
            return;
        }

        var tabPage = queryTabs.TabPages[index];
        var page = GetQueryPage(tabPage);
        if (page is not null)
        {
            page.GridResults.DataSource = null;
            page.TxtSql.Clear();
        }

        var wasSelected = queryTabs.SelectedIndex == index;
        queryTabs.TabPages.RemoveAt(index);

        if (wasSelected && queryTabs.TabPages.Count > 0)
        {
            var newIndex = Math.Min(index, queryTabs.TabPages.Count - 1);
            queryTabs.SelectedIndex = newIndex;
        }
    }

    private void CloseOtherQueryTabs(int keepIndex)
    {
        if (keepIndex < 0 || keepIndex >= queryTabs.TabPages.Count)
        {
            return;
        }

        var keepPage = GetQueryPage(queryTabs.TabPages[keepIndex]);
        for (var i = queryTabs.TabPages.Count - 1; i >= 0; i--)
        {
            if (i == keepIndex)
            {
                continue;
            }

            var page = GetQueryPage(queryTabs.TabPages[i]);
            if (page is not null)
            {
                page.GridResults.DataSource = null;
                page.TxtSql.Clear();
            }
            queryTabs.TabPages.RemoveAt(i);
        }

        if (keepPage is not null)
        {
            queryTabs.SelectedTab = queryTabs.TabPages[0];
        }
    }

    private void CloseLeftQueryTabs(int keepIndex)
    {
        if (keepIndex <= 0 || keepIndex >= queryTabs.TabPages.Count)
        {
            return;
        }

        for (var i = keepIndex - 1; i >= 0; i--)
        {
            var page = GetQueryPage(queryTabs.TabPages[i]);
            if (page is not null)
            {
                page.GridResults.DataSource = null;
                page.TxtSql.Clear();
            }
            queryTabs.TabPages.RemoveAt(i);
        }

        queryTabs.SelectedIndex = 0;
    }

    private void CloseRightQueryTabs(int keepIndex)
    {
        if (keepIndex < 0 || keepIndex >= queryTabs.TabPages.Count)
        {
            return;
        }

        for (var i = queryTabs.TabPages.Count - 1; i > keepIndex; i--)
        {
            var page = GetQueryPage(queryTabs.TabPages[i]);
            if (page is not null)
            {
                page.GridResults.DataSource = null;
                page.TxtSql.Clear();
            }
            queryTabs.TabPages.RemoveAt(i);
        }

        queryTabs.SelectedIndex = keepIndex;
    }

    private void CloseAllQueryTabs()
    {
        foreach (TabPage tabPage in queryTabs.TabPages)
        {
            var page = GetQueryPage(tabPage);
            if (page is not null)
            {
                page.GridResults.DataSource = null;
                page.TxtSql.Clear();
            }
        }
        queryTabs.TabPages.Clear();
        AddQueryTab();
    }

    private void ShowQueryTabContextMenu(int index, Point location)
    {
        _contextMenuQueryTabIndex = index;
        var canClose = queryTabs.TabPages.Count > 1;

        var menu = new ContextMenuStrip
        {
            BackColor = Color.White,
            ForeColor = Color.FromArgb(15, 23, 42),
            Font = new Font("Segoe UI", 9F, GraphicsUnit.Point),
            ShowImageMargin = false,
            RenderMode = ToolStripRenderMode.ManagerRenderMode,
            Renderer = new LightMenuRenderer(),
        };

        var itemNew = new ToolStripMenuItem("新建查询");
        itemNew.Click += (_, _) => AddQueryTab();
        menu.Items.Add(itemNew);

        menu.Items.Add(new ToolStripSeparator());

        var itemClose = new ToolStripMenuItem("关闭");
        itemClose.Enabled = canClose;
        itemClose.Click += (_, _) =>
        {
            if (_contextMenuQueryTabIndex >= 0)
            {
                CloseQueryTab(_contextMenuQueryTabIndex);
            }
        };
        menu.Items.Add(itemClose);

        var itemCloseOthers = new ToolStripMenuItem("关闭其他");
        itemCloseOthers.Enabled = canClose;
        itemCloseOthers.Click += (_, _) =>
        {
            if (_contextMenuQueryTabIndex >= 0)
            {
                CloseOtherQueryTabs(_contextMenuQueryTabIndex);
            }
        };
        menu.Items.Add(itemCloseOthers);

        var itemCloseLeft = new ToolStripMenuItem("关闭左侧所有");
        itemCloseLeft.Enabled = index > 0;
        itemCloseLeft.Click += (_, _) =>
        {
            if (_contextMenuQueryTabIndex >= 0)
            {
                CloseLeftQueryTabs(_contextMenuQueryTabIndex);
            }
        };
        menu.Items.Add(itemCloseLeft);

        var itemCloseRight = new ToolStripMenuItem("关闭右侧所有");
        itemCloseRight.Enabled = index < queryTabs.TabPages.Count - 1;
        itemCloseRight.Click += (_, _) =>
        {
            if (_contextMenuQueryTabIndex >= 0)
            {
                CloseRightQueryTabs(_contextMenuQueryTabIndex);
            }
        };
        menu.Items.Add(itemCloseRight);

        var itemCloseAll = new ToolStripMenuItem("关闭所有");
        itemCloseAll.Click += (_, _) => CloseAllQueryTabs();
        menu.Items.Add(itemCloseAll);

        foreach (ToolStripItem item in menu.Items)
        {
            item.BackColor = Color.White;
            item.ForeColor = Color.FromArgb(15, 23, 42);
            item.Margin = new Padding(8, 4, 8, 4);
        }

        menu.Closed += (_, _) => { _hoveredCloseIndex = -1; queryTabs.Invalidate(); };
        menu.Show(queryTabs, location);
    }
}

internal sealed class LightMenuRenderer : ToolStripProfessionalRenderer
{
    public LightMenuRenderer() : base(new LightMenuColorTable())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var rc = new Rectangle(Point.Empty, e.Item.Size);
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        if (e.Item.Selected || e.Item.Pressed)
        {
            using var brush = new SolidBrush(Color.FromArgb(204, 251, 241));
            g.FillRectangle(brush, rc);
        }
        else
        {
            using var brush = new SolidBrush(Color.White);
            g.FillRectangle(brush, rc);
        }
    }
}

internal sealed class LightMenuColorTable : ProfessionalColorTable
{
    public override Color MenuBorder => Color.FromArgb(226, 232, 240);
    public override Color MenuItemBorder => Color.Transparent;
    public override Color MenuItemSelected => Color.FromArgb(204, 251, 241);
    public override Color MenuItemSelectedGradientBegin => Color.FromArgb(204, 251, 241);
    public override Color MenuItemSelectedGradientEnd => Color.FromArgb(204, 251, 241);
    public override Color MenuItemPressedGradientBegin => Color.FromArgb(204, 251, 241);
    public override Color MenuItemPressedGradientEnd => Color.FromArgb(204, 251, 241);
    public override Color MenuStripGradientBegin => Color.White;
    public override Color MenuStripGradientEnd => Color.White;
    public override Color ToolStripBorder => Color.Transparent;
    public override Color ToolStripContentPanelGradientBegin => Color.White;
    public override Color ToolStripContentPanelGradientEnd => Color.White;
    public override Color ToolStripPanelGradientBegin => Color.White;
    public override Color ToolStripPanelGradientEnd => Color.White;
    public override Color SeparatorDark => Color.FromArgb(226, 232, 240);
    public override Color SeparatorLight => Color.FromArgb(241, 245, 249);
    public override Color CheckBackground => Color.FromArgb(204, 251, 241);
    public override Color CheckPressedBackground => Color.FromArgb(204, 251, 241);
    public override Color CheckSelectedBackground => Color.FromArgb(204, 251, 241);
    public override Color ImageMarginGradientBegin => Color.White;
    public override Color ImageMarginGradientMiddle => Color.White;
    public override Color ImageMarginGradientEnd => Color.White;
}
