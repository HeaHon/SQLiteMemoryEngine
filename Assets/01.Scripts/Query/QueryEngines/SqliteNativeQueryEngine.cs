using System;
using System.Collections.Generic;

public class SqliteNativeQueryEngine : IQueryEngine, IQueryBenchmarkSession
{
    public string FormatName => "SQLite (Native C)";
    private readonly string dbPath;

    private SqliteNativeConnection persistentDb;
    private SqlCompilation preparedCompilation;
    private IntPtr preparedStmt = IntPtr.Zero;

    public SqliteNativeQueryEngine(string dbPath)
    {
        this.dbPath = dbPath;
    }

    public QueryResult Execute(QuerySpec query)
    {
        using (var db = new SqliteNativeConnection(dbPath, readOnly: true))
        {
            return ExecuteInternal(db, query);
        }
    }

    public QueryResult ExecuteForVerification(QuerySpec query)
    {
        EnsureOpen();
        return ExecuteInternal(persistentDb, query);
    }

    public void Prepare(QuerySpec query)
    {
        EnsureOpen();
        FinalizePrepared();
        preparedCompilation = SqlQueryCompiler.Compile(query);
        preparedStmt = persistentDb.PrepareStatement(preparedCompilation.Sql);
    }

    public int ExecuteNoAlloc(QuerySpec query)
    {
        if (persistentDb == null || preparedStmt == IntPtr.Zero || preparedCompilation == null) return -1;
        return persistentDb.ExecutePreparedCountNoAlloc(preparedStmt, preparedCompilation.Parameters);
    }

    private void EnsureOpen()
    {
        if (persistentDb == null)
        {
            persistentDb = new SqliteNativeConnection(dbPath, readOnly: true);
        }
    }

    private void FinalizePrepared()
    {
        if (preparedStmt != IntPtr.Zero)
        {
            SqliteNativeConnection.FinalizeStatement(preparedStmt);
            preparedStmt = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        FinalizePrepared();
        persistentDb?.Dispose();
        persistentDb = null;
    }

    private static QueryResult ExecuteInternal(SqliteNativeConnection db, QuerySpec query)
    {
        var result = new QueryResult();
        if (query == null)
        {
            result.IsSuccess = false;
            result.ErrorMessage = "QuerySpec가 null입니다.";
            return result;
        }

        try
        {
            if (!db.IsOpen)
            {
                result.IsSuccess = false;
                result.ErrorMessage = db.LastError ?? "SQLite Native C 연결에 실패했습니다.";
                return result;
            }

            SqlCompilation compiled = SqlQueryCompiler.Compile(query);
            bool success = db.ExecuteQuery(compiled.Sql, compiled.Parameters, stmt =>
            {
                int count = SqliteNativeConnection.GetColumnCount(stmt);
                var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < count; i++)
                {
                    row[SqliteNativeConnection.GetColumnName(stmt, i)] = SqliteNativeConnection.GetValue(stmt, i);
                }
                result.AddProjectedRow(query, row);
            });

            if (!success)
            {
                result.IsSuccess = false;
                result.ErrorMessage = db.LastError ?? "SQLite Native C 쿼리 실행에 실패했습니다.";
                return result;
            }

            result.Complete(query);
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }
}