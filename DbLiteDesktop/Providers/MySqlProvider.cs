using System.Data;
using DbLiteDesktop.Models;
using DbLiteDesktop.Services;
using DbLiteDesktop.Utils;
using MySqlConnector;

namespace DbLiteDesktop.Providers;

public class MySqlProvider : IDatabaseProvider
{
    private static readonly Func<string, string> Quote = IdentifierQuoteHelper.QuoteMySql;
    private MySqlCommand? _currentCommand;

    public void CancelQuery()
    {
        try { _currentCommand?.Cancel(); } catch { /* ignored */ }
    }

    public bool TestConnection(DbConnectionConfig config, string password)
    {
        using var connection = CreateConnection(config, password);
        connection.Open();

        using var command = new MySqlCommand("SELECT 1;", connection);
        return command.ExecuteScalar() is not null;
    }

    public List<string> GetTables(DbConnectionConfig config, string password)
    {
        using var connection = CreateConnection(config, password);
        connection.Open();

        using var command = new MySqlCommand("SHOW TABLES;", connection);
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

        using var command = new MySqlCommand(
            $"SHOW FULL COLUMNS FROM {Quote(tableName)};",
            connection
        );
        using var reader = command.ExecuteReader();
        var items = new List<TableColumnInfo>();

        while (reader.Read())
        {
            items.Add(new TableColumnInfo
            {
                Name = reader["Field"]?.ToString() ?? string.Empty,
                Type = reader["Type"]?.ToString() ?? string.Empty,
                Nullable = reader["Null"]?.ToString() ?? string.Empty,
                Key = reader["Key"]?.ToString() ?? string.Empty,
                DefaultValue = reader["Default"]?.ToString(),
                Extra = reader["Extra"]?.ToString(),
                Comment = reader["Comment"]?.ToString()
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

        using var command = new MySqlCommand(sql, connection)
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

    public string BuildPreviewSql(string tableName, int limit = 100) =>
        ProviderHelper.BuildPreviewSql(tableName, Quote, limit);

    public long GetRowCount(DbConnectionConfig config, string password, string tableName)
    {
        using var connection = CreateConnection(config, password);
        connection.Open();

        using var command = new MySqlCommand(
            $"SELECT COUNT(*) FROM {Quote(tableName)};",
            connection
        );
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
    ) => ProviderHelper.BuildFilteredPreviewSql(tableName, columns, selectedColumn, keyword, exactMatch, page, pageSize, Quote, "CHAR");

    private static MySqlConnection CreateConnection(DbConnectionConfig config, string password)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = config.Host,
            Port = (uint)(config.Port ?? 3306),
            Database = config.DatabaseName,
            UserID = config.Username,
            Password = password,
            CharacterSet = "utf8mb4",
            ConnectionTimeout = (uint)(config.ConnectionTimeoutSec ?? 10),
            DefaultCommandTimeout = (uint)(config.CommandTimeoutSec ?? 30),
            Pooling = true,
            MinimumPoolSize = 1,
            MaximumPoolSize = 10
        };

        return new(builder.ConnectionString);
    }
}
