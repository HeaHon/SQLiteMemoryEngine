using System;
using System.Collections.Generic;

public class SqliteNativeQueryEngine : IQueryEngine
{
    public string FormatName => "SQLite (Native C)";
    private readonly string dbPath;

    public SqliteNativeQueryEngine(string dbPath)
    {
        this.dbPath = dbPath;
    }

    public QueryResult Execute(QuerySpec query)
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
            SqlCompilation compiled = SqlQueryCompiler.Compile(query);
            using (var db = new SqliteNativeConnection(dbPath))
            {
                if (!db.IsOpen)
                {
                    result.IsSuccess = false;
                    result.ErrorMessage = db.LastError ?? "SQLite Native C 연결에 실패했습니다.";
                    return result;
                }

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

    public QueryResult ExecuteSql(string sqlQuery)
    {
        QueryParseResult parsed = QuerySpecParser.Parse(sqlQuery);
        if (!parsed.IsSuccess)
        {
            return new QueryResult { IsSuccess = false, ErrorMessage = parsed.ErrorMessage };
        }
        return Execute(parsed.Spec);
    }
}
