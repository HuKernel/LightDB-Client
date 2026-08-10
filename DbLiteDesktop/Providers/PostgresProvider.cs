using System.Collections.Concurrent;
using System.Data;
using DbLiteDesktop.Models;
using DbLiteDesktop.Services;
using DbLiteDesktop.Utils;
using Npgsql;
using Pgvector;

namespace DbLiteDesktop.Providers;

public class PostgresProvider : IDatabaseProvider
{
    private static readonly Func<string, string> Quote = IdentifierQuoteHelper.QuotePostgres;
    private static readonly ConcurrentDictionary<string, NpgsqlDataSource> DataSources = new();
    private NpgsqlCommand? _currentCommand;

    public void CancelQuery()
    {
        try { _currentCommand?.Cancel(); } catch { /* ignored */ }
    }

    public bool TestConnection(DbConnectionConfig config, string password)
    {
        using var connection = GetDataSource(config, password).OpenConnection();

        using var command = new NpgsqlCommand("SELECT 1;", connection);
        return command.ExecuteScalar() is not null;
    }

    public List<string> GetTables(DbConnectionConfig config, string password)
    {
        using var connection = GetDataSource(config, password).OpenConnection();

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
        using var connection = GetDataSource(config, password).OpenConnection();

        using var command = new NpgsqlCommand(
            """
            SELECT c.column_name,
                   format_type(a.atttypid, a.atttypmod) AS column_type,
                   c.is_nullable,
                   c.column_default,
                   a.atttypid::regtype::text AS udt_name,
                   col_description((c.table_schema || '.' || c.table_name)::regclass, c.ordinal_position) AS comment
            FROM information_schema.columns c
            JOIN pg_attribute a
              ON a.attrelid = (c.table_schema || '.' || c.table_name)::regclass
             AND a.attname = c.column_name
            WHERE c.table_schema = 'public' AND c.table_name = $1
            ORDER BY c.ordinal_position;
            """,
            connection
        );
        command.Parameters.AddWithValue(tableName);

        using var reader = command.ExecuteReader();
        var items = new List<TableColumnInfo>();

        while (reader.Read())
        {
            var type = reader["column_type"]?.ToString() ?? string.Empty;
            var udtName = reader["udt_name"]?.ToString() ?? string.Empty;
            var isVector = string.Equals(udtName, "vector", StringComparison.OrdinalIgnoreCase);

            items.Add(new TableColumnInfo
            {
                Name = reader["column_name"]?.ToString() ?? string.Empty,
                Type = type,
                Nullable = reader["is_nullable"]?.ToString() ?? string.Empty,
                DefaultValue = reader["column_default"]?.ToString(),
                Extra = string.Empty,
                Comment = reader["comment"]?.ToString(),
                IsVector = isVector,
                VectorDimension = isVector ? ParseVectorDimension(type) : 0
            });
        }

        return items;
    }

    private static int ParseVectorDimension(string type)
    {
        // vector(1536) → 1536;vector → 0(未知维度)
        var open = type.IndexOf('(');
        var close = type.IndexOf(')');
        if (open > 0 && close > open && int.TryParse(type.Substring(open + 1, close - open - 1), out var dim))
        {
            return dim;
        }
        return 0;
    }

    public DataTable ExecuteQuery(DbConnectionConfig config, string password, string sql, int maxRows = 1000)
    {
        if (!SqlGuardService.IsReadonlySql(sql))
        {
            throw new InvalidOperationException("当前工具只允许执行只读 SQL");
        }

        using var connection = GetDataSource(config, password).OpenConnection();

        using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = config.CommandTimeoutSec ?? 30
        };

        using var reader = command.ExecuteReader();
        var tables = LoadTablesPg(reader, maxRows);
        return tables.Count > 0 ? tables[0] : new DataTable();
    }

    public List<DataTable> ExecuteQueryMultiple(DbConnectionConfig config, string password, string sql, int maxRows = 1000)
    {
        if (!SqlGuardService.IsReadonlySql(sql))
        {
            throw new InvalidOperationException("当前工具只允许执行只读 SQL");
        }

        using var connection = GetDataSource(config, password).OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = config.CommandTimeoutSec ?? 30;
        _currentCommand = command;
        try
        {
            using var reader = command.ExecuteReader();
            return LoadTablesPg(reader, maxRows);
        }
        finally
        {
            _currentCommand = null;
        }
    }

    /// <summary>
    /// 手动读取到 DataTable:DataTable.Load 无法读取 pgvector 的 vector 列(抛 InvalidCastException),
    /// 这里对 vector 列用 Pgvector.Vector 读取后转为截断的文本显示。
    /// </summary>
    private static List<DataTable> LoadTablesPg(NpgsqlDataReader reader, int maxRows)
    {
        var results = new List<DataTable>();
        do
        {
            var table = new DataTable();
            var vectorOrdinals = new HashSet<int>();

            for (var i = 0; i < reader.FieldCount; i++)
            {
                var isVector = string.Equals(reader.GetDataTypeName(i), "vector", StringComparison.OrdinalIgnoreCase);
                if (isVector)
                {
                    vectorOrdinals.Add(i);
                }

                var name = reader.GetName(i);
                var finalName = name;
                var suffix = 1;
                while (table.Columns.Contains(finalName))
                {
                    finalName = $"{name}_{suffix++}";
                }
                table.Columns.Add(new DataColumn(finalName, isVector ? typeof(string) : reader.GetFieldType(i)));
            }

            var rowCount = 0;
            while (reader.Read() && rowCount < maxRows)
            {
                var values = new object[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    if (reader.IsDBNull(i))
                    {
                        values[i] = DBNull.Value;
                    }
                    else if (vectorOrdinals.Contains(i))
                    {
                        var vector = reader.GetFieldValue<Vector>(i);
                        values[i] = ProviderHelper.TruncateVectorText($"[{string.Join(',', vector)}]");
                    }
                    else
                    {
                        values[i] = reader.GetValue(i);
                    }
                }
                table.Rows.Add(values);
                rowCount++;
            }

            results.Add(table);
        } while (!reader.IsClosed && reader.NextResult());

        return results;
    }

    public long GetRowCount(DbConnectionConfig config, string password, string tableName)
    {
        using var connection = GetDataSource(config, password).OpenConnection();

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

    private static NpgsqlDataSource GetDataSource(DbConnectionConfig config, string password)
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

        return DataSources.GetOrAdd(builder.ConnectionString, connectionString =>
        {
            var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
            dataSourceBuilder.UseVector();
            return dataSourceBuilder.Build();
        });
    }
}
