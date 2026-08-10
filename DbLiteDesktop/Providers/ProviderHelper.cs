using System.Data;
using System.Data.Common;

namespace DbLiteDesktop.Providers;

/// <summary>
/// Shared helpers for database providers — eliminates duplicated TrimRows and SQL-building logic.
/// </summary>
public static class ProviderHelper
{
    public static void TrimRows(DataTable table, int maxRows)
    {
        while (table.Rows.Count > maxRows)
        {
            table.Rows.RemoveAt(table.Rows.Count - 1);
        }
    }

    public static List<DataTable> LoadAllTables(DbDataReader reader, int maxRows)
    {
        var results = new List<DataTable>();
        do
        {
            var table = new DataTable();
            table.Load(reader);
            TrimRows(table, maxRows);
            TruncateVectorCells(table);
            results.Add(table);
        } while (!reader.IsClosed && reader.NextResult());
        return results;
    }

    /// <summary>
    /// 将形如 [0.1,0.2,...] 的超长向量文本截断为 "[0.1, 0.2, 0.3, …] · N 维",
    /// 避免高维向量撑爆网格显示与列宽计算。
    /// </summary>
    public static void TruncateVectorCells(DataTable table)
    {
        foreach (DataColumn column in table.Columns)
        {
            if (column.DataType != typeof(string))
            {
                continue;
            }

            foreach (DataRow row in table.Rows)
            {
                if (row[column] is string value)
                {
                    row[column] = TruncateVectorText(value);
                }
            }
        }
    }

    public static string TruncateVectorText(string value)
    {
        if (value.Length < 200 || value[0] != '[' || value[^1] != ']')
        {
            return value;
        }

        var dims = 1;
        var thirdComma = -1;
        for (var i = 1; i < value.Length - 1; i++)
        {
            if (value[i] != ',')
            {
                continue;
            }
            dims++;
            if (dims == 4)
            {
                thirdComma = i;
                break;
            }
        }

        var head = thirdComma > 0 ? value[..thirdComma] : value[..^1];
        return $"{head}, …] · {dims} 维";
    }

    public static string BuildPreviewSql(
        string tableName, Func<string, string> quote, int limit = 100)
    {
        return $"SELECT * FROM {quote(tableName)} LIMIT {limit};";
    }

    public static string BuildPagedPreviewSql(
        string tableName, int page, int pageSize, Func<string, string> quote)
    {
        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Max(pageSize, 1);
        var offset = (safePage - 1) * safePageSize;
        return $"SELECT * FROM {quote(tableName)} LIMIT {safePageSize} OFFSET {offset};";
    }

    public static string BuildFilteredPreviewSql(
        string tableName,
        IReadOnlyList<string> columns,
        string? selectedColumn,
        string keyword,
        bool exactMatch,
        int page,
        int pageSize,
        Func<string, string> quote,
        string castType,
        string likeKeyword = "LIKE")
    {
        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Max(pageSize, 1);
        var offset = (safePage - 1) * safePageSize;
        var escaped = keyword.Replace("'", "''");
        var op = exactMatch ? "=" : likeKeyword;
        var val = exactMatch ? $"'{escaped}'" : $"'%{escaped}%'";

        var targets = string.IsNullOrWhiteSpace(selectedColumn)
            ? columns
            : columns.Where(c => string.Equals(c, selectedColumn, StringComparison.OrdinalIgnoreCase)).ToList();

        var conditions = targets
            .Select(c => $"CAST({quote(c)} AS {castType}) {op} {val}");

        return $"SELECT * FROM {quote(tableName)} WHERE {string.Join(" OR ", conditions)} LIMIT {safePageSize} OFFSET {offset};";
    }
}
