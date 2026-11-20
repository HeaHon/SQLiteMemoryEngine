using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RocksDbSharp;

public class RocksDbQueryEngine : IQueryEngine, IQueryBenchmarkSession
{
    private sealed class TableDescriptor
    {
        public string KeyPrefix { get; set; }
        public Func<BinaryReader, Dictionary<string, object>> BinaryDecoder { get; set; }
    }

    private static readonly Dictionary<string, TableDescriptor> TableRegistry
        = new Dictionary<string, TableDescriptor>(StringComparer.OrdinalIgnoreCase);

    static RocksDbQueryEngine()
    {
        RegisterBinaryTable(nameof(RelMasterItem_C1), "item_", r => new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["item_id"] = r.ReadInt32(),
            ["name"] = r.ReadString(),
            ["item_type"] = r.ReadString(),
            ["rarity"] = r.ReadInt32(),
            ["atk"] = r.ReadInt32(),
            ["price"] = r.ReadInt32()
        });
        RegisterBinaryTable(nameof(RelRuntimeCharacter_C1), "char_", r => new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["character_id"] = r.ReadInt32(),
            ["name"] = r.ReadString(),
            ["level"] = r.ReadInt32(),
            ["hp"] = r.ReadInt32(),
            ["pos_x"] = r.ReadSingle(),
            ["pos_y"] = r.ReadSingle()
        });
        RegisterBinaryTable(nameof(RelMasterItem_C2), "item_", r => new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["item_id"] = r.ReadInt32(),
            ["item_code"] = r.ReadString(),
            ["name"] = r.ReadString(),
            ["item_type"] = r.ReadString(),
            ["rarity"] = r.ReadInt32(),
            ["req_level"] = r.ReadInt32(),
            ["base_atk"] = r.ReadInt32()
        });
        RegisterBinaryTable(nameof(RelItemTag_C2), "tag_", r => new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["item_id"] = r.ReadInt32(),
            ["tag_name"] = r.ReadString()
        });
        RegisterBinaryTable(nameof(RelRuntimeCharacter_C2), "char_", r => new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["character_id"] = r.ReadInt32(),
            ["name"] = r.ReadString(),
            ["level"] = r.ReadString(),
            ["cur_hp"] = r.ReadInt32(),
            ["pos_x"] = r.ReadSingle(),
            ["pos_y"] = r.ReadSingle()
        });
        RegisterBinaryTable(nameof(RelRuntimeInventory_C2), "inv_", r => new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["instance_id"] = r.ReadInt32(),
            ["character_id"] = r.ReadInt32(),
            ["item_id"] = r.ReadInt32(),
            ["quantity"] = r.ReadInt32()
        });
        RegisterBinaryTable(nameof(RelSystemUser_C3), "user_", r => new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["user_id"] = r.ReadInt32(),
            ["account_name"] = r.ReadString(),
            ["created_at"] = r.ReadInt64()
        });
        RegisterBinaryTable(nameof(RelRuntimeCharacter_C3), "char_", r => new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["character_id"] = r.ReadInt32(),
            ["user_id"] = r.ReadInt32(),
            ["name"] = r.ReadString(),
            ["guild_id"] = r.ReadInt32()
        });
        RegisterBinaryTable(nameof(RelMasterItem_C3), "item_", r => new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["item_id"] = r.ReadInt32(),
            ["name"] = r.ReadString(),
            ["rarity"] = r.ReadInt32()
        });
        RegisterBinaryTable(nameof(RelMasterEffect_C3), "effect_", r => new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["effect_id"] = r.ReadInt32(),
            ["effect_type"] = r.ReadString(),
            ["value"] = r.ReadInt32()
        });
        RegisterBinaryTable(nameof(RelItemEffectLink_C3), "link_", r => new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["item_id"] = r.ReadInt32(),
            ["effect_id"] = r.ReadInt32()
        });
        RegisterBinaryTable(nameof(RelRuntimeInventory_C3), "inv_", r => new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["instance_id"] = r.ReadInt32(),
            ["character_id"] = r.ReadInt32(),
            ["item_id"] = r.ReadInt32(),
            ["durability"] = r.ReadInt32()
        });

        RegisterJsonTable("Doc_Item_C1", "doc_item_");
        RegisterJsonTable("Doc_Char_C2", "doc_char_");
        RegisterJsonTable("Doc_User_C3", "doc_user_");
        RegisterJsonTable("Sparse_Item_C1", "sparse_item_");
        RegisterJsonTable("Sparse_Entity_C2", "sparse_ent_");
        RegisterJsonTable("Sparse_Event_C3", "sparse_ev_");
    }

    public static void RegisterBinaryTable(string tableName, string keyPrefix, Func<BinaryReader, Dictionary<string, object>> decoder)
    {
        TableRegistry[tableName] = new TableDescriptor { KeyPrefix = keyPrefix, BinaryDecoder = decoder };
    }

    public static void RegisterJsonTable(string tableName, string keyPrefix)
    {
        TableRegistry[tableName] = new TableDescriptor { KeyPrefix = keyPrefix, BinaryDecoder = null };
    }

    public string FormatName => "RocksDB";
    private readonly string dbFolder;

    private RocksDb _persistentDb;
    private byte[] _preparedPrimaryKeyBytes;
    private byte[] _preparedTablePrefixBytes;
    private string _preparedTablePrefix;
    private bool _preparedIsSecondaryPrefix;

    private static readonly byte[] ByMarkerBytes = Encoding.UTF8.GetBytes("_by_");

    private static bool StartsWithBytes(byte[] source, byte[] prefix)
    {
        if (source == null || prefix == null || source.Length < prefix.Length) return false;
        for (int i = 0; i < prefix.Length; i++)
            if (source[i] != prefix[i]) return false;
        return true;
    }

    private static bool ContainsBytes(byte[] source, byte[] pattern)
    {
        if (source == null || pattern == null || source.Length < pattern.Length) return false;
        for (int i = 0; i <= source.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (source[i + j] != pattern[j]) { match = false; break; }
            }
            if (match) return true;
        }
        return false;
    }

    public RocksDbQueryEngine(string dbFolder)
    {
        this.dbFolder = dbFolder;
    }

    public QueryResult Execute(QuerySpec query)
    {
        if (string.IsNullOrEmpty(dbFolder) || !Directory.Exists(dbFolder))
        {
            return new QueryResult { IsSuccess = false, ErrorMessage = $"RocksDB 디렉토리를 찾을 수 없습니다: {dbFolder}" };
        }

        var options = new DbOptions().SetCreateIfMissing(false);
        using (var db = RocksDb.Open(options, Path.GetFullPath(dbFolder)))
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
        _preparedPrimaryKeyBytes = null;
        _preparedIsSecondaryPrefix = false;

        // Case F: 보조 프리픽스 인덱스 활용 (inv_by_char_, char_by_user_, sparse_ev_by_user_)
        if (TryGetSecondaryPrefix(query, out string secPrefix))
        {
            _preparedTablePrefix = secPrefix;
            _preparedTablePrefixBytes = Encoding.UTF8.GetBytes(secPrefix);
            _preparedIsSecondaryPrefix = true;
            return;
        }

        _preparedTablePrefix = GetKeyPrefix(query.Table);
        _preparedTablePrefixBytes = Encoding.UTF8.GetBytes(_preparedTablePrefix);

        if ((query.Operation == QueryOperation.PrimaryKeyLookup || query.Operation == QueryOperation.PartialFieldAccess)
            && query.TryGetEquality(query.PrimaryKeyField, out object id))
        {
            _preparedPrimaryKeyBytes = Encoding.UTF8.GetBytes(_preparedTablePrefix + Convert.ToString(id));
        }
    }

    public int ExecuteNoAlloc(QuerySpec query)
    {
        if (_persistentDb == null) return -1;

        if (_preparedPrimaryKeyBytes != null)
        {
            byte[] val = _persistentDb.Get(_preparedPrimaryKeyBytes);
            return val != null ? 1 : 0;
        }

        int count = 0;
        using (var iterator = _persistentDb.NewIterator())
        {
            for (iterator.Seek(_preparedTablePrefixBytes); iterator.Valid(); iterator.Next())
            {
                byte[] keyBytes = iterator.Key();
                if (!StartsWithBytes(keyBytes, _preparedTablePrefixBytes)) break;
                if (!_preparedIsSecondaryPrefix && ContainsBytes(keyBytes, ByMarkerBytes)) continue;
                _ = iterator.Value().Length;
                count++;
            }
        }
        return count;
    }

    private static bool TryGetSecondaryPrefix(QuerySpec query, out string prefix)
    {
        if (query.Joins.Count == 0 && query.Table.Equals(nameof(RelRuntimeInventory_C2), StringComparison.OrdinalIgnoreCase)
            && query.TryGetEquality("character_id", out object charId))
        {
            prefix = $"inv_by_char_{charId}_";
            return true;
        }
        if (query.Joins.Count == 0 && query.Table.Equals(nameof(RelRuntimeCharacter_C3), StringComparison.OrdinalIgnoreCase)
            && query.TryGetEquality("user_id", out object userId))
        {
            prefix = $"char_by_user_{userId}_";
            return true;
        }
        if (query.Joins.Count == 0 && query.Table.Equals("Sparse_Event_C3", StringComparison.OrdinalIgnoreCase)
            && query.TryGetEquality("user_id", out object evUserId))
        {
            prefix = $"sparse_ev_by_user_{evUserId}_";
            return true;
        }
        prefix = null;
        return false;
    }

    private void EnsureOpen()
    {
        if (_persistentDb == null)
        {
            if (string.IsNullOrEmpty(dbFolder) || !Directory.Exists(dbFolder))
                throw new DirectoryNotFoundException($"RocksDB 디렉토리를 찾을 수 없습니다: {dbFolder}");

            var options = new DbOptions().SetCreateIfMissing(false);
            _persistentDb = RocksDb.Open(options, Path.GetFullPath(dbFolder));
        }
    }

    public void Dispose()
    {
        _persistentDb?.Dispose();
        _persistentDb = null;
    }

    private static QueryResult ExecuteInternal(RocksDb db, QuerySpec query)
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
                if (query.Table.StartsWith("Doc_", StringComparison.OrdinalIgnoreCase) ||
                    query.Table.StartsWith("Sparse_", StringComparison.OrdinalIgnoreCase))
                {
                    ExecuteDocOrSparseRelation(db, query, result);
                }
                else
                {
                    ExecuteRelation(db, query, result);
                }
            }
            else if (query.Operation == QueryOperation.PrimaryKeyLookup || query.Operation == QueryOperation.PartialFieldAccess)
            {
                if (query.TryGetEquality(query.PrimaryKeyField, out object id))
                {
                    Dictionary<string, object> row = ReadByPrimaryKey(db, query.Table, Convert.ToString(id));
                    if (row != null && QueryRuntime.Matches(row, query.Predicates)) result.AddProjectedRow(query, row);
                }
            }
            else if (TryGetSecondaryPrefix(query, out string secPrefix))
            {
                foreach (Dictionary<string, object> row in EnumerateByKeyPrefix(db, secPrefix, query.Table))
                {
                    if (QueryRuntime.Matches(row, query.Predicates)) result.AddProjectedRow(query, row);
                }
            }
            else
            {
                foreach (Dictionary<string, object> row in EnumerateTable(db, query.Table))
                {
                    if (QueryRuntime.Matches(row, query.Predicates)) result.AddProjectedRow(query, row);
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

    private static void ExecuteDocOrSparseRelation(RocksDb db, QuerySpec query, QueryResult result)
    {
        IEnumerable<Dictionary<string, object>> roots;
        if (query.TryGetEquality(query.PrimaryKeyField, out object pkVal))
        {
            roots = EnumerateSinglePrimary(db, query.Table, Convert.ToString(pkVal));
        }
        else
        {
            roots = EnumerateTable(db, query.Table);
        }

        foreach (Dictionary<string, object> root in roots)
        {
            foreach (var flatRow in DocumentUnwindHelper.Unwind(query.Table, root))
            {
                if (QueryRuntime.Matches(flatRow, query.Predicates))
                {
                    result.AddProjectedRow(query, flatRow);
                }
            }
        }
    }

    private static void ExecuteRelation(RocksDb db, QuerySpec query, QueryResult result)
    {
        if (query.Joins.Exists(j => j.Table.IndexOf("Tag", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            if (!query.TryGetEquality("tag_name", out object tagVal))
                throw new InvalidOperationException("Tag 조인 조회에는 tag_name 조건이 필요합니다.");
            string targetTag = Convert.ToString(tagVal);
            foreach (var tagRow in EnumerateTable(db, nameof(RelItemTag_C2)))
            {
                if (!string.Equals(Convert.ToString(Value(tagRow, "tag_name")), targetTag, StringComparison.Ordinal)) continue;
                var item = ReadByPrimaryKey(db, nameof(RelMasterItem_C2), Convert.ToString(Value(tagRow, "item_id")));
                if (item == null) continue;
                var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["item_id"] = Value(item, "item_id"),
                    ["name"] = Value(item, "name"),
                    ["tag_name"] = Value(tagRow, "tag_name")
                };
                if (QueryRuntime.Matches(row, query.Predicates)) result.AddProjectedRow(query, row);
            }
            return;
        }
        if (!query.TryGetEquality(query.RelationAnchorField, out object anchor))
            throw new InvalidOperationException("관계형 조회 기준값이 없습니다.");

        if (query.ComplexityLevel == 2)
        {
            int characterId = Convert.ToInt32(anchor);
            Dictionary<string, object> character = ReadByPrimaryKey(db, nameof(RelRuntimeCharacter_C2), characterId.ToString());
            if (character == null) return;

            foreach (Dictionary<string, object> inventory in EnumerateByKeyPrefix(db, $"inv_by_char_{characterId}_", nameof(RelRuntimeInventory_C2)))
            {
                Dictionary<string, object> item = ReadByPrimaryKey(db, nameof(RelMasterItem_C2), Convert.ToString(Value(inventory, "item_id")));
                if (item == null) continue;
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

        int anchorId = Convert.ToInt32(anchor);
        IEnumerable<Dictionary<string, object>> matchedCharacters = query.RelationAnchorField.Equals("user_id", StringComparison.OrdinalIgnoreCase)
            ? EnumerateByKeyPrefix(db, $"char_by_user_{anchorId}_", nameof(RelRuntimeCharacter_C3))
            : EnumerateSinglePrimary(db, nameof(RelRuntimeCharacter_C3), anchorId.ToString());

        foreach (Dictionary<string, object> character in matchedCharacters)
        {
            Dictionary<string, object> user = ReadByPrimaryKey(db, nameof(RelSystemUser_C3), Convert.ToString(Value(character, "user_id")));
            int characterId = Convert.ToInt32(Value(character, "character_id"));
            foreach (Dictionary<string, object> inventory in EnumerateByKeyPrefix(db, $"inv_by_char_{characterId}_", nameof(RelRuntimeInventory_C3)))
            {
                Dictionary<string, object> item = ReadByPrimaryKey(db, nameof(RelMasterItem_C3), Convert.ToString(Value(inventory, "item_id")));
                if (item == null) continue;
                var links = ToList(EnumerateByKeyPrefix(db, $"link_{Convert.ToInt32(Value(item, "item_id"))}_", nameof(RelItemEffectLink_C3)));
                if (links.Count == 0) links.Add(null);

                foreach (Dictionary<string, object> link in links)
                {
                    Dictionary<string, object> effect = link == null ? null : ReadByPrimaryKey(db, nameof(RelMasterEffect_C3), Convert.ToString(Value(link, "effect_id")));
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

    private static Dictionary<string, object> ReadByPrimaryKey(RocksDb db, string table, string id)
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

    private static IEnumerable<Dictionary<string, object>> EnumerateByKeyPrefix(RocksDb db, string keyPrefix, string table)
    {
        byte[] prefixBytes = Encoding.UTF8.GetBytes(keyPrefix);
        using (var iterator = db.NewIterator())
        {
            for (iterator.Seek(prefixBytes); iterator.Valid(); iterator.Next())
            {
                string key = Encoding.UTF8.GetString(iterator.Key());
                if (!key.StartsWith(keyPrefix, StringComparison.Ordinal)) break;
                yield return ReadRowByTable(table, iterator.Value());
            }
        }
    }

    private static IEnumerable<Dictionary<string, object>> EnumerateSinglePrimary(RocksDb db, string table, string id)
    {
        Dictionary<string, object> row = ReadByPrimaryKey(db, table, id);
        if (row != null) yield return row;
    }

    private static Dictionary<string, object> ReadRowByTable(string table, byte[] bytes)
    {
        if (TableRegistry.TryGetValue(table, out var desc) && desc.BinaryDecoder != null)
        {
            using (var stream = new MemoryStream(bytes))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                return desc.BinaryDecoder(reader);
            }
        }

        string json = Encoding.UTF8.GetString(bytes);
        var parsed = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
        var normalized = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (parsed != null)
        {
            foreach (var kv in parsed) normalized[kv.Key] = NormalizeJToken(kv.Value);
        }
        return normalized;
    }

    private static object NormalizeJToken(object value)
    {
        if (value is JArray arr)
        {
            var list = new List<object>(arr.Count);
            foreach (var item in arr) list.Add(NormalizeJToken(item));
            return list;
        }
        if (value is JObject obj)
        {
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in obj.Properties()) dict[prop.Name] = NormalizeJToken(prop.Value);
            return dict;
        }
        if (value is JValue jv) return jv.Value;
        return value;
    }

    private static string GetKeyPrefix(string table)
    {
        if (TableRegistry.TryGetValue(table, out var desc)) return desc.KeyPrefix;
        if (table.IndexOf("LINK", StringComparison.OrdinalIgnoreCase) >= 0) return "link_";
        if (table.IndexOf("USER", StringComparison.OrdinalIgnoreCase) >= 0) return "user_";
        if (table.IndexOf("CHAR", StringComparison.OrdinalIgnoreCase) >= 0) return "char_";
        if (table.IndexOf("INV", StringComparison.OrdinalIgnoreCase) >= 0) return "inv_";
        if (table.IndexOf("EFF", StringComparison.OrdinalIgnoreCase) >= 0) return "effect_";
        if (table.IndexOf("TAG", StringComparison.OrdinalIgnoreCase) >= 0) return "tag_";
        return "item_";
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
        return QueryRuntime.TryGetValue(row, field, out var value) ? value : null;
    }
}