using System.Data;

namespace DbLiteDesktop.Controls;

public sealed class QueryResultsContainer : UserControl
{
    private readonly TabControl _tabs = new();
    private DataGridView _primary = null!;
    private DataGridView _active = null!;
    private Action<DataGridView>? _styleAction;

    public event DataGridViewSortCompareEventHandler? SortCompare;

    public DataGridView ActiveGrid => _active;
    public int ResultCount => _tabs.TabPages.Count;

    public IReadOnlyList<DataGridView> AllGrids
    {
        get
        {
            var list = new List<DataGridView>(_tabs.TabPages.Count);
            foreach (TabPage tp in _tabs.TabPages)
            {
                foreach (Control c in tp.Controls)
                {
                    if (c is DataGridView g)
                    {
                        list.Add(g);
                    }
                }
            }
            return list;
        }
    }

    public QueryResultsContainer()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.White;

        _tabs.Dock = DockStyle.Fill;
        _tabs.Padding = new Point(10, 4);
        _tabs.Alignment = TabAlignment.Top;
        _tabs.SizeMode = TabSizeMode.Normal;
        Controls.Add(_tabs);

        _primary = CreateGrid();
        ResetToSingle();

        _tabs.SelectedIndexChanged += (_, _) =>
        {
            if (_tabs.SelectedTab?.Controls.Count > 0
                && _tabs.SelectedTab.Controls[0] is DataGridView g)
            {
                _active = g;
            }
        };
    }

    public DataGridView CreateGrid()
    {
        var g = new DataGridView();
        ConfigureGrid(g);
        _styleAction?.Invoke(g);
        return g;
    }

    public void ResetToSingle()
    {
        _tabs.TabPages.Clear();
        var tp = new TabPage("结果");
        tp.Controls.Add(_primary);
        _tabs.TabPages.Add(tp);
        _active = _primary;
    }

    public void SetResults(IList<DataTable> tables)
    {
        if (tables.Count == 0)
        {
            ResetToSingle();
            _primary.DataSource = null;
            return;
        }

        if (tables.Count == 1)
        {
            ResetToSingle();
            _primary.DataSource = tables[0];
            _active = _primary;
            return;
        }

        _tabs.TabPages.Clear();
        for (var i = 0; i < tables.Count; i++)
        {
            var grid = CreateGrid();
            grid.DataSource = tables[i];

            var tp = new TabPage($"结果 {i + 1} · {tables[i].Rows.Count} 行");
            tp.Controls.Add(grid);
            _tabs.TabPages.Add(tp);
        }
        _active = (DataGridView)_tabs.TabPages[0]!.Controls[0];
    }

    public void Clear()
    {
        ResetToSingle();
        _primary.DataSource = null;
    }

    public void SetStyle(Action<DataGridView> style)
    {
        _styleAction = style;
        foreach (var g in AllGrids)
        {
            style(g);
        }
    }

    public void AutoResizeAll()
    {
        foreach (var g in AllGrids)
        {
            g.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);
        }
    }

    private void ConfigureGrid(DataGridView g)
    {
        g.AllowUserToAddRows = false;
        g.AllowUserToDeleteRows = false;
        g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        g.Dock = DockStyle.Fill;
        g.ReadOnly = true;
        g.RowHeadersVisible = false;
        g.AllowUserToOrderColumns = true;
        g.BackgroundColor = Color.White;
        g.SortCompare += (s, e) => SortCompare?.Invoke(s, e);
    }
}
