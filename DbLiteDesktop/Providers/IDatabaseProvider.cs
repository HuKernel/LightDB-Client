using System.Data;
using DbLiteDesktop.Models;

namespace DbLiteDesktop.Providers;

public interface IDatabaseProvider
{
    bool TestConnection(DbConnectionConfig config, string password);
    List<string> GetTables(DbConnectionConfig config, string password);
    List<TableColumnInfo> GetColumns(DbConnectionConfig config, string password, string tableName);
    DataTable ExecuteQuery(DbConnectionConfig config, string password, string sql, int maxRows = 1000);
    List<DataTable> ExecuteQueryMultiple(DbConnectionConfig config, string password, string sql, int maxRows = 1000);
    void CancelQuery();
    long GetRowCount(DbConnectionConfig config, string password, string tableName);
    string BuildPreviewSql(string tableName, int limit = 100);
    string BuildPagedPreviewSql(string tableName, int page, int pageSize);
    string BuildFilteredPreviewSql(
        string tableName,
        IReadOnlyList<string> columns,
        string? selectedColumn,
        string keyword,
        bool exactMatch,
        int page,
        int pageSize
    );
}
