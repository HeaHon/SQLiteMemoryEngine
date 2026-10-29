using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RocksDbSharp;

public class RocksDbQueryEngine : IQueryEngine, IDisposable
{
    public string FormatName => "RocksDB";
    private readonly string dbFolder;

    public RocksDbQueryEngine(string dbFolder)
    {
        this.dbFolder = dbFolder;
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
        if (string.IsNullOrEmpty(dbFolder) || !Directory.Exists(dbFolder))
        {
            result.IsSuccess = false;
            result.ErrorMessage = $"RocksDB 디렉토리를 찾을 수 없습니다: {dbFolder}";
            return result;
        }

        try
        {
            var options = new DbOptions().SetCreateIfMissing(false);
            using (var db = RocksDb.Open(options, Path.GetFullPath(dbFolder)))
            {
                if (query.Operation == QueryOperation.RelationTraversal) ExecuteRelation(db, query, result);
                else if (query.Operation == QueryOperation.PrimaryKeyLookup || query.Operation == QueryOperation.PartialFieldAccess)
                {
                    object id;
                    if (query.TryGetEquality(query.PrimaryKeyField, out id))
                    {
                        Dictionary<string, object> row = ReadByPrimaryKey(db, query.Table, Convert.ToInt32(id));
                        if (row != null && QueryRuntime.Matches(row, query.Predicates)) result.AddProjectedRow(query, row);
                    }
                }
                else
                {
                    foreach (Dictionary<string, object> row in EnumerateTable(db, query.Table))
                    {
                        if (QueryRuntime.Matches(row, query.Predicates)) result.AddProjectedRow(query, row);
                    }
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
        if (!parsed.IsSuccess) return new QueryResult { IsSuccess = false, ErrorMessage = parsed.ErrorMessage };
        return Execute(parsed.Spec);
    }

    private static void ExecuteRelation(RocksDb db, QuerySpec query, QueryResult result)
    {
        object anchor;
        if (!query.TryGetEquality(query.RelationAnchorField, out anchor)) throw new InvalidOperationException("관계형 조회 기준값이 없습니다.");

        if (query.ComplexityLevel == 2)
        {
            foreach (Dictionary<string, object> inventory in EnumerateTable(db, "Runtime_Inventory_C2"))
            {
                if (!QueryRuntime.Compare(Value(inventory, "character_id"), anchor, QueryComparison.Equal)) continue;
                Dictionary<string, object> character = ReadByPrimaryKey(db, "Runtime_Character_C2", Convert.ToInt32(Value(inventory, "character_id")));
                Dictionary<string, object> item = ReadByPrimaryKey(db, "Master_Item_C2", Convert.ToInt32(Value(inventory, "item_id")));
                var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["character_id"] = Value(character, "character_id"),
                    ["character_name"] = Value(character, "name"),
                    ["item_id"] = Value(item, "item_id"),
                    ["item_name"] = Value(item, "name"),
                    ["instance_id"] = Value(inventory, "instance_id"),
                    ["quantity"] = Value(inventory, "quantity")
                };
                if (QueryRuntime.Matches(row, query.Predicates)) result.AddProjectedRow(query, row);
            }
            return;
        }

        if (query.ComplexityLevel != 3) throw new NotSupportedException("C1에는 관계형 traversal 대상이 없습니다.");

        var characters = new List<Dictionary<string, object>>();
        foreach (Dictionary<string, object> character in EnumerateTable(db, "Runtime_Character_C3"))
        {
            string field = query.RelationAnchorField;
            if (QueryRuntime.Compare(Value(character, field), anchor, QueryComparison.Equal)) characters.Add(character);
        }

        foreach (Dictionary<string, object> character in characters)
        {
            Dictionary<string, object> user = ReadByPrimaryKey(db, "System_User_C3", Convert.ToInt32(Value(character, "user_id")));
            foreach (Dictionary<string, object> inventory in EnumerateTable(db, "Runtime_Inventory_C3"))
            {
                if (!QueryRuntime.Compare(Value(inventory, "character_id"), Value(character, "character_id"), QueryComparison.Equal)) continue;
                Dictionary<string, object> item = ReadByPrimaryKey(db, "Master_Item_C3", Convert.ToInt32(Value(inventory, "item_id")));
                var links = new List<Dictionary<string, object>>();
                foreach (Dictionary<string, object> link in EnumerateTable(db, "Item_Effect_Link_C3"))
                {
                    if (QueryRuntime.Compare(Value(link, "item_id"), Value(item, "item_id"), QueryComparison.Equal)) links.Add(link);
                }
                if (links.Count == 0) links.Add(null);

                foreach (Dictionary<string, object> link in links)
                {
                    Dictionary<string, object> effect = link == null ? null : ReadByPrimaryKey(db, "Effect_C3", Convert.ToInt32(Value(link, "effect_id")));
                    var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["user_id"] = Value(user, "user_id"),
                        ["account_name"] = Value(user, "account_name"),
                        ["character_id"] = Value(character, "character_id"),
                        ["character_name"] = Value(character, "name"),
                        ["item_id"] = Value(item, "item_id"),
                        ["item_name"] = Value(item, "name"),
                        ["instance_id"] = Value(inventory, "instance_id"),
                        ["effect_type"] = Value(effect, "effect_type")
                    };
                    if (QueryRuntime.Matches(row, query.Predicates)) result.AddProjectedRow(query, row);
                }
            }
        }
    }

    private static Dictionary<string, object> ReadByPrimaryKey(RocksDb db, string table, int id)
    {
        string prefix = GetKeyPrefix(table);
        byte[] value = db.Get(Encoding.UTF8.GetBytes(prefix + id));
        return value == null ? null : ReadRowByTable(table, value);
    }

    private static IEnumerable<Dictionary<string, object>> EnumerateTable(RocksDb db, string table)
    {
        string prefix = GetKeyPrefix(table);
        byte[] prefixBytes = Encoding.UTF8.GetBytes(prefix);
        using (var iterator = db.NewIterator())
        {
            for (iterator.Seek(prefixBytes); iterator.Valid(); iterator.Next())
            {
                string key = Encoding.UTF8.GetString(iterator.Key());
                if (!key.StartsWith(prefix, StringComparison.Ordinal)) break;
                if (key.StartsWith("idx_", StringComparison.Ordinal) || key.Contains("_by_")) continue;
                yield return ReadRowByTable(table, iterator.Value());
            }
        }
    }

    private static Dictionary<string, object> ReadRowByTable(string table, byte[] bytes)
    {
        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        using (var stream = new MemoryStream(bytes))
        using (var reader = new BinaryReader(stream, Encoding.UTF8))
        {
            if (table.IndexOf("Master_Item_C1", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                row["item_id"] = reader.ReadInt32(); row["name"] = reader.ReadString(); row["item_type"] = reader.ReadString();
                row["rarity"] = reader.ReadInt32(); row["atk"] = reader.ReadInt32(); row["price"] = reader.ReadInt32();
            }
            else if (table.IndexOf("Master_Item_C2", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                row["item_id"] = reader.ReadInt32(); row["item_code"] = reader.ReadString(); row["name"] = reader.ReadString(); row["item_type"] = reader.ReadString();
                row["rarity"] = reader.ReadInt32(); row["req_level"] = reader.ReadInt32(); row["base_atk"] = reader.ReadInt32();
            }
            else if (table.IndexOf("Master_Item_C3", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                row["item_id"] = reader.ReadInt32(); row["name"] = reader.ReadString(); row["rarity"] = reader.ReadInt32();
            }
            else if (table.IndexOf("Runtime_Character_C2", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                row["character_id"] = reader.ReadInt32(); row["name"] = reader.ReadString(); row["level"] = reader.ReadString();
                row["cur_hp"] = reader.ReadInt32(); row["pos_x"] = reader.ReadSingle(); row["pos_y"] = reader.ReadSingle();
            }
            else if (table.IndexOf("Runtime_Character_C3", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                row["character_id"] = reader.ReadInt32(); row["user_id"] = reader.ReadInt32(); row["name"] = reader.ReadString(); row["guild_id"] = reader.ReadInt32();
            }
            else if (table.IndexOf("Runtime_Inventory_C2", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                row["instance_id"] = reader.ReadInt32(); row["character_id"] = reader.ReadInt32(); row["item_id"] = reader.ReadInt32(); row["quantity"] = reader.ReadInt32();
            }
            else if (table.IndexOf("Runtime_Inventory_C3", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                row["instance_id"] = reader.ReadInt32(); row["character_id"] = reader.ReadInt32(); row["item_id"] = reader.ReadInt32(); row["durability"] = reader.ReadInt32();
            }
            else if (table.IndexOf("System_User_C3", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                row["user_id"] = reader.ReadInt32(); row["account_name"] = reader.ReadString(); row["created_at"] = reader.ReadInt64();
            }
            else if (table.IndexOf("Master_Effect_C3", StringComparison.OrdinalIgnoreCase) >= 0 || table.Equals("Effect_C3", StringComparison.OrdinalIgnoreCase))
            {
                row["effect_id"] = reader.ReadInt32(); row["effect_type"] = reader.ReadString(); row["value"] = reader.ReadInt32();
            }
            else if (table.IndexOf("Item_Effect_Link_C3", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                row["item_id"] = reader.ReadInt32(); row["effect_id"] = reader.ReadInt32();
            }
            else if (table.IndexOf("Item_Tag_C2", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                row["item_id"] = reader.ReadInt32(); row["tag_name"] = reader.ReadString();
            }
        }
        return row;
    }

    private static string GetKeyPrefix(string table)
    {
        if (table.IndexOf("ITEM_EFFECT_LINK", StringComparison.OrdinalIgnoreCase) >= 0) return "link_";
        if (table.IndexOf("USER", StringComparison.OrdinalIgnoreCase) >= 0) return "user_";
        if (table.IndexOf("CHARACTER", StringComparison.OrdinalIgnoreCase) >= 0) return "char_";
        if (table.IndexOf("INVENTORY", StringComparison.OrdinalIgnoreCase) >= 0) return "inv_";
        if (table.IndexOf("EFFECT", StringComparison.OrdinalIgnoreCase) >= 0) return "effect_";
        if (table.IndexOf("TAG", StringComparison.OrdinalIgnoreCase) >= 0) return "tag_";
        return "item_";
    }

    private static object Value(Dictionary<string, object> row, string field)
    {
        if (row == null) return null;
        object value;
        return QueryRuntime.TryGetValue(row, field, out value) ? value : null;
    }

    public void Dispose()
    {
    }
}
