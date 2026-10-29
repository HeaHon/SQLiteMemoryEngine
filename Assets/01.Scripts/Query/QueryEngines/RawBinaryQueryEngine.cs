using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

public class RawBinaryQueryEngine : IQueryEngine
{
    private sealed class TableIndex
    {
        public int DataStart;
        public int RowCount;
        public readonly Dictionary<int, long> Primary = new Dictionary<int, long>();
        public readonly Dictionary<string, Dictionary<int, List<long>>> Secondary =
            new Dictionary<string, Dictionary<int, List<long>>>(StringComparer.OrdinalIgnoreCase);

        public void AddSecondary(string field, int value, long offset)
        {
            Dictionary<int, List<long>> values;
            if (!Secondary.TryGetValue(field, out values))
            {
                values = new Dictionary<int, List<long>>();
                Secondary.Add(field, values);
            }

            List<long> offsets;
            if (!values.TryGetValue(value, out offsets))
            {
                offsets = new List<long>();
                values.Add(value, offsets);
            }
            offsets.Add(offset);
        }
    }

    private sealed class BinaryCursor
    {
        private readonly byte[] data;
        public int Position { get; private set; }

        public BinaryCursor(byte[] data, int position)
        {
            this.data = data;
            Position = position;
        }

        public int ReadInt32()
        {
            Ensure(sizeof(int));
            int value = BitConverter.ToInt32(data, Position);
            Position += sizeof(int);
            return value;
        }

        public long ReadInt64()
        {
            Ensure(sizeof(long));
            long value = BitConverter.ToInt64(data, Position);
            Position += sizeof(long);
            return value;
        }

        public float ReadSingle()
        {
            Ensure(sizeof(float));
            float value = BitConverter.ToSingle(data, Position);
            Position += sizeof(float);
            return value;
        }

        public string ReadString()
        {
            int byteCount = Read7BitEncodedInt();
            Ensure(byteCount);
            string value = Encoding.UTF8.GetString(data, Position, byteCount);
            Position += byteCount;
            return value;
        }

        public void SkipString()
        {
            int byteCount = Read7BitEncodedInt();
            Ensure(byteCount);
            Position += byteCount;
        }

        private int Read7BitEncodedInt()
        {
            int value = 0;
            int shift = 0;
            while (true)
            {
                Ensure(1);
                byte current = data[Position++];
                value |= (current & 0x7f) << shift;
                if ((current & 0x80) == 0) return value;
                shift += 7;
                if (shift >= 35) throw new InvalidDataException("Raw Binary 문자열 길이가 잘못되었습니다.");
            }
        }

        private void Ensure(int byteCount)
        {
            if (byteCount < 0 || Position > data.Length - byteCount)
                throw new EndOfStreamException("Raw Binary 데이터가 손상되었거나 예상보다 짧습니다.");
        }
    }

    public string FormatName => "RawBinary";
    private readonly string filePath;
    private readonly byte[] buffer;
    private readonly Dictionary<string, TableIndex> tableIndexes =
        new Dictionary<string, TableIndex>(StringComparer.OrdinalIgnoreCase);

    public RawBinaryQueryEngine(string filePath)
    {
        this.filePath = filePath;
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            throw new FileNotFoundException("Raw Binary 파일을 찾을 수 없습니다.", filePath);

        // 파일을 한 번만 읽고 이후 쿼리는 메모리 버퍼와 오프셋 인덱스를 재사용한다.
        buffer = File.ReadAllBytes(filePath);
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
                int key = 0;
                Dictionary<string, object> row;
                bool hasPrimaryKey = query.TryGetEquality(query.PrimaryKeyField, out keyValue) &&
                                     TryGetInt32(keyValue, out key);

                if (hasPrimaryKey)
                {
                    if (TryReadPrimary(query.Table, key, out row) && QueryRuntime.Matches(row, query.Predicates))
                        result.AddProjectedRow(query, row);
                }
                else
                {
                    foreach (Dictionary<string, object> candidate in EnumerateFilteredRows(query.Table, query.Predicates))
                        result.AddProjectedRow(query, candidate);
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
        var cursor = new BinaryCursor(buffer, 0);
        string fileName = Path.GetFileNameWithoutExtension(filePath);

        if (fileName.IndexOf("C1", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            int itemCount = ReadPrimaryHeader(cursor, "Master_Item_C1");
            ScanRows(cursor, "Master_Item_C1", itemCount);
            ScanRows(cursor, "Runtime_Character_C1", cursor.ReadInt32());
            return;
        }

        if (fileName.IndexOf("C2", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            int itemCount = ReadPrimaryHeader(cursor, "Master_Item_C2");
            ScanRows(cursor, "Master_Item_C2", itemCount);
            ScanRows(cursor, "Item_Tag_C2", cursor.ReadInt32());
            ScanRows(cursor, "Runtime_Character_C2", cursor.ReadInt32());
            ScanRows(cursor, "Runtime_Inventory_C2", cursor.ReadInt32());
            return;
        }

        if (fileName.IndexOf("C3", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            int userCount = ReadPrimaryHeader(cursor, "System_User_C3");
            ScanRows(cursor, "System_User_C3", userCount);
            ScanRows(cursor, "Runtime_Character_C3", cursor.ReadInt32());
            ScanRows(cursor, "Master_Item_C3", cursor.ReadInt32());
            ScanRows(cursor, "Master_Effect_C3", cursor.ReadInt32());
            ScanRows(cursor, "Item_Effect_Link_C3", cursor.ReadInt32());
            ScanRows(cursor, "Runtime_Inventory_C3", cursor.ReadInt32());
            return;
        }

        throw new InvalidDataException("Raw Binary 파일명에서 복잡도(C1/C2/C3)를 판별할 수 없습니다.");
    }

    private int ReadPrimaryHeader(BinaryCursor cursor, string table)
    {
        int count = cursor.ReadInt32();
        TableIndex index = GetOrCreateIndex(table);
        for (int i = 0; i < count; i++)
        {
            int id = cursor.ReadInt32();
            long offset = cursor.ReadInt64();
            index.Primary[id] = offset;
        }
        return count;
    }

    private void ScanRows(BinaryCursor cursor, string table, int count)
    {
        TableIndex index = GetOrCreateIndex(table);
        index.DataStart = cursor.Position;
        index.RowCount = count;

        for (int i = 0; i < count; i++)
        {
            long offset = cursor.Position;
            int primary;
            int secondary;
            ScanRow(cursor, table, out primary, out secondary);
            index.Primary[primary] = offset;
            AddRelationIndex(index, table, secondary, offset);
        }
    }

    private static void AddRelationIndex(TableIndex index, string table, int secondary, long offset)
    {
        if (table.Equals("Runtime_Inventory_C2", StringComparison.OrdinalIgnoreCase))
            index.AddSecondary("character_id", secondary, offset);
        else if (table.Equals("Runtime_Character_C3", StringComparison.OrdinalIgnoreCase))
            index.AddSecondary("user_id", secondary, offset);
        else if (table.Equals("Runtime_Inventory_C3", StringComparison.OrdinalIgnoreCase))
            index.AddSecondary("character_id", secondary, offset);
        else if (table.Equals("Item_Effect_Link_C3", StringComparison.OrdinalIgnoreCase))
            index.AddSecondary("item_id", secondary, offset);
    }

    private static void ScanRow(BinaryCursor cursor, string table, out int primary, out int secondary)
    {
        secondary = 0;
        if (table.Equals("Master_Item_C1", StringComparison.OrdinalIgnoreCase))
        {
            primary = cursor.ReadInt32(); cursor.SkipString(); cursor.SkipString();
            cursor.ReadInt32(); cursor.ReadInt32(); cursor.ReadInt32(); return;
        }
        if (table.Equals("Runtime_Character_C1", StringComparison.OrdinalIgnoreCase))
        {
            primary = cursor.ReadInt32(); cursor.SkipString(); cursor.ReadInt32(); cursor.ReadInt32(); cursor.ReadSingle(); cursor.ReadSingle(); return;
        }
        if (table.Equals("Master_Item_C2", StringComparison.OrdinalIgnoreCase))
        {
            primary = cursor.ReadInt32(); cursor.SkipString(); cursor.SkipString(); cursor.SkipString();
            cursor.ReadInt32(); cursor.ReadInt32(); cursor.ReadInt32(); return;
        }
        if (table.Equals("Item_Tag_C2", StringComparison.OrdinalIgnoreCase))
        {
            primary = cursor.ReadInt32(); cursor.SkipString(); return;
        }
        if (table.Equals("Runtime_Character_C2", StringComparison.OrdinalIgnoreCase))
        {
            primary = cursor.ReadInt32(); cursor.SkipString(); cursor.SkipString(); cursor.ReadInt32(); cursor.ReadSingle(); cursor.ReadSingle(); return;
        }
        if (table.Equals("Runtime_Inventory_C2", StringComparison.OrdinalIgnoreCase))
        {
            primary = cursor.ReadInt32(); secondary = cursor.ReadInt32(); cursor.ReadInt32(); cursor.ReadInt32(); return;
        }
        if (table.Equals("System_User_C3", StringComparison.OrdinalIgnoreCase))
        {
            primary = cursor.ReadInt32(); cursor.SkipString(); cursor.ReadInt64(); return;
        }
        if (table.Equals("Runtime_Character_C3", StringComparison.OrdinalIgnoreCase))
        {
            primary = cursor.ReadInt32(); secondary = cursor.ReadInt32(); cursor.SkipString(); cursor.ReadInt32(); return;
        }
        if (table.Equals("Master_Item_C3", StringComparison.OrdinalIgnoreCase))
        {
            primary = cursor.ReadInt32(); cursor.SkipString(); cursor.ReadInt32(); return;
        }
        if (table.Equals("Master_Effect_C3", StringComparison.OrdinalIgnoreCase))
        {
            primary = cursor.ReadInt32(); cursor.SkipString(); cursor.ReadInt32(); return;
        }
        if (table.Equals("Item_Effect_Link_C3", StringComparison.OrdinalIgnoreCase))
        {
            primary = cursor.ReadInt32(); secondary = primary; cursor.ReadInt32(); return;
        }
        if (table.Equals("Runtime_Inventory_C3", StringComparison.OrdinalIgnoreCase))
        {
            primary = cursor.ReadInt32(); secondary = cursor.ReadInt32(); cursor.ReadInt32(); cursor.ReadInt32(); return;
        }

        throw new InvalidDataException("지원하지 않는 Raw Binary 테이블입니다: " + table);
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

        row = ReadRowAt(table, offset);
        return true;
    }

    private IEnumerable<Dictionary<string, object>> EnumerateRows(string table)
    {
        TableIndex index;
        if (!tableIndexes.TryGetValue(table, out index)) yield break;

        var cursor = new BinaryCursor(buffer, index.DataStart);
        for (int i = 0; i < index.RowCount; i++) yield return ReadRowByTable(table, cursor);
    }

    private IEnumerable<Dictionary<string, object>> EnumerateRowsBySecondary(string table, string field, int key)
    {
        TableIndex index;
        Dictionary<int, List<long>> values;
        List<long> offsets;
        if (!tableIndexes.TryGetValue(table, out index) ||
            !index.Secondary.TryGetValue(field, out values) ||
            !values.TryGetValue(key, out offsets)) yield break;

        for (int i = 0; i < offsets.Count; i++) yield return ReadRowAt(table, offsets[i]);
    }

    private Dictionary<string, object> ReadRowAt(string table, long offset)
    {
        if (offset < 0 || offset > int.MaxValue) throw new InvalidDataException("Raw Binary 오프셋이 유효하지 않습니다.");
        return ReadRowByTable(table, new BinaryCursor(buffer, (int)offset));
    }

    private IEnumerable<Dictionary<string, object>> EnumerateFilteredRows(string table, List<QueryPredicate> predicates)
    {
        TableIndex index;
        if (!tableIndexes.TryGetValue(table, out index)) yield break;

        var cursor = new BinaryCursor(buffer, index.DataStart);
        if (table.Equals("Master_Item_C1", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 0; i < index.RowCount; i++)
            {
                int itemId = cursor.ReadInt32();
                string name = cursor.ReadString();
                string itemType = cursor.ReadString();
                int rarity = cursor.ReadInt32();
                int atk = cursor.ReadInt32();
                int price = cursor.ReadInt32();
                if (!MatchesItemC1(predicates, itemId, name, itemType, rarity, atk, price)) continue;
                yield return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["item_id"] = itemId, ["name"] = name, ["item_type"] = itemType,
                    ["rarity"] = rarity, ["atk"] = atk, ["price"] = price
                };
            }
            yield break;
        }

        if (table.Equals("Master_Item_C2", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 0; i < index.RowCount; i++)
            {
                int itemId = cursor.ReadInt32();
                string itemCode = cursor.ReadString();
                string name = cursor.ReadString();
                string itemType = cursor.ReadString();
                int rarity = cursor.ReadInt32();
                int reqLevel = cursor.ReadInt32();
                int baseAtk = cursor.ReadInt32();
                if (!MatchesItemC2(predicates, itemId, itemCode, name, itemType, rarity, reqLevel, baseAtk)) continue;
                yield return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["item_id"] = itemId, ["item_code"] = itemCode, ["name"] = name,
                    ["item_type"] = itemType, ["rarity"] = rarity, ["req_level"] = reqLevel,
                    ["base_atk"] = baseAtk
                };
            }
            yield break;
        }

        if (table.Equals("Master_Item_C3", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 0; i < index.RowCount; i++)
            {
                int itemId = cursor.ReadInt32();
                string name = cursor.ReadString();
                int rarity = cursor.ReadInt32();
                if (!MatchesItemC3(predicates, itemId, name, rarity)) continue;
                yield return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["item_id"] = itemId, ["name"] = name, ["rarity"] = rarity
                };
            }
            yield break;
        }

        foreach (Dictionary<string, object> row in EnumerateRows(table))
            if (QueryRuntime.Matches(row, predicates)) yield return row;
    }

    private static bool MatchesItemC1(List<QueryPredicate> predicates, int itemId, string name, string itemType, int rarity, int atk, int price)
    {
        for (int i = 0; i < predicates.Count; i++)
        {
            QueryPredicate predicate = predicates[i];
            object actual;
            if (predicate.Field.Equals("item_id", StringComparison.OrdinalIgnoreCase)) actual = itemId;
            else if (predicate.Field.Equals("name", StringComparison.OrdinalIgnoreCase)) actual = name;
            else if (predicate.Field.Equals("item_type", StringComparison.OrdinalIgnoreCase)) actual = itemType;
            else if (predicate.Field.Equals("rarity", StringComparison.OrdinalIgnoreCase)) actual = rarity;
            else if (predicate.Field.Equals("atk", StringComparison.OrdinalIgnoreCase)) actual = atk;
            else if (predicate.Field.Equals("price", StringComparison.OrdinalIgnoreCase)) actual = price;
            else return false;
            if (!QueryRuntime.Compare(actual, predicate.Value, predicate.Comparison)) return false;
        }
        return true;
    }

    private static bool MatchesItemC2(List<QueryPredicate> predicates, int itemId, string itemCode, string name, string itemType, int rarity, int reqLevel, int baseAtk)
    {
        for (int i = 0; i < predicates.Count; i++)
        {
            QueryPredicate predicate = predicates[i];
            object actual;
            if (predicate.Field.Equals("item_id", StringComparison.OrdinalIgnoreCase)) actual = itemId;
            else if (predicate.Field.Equals("item_code", StringComparison.OrdinalIgnoreCase)) actual = itemCode;
            else if (predicate.Field.Equals("name", StringComparison.OrdinalIgnoreCase)) actual = name;
            else if (predicate.Field.Equals("item_type", StringComparison.OrdinalIgnoreCase)) actual = itemType;
            else if (predicate.Field.Equals("rarity", StringComparison.OrdinalIgnoreCase)) actual = rarity;
            else if (predicate.Field.Equals("req_level", StringComparison.OrdinalIgnoreCase)) actual = reqLevel;
            else if (predicate.Field.Equals("base_atk", StringComparison.OrdinalIgnoreCase)) actual = baseAtk;
            else return false;
            if (!QueryRuntime.Compare(actual, predicate.Value, predicate.Comparison)) return false;
        }
        return true;
    }

    private static bool MatchesItemC3(List<QueryPredicate> predicates, int itemId, string name, int rarity)
    {
        for (int i = 0; i < predicates.Count; i++)
        {
            QueryPredicate predicate = predicates[i];
            object actual;
            if (predicate.Field.Equals("item_id", StringComparison.OrdinalIgnoreCase)) actual = itemId;
            else if (predicate.Field.Equals("name", StringComparison.OrdinalIgnoreCase)) actual = name;
            else if (predicate.Field.Equals("rarity", StringComparison.OrdinalIgnoreCase)) actual = rarity;
            else return false;
            if (!QueryRuntime.Compare(actual, predicate.Value, predicate.Comparison)) return false;
        }
        return true;
    }

    private void ExecuteRelation(QuerySpec query, QueryResult result)
    {
        object anchor;
        int anchorId;
        if (!query.TryGetEquality(query.RelationAnchorField, out anchor) || !TryGetInt32(anchor, out anchorId))
            throw new InvalidOperationException("관계형 조회 기준값이 없습니다.");

        if (query.ComplexityLevel == 2)
        {
            Dictionary<string, object> character;
            if (!TryReadPrimary("Runtime_Character_C2", anchorId, out character)) return;

            foreach (Dictionary<string, object> inventory in EnumerateRowsBySecondary("Runtime_Inventory_C2", "character_id", anchorId))
            {
                Dictionary<string, object> item;
                if (!TryReadPrimary("Master_Item_C2", Convert.ToInt32(Value(inventory, "item_id")), out item)) continue;
                var joined = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
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

        IEnumerable<Dictionary<string, object>> matchedCharacters =
            query.RelationAnchorField.Equals("user_id", StringComparison.OrdinalIgnoreCase)
                ? EnumerateRowsBySecondary("Runtime_Character_C3", "user_id", anchorId)
                : EnumerateSinglePrimary("Runtime_Character_C3", anchorId);

        foreach (Dictionary<string, object> character in matchedCharacters)
        {
            Dictionary<string, object> user;
            if (!TryReadPrimary("System_User_C3", Convert.ToInt32(Value(character, "user_id")), out user)) continue;
            int characterId = Convert.ToInt32(Value(character, "character_id"));

            foreach (Dictionary<string, object> inventory in EnumerateRowsBySecondary("Runtime_Inventory_C3", "character_id", characterId))
            {
                Dictionary<string, object> item;
                if (!TryReadPrimary("Master_Item_C3", Convert.ToInt32(Value(inventory, "item_id")), out item)) continue;

                List<Dictionary<string, object>> links = ToList(EnumerateRowsBySecondary(
                    "Item_Effect_Link_C3", "item_id", Convert.ToInt32(Value(item, "item_id"))));
                if (links.Count == 0) links.Add(null);

                for (int l = 0; l < links.Count; l++)
                {
                    Dictionary<string, object> link = links[l];
                    Dictionary<string, object> effect = null;
                    if (link != null) TryReadPrimary("Master_Effect_C3", Convert.ToInt32(Value(link, "effect_id")), out effect);

                    var joined = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
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

    private IEnumerable<Dictionary<string, object>> EnumerateSinglePrimary(string table, int key)
    {
        Dictionary<string, object> row;
        if (TryReadPrimary(table, key, out row)) yield return row;
    }

    private Dictionary<string, object> ReadRowByTable(string table, BinaryCursor cursor)
    {
        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (table.Equals("Master_Item_C1", StringComparison.OrdinalIgnoreCase))
        {
            row["item_id"] = cursor.ReadInt32(); row["name"] = cursor.ReadString(); row["item_type"] = cursor.ReadString();
            row["rarity"] = cursor.ReadInt32(); row["atk"] = cursor.ReadInt32(); row["price"] = cursor.ReadInt32();
        }
        else if (table.Equals("Runtime_Character_C1", StringComparison.OrdinalIgnoreCase))
        {
            row["character_id"] = cursor.ReadInt32(); row["name"] = cursor.ReadString(); row["level"] = cursor.ReadInt32();
            row["hp"] = cursor.ReadInt32(); row["pos_x"] = cursor.ReadSingle(); row["pos_y"] = cursor.ReadSingle();
        }
        else if (table.Equals("Master_Item_C2", StringComparison.OrdinalIgnoreCase))
        {
            row["item_id"] = cursor.ReadInt32(); row["item_code"] = cursor.ReadString(); row["name"] = cursor.ReadString(); row["item_type"] = cursor.ReadString();
            row["rarity"] = cursor.ReadInt32(); row["req_level"] = cursor.ReadInt32(); row["base_atk"] = cursor.ReadInt32();
        }
        else if (table.Equals("Item_Tag_C2", StringComparison.OrdinalIgnoreCase))
        {
            row["item_id"] = cursor.ReadInt32(); row["tag_name"] = cursor.ReadString();
        }
        else if (table.Equals("Runtime_Character_C2", StringComparison.OrdinalIgnoreCase))
        {
            row["character_id"] = cursor.ReadInt32(); row["name"] = cursor.ReadString(); row["level"] = cursor.ReadString();
            row["cur_hp"] = cursor.ReadInt32(); row["pos_x"] = cursor.ReadSingle(); row["pos_y"] = cursor.ReadSingle();
        }
        else if (table.Equals("Runtime_Inventory_C2", StringComparison.OrdinalIgnoreCase))
        {
            row["instance_id"] = cursor.ReadInt32(); row["character_id"] = cursor.ReadInt32(); row["item_id"] = cursor.ReadInt32(); row["quantity"] = cursor.ReadInt32();
        }
        else if (table.Equals("System_User_C3", StringComparison.OrdinalIgnoreCase))
        {
            row["user_id"] = cursor.ReadInt32(); row["account_name"] = cursor.ReadString(); row["created_at"] = cursor.ReadInt64();
        }
        else if (table.Equals("Runtime_Character_C3", StringComparison.OrdinalIgnoreCase))
        {
            row["character_id"] = cursor.ReadInt32(); row["user_id"] = cursor.ReadInt32(); row["name"] = cursor.ReadString(); row["guild_id"] = cursor.ReadInt32();
        }
        else if (table.Equals("Master_Item_C3", StringComparison.OrdinalIgnoreCase))
        {
            row["item_id"] = cursor.ReadInt32(); row["name"] = cursor.ReadString(); row["rarity"] = cursor.ReadInt32();
        }
        else if (table.Equals("Master_Effect_C3", StringComparison.OrdinalIgnoreCase))
        {
            row["effect_id"] = cursor.ReadInt32(); row["effect_type"] = cursor.ReadString(); row["value"] = cursor.ReadInt32();
        }
        else if (table.Equals("Item_Effect_Link_C3", StringComparison.OrdinalIgnoreCase))
        {
            row["item_id"] = cursor.ReadInt32(); row["effect_id"] = cursor.ReadInt32();
        }
        else if (table.Equals("Runtime_Inventory_C3", StringComparison.OrdinalIgnoreCase))
        {
            row["instance_id"] = cursor.ReadInt32(); row["character_id"] = cursor.ReadInt32(); row["item_id"] = cursor.ReadInt32(); row["durability"] = cursor.ReadInt32();
        }
        else
        {
            throw new InvalidDataException("지원하지 않는 Raw Binary 테이블입니다: " + table);
        }
        return row;
    }

    private static List<Dictionary<string, object>> ToList(IEnumerable<Dictionary<string, object>> rows)
    {
        var list = new List<Dictionary<string, object>>();
        foreach (Dictionary<string, object> row in rows) list.Add(row);
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
}
