using System;
using System.Collections.Generic;
using System.Reflection;
using SQLite;

public class SqliteQueryEngine : IQueryEngine
{
    public string FormatName => "SQLite (sqlite-net)";
    private readonly string dbPath;

    public SqliteQueryEngine(string dbPath)
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
            using (var db = new SQLiteConnection(dbPath, SQLiteOpenFlags.ReadOnly))
            {
                if (query.Operation == QueryOperation.RelationTraversal)
                {
                    if (query.ComplexityLevel == 2) ExecuteTyped<SqliteRelationC2Row>(db, compiled, query, result);
                    else if (query.ComplexityLevel == 3) ExecuteTyped<SqliteRelationC3Row>(db, compiled, query, result);
                    else throw new NotSupportedException("C1에는 관계형 traversal 대상이 없습니다.");
                }
                else if (query.Table.IndexOf("Master_Item_C1", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ExecuteTyped<SqliteItemC1Row>(db, compiled, query, result);
                }
                else if (query.Table.IndexOf("Master_Item_C2", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ExecuteTyped<SqliteItemC2Row>(db, compiled, query, result);
                }
                else if (query.Table.IndexOf("Master_Item_C3", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ExecuteTyped<SqliteItemC3Row>(db, compiled, query, result);
                }
                else if (query.Table.IndexOf("Runtime_Inventory_C2", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ExecuteTyped<SqliteInventoryC2Row>(db, compiled, query, result);
                }
                else if (query.Table.IndexOf("Runtime_Inventory_C3", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ExecuteTyped<SqliteInventoryC3Row>(db, compiled, query, result);
                }
                else
                {
                    throw new NotSupportedException($"sqlite-net DTO가 없는 테이블입니다: {query.Table}");
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

    private static void ExecuteTyped<T>(SQLiteConnection db, SqlCompilation compiled, QuerySpec query, QueryResult result)
        where T : new()
    {
        List<T> rows = db.Query<T>(compiled.Sql, compiled.Parameters);
        for (int i = 0; i < rows.Count; i++) result.AddProjectedRow(query, ToDictionary(rows[i]));
    }

    private static Dictionary<string, object> ToDictionary<T>(T value)
    {
        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        PropertyInfo[] properties = typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public);
        for (int i = 0; i < properties.Length; i++)
        {
            if (properties[i].CanRead) row[properties[i].Name] = properties[i].GetValue(value, null);
        }
        return row;
    }
}

public class SqliteItemC1Row
{
    public int item_id { get; set; }
    public string name { get; set; }
    public string item_type { get; set; }
    public int rarity { get; set; }
    public int atk { get; set; }
    public int price { get; set; }
}

public class SqliteItemC2Row
{
    public int item_id { get; set; }
    public string item_code { get; set; }
    public string name { get; set; }
    public string item_type { get; set; }
    public int rarity { get; set; }
    public int req_level { get; set; }
    public int base_atk { get; set; }
}

public class SqliteItemC3Row
{
    public int item_id { get; set; }
    public string name { get; set; }
    public int rarity { get; set; }
}

public class SqliteInventoryC2Row
{
    public int instance_id { get; set; }
    public int character_id { get; set; }
    public int item_id { get; set; }
    public int quantity { get; set; }
}

public class SqliteInventoryC3Row
{
    public int instance_id { get; set; }
    public int character_id { get; set; }
    public int item_id { get; set; }
    public int durability { get; set; }
}

public class SqliteRelationC2Row
{
    public int character_id { get; set; }
    public string character_name { get; set; }
    public int item_id { get; set; }
    public string item_name { get; set; }
    public int instance_id { get; set; }
    public int quantity { get; set; }
}

public class SqliteRelationC3Row
{
    public int user_id { get; set; }
    public string account_name { get; set; }
    public int character_id { get; set; }
    public string character_name { get; set; }
    public int item_id { get; set; }
    public string item_name { get; set; }
    public int instance_id { get; set; }
    public string effect_type { get; set; }
}
