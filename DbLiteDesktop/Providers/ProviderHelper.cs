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
            results.Add(table);
        } while (!reader.IsClosed && reader.NextResult());
        return results;
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
