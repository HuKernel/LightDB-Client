using System.Data;
using DbLiteDesktop.Models;
using DbLiteDesktop.Services;
using DbLiteDesktop.Utils;
using Npgsql;

namespace DbLiteDesktop.Providers;

public class PostgresProvider : IDatabaseProvider
{
    private static readonly Func<string, string> Quote = IdentifierQuoteHelper.QuotePostgres;
    private NpgsqlCommand? _currentCommand;

    public void CancelQuery()
    {
        try { _currentCommand?.Cancel(); } catch { /* ignored */ }
    }

    public bool TestConnection(DbConnectionConfig config, string password)
    {
        using var connection = CreateConnection(config, password);
        connection.Open();

        using var command = new NpgsqlCommand("SELECT 1;", connection);
        return command.ExecuteScalar() is not null;
    }

    public List<string> GetTables(DbConnectionConfig config, string password)
    {
        using var connection = CreateConnection(config, password);
        connection.Open();

        using var command = new NpgsqlCommand(
            """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_type = 'BASE TABLE'
            ORDER BY table_name;
            """,
            connection
        );
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
        using var connection = CreateConnection(config, password);
        connection.Open();

        using var command = new NpgsqlCommand(
            """
            SELECT column_name, data_type, is_nullable, column_default,
                   col_description((table_schema || '.' || table_name)::regclass, ordinal_position) AS comment
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = $1
            ORDER BY ordinal_position;
            """,
            connection
        );
        command.Parameters.AddWithValue(tableName);

        using var reader = command.ExecuteReader();
        var items = new List<TableColumnInfo>();

        while (reader.Read())
        {
            items.Add(new TableColumnInfo
            {
                Name = reader["column_name"]?.ToString() ?? string.Empty,
                Type = reader["data_type"]?.ToString() ?? string.Empty,
                Nullable = reader["is_nullable"]?.ToString() ?? string.Empty,
                DefaultValue = reader["column_default"]?.ToString(),
                Extra = string.Empty,
                Comment = reader["comment"]?.ToString()
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

        using var connection = CreateConnection(config, password);
        connection.Open();

        using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = config.CommandTimeoutSec ?? 30
        };

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

        using var connection = CreateConnection(config, password);
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

    public long GetRowCount(DbConnectionConfig config, string password, string tableName)
    {
        using var connection = CreateConnection(config, password);
        connection.Open();

        using var command = new NpgsqlCommand(
            $"SELECT COUNT(*) FROM {Quote(tableName)};",
            connection
        );
        return Convert.ToInt64(command.ExecuteScalar());
    }

    public string BuildPreviewSql(string tableName, int limit = 100) =>
        ProviderHelper.BuildPreviewSql(tableName, Quote, limit);

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
    ) => ProviderHelper.BuildFilteredPreviewSql(tableName, columns, selectedColumn, keyword, exactMatch, page, pageSize, Quote, "TEXT", "ILIKE");

    private static NpgsqlConnection CreateConnection(DbConnectionConfig config, string password)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = config.Host ?? "localhost",
            Port = config.Port ?? 5432,
            Database = config.DatabaseName,
            Username = config.Username,
            Password = password,
            Timeout = config.ConnectionTimeoutSec ?? 10,
            CommandTimeout = config.CommandTimeoutSec ?? 30,
            Pooling = true,
            MinPoolSize = 1,
            MaxPoolSize = 10
        };

        return new(builder.ConnectionString);
    }
}
