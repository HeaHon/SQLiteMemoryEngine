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
            else if (query.Operation == QueryOperation.PartialFieldAccess && TryExecutePartial(query, result)) { }
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

    private bool TryExecutePartial(QuerySpec query, QueryResult result)
    {
        object keyValue;
        int key;
        if (query.Projection.Count != 1 || query.Predicates.Count != 1 || !query.TryGetEquality(query.PrimaryKeyField, out keyValue) ||
            !int.TryParse(Convert.ToString(keyValue), out key)) return false;

        string field = query.Projection[0].Field;
        if (query.Table.IndexOf("Master_Item_C1", StringComparison.OrdinalIgnoreCase) >= 0 && rootDataSet.C1Data.HasValue)
        {
            var data = rootDataSet.C1Data.Value;
            for (int i = 0; i < data.ItemsLength; i++)
            {
                var item = data.Items(i).Value;
                if (item.ItemId != key) continue;
                var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["item_id"] = item.ItemId };
                AddItemField(row, field, item.Name, item.ItemType, item.Rarity, item.Atk, item.Price);
                if (QueryRuntime.Matches(row, query.Predicates)) result.AddProjectedRow(query, row);
                return true;
            }
            return true;
        }

        if (query.Table.IndexOf("Master_Item_C2", StringComparison.OrdinalIgnoreCase) >= 0 && rootDataSet.C2Data.HasValue)
        {
            var data = rootDataSet.C2Data.Value;
            for (int i = 0; i < data.ItemsLength; i++)
            {
                var item = data.Items(i).Value;
                if (item.ItemId != key) continue;
                var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["item_id"] = item.ItemId };
                AddItemField(row, field, item.Name, item.ItemCode, item.Rarity, item.ReqLevel, item.BaseAtk);
                if (QueryRuntime.Matches(row, query.Predicates)) result.AddProjectedRow(query, row);
                return true;
            }
            return true;
        }

        if (query.Table.IndexOf("Master_Item_C3", StringComparison.OrdinalIgnoreCase) >= 0 && rootDataSet.C3Data.HasValue)
        {
            var data = rootDataSet.C3Data.Value;
            for (int i = 0; i < data.ItemsLength; i++)
            {
                var item = data.Items(i).Value;
                if (item.ItemId != key) continue;
                var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["item_id"] = item.ItemId, ["name"] = item.Name, ["rarity"] = item.Rarity };
                if (QueryRuntime.Matches(row, query.Predicates)) result.AddProjectedRow(query, row);
                return true;
            }
            return true;
        }

        return false;
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
            List<Dictionary<string, object>> characters = ToList(EnumerateTable("Runtime_Character_C2"));
            List<Dictionary<string, object>> items = ToList(EnumerateTable("Master_Item_C2"));
            foreach (Dictionary<string, object> inventory in EnumerateTable("Runtime_Inventory_C2"))
            {
                if (!QueryRuntime.Compare(Value(inventory, "character_id"), anchor, QueryComparison.Equal)) continue;
                Dictionary<string, object> character = FindById(characters, "character_id", Value(inventory, "character_id"));
                Dictionary<string, object> item = FindById(items, "item_id", Value(inventory, "item_id"));
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
        List<Dictionary<string, object>> users = ToList(EnumerateTable("System_User_C3"));
        List<Dictionary<string, object>> charactersC3 = ToList(EnumerateTable("Runtime_Character_C3"));
        List<Dictionary<string, object>> itemsC3 = ToList(EnumerateTable("Master_Item_C3"));
        List<Dictionary<string, object>> links = ToList(EnumerateTable("Item_Effect_Link_C3"));
        List<Dictionary<string, object>> effects = ToList(EnumerateTable("Effect_C3"));

        foreach (Dictionary<string, object> character in charactersC3)
        {
            object compareValue = query.RelationAnchorField.Equals("user_id", StringComparison.OrdinalIgnoreCase)
                ? Value(character, "user_id") : Value(character, "character_id");
            if (!QueryRuntime.Compare(compareValue, anchor, QueryComparison.Equal)) continue;
            Dictionary<string, object> user = FindById(users, "user_id", Value(character, "user_id"));

            foreach (Dictionary<string, object> inventory in EnumerateTable("Runtime_Inventory_C3"))
            {
                if (!QueryRuntime.Compare(Value(inventory, "character_id"), Value(character, "character_id"), QueryComparison.Equal)) continue;
                Dictionary<string, object> item = FindById(itemsC3, "item_id", Value(inventory, "item_id"));
                List<Dictionary<string, object>> itemLinks = FindAllById(links, "item_id", Value(item, "item_id"));
                if (itemLinks.Count == 0) itemLinks.Add(null);

                foreach (Dictionary<string, object> link in itemLinks)
                {
                    Dictionary<string, object> effect = link == null ? null : FindById(effects, "effect_id", Value(link, "effect_id"));
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
