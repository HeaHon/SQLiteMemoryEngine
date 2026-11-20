using System;
using System.Collections.Generic;
using System.Reflection;
using SQLite;

public class SqliteORMQueryEngine : IQueryEngine, IQueryBenchmarkSession
{
    public string FormatName => "SQLite (sqlite-net)";
    private readonly string dbPath;

    private SQLiteConnection _persistentDb;
    private SqlCompilation _preparedSql;
    private Func<SQLiteConnection, SqlCompilation, int> _noAllocRunner;

    // 테이블명 -> 검증용 실행기 / 측정용 실행기 레지스트리
    private static readonly Dictionary<string, Action<SQLiteConnection, SqlCompilation, QuerySpec, QueryResult>> VerificationRunners
        = new Dictionary<string, Action<SQLiteConnection, SqlCompilation, QuerySpec, QueryResult>>(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, Func<SQLiteConnection, SqlCompilation, int>> NoAllocRunners
        = new Dictionary<string, Func<SQLiteConnection, SqlCompilation, int>>(StringComparer.OrdinalIgnoreCase);

    static SqliteORMQueryEngine()
    {
        // Relational
        RegisterTable<RelMasterItem_C1>(nameof(RelMasterItem_C1));
        RegisterTable<RelRuntimeCharacter_C1>(nameof(RelRuntimeCharacter_C1));
        RegisterTable<RelMasterItem_C2>(nameof(RelMasterItem_C2));
        RegisterTable<RelItemTag_C2>(nameof(RelItemTag_C2));
        RegisterTable<RelRuntimeCharacter_C2>(nameof(RelRuntimeCharacter_C2));
        RegisterTable<RelRuntimeInventory_C2>(nameof(RelRuntimeInventory_C2));
        RegisterTable<RelSystemUser_C3>(nameof(RelSystemUser_C3));
        RegisterTable<RelRuntimeCharacter_C3>(nameof(RelRuntimeCharacter_C3));
        RegisterTable<RelMasterItem_C3>(nameof(RelMasterItem_C3));
        RegisterTable<RelMasterEffect_C3>(nameof(RelMasterEffect_C3));
        RegisterTable<RelItemEffectLink_C3>(nameof(RelItemEffectLink_C3));
        RegisterTable<RelRuntimeInventory_C3>(nameof(RelRuntimeInventory_C3));

        // Document (정규화 테이블)
        RegisterTable<SqliteDocItemC1Row>("Doc_Item_C1");
        RegisterTable<SqliteDocCharC2Row>("Doc_Char_C2");
        RegisterTable<SqliteDocUserC3Row>("Doc_User_C3");

        // Sparse (EAV 테이블)
        RegisterTable<SqliteSparseItemC1Row>("Sparse_Item_C1");
        RegisterTable<SqliteSparseEntityC2Row>("Sparse_Entity_C2");
        RegisterTable<SqliteSparseEventC3Row>("Sparse_Event_C3");
    }

    public static void RegisterTable<TRow>(string tableName) where TRow : new()
    {
        VerificationRunners[tableName] = ExecuteTyped<TRow>;
        NoAllocRunners[tableName] = CountTyped<TRow>;
    }

    public SqliteORMQueryEngine(string dbPath)
    {
        this.dbPath = dbPath;
    }

    public QueryResult Execute(QuerySpec query)
    {
        using (var db = new SQLiteConnection(dbPath, SQLiteOpenFlags.ReadOnly))
        {
            return ExecuteInternal(db, query);
        }
    }

    public QueryResult ExecuteForVerification(QuerySpec query)
    {
        EnsureOpen();
        return ExecuteInternal(_persistentDb, query);
    }

    public void Prepare(QuerySpec query)
    {
        EnsureOpen();
        _preparedSql = SqlQueryCompiler.Compile(query);
        _noAllocRunner = ResolveNoAllocRunner(query);
    }

    public int ExecuteNoAlloc(QuerySpec query)
    {
        if (_persistentDb == null || _preparedSql == null || _noAllocRunner == null) return -1;
        return _noAllocRunner(_persistentDb, _preparedSql);
    }

    private void EnsureOpen()
    {
        if (_persistentDb == null)
        {
            _persistentDb = new SQLiteConnection(dbPath, SQLiteOpenFlags.ReadOnly);
        }
    }

    public void Dispose()
    {
        _persistentDb?.Dispose();
        _persistentDb = null;
    }

    private QueryResult ExecuteInternal(SQLiteConnection db, QuerySpec query)
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
            Action<SQLiteConnection, SqlCompilation, QuerySpec, QueryResult> runner = ResolveVerificationRunner(query);
            runner(db, compiled, query, result);
            result.Complete(query);
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private static Action<SQLiteConnection, SqlCompilation, QuerySpec, QueryResult> ResolveVerificationRunner(QuerySpec query)
    {
        string t = query.Table ?? string.Empty;

        if (query.Operation == QueryOperation.RelationTraversal)
        {
            if (t.StartsWith("Doc_", StringComparison.OrdinalIgnoreCase))
            {
                if (query.ComplexityLevel == 1) return ExecuteTyped<SqliteDocRelationC1Row>;
                if (query.ComplexityLevel == 2) return ExecuteTyped<SqliteDocRelationC2Row>;
                if (query.ComplexityLevel == 3) return ExecuteTyped<SqliteDocRelationC3Row>;
            }
            if (t.StartsWith("Sparse_", StringComparison.OrdinalIgnoreCase))
            {
                return query.ComplexityLevel == 3
                    ? (Action<SQLiteConnection, SqlCompilation, QuerySpec, QueryResult>)ExecuteTyped<SqliteSparseRelationC3Row>
                    : ExecuteTyped<SqliteSparseRelationRow>;
            }
            if (query.ComplexityLevel == 2) return query.Joins.Exists(j => j.Table.IndexOf("Tag", StringComparison.OrdinalIgnoreCase) >= 0) ? (Action<SQLiteConnection, SqlCompilation, QuerySpec, QueryResult>)ExecuteTyped<SqliteRelTagRow> : ExecuteTyped<SqliteRelationC2Row>;
            if (query.ComplexityLevel == 3) return ExecuteTyped<SqliteRelationC3Row>;
            throw new NotSupportedException("C1에는 관계형 traversal 대상이 없습니다.");
        }

        if (VerificationRunners.TryGetValue(t, out var runner)) return runner;
        throw new NotSupportedException($"sqlite-net DTO가 없는 테이블입니다: {query.Table}");
    }

    private static Func<SQLiteConnection, SqlCompilation, int> ResolveNoAllocRunner(QuerySpec query)
    {
        string t = query.Table ?? string.Empty;

        if (query.Operation == QueryOperation.RelationTraversal)
        {
            if (t.StartsWith("Doc_", StringComparison.OrdinalIgnoreCase))
            {
                if (query.ComplexityLevel == 1) return CountTyped<SqliteDocRelationC1Row>;
                if (query.ComplexityLevel == 2) return CountTyped<SqliteDocRelationC2Row>;
                if (query.ComplexityLevel == 3) return CountTyped<SqliteDocRelationC3Row>;
            }
            if (t.StartsWith("Sparse_", StringComparison.OrdinalIgnoreCase))
            {
                return query.ComplexityLevel == 3
                    ? (Func<SQLiteConnection, SqlCompilation, int>)CountTyped<SqliteSparseRelationC3Row>
                    : CountTyped<SqliteSparseRelationRow>;
            }
            if (query.ComplexityLevel == 2) return query.Joins.Exists(j => j.Table.IndexOf("Tag", StringComparison.OrdinalIgnoreCase) >= 0) ? (Func<SQLiteConnection, SqlCompilation, int>)CountTyped<SqliteRelTagRow> : CountTyped<SqliteRelationC2Row>;
            if (query.ComplexityLevel == 3) return CountTyped<SqliteRelationC3Row>;
        }

        if (NoAllocRunners.TryGetValue(t, out var runner)) return runner;
        throw new NotSupportedException($"sqlite-net DTO가 없는 테이블입니다: {query.Table}");
    }

    private static void ExecuteTyped<T>(SQLiteConnection db, SqlCompilation compiled, QuerySpec query, QueryResult result)
        where T : new()
    {
        List<T> rows = db.Query<T>(compiled.Sql, compiled.Parameters);
        for (int i = 0; i < rows.Count; i++) result.AddProjectedRow(query, ToDictionary(rows[i]));
    }

    private static int CountTyped<T>(SQLiteConnection db, SqlCompilation compiled)
        where T : new()
    {
        List<T> rows = db.Query<T>(compiled.Sql, compiled.Parameters);
        return rows.Count;
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

#region Join / Doc / Sparse DTOs
public class SqliteRelationC2Row { public int character_id { get; set; } public string character_name { get; set; } public int item_id { get; set; } public string item_name { get; set; } public int instance_id { get; set; } public int quantity { get; set; } }
public class SqliteRelationC3Row { public int user_id { get; set; } public string account_name { get; set; } public int character_id { get; set; } public string character_name { get; set; } public int item_id { get; set; } public string item_name { get; set; } public int instance_id { get; set; } public string effect_type { get; set; } }

public class SqliteDocItemC1Row { public int item_id { get; set; } public string name { get; set; } public string item_type { get; set; } public int rarity { get; set; } public int atk { get; set; } public int price { get; set; } }
public class SqliteDocCharC2Row { public int character_id { get; set; } public string name { get; set; } public int level { get; set; } public int cur_hp { get; set; } }
public class SqliteDocUserC3Row { public int user_id { get; set; } public string account_name { get; set; } public long created_at { get; set; } }

public class SqliteDocRelationC1Row { public int item_id { get; set; } public string name { get; set; } public string tag_name { get; set; } }
public class SqliteDocRelationC2Row { public int character_id { get; set; } public string name { get; set; } public int instance_id { get; set; } public string item_name { get; set; } }
public class SqliteDocRelationC3Row { public int user_id { get; set; } public string account_name { get; set; } public int character_id { get; set; } public int instance_id { get; set; } }

public class SqliteSparseItemC1Row { public int item_id { get; set; } public string item_type { get; set; } public string name { get; set; } }
public class SqliteSparseEntityC2Row { public int entity_id { get; set; } public string category { get; set; } public string name { get; set; } }
public class SqliteSparseEventC3Row { public string event_id { get; set; } public int user_id { get; set; } public string event_type { get; set; } public long timestamp { get; set; } }

public class SqliteSparseRelationRow { public int item_id { get; set; } public int entity_id { get; set; } public string name { get; set; } public string attr_key { get; set; } public string attr_value { get; set; } }
public class SqliteSparseRelationC3Row { public string event_id { get; set; } public string event_type { get; set; } public string payload_key { get; set; } public string payload_value { get; set; } }
public class SqliteRelTagRow { public int item_id { get; set; } public string name { get; set; } public string tag_name { get; set; } }
#endregion