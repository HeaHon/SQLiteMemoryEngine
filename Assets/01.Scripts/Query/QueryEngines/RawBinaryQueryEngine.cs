using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

public class RawBinaryQueryEngine : IQueryEngine
{
    private sealed class TableIndex
    {
        public readonly List<long> Offsets = new List<long>();
        public readonly Dictionary<int, long> Primary = new Dictionary<int, long>();
    }

    public string FormatName => "RawBinary";
    private readonly string filePath;
    private readonly Dictionary<string, TableIndex> tableIndexes = new Dictionary<string, TableIndex>(StringComparer.OrdinalIgnoreCase);

    public RawBinaryQueryEngine(string filePath)
    {
        this.filePath = filePath;
        BuildIndexFromFileLayout();
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
            if (query.Operation == QueryOperation.RelationTraversal)
            {
                ExecuteRelation(query, result);
            }
            else
            {
                object keyValue;
                int key;
                Dictionary<string, object> row;
                if (query.TryGetEquality(query.PrimaryKeyField, out keyValue) &&
                    TryGetInt32(keyValue, out key) &&
                    TryReadPrimary(query.Table, key, out row))
                {
                    if (QueryRuntime.Matches(row, query.Predicates)) result.AddProjectedRow(query, row);
                }
                else
                {
                    foreach (Dictionary<string, object> candidate in EnumerateRows(query.Table))
                    {
                        if (QueryRuntime.Matches(candidate, query.Predicates)) result.AddProjectedRow(query, candidate);
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

    private void BuildIndexFromFileLayout()
    {
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var reader = new BinaryReader(fs, Encoding.UTF8))
        {
            if (fs.Length < sizeof(int)) return;

            string fileName = Path.GetFileNameWithoutExtension(filePath);
            if (fileName.IndexOf("C1", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                int itemCount = ReadPrimaryHeader(reader, "Master_Item_C1");
                ReadRows(reader, "Master_Item_C1", itemCount);
                ReadRows(reader, "Runtime_Character_C1", reader.ReadInt32());
                return;
            }

            if (fileName.IndexOf("C2", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                int itemCount = ReadPrimaryHeader(reader, "Master_Item_C2");
                ReadRows(reader, "Master_Item_C2", itemCount);
                ReadRows(reader, "Item_Tag_C2", reader.ReadInt32());
                ReadRows(reader, "Runtime_Character_C2", reader.ReadInt32());
                ReadRows(reader, "Runtime_Inventory_C2", reader.ReadInt32());
                return;
            }

            if (fileName.IndexOf("C3", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                int userCount = ReadPrimaryHeader(reader, "System_User_C3");
                ReadRows(reader, "System_User_C3", userCount);
                ReadRows(reader, "Runtime_Character_C3", reader.ReadInt32());
                ReadRows(reader, "Master_Item_C3", reader.ReadInt32());
                ReadRows(reader, "Master_Effect_C3", reader.ReadInt32());
                ReadRows(reader, "Item_Effect_Link_C3", reader.ReadInt32());
                ReadRows(reader, "Runtime_Inventory_C3", reader.ReadInt32());
            }
        }
    }

    private int ReadPrimaryHeader(BinaryReader reader, string table)
    {
        int count = reader.ReadInt32();
        TableIndex index = GetOrCreateIndex(table);
        for (int i = 0; i < count; i++)
        {
            int id = reader.ReadInt32();
            long offset = reader.ReadInt64();
            index.Primary[id] = offset;
        }
        return count;
    }

    private void ReadRows(BinaryReader reader, string table, int count)
    {
        TableIndex index = GetOrCreateIndex(table);
        for (int i = 0; i < count; i++)
        {
            long offset = reader.BaseStream.Position;
            Dictionary<string, object> row = ReadRowByTable(table, reader);
            index.Offsets.Add(offset);

            int id;
            if (TryGetInt32(row, QuerySpec.GetPrimaryKeyField(table), out id)) index.Primary[id] = offset;
        }
    }

    private TableIndex GetOrCreateIndex(string table)
    {
        TableIndex index;
        if (!tableIndexes.TryGetValue(table, out index))
        {
            index = new TableIndex();
            tableIndexes.Add(table, index);
        }
        return index;
    }

    private bool TryReadPrimary(string table, int key, out Dictionary<string, object> row)
    {
        TableIndex index;
        long offset;
        if (!tableIndexes.TryGetValue(table, out index) || !index.Primary.TryGetValue(key, out offset))
        {
            row = null;
            return false;
        }

        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var reader = new BinaryReader(fs, Encoding.UTF8))
        {
            fs.Seek(offset, SeekOrigin.Begin);
            row = ReadRowByTable(table, reader);
            return true;
        }
    }

    private IEnumerable<Dictionary<string, object>> EnumerateRows(string table)
    {
        TableIndex index;
        if (!tableIndexes.TryGetValue(table, out index)) yield break;

        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var reader = new BinaryReader(fs, Encoding.UTF8))
        {
            for (int i = 0; i < index.Offsets.Count; i++)
            {
                fs.Seek(index.Offsets[i], SeekOrigin.Begin);
                yield return ReadRowByTable(table, reader);
            }
        }
    }

    private void ExecuteRelation(QuerySpec query, QueryResult result)
    {
        object anchor;
        if (!query.TryGetEquality(query.RelationAnchorField, out anchor))
            throw new InvalidOperationException("관계형 조회 기준값이 없습니다.");

        if (query.ComplexityLevel == 2)
        {
            List<Dictionary<string, object>> characters = ToList(EnumerateRows("Runtime_Character_C2"));
            List<Dictionary<string, object>> items = ToList(EnumerateRows("Master_Item_C2"));
            foreach (Dictionary<string, object> inventory in EnumerateRows("Runtime_Inventory_C2"))
            {
                if (!QueryRuntime.Compare(Value(inventory, "character_id"), anchor, QueryComparison.Equal)) continue;
                Dictionary<string, object> character = FindById(characters, "character_id", Value(inventory, "character_id"));
                Dictionary<string, object> item = FindById(items, "item_id", Value(inventory, "item_id"));
                Dictionary<string, object> joined = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["character_id"] = Value(character, "character_id"), ["character_name"] = Value(character, "name"),
                    ["item_id"] = Value(item, "item_id"), ["item_name"] = Value(item, "name"),
                    ["instance_id"] = Value(inventory, "instance_id"), ["quantity"] = Value(inventory, "quantity")
                };
                if (QueryRuntime.Matches(joined, query.Predicates)) result.AddProjectedRow(query, joined);
            }
            return;
        }

        if (query.ComplexityLevel != 3) throw new NotSupportedException("C1에는 관계형 traversal 대상이 없습니다.");

        List<Dictionary<string, object>> users = ToList(EnumerateRows("System_User_C3"));
        List<Dictionary<string, object>> charactersC3 = ToList(EnumerateRows("Runtime_Character_C3"));
        List<Dictionary<string, object>> itemsC3 = ToList(EnumerateRows("Master_Item_C3"));
        List<Dictionary<string, object>> links = ToList(EnumerateRows("Item_Effect_Link_C3"));
        List<Dictionary<string, object>> effects = ToList(EnumerateRows("Master_Effect_C3"));

        foreach (Dictionary<string, object> character in charactersC3)
        {
            object compareValue = query.RelationAnchorField.Equals("user_id", StringComparison.OrdinalIgnoreCase)
                ? Value(character, "user_id") : Value(character, "character_id");
            if (!QueryRuntime.Compare(compareValue, anchor, QueryComparison.Equal)) continue;

            Dictionary<string, object> user = FindById(users, "user_id", Value(character, "user_id"));
            foreach (Dictionary<string, object> inventory in EnumerateRows("Runtime_Inventory_C3"))
            {
                if (!QueryRuntime.Compare(Value(inventory, "character_id"), Value(character, "character_id"), QueryComparison.Equal)) continue;
                Dictionary<string, object> item = FindById(itemsC3, "item_id", Value(inventory, "item_id"));
                List<Dictionary<string, object>> itemLinks = FindAllById(links, "item_id", Value(item, "item_id"));
                if (itemLinks.Count == 0) itemLinks.Add(null);

                foreach (Dictionary<string, object> link in itemLinks)
                {
                    Dictionary<string, object> effect = link == null ? null : FindById(effects, "effect_id", Value(link, "effect_id"));
                    Dictionary<string, object> joined = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["user_id"] = Value(user, "user_id"), ["account_name"] = Value(user, "account_name"),
                        ["character_id"] = Value(character, "character_id"), ["character_name"] = Value(character, "name"),
                        ["item_id"] = Value(item, "item_id"), ["item_name"] = Value(item, "name"),
                        ["instance_id"] = Value(inventory, "instance_id"), ["effect_type"] = Value(effect, "effect_type")
                    };
                    if (QueryRuntime.Matches(joined, query.Predicates)) result.AddProjectedRow(query, joined);
                }
            }
        }
    }

    private Dictionary<string, object> ReadRowByTable(string table, BinaryReader reader)
    {
        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (table.Equals("Master_Item_C1", StringComparison.OrdinalIgnoreCase))
        {
            row["item_id"] = reader.ReadInt32(); row["name"] = reader.ReadString(); row["item_type"] = reader.ReadString();
            row["rarity"] = reader.ReadInt32(); row["atk"] = reader.ReadInt32(); row["price"] = reader.ReadInt32();
        }
        else if (table.Equals("Runtime_Character_C1", StringComparison.OrdinalIgnoreCase))
        {
            row["character_id"] = reader.ReadInt32(); row["name"] = reader.ReadString(); row["level"] = reader.ReadInt32();
            row["hp"] = reader.ReadInt32(); row["pos_x"] = reader.ReadSingle(); row["pos_y"] = reader.ReadSingle();
        }
        else if (table.Equals("Master_Item_C2", StringComparison.OrdinalIgnoreCase))
        {
            row["item_id"] = reader.ReadInt32(); row["item_code"] = reader.ReadString(); row["name"] = reader.ReadString();
            row["item_type"] = reader.ReadString(); row["rarity"] = reader.ReadInt32(); row["req_level"] = reader.ReadInt32(); row["base_atk"] = reader.ReadInt32();
        }
        else if (table.Equals("Item_Tag_C2", StringComparison.OrdinalIgnoreCase))
        {
            row["item_id"] = reader.ReadInt32(); row["tag_name"] = reader.ReadString();
        }
        else if (table.Equals("Runtime_Character_C2", StringComparison.OrdinalIgnoreCase))
        {
            row["character_id"] = reader.ReadInt32(); row["name"] = reader.ReadString(); row["level"] = reader.ReadString();
            row["cur_hp"] = reader.ReadInt32(); row["pos_x"] = reader.ReadSingle(); row["pos_y"] = reader.ReadSingle();
        }
        else if (table.Equals("Runtime_Inventory_C2", StringComparison.OrdinalIgnoreCase))
        {
            row["instance_id"] = reader.ReadInt32(); row["character_id"] = reader.ReadInt32(); row["item_id"] = reader.ReadInt32(); row["quantity"] = reader.ReadInt32();
        }
        else if (table.Equals("System_User_C3", StringComparison.OrdinalIgnoreCase))
        {
            row["user_id"] = reader.ReadInt32(); row["account_name"] = reader.ReadString(); row["created_at"] = reader.ReadInt64();
        }
        else if (table.Equals("Runtime_Character_C3", StringComparison.OrdinalIgnoreCase))
        {
            row["character_id"] = reader.ReadInt32(); row["user_id"] = reader.ReadInt32(); row["name"] = reader.ReadString(); row["guild_id"] = reader.ReadInt32();
        }
        else if (table.Equals("Master_Item_C3", StringComparison.OrdinalIgnoreCase))
        {
            row["item_id"] = reader.ReadInt32(); row["name"] = reader.ReadString(); row["rarity"] = reader.ReadInt32();
        }
        else if (table.Equals("Master_Effect_C3", StringComparison.OrdinalIgnoreCase))
        {
            row["effect_id"] = reader.ReadInt32(); row["effect_type"] = reader.ReadString(); row["value"] = reader.ReadInt32();
        }
        else if (table.Equals("Item_Effect_Link_C3", StringComparison.OrdinalIgnoreCase))
        {
            row["item_id"] = reader.ReadInt32(); row["effect_id"] = reader.ReadInt32();
        }
        else if (table.Equals("Runtime_Inventory_C3", StringComparison.OrdinalIgnoreCase))
        {
            row["instance_id"] = reader.ReadInt32(); row["character_id"] = reader.ReadInt32(); row["item_id"] = reader.ReadInt32(); row["durability"] = reader.ReadInt32();
        }
        else
        {
            throw new InvalidOperationException("지원하지 않는 Raw Binary 테이블입니다: " + table);
        }
        return row;
    }

    private static List<Dictionary<string, object>> ToList(IEnumerable<Dictionary<string, object>> rows)
    {
        var list = new List<Dictionary<string, object>>();
        foreach (Dictionary<string, object> row in rows) list.Add(row);
        return list;
    }

    private static Dictionary<string, object> FindById(List<Dictionary<string, object>> rows, string field, object value)
    {
        for (int i = 0; i < rows.Count; i++) if (QueryRuntime.Compare(Value(rows[i], field), value, QueryComparison.Equal)) return rows[i];
        return null;
    }

    private static List<Dictionary<string, object>> FindAllById(List<Dictionary<string, object>> rows, string field, object value)
    {
        var list = new List<Dictionary<string, object>>();
        for (int i = 0; i < rows.Count; i++) if (QueryRuntime.Compare(Value(rows[i], field), value, QueryComparison.Equal)) list.Add(rows[i]);
        return list;
    }

    private static object Value(Dictionary<string, object> row, string field)
    {
        if (row == null) return null;
        object value;
        return QueryRuntime.TryGetValue(row, field, out value) ? value : null;
    }

    private static bool TryGetInt32(object value, out int result)
    {
        if (value is int)
        {
            result = (int)value;
            return true;
        }
        return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryGetInt32(Dictionary<string, object> row, string field, out int result)
    {
        object value;
        if (!QueryRuntime.TryGetValue(row, field, out value))
        {
            result = 0;
            return false;
        }
        return TryGetInt32(value, out result);
    }
}
