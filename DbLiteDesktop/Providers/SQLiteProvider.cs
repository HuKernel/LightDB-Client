using System.Data;
using DbLiteDesktop.Models;
using DbLiteDesktop.Services;
using DbLiteDesktop.Utils;
using Microsoft.Data.Sqlite;

namespace DbLiteDesktop.Providers;

public class SQLiteProvider : IDatabaseProvider
{
    private static readonly Func<string, string> Quote = IdentifierQuoteHelper.QuoteSQLite;
    private SqliteCommand? _currentCommand;

    public void CancelQuery()
    {
        try { _currentCommand?.Cancel(); } catch { /* ignored */ }
    }

    public bool TestConnection(DbConnectionConfig config, string password)
    {
        using var connection = CreateConnection(config);
        connection.Open();
        return true;
    }

    public List<string> GetTables(DbConnectionConfig config, string password)
    {
        using var connection = CreateConnection(config);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table'
              AND name NOT LIKE 'sqlite_%'
            ORDER BY name;
            """;

        using var reader = command.ExecuteReader();
        var items = new List<string>();

        while (reader.Read())
        {
            items.Add(reader.GetString(0));
        }

        return items;
    }

    public List<TableColumnInfo> GetColumns(DbConnectionConfig config, string password, string tableName)
    {
        using var connection = CreateConnection(config);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({Quote(tableName)});";

        using var reader = command.ExecuteReader();
        var items = new List<TableColumnInfo>();

        while (reader.Read())
        {
            items.Add(new TableColumnInfo
            {
                Name = reader["name"]?.ToString() ?? string.Empty,
                Type = reader["type"]?.ToString() ?? string.Empty,
                Nullable = reader["notnull"]?.ToString() == "1" ? "NO" : "YES",
                Key = reader["pk"]?.ToString() == "1" ? "PRI" : string.Empty,
                DefaultValue = reader["dflt_value"]?.ToString(),
                Extra = string.Empty,
                Comment = string.Empty
            });
        }

        return items;
    }

    public DataTable ExecuteQuery(DbConnectionConfig config, string password, string sql, int maxRows = 1000)
    {
        if (!SqlGuardService.IsReadonlySql(sql))
        {
            throw new InvalidOperationException("当前工具只允许执行只读 SQL");
        }

        using var connection = CreateConnection(config);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = config.CommandTimeoutSec ?? 30;

        using var reader = command.ExecuteReader();
        var table = new DataTable();
        table.Load(reader);
        ProviderHelper.TrimRows(table, maxRows);
        return table;
    }

    public List<DataTable> ExecuteQueryMultiple(DbConnectionConfig config, string password, string sql, int maxRows = 1000)
    {
        if (!SqlGuardService.IsReadonlySql(sql))
        {
            throw new InvalidOperationException("当前工具只允许执行只读 SQL");
        }

        using var connection = CreateConnection(config);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = config.CommandTimeoutSec ?? 30;
        _currentCommand = command;
        try
        {
            using var reader = command.ExecuteReader();
            return ProviderHelper.LoadAllTables(reader, maxRows);
        }
        finally
        {
            _currentCommand = null;
        }
    }

    public string BuildPreviewSql(string tableName, int limit = 100) =>
        ProviderHelper.BuildPreviewSql(tableName, Quote, limit);

    public long GetRowCount(DbConnectionConfig config, string password, string tableName)
    {
        using var connection = CreateConnection(config);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {Quote(tableName)};";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    public string BuildPagedPreviewSql(string tableName, int page, int pageSize) =>
        ProviderHelper.BuildPagedPreviewSql(tableName, page, pageSize, Quote);

    public string BuildFilteredPreviewSql(
        string tableName,
        IReadOnlyList<string> columns,
        string? selectedColumn,
        string keyword,
        bool exactMatch,
        int page,
        int pageSize
    ) => ProviderHelper.BuildFilteredPreviewSql(tableName, columns, selectedColumn, keyword, exactMatch, page, pageSize, Quote, "TEXT");

    private static SqliteConnection CreateConnection(DbConnectionConfig config)
    {
        return new($"Data Source={config.FilePath}");
    }
}
