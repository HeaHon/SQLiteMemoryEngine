using System;
using System.Collections.Generic;
using System.IO;
using Google.FlatBuffers;
using GameData.Schema;

public class FlatBuffersQueryEngine : IQueryEngine
{
    public string FormatName => "FlatBuffers";
    private readonly byte[] buffer;
    private readonly MasterGameDataSet rootDataSet;

    public FlatBuffersQueryEngine(string filePath)
    {
        buffer = File.ReadAllBytes(filePath);
        var byteBuffer = new ByteBuffer(buffer);
        rootDataSet = MasterGameDataSet.GetRootAsMasterGameDataSet(byteBuffer);
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
            if (query.Operation == QueryOperation.RelationTraversal) ExecuteRelation(query, result);
            else if ((query.Operation == QueryOperation.PrimaryKeyLookup || query.Operation == QueryOperation.PartialFieldAccess) &&
                     TryExecuteIndexedLookup(query, result)) { }
            else
            {
                foreach (Dictionary<string, object> row in EnumerateTable(query.Table))
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

    private bool TryExecuteIndexedLookup(QuerySpec query, QueryResult result)
    {
        object keyValue;
        int key;
        if (!query.TryGetEquality(query.PrimaryKeyField, out keyValue) ||
            !int.TryParse(Convert.ToString(keyValue), out key)) return false;

        bool partial = query.Operation == QueryOperation.PartialFieldAccess && query.Projection.Count == 1 && query.Predicates.Count == 1;
        string field = partial ? query.Projection[0].Field : null;
        if (query.Table.IndexOf("Master_Item_C1", StringComparison.OrdinalIgnoreCase) >= 0 && rootDataSet.C1Data.HasValue)
        {
            var data = rootDataSet.C1Data.Value;
            int index = FindItemC1(data, key);
            if (index < 0) return true;
            var item = data.Items(index).Value;
            var row = partial ? CreatePartialC1Row(item, field) : CreateFullC1Row(item);
            if (QueryRuntime.Matches(row, query.Predicates)) result.AddProjectedRow(query, row);
            return true;
        }

        if (query.Table.IndexOf("Master_Item_C2", StringComparison.OrdinalIgnoreCase) >= 0 && rootDataSet.C2Data.HasValue)
        {
            var data = rootDataSet.C2Data.Value;
            int index = FindItemC2(data, key);
            if (index < 0) return true;
            var item = data.Items(index).Value;
            var row = partial ? CreatePartialC2Row(item, field) : CreateFullC2Row(item);
            if (QueryRuntime.Matches(row, query.Predicates)) result.AddProjectedRow(query, row);
            return true;
        }

        if (query.Table.IndexOf("Master_Item_C3", StringComparison.OrdinalIgnoreCase) >= 0 && rootDataSet.C3Data.HasValue)
        {
            var data = rootDataSet.C3Data.Value;
            int index = FindItemC3(data, key);
            if (index < 0) return true;
            var item = data.Items(index).Value;
            var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["item_id"] = item.ItemId,
                ["name"] = item.Name,
                ["rarity"] = item.Rarity
            };
            if (QueryRuntime.Matches(row, query.Predicates)) result.AddProjectedRow(query, row);
            return true;
        }

        return false;
    }

    private static int FindItemC1(DataSet_C1_Schema data, int key)
    {
        int low = 0;
        int high = data.ItemsLength - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            int value = data.Items(middle).Value.ItemId;
            if (value == key) return middle;
            if (value < key) low = middle + 1;
            else high = middle - 1;
        }
        return -1;
    }

    private static int FindItemC2(DataSet_C2_Schema data, int key)
    {
        int low = 0;
        int high = data.ItemsLength - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            int value = data.Items(middle).Value.ItemId;
            if (value == key) return middle;
            if (value < key) low = middle + 1;
            else high = middle - 1;
        }
        return -1;
    }

    private static int FindItemC3(DataSet_C3_Schema data, int key)
    {
        int low = 0;
        int high = data.ItemsLength - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            int value = data.Items(middle).Value.ItemId;
            if (value == key) return middle;
            if (value < key) low = middle + 1;
            else high = middle - 1;
        }
        return -1;
    }

    private static Dictionary<string, object> CreateFullC1Row(Item_C1_Table item)
    {
        return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["item_id"] = item.ItemId, ["name"] = item.Name, ["item_type"] = item.ItemType,
            ["rarity"] = item.Rarity, ["atk"] = item.Atk, ["price"] = item.Price
        };
    }

    private static Dictionary<string, object> CreateFullC2Row(Item_C2_Table item)
    {
        return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["item_id"] = item.ItemId, ["item_code"] = item.ItemCode, ["name"] = item.Name,
            ["item_type"] = item.ItemType, ["rarity"] = item.Rarity, ["req_level"] = item.ReqLevel,
            ["base_atk"] = item.BaseAtk
        };
    }

    private static Dictionary<string, object> CreatePartialC1Row(Item_C1_Table item, string field)
    {
        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["item_id"] = item.ItemId };
        AddItemField(row, field, item.Name, item.ItemType, item.Rarity, item.Atk, item.Price);
        return row;
    }

    private static Dictionary<string, object> CreatePartialC2Row(Item_C2_Table item, string field)
    {
        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["item_id"] = item.ItemId };
        AddItemField(row, field, item.Name, item.ItemCode, item.Rarity, item.ReqLevel, item.BaseAtk);
        return row;
    }

    private static void AddItemField(Dictionary<string, object> row, string field, object first, object second, object third, object fourth, object fifth)
    {
        if (field.Equals("name", StringComparison.OrdinalIgnoreCase)) row["name"] = first;
        else if (field.Equals("item_type", StringComparison.OrdinalIgnoreCase) || field.Equals("item_code", StringComparison.OrdinalIgnoreCase)) row[field] = second;
        else if (field.Equals("rarity", StringComparison.OrdinalIgnoreCase)) row["rarity"] = third;
        else if (field.Equals("atk", StringComparison.OrdinalIgnoreCase) || field.Equals("req_level", StringComparison.OrdinalIgnoreCase)) row[field] = fourth;
        else if (field.Equals("price", StringComparison.OrdinalIgnoreCase) || field.Equals("base_atk", StringComparison.OrdinalIgnoreCase)) row[field] = fifth;
    }

    public QueryResult ExecuteSql(string sqlQuery)
    {
        QueryParseResult parsed = QuerySpecParser.Parse(sqlQuery);
        if (!parsed.IsSuccess) return new QueryResult { IsSuccess = false, ErrorMessage = parsed.ErrorMessage };
        return Execute(parsed.Spec);
    }

    private IEnumerable<Dictionary<string, object>> EnumerateTable(string table)
    {
        if (table.IndexOf("Master_Item_C1", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            if (!rootDataSet.C1Data.HasValue) yield break;
            var data = rootDataSet.C1Data.Value;
            for (int i = 0; i < data.ItemsLength; i++)
            {
                var item = data.Items(i).Value;
                yield return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["item_id"] = item.ItemId, ["name"] = item.Name, ["item_type"] = item.ItemType,
                    ["rarity"] = item.Rarity, ["atk"] = item.Atk, ["price"] = item.Price
                };
            }
            yield break;
        }

        if (table.IndexOf("Runtime_Character_C1", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            if (!rootDataSet.C1Data.HasValue) yield break;
            var data = rootDataSet.C1Data.Value;
            for (int i = 0; i < data.CharactersLength; i++)
            {
                var item = data.Characters(i).Value;
                yield return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["character_id"] = item.CharacterId, ["name"] = item.Name, ["level"] = item.Level,
                    ["hp"] = item.Hp, ["pos_x"] = item.PosX, ["pos_y"] = item.PosY
                };
            }
            yield break;
        }

        if (table.IndexOf("C2", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            if (!rootDataSet.C2Data.HasValue) yield break;
            var data = rootDataSet.C2Data.Value;
            if (table.IndexOf("Master_Item", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                for (int i = 0; i < data.ItemsLength; i++)
                {
                    var item = data.Items(i).Value;
                    yield return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["item_id"] = item.ItemId, ["item_code"] = item.ItemCode, ["name"] = item.Name, ["item_type"] = item.ItemType,
                        ["rarity"] = item.Rarity, ["req_level"] = item.ReqLevel, ["base_atk"] = item.BaseAtk
                    };
                }
            }
            else if (table.IndexOf("Item_Tag", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                for (int i = 0; i < data.TagsLength; i++)
                {
                    var item = data.Tags(i).Value;
                    yield return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["item_id"] = item.ItemId, ["tag_name"] = item.TagName };
                }
            }
            else if (table.IndexOf("Runtime_Character", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                for (int i = 0; i < data.CharactersLength; i++)
                {
                    var item = data.Characters(i).Value;
                    yield return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["character_id"] = item.CharacterId, ["name"] = item.Name, ["level"] = item.Level,
                        ["cur_hp"] = item.CurHp, ["pos_x"] = item.PosX, ["pos_y"] = item.PosY
                    };
                }
            }
            else if (table.IndexOf("Inventory", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                for (int i = 0; i < data.InventoriesLength; i++)
                {
                    var item = data.Inventories(i).Value;
                    yield return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["instance_id"] = item.InstanceId, ["character_id"] = item.CharacterId, ["item_id"] = item.ItemId, ["quantity"] = item.Quantity
                    };
                }
            }
            yield break;
        }

        if (table.IndexOf("C3", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            if (!rootDataSet.C3Data.HasValue) yield break;
            var data = rootDataSet.C3Data.Value;
            if (table.IndexOf("System_User", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                for (int i = 0; i < data.UsersLength; i++)
                {
                    var item = data.Users(i).Value;
                    yield return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["user_id"] = item.UserId, ["account_name"] = item.AccountName, ["created_at"] = item.CreatedAt
                    };
                }
            }
            else if (table.IndexOf("Runtime_Character", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                for (int i = 0; i < data.CharactersLength; i++)
                {
                    var item = data.Characters(i).Value;
                    yield return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["character_id"] = item.CharacterId, ["user_id"] = item.UserId, ["name"] = item.Name, ["guild_id"] = item.GuildId
                    };
                }
            }
            else if (table.IndexOf("Master_Item", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                for (int i = 0; i < data.ItemsLength; i++)
                {
                    var item = data.Items(i).Value;
                    yield return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["item_id"] = item.ItemId, ["name"] = item.Name, ["rarity"] = item.Rarity };
                }
            }
            else if (table.IndexOf("Effect_Link", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                for (int i = 0; i < data.LinksLength; i++)
                {
                    var item = data.Links(i).Value;
                    yield return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["item_id"] = item.ItemId, ["effect_id"] = item.EffectId };
                }
            }
            else if (table.IndexOf("Effect", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                for (int i = 0; i < data.EffectsLength; i++)
                {
                    var item = data.Effects(i).Value;
                    yield return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["effect_id"] = item.EffectId, ["effect_type"] = item.EffectType, ["value"] = item.Value };
                }
            }
            else if (table.IndexOf("Inventory", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                for (int i = 0; i < data.InventoriesLength; i++)
                {
                    var item = data.Inventories(i).Value;
                    yield return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["instance_id"] = item.InstanceId, ["character_id"] = item.CharacterId, ["item_id"] = item.ItemId, ["durability"] = item.Durability
                    };
                }
            }
        }
    }

    private void ExecuteRelation(QuerySpec query, QueryResult result)
    {
        object anchor;
        if (!query.TryGetEquality(query.RelationAnchorField, out anchor)) throw new InvalidOperationException("관계형 조회 기준값이 없습니다.");

        if (query.ComplexityLevel == 2)
        {
            if (!rootDataSet.C2Data.HasValue) return;
            var data = rootDataSet.C2Data.Value;
            int characterId = Convert.ToInt32(anchor);
            Dictionary<string, object> character = FindCharacterC2(data, characterId);
            if (character == null) return;

            foreach (Dictionary<string, object> inventory in EnumerateC2InventoriesByCharacter(data, characterId))
            {
                Dictionary<string, object> item = FindItemC2Row(data, Convert.ToInt32(Value(inventory, "item_id")));
                if (item == null) continue;
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
        if (!rootDataSet.C3Data.HasValue) return;
        var c3Data = rootDataSet.C3Data.Value;
        int anchorId = Convert.ToInt32(anchor);

        IEnumerable<Dictionary<string, object>> matchedCharacters = query.RelationAnchorField.Equals("user_id", StringComparison.OrdinalIgnoreCase)
            ? EnumerateC3CharactersByUser(c3Data, anchorId)
            : EnumerateSingleCharacterC3(c3Data, anchorId);

        foreach (Dictionary<string, object> character in matchedCharacters)
        {
            Dictionary<string, object> user = FindUserC3(c3Data, Convert.ToInt32(Value(character, "user_id")));
            if (user == null) continue;

            int characterId = Convert.ToInt32(Value(character, "character_id"));
            foreach (Dictionary<string, object> inventory in EnumerateC3InventoriesByCharacter(c3Data, characterId))
            {
                Dictionary<string, object> item = FindItemC3Row(c3Data, Convert.ToInt32(Value(inventory, "item_id")));
                if (item == null) continue;
                List<Dictionary<string, object>> itemLinks = ToList(EnumerateC3LinksByItem(c3Data, Convert.ToInt32(Value(item, "item_id"))));
                if (itemLinks.Count == 0) itemLinks.Add(null);

                foreach (Dictionary<string, object> link in itemLinks)
                {
                    Dictionary<string, object> effect = link == null ? null : FindEffectC3(c3Data, Convert.ToInt32(Value(link, "effect_id")));
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

    private static Dictionary<string, object> FindCharacterC2(DataSet_C2_Schema data, int key)
    {
        int low = 0;
        int high = data.CharactersLength - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            var value = data.Characters(middle).Value;
            if (value.CharacterId == key)
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["character_id"] = value.CharacterId, ["name"] = value.Name, ["level"] = value.Level,
                    ["cur_hp"] = value.CurHp, ["pos_x"] = value.PosX, ["pos_y"] = value.PosY
                };
            if (value.CharacterId < key) low = middle + 1;
            else high = middle - 1;
        }
        return null;
    }

    private static Dictionary<string, object> FindItemC2Row(DataSet_C2_Schema data, int key)
    {
        int low = 0;
        int high = data.ItemsLength - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            var value = data.Items(middle).Value;
            if (value.ItemId == key)
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["item_id"] = value.ItemId, ["item_code"] = value.ItemCode, ["name"] = value.Name,
                    ["item_type"] = value.ItemType, ["rarity"] = value.Rarity, ["req_level"] = value.ReqLevel,
                    ["base_atk"] = value.BaseAtk
                };
            if (value.ItemId < key) low = middle + 1;
            else high = middle - 1;
        }
        return null;
    }

    private static IEnumerable<Dictionary<string, object>> EnumerateC2InventoriesByCharacter(DataSet_C2_Schema data, int characterId)
    {
        for (int i = 0; i < data.InventoriesLength; i++)
        {
            var value = data.Inventories(i).Value;
            if (value.CharacterId != characterId) continue;
            yield return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["instance_id"] = value.InstanceId, ["character_id"] = value.CharacterId,
                ["item_id"] = value.ItemId, ["quantity"] = value.Quantity
            };
        }
    }

    private static Dictionary<string, object> FindUserC3(DataSet_C3_Schema data, int key)
    {
        int low = 0;
        int high = data.UsersLength - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            var value = data.Users(middle).Value;
            if (value.UserId == key)
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["user_id"] = value.UserId, ["account_name"] = value.AccountName, ["created_at"] = value.CreatedAt
                };
            if (value.UserId < key) low = middle + 1;
            else high = middle - 1;
        }
        return null;
    }

    private static Dictionary<string, object> FindItemC3Row(DataSet_C3_Schema data, int key)
    {
        int low = 0;
        int high = data.ItemsLength - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            var value = data.Items(middle).Value;
            if (value.ItemId == key)
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["item_id"] = value.ItemId, ["name"] = value.Name, ["rarity"] = value.Rarity
                };
            if (value.ItemId < key) low = middle + 1;
            else high = middle - 1;
        }
        return null;
    }

    private static Dictionary<string, object> FindEffectC3(DataSet_C3_Schema data, int key)
    {
        int low = 0;
        int high = data.EffectsLength - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            var value = data.Effects(middle).Value;
            if (value.EffectId == key)
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["effect_id"] = value.EffectId, ["effect_type"] = value.EffectType, ["value"] = value.Value
                };
            if (value.EffectId < key) low = middle + 1;
            else high = middle - 1;
        }
        return null;
    }

    private static IEnumerable<Dictionary<string, object>> EnumerateC3CharactersByUser(DataSet_C3_Schema data, int userId)
    {
        for (int i = 0; i < data.CharactersLength; i++)
        {
            var value = data.Characters(i).Value;
            if (value.UserId != userId) continue;
            yield return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["character_id"] = value.CharacterId, ["user_id"] = value.UserId,
                ["name"] = value.Name, ["guild_id"] = value.GuildId
            };
        }
    }

    private static IEnumerable<Dictionary<string, object>> EnumerateSingleCharacterC3(DataSet_C3_Schema data, int characterId)
    {
        int low = 0;
        int high = data.CharactersLength - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            var value = data.Characters(middle).Value;
            if (value.CharacterId == characterId)
            {
                yield return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["character_id"] = value.CharacterId, ["user_id"] = value.UserId,
                    ["name"] = value.Name, ["guild_id"] = value.GuildId
                };
                yield break;
            }
            if (value.CharacterId < characterId) low = middle + 1;
            else high = middle - 1;
        }
    }

    private static IEnumerable<Dictionary<string, object>> EnumerateC3InventoriesByCharacter(DataSet_C3_Schema data, int characterId)
    {
        for (int i = 0; i < data.InventoriesLength; i++)
        {
            var value = data.Inventories(i).Value;
            if (value.CharacterId != characterId) continue;
            yield return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["instance_id"] = value.InstanceId, ["character_id"] = value.CharacterId,
                ["item_id"] = value.ItemId, ["durability"] = value.Durability
            };
        }
    }

    private static IEnumerable<Dictionary<string, object>> EnumerateC3LinksByItem(DataSet_C3_Schema data, int itemId)
    {
        for (int i = 0; i < data.LinksLength; i++)
        {
            var value = data.Links(i).Value;
            if (value.ItemId != itemId) continue;
            yield return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["item_id"] = value.ItemId, ["effect_id"] = value.EffectId
            };
        }
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
}
