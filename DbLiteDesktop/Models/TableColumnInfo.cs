namespace DbLiteDesktop.Models;

public class TableColumnInfo
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Nullable { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string? DefaultValue { get; set; }
    public string? Extra { get; set; }
    public string? Comment { get; set; }

    public bool IsVector { get; set; }
    public int VectorDimension { get; set; }
}
