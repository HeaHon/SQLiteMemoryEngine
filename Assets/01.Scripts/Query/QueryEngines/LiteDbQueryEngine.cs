using System;
using System.Collections;
using System.Collections.Generic;
using LiteDB;

public class LiteDbQueryEngine : IQueryEngine, IQueryBenchmarkSession
{
    public string FormatName => "LiteDB";
    private readonly string dbPath;

    private LiteDatabase _persistentDb;
    private string _preparedCollectionName;
    private BsonExpression _preparedFilter;
    private ILiteCollection<BsonDocument> _preparedCollection;

    public LiteDbQueryEngine(string dbPath)
    {
        this.dbPath = dbPath;
    }

    public QueryResult Execute(QuerySpec query)
    {
        using (var db = new LiteDatabase($"Filename={dbPath};ReadOnly=true;"))
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
        _preparedCollectionName = QuerySpec.GetCollectionName(query.Table);
        _preparedCollection = _persistentDb.GetCollection<BsonDocument>(_preparedCollectionName);

        // RelationTraversal on Doc/Sparse filters on unwound child fields in C#
        if (query.Operation == QueryOperation.RelationTraversal &&
            (query.Table.StartsWith("Doc_", StringComparison.OrdinalIgnoreCase) ||
             query.Table.StartsWith("Sparse_", StringComparison.OrdinalIgnoreCase)))
        {
            _preparedFilter = BuildRootOnlyFilter(query);
        }
        else
        {
            _preparedFilter = BuildBsonFilter(query.Predicates);
        }
    }

    public int ExecuteNoAlloc(QuerySpec query)
    {
        if (_persistentDb == null || _preparedCollection == null) return -1;

        if (query.Operation == QueryOperation.RelationTraversal)
        {
            if (query.Table.StartsWith("Doc_", StringComparison.OrdinalIgnoreCase) ||
                query.Table.StartsWith("Sparse_", StringComparison.OrdinalIgnoreCase))
            {
                return CountBsonDocuments(_preparedCollection, _preparedFilter);
            }
            return CountRelationNoAlloc(_persistentDb, query);
        }

        return CountBsonDocuments(_preparedCollection, _preparedFilter);
    }

    private void EnsureOpen()
    {
        if (_persistentDb == null)
        {
            _persistentDb = new LiteDatabase($"Filename={dbPath};ReadOnly=true;");
        }
    }

    public void Dispose()
    {
        _persistentDb?.Dispose();
        _persistentDb = null;
    }

    private static int CountBsonDocuments(ILiteCollection<BsonDocument> collection, BsonExpression filter)
    {
        int count = 0;
        IEnumerable<BsonDocument> docs = filter == null ? collection.FindAll() : collection.Find(filter);
        foreach (BsonDocument doc in docs)
        {
            _ = doc.Count;
            count++;
        }
        return count;
    }

    private static int CountRelationNoAlloc(LiteDatabase db, QuerySpec query)
    {
        if (query.Joins.Exists(j => j.Table.IndexOf("Tag", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            if (!query.TryGetEquality("tag_name", out object tagVal)) return 0;
            var tagCol = db.GetCollection<BsonDocument>("tags");
            int tagHits = 0;
            foreach (var _ in tagCol.Find(Query.EQ("tag_name", ToBsonValue(tagVal)))) tagHits++;
            return tagHits;
        }
        if (!query.TryGetEquality(query.RelationAnchorField, out object anchorValue)) return 0;

        int count = 0;
        if (query.ComplexityLevel == 2)
        {
            var invCol = db.GetCollection<BsonDocument>("inventories");
            var itemCol = db.GetCollection<BsonDocument>("items");
            BsonValue anchorBson = ToBsonValue(anchorValue);

            foreach (BsonDocument inv in invCol.Find(Query.EQ("character_id", anchorBson)))
            {
                foreach (BsonDocument item in itemCol.Find(Query.EQ("item_id", inv["item_id"])))
                {
                    _ = item["name"];
                    count++;
                }
            }
            return count;
        }

        if (query.ComplexityLevel == 3)
        {
            var charCol = db.GetCollection<BsonDocument>("characters");
            var invCol = db.GetCollection<BsonDocument>("inventories");
            var itemCol = db.GetCollection<BsonDocument>("items");
            var linkCol = db.GetCollection<BsonDocument>("links");
            var effCol = db.GetCollection<BsonDocument>("effects");
            BsonValue anchorBson = ToBsonValue(anchorValue);

            foreach (BsonDocument ch in charCol.Find(Query.EQ(query.RelationAnchorField, anchorBson)))
            {
                foreach (BsonDocument inv in invCol.Find(Query.EQ("character_id", ch["character_id"])))
                {
                    foreach (BsonDocument item in itemCol.Find(Query.EQ("item_id", inv["item_id"])))
                    {
                        bool hasLink = false;
                        foreach (BsonDocument link in linkCol.Find(Query.EQ("item_id", item["item_id"])))
                        {
                            hasLink = true;
                            foreach (BsonDocument eff in effCol.Find(Query.EQ("effect_id", link["effect_id"])))
                            {
                                _ = eff["effect_type"];
                                count++;
                            }
                        }
                        if (!hasLink) count++;
                    }
                }
            }
        }
        return count;
    }

    private static QueryResult ExecuteInternal(LiteDatabase db, QuerySpec query)
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
            else
            {
                string collectionName = QuerySpec.GetCollectionName(query.Table);
                foreach (Dictionary<string, object> row in ReadCollection(db, collectionName, query.Predicates))
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

    /// <summary>
    /// 검증 모드 전용: Document/Sparse 중첩 문서를 SQL JOIN과 동일한 평면 행으로 펼쳐서 비교한다.
    /// </summary>
    private static void ExecuteDocOrSparseRelation(LiteDatabase db, QuerySpec query, QueryResult result)
    {
        string colName = QuerySpec.GetCollectionName(query.Table);
        var rootFilter = BuildRootOnlyFilter(query);
        ILiteCollection<BsonDocument> col = db.GetCollection<BsonDocument>(colName);
        IEnumerable<BsonDocument> docs = rootFilter == null ? col.FindAll() : col.Find(rootFilter);

        foreach (BsonDocument doc in docs)
        {
            var rootRow = DocToDict(doc);
            foreach (var flatRow in DocumentUnwindHelper.Unwind(query.Table, rootRow))
            {
                if (QueryRuntime.Matches(flatRow, query.Predicates))
                {
                    result.AddProjectedRow(query, flatRow);
                }
            }
        }
    }

    private static BsonExpression BuildRootOnlyFilter(QuerySpec query)
    {
        if (query.TryGetEquality(query.PrimaryKeyField, out object pkVal))
        {
            return Query.EQ(query.PrimaryKeyField, ToBsonValue(pkVal));
        }
        return null;
    }

    private static void ExecuteRelation(LiteDatabase db, QuerySpec query, QueryResult result)
    {
        if (query.Joins.Exists(j => j.Table.IndexOf("Tag", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            if (!query.TryGetEquality("tag_name", out object tagVal))
                throw new InvalidOperationException("Tag 조인 조회에는 tag_name 조건이 필요합니다.");
            var tagRows = ReadCollection(db, "tags", Equality("tag_name", tagVal));
            for (int i = 0; i < tagRows.Count; i++)
            {
                object itemId = Value(tagRows[i], "item_id");
                var itemRows = ReadCollection(db, "items", Equality("item_id", itemId));
                var item = FindById(itemRows, "item_id", itemId);
                if (item == null) continue;
                var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["item_id"] = Value(item, "item_id"),
                    ["name"] = Value(item, "name"),
                    ["tag_name"] = Value(tagRows[i], "tag_name")
                };
                if (QueryRuntime.Matches(row, query.Predicates)) result.AddProjectedRow(query, row);
            }
            return;
        }
        if (!query.TryGetEquality(query.RelationAnchorField, out object anchorValue))
            throw new InvalidOperationException("관계형 조회에는 기준 FK equality 조건이 필요합니다.");

        if (query.ComplexityLevel == 2)
        {
            List<Dictionary<string, object>> inventories = ReadCollection(db, "inventories", Equality("character_id", anchorValue));
            List<Dictionary<string, object>> characters = ReadCollection(db, "characters", Equality("character_id", anchorValue));
            for (int i = 0; i < inventories.Count; i++)
            {
                Dictionary<string, object> inventory = inventories[i];
                if (!QueryRuntime.Compare(Value(inventory, "character_id"), anchorValue, QueryComparison.Equal)) continue;

                Dictionary<string, object> character = FindById(characters, "character_id", Value(inventory, "character_id"));
                List<Dictionary<string, object>> matchingItems = ReadCollection(db, "items", Equality("item_id", Value(inventory, "item_id")));
                Dictionary<string, object> item = FindById(matchingItems, "item_id", Value(inventory, "item_id"));
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

        List<Dictionary<string, object>> matchedCharacters = ReadCollection(db, "characters", Equality(query.RelationAnchorField, anchorValue));

        for (int c = 0; c < matchedCharacters.Count; c++)
        {
            Dictionary<string, object> character = matchedCharacters[c];
            List<Dictionary<string, object>> matchingUsers = ReadCollection(db, "users", Equality("user_id", Value(character, "user_id")));
            Dictionary<string, object> user = FindById(matchingUsers, "user_id", Value(character, "user_id"));
            List<Dictionary<string, object>> inventories = ReadCollection(db, "inventories", Equality("character_id", Value(character, "character_id")));
            for (int i = 0; i < inventories.Count; i++)
            {
                Dictionary<string, object> inventory = inventories[i];
                if (!QueryRuntime.Compare(Value(inventory, "character_id"), Value(character, "character_id"), QueryComparison.Equal)) continue;

                List<Dictionary<string, object>> matchingItems = ReadCollection(db, "items", Equality("item_id", Value(inventory, "item_id")));
                Dictionary<string, object> item = FindById(matchingItems, "item_id", Value(inventory, "item_id"));
                List<Dictionary<string, object>> itemLinks = ReadCollection(db, "links", Equality("item_id", Value(item, "item_id")));
                if (itemLinks.Count == 0) itemLinks.Add(null);

                for (int l = 0; l < itemLinks.Count; l++)
                {
                    Dictionary<string, object> link = itemLinks[l];
                    List<Dictionary<string, object>> matchingEffects = link == null
                        ? new List<Dictionary<string, object>>()
                        : ReadCollection(db, "effects", Equality("effect_id", Value(link, "effect_id")));
                    Dictionary<string, object> effect = link == null ? null : FindById(matchingEffects, "effect_id", Value(link, "effect_id"));
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

    private static BsonExpression BuildBsonFilter(List<QueryPredicate> predicates)
    {
        if (predicates == null || predicates.Count == 0) return null;

        var expressions = new List<BsonExpression>(predicates.Count);
        for (int i = 0; i < predicates.Count; i++)
        {
            QueryPredicate predicate = predicates[i];
            BsonValue value = ToBsonValue(predicate.Value);
            switch (predicate.Comparison)
            {
                case QueryComparison.Equal: expressions.Add(Query.EQ(predicate.Field, value)); break;
                case QueryComparison.NotEqual: expressions.Add(Query.Not(predicate.Field, value)); break;
                case QueryComparison.GreaterThan: expressions.Add(Query.GT(predicate.Field, value)); break;
                case QueryComparison.GreaterThanOrEqual: expressions.Add(Query.GTE(predicate.Field, value)); break;
                case QueryComparison.LessThan: expressions.Add(Query.LT(predicate.Field, value)); break;
                case QueryComparison.LessThanOrEqual: expressions.Add(Query.LTE(predicate.Field, value)); break;
            }
        }
        return expressions.Count == 1 ? expressions[0] : Query.And(expressions.ToArray());
    }

    private static List<Dictionary<string, object>> ReadCollection(LiteDatabase db, string collectionName, List<QueryPredicate> predicates)
    {
        var rows = new List<Dictionary<string, object>>();
        ILiteCollection<BsonDocument> collection = db.GetCollection<BsonDocument>(collectionName);
        BsonExpression filter = BuildBsonFilter(predicates);
        IEnumerable<BsonDocument> documents = filter == null ? collection.FindAll() : collection.Find(filter);

        foreach (BsonDocument document in documents)
        {
            rows.Add(DocToDict(document));
        }
        return rows;
    }

    private static Dictionary<string, object> DocToDict(BsonDocument document)
    {
        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (string key in document.Keys)
        {
            if (key.Equals("_id", StringComparison.OrdinalIgnoreCase)) continue;
            row[key] = BsonValueToObject(document[key]);
        }
        return row;
    }

    private static List<QueryPredicate> Equality(string field, object value)
    {
        return new List<QueryPredicate> { new QueryPredicate { Field = field, Comparison = QueryComparison.Equal, Value = value } };
    }

    private static BsonValue ToBsonValue(object value)
    {
        if (value == null) return BsonValue.Null;
        if (value is int i) return new BsonValue(i);
        if (value is long l) return new BsonValue(l);
        if (value is double d) return new BsonValue(d);
        if (value is float f) return new BsonValue((double)f);
        if (value is bool b) return new BsonValue(b);
        return new BsonValue(Convert.ToString(value));
    }

    private static Dictionary<string, object> FindById(List<Dictionary<string, object>> rows, string field, object value)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (QueryRuntime.Compare(Value(rows[i], field), value, QueryComparison.Equal)) return rows[i];
        }
        return null;
    }

    private static object Value(Dictionary<string, object> row, string field)
    {
        if (row == null) return null;
        return QueryRuntime.TryGetValue(row, field, out var value) ? value : null;
    }

    private static object BsonValueToObject(BsonValue value)
    {
        if (value.IsNull) return null;
        if (value.IsInt32) return value.AsInt32;
        if (value.IsInt64) return value.AsInt64;
        if (value.IsDouble) return value.AsDouble;
        if (value.IsString) return value.AsString;
        if (value.IsBoolean) return value.AsBoolean;
        if (value.IsDateTime) return value.AsDateTime;
        if (value.IsArray)
        {
            var list = new List<object>();
            foreach (var item in value.AsArray) list.Add(BsonValueToObject(item));
            return list;
        }
        if (value.IsDocument)
        {
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in value.AsDocument.Keys) dict[key] = BsonValueToObject(value.AsDocument[key]);
            return dict;
        }
        return value.ToString();
    }
}

/// <summary>
/// Document / Sparse 중첩 구조를 검증용 평면 행으로 펼치는 공통 헬퍼 (LiteDB & RocksDB 공용)
/// </summary>
public static class DocumentUnwindHelper
{
    public static IEnumerable<Dictionary<string, object>> Unwind(string table, Dictionary<string, object> root)
    {
        if (root == null) yield break;

        if (table.Equals("Doc_Item_C1", StringComparison.OrdinalIgnoreCase))
        {
            if (TryGetEnumerable(root, "tags", out var tags))
            {
                foreach (var tag in tags)
                {
                    var row = CloneScalars(root);
                    row["tag_name"] = tag;
                    yield return row;
                }
                yield break;
            }
        }
        else if (table.Equals("Doc_Char_C2", StringComparison.OrdinalIgnoreCase))
        {
            if (TryGetEnumerable(root, "inventories", out var invs))
            {
                foreach (var invObj in invs)
                {
                    if (!(invObj is IDictionary<string, object> inv)) continue;
                    var row = CloneScalars(root);
                    foreach (var kv in inv) row[kv.Key] = kv.Value;
                    yield return row;
                }
                yield break;
            }
        }
        else if (table.Equals("Doc_User_C3", StringComparison.OrdinalIgnoreCase))
        {
            if (TryGetEnumerable(root, "characters", out var chars))
            {
                foreach (var chObj in chars)
                {
                    if (!(chObj is IDictionary<string, object> ch)) continue;
                    if (TryGetEnumerable(ch, "inventories", out var invs))
                    {
                        foreach (var invObj in invs)
                        {
                            if (!(invObj is IDictionary<string, object> inv)) continue;
                            var row = CloneScalars(root);
                            foreach (var kv in ch)
                            {
                                if (!kv.Key.Equals("inventories", StringComparison.OrdinalIgnoreCase)) row[kv.Key] = kv.Value;
                            }
                            foreach (var kv in inv) row[kv.Key] = kv.Value;
                            yield return row;
                        }
                    }
                }
                yield break;
            }
        }
        else if (table.Equals("Sparse_Item_C1", StringComparison.OrdinalIgnoreCase))
        {
            if (TryGetDict(root, "attributes", out var attrs))
            {
                foreach (var kv in attrs)
                {
                    var row = CloneScalars(root);
                    row["attr_key"] = kv.Key;
                    row["attr_value"] = Convert.ToString(kv.Value);
                    yield return row;
                }
                yield break;
            }
        }
        else if (table.Equals("Sparse_Entity_C2", StringComparison.OrdinalIgnoreCase))
        {
            if (TryGetDict(root, "stats", out var stats))
            {
                foreach (var kv in stats)
                {
                    var row = CloneScalars(root);
                    row["attr_key"] = kv.Key;
                    row["attr_value"] = Convert.ToString(kv.Value);
                    yield return row;
                }
                yield break;
            }
        }
        else if (table.Equals("Sparse_Event_C3", StringComparison.OrdinalIgnoreCase))
        {
            if (TryGetDict(root, "payload", out var payload))
            {
                foreach (var kv in payload)
                {
                    var row = CloneScalars(root);
                    row["payload_key"] = kv.Key;
                    row["payload_value"] = Convert.ToString(kv.Value);
                    yield return row;
                }
                yield break;
            }
        }

        yield return root;
    }

    private static Dictionary<string, object> CloneScalars(IDictionary<string, object> source)
    {
        var copy = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in source) copy[kv.Key] = kv.Value;
        return copy;
    }

    private static bool TryGetEnumerable(IDictionary<string, object> dict, string key, out IEnumerable list)
    {
        foreach (var kv in dict)
        {
            if (kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && kv.Value is IEnumerable e && !(kv.Value is string))
            {
                list = e;
                return true;
            }
        }
        list = null;
        return false;
    }

    private static bool TryGetDict(IDictionary<string, object> dict, string key, out IDictionary<string, object> nested)
    {
        foreach (var kv in dict)
        {
            if (kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && kv.Value is IDictionary<string, object> d)
            {
                nested = d;
                return true;
            }
            if (kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && kv.Value is Newtonsoft.Json.Linq.JObject jobj)
            {
                nested = jobj.ToObject<Dictionary<string, object>>();
                return true;
            }
        }
        nested = null;
        return false;
    }
}