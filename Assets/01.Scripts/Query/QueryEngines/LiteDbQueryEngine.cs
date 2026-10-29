using System;
using System.Collections.Generic;
using LiteDB;

public class LiteDbQueryEngine : IQueryEngine
{
    public string FormatName => "LiteDB";
    private readonly string dbPath;

    public LiteDbQueryEngine(string dbPath)
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
            using (var db = new LiteDatabase($"Filename={dbPath};ReadOnly=true;"))
            {
                if (query.Operation == QueryOperation.RelationTraversal)
                {
                    ExecuteRelation(db, query, result);
                }
                else
                {
                    string collectionName = QuerySpec.GetCollectionName(query.Table);
                    foreach (Dictionary<string, object> row in ReadCollection(db, collectionName, query.Predicates))
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

    private static void ExecuteRelation(LiteDatabase db, QuerySpec query, QueryResult result)
    {
        object anchorValue;
        if (!query.TryGetEquality(query.RelationAnchorField, out anchorValue))
            throw new InvalidOperationException("관계형 조회에는 기준 FK equality 조건이 필요합니다.");

        if (query.ComplexityLevel == 2)
        {
            List<Dictionary<string, object>> inventories = ReadCollection(db, "inventories", Equality("character_id", anchorValue));
            List<Dictionary<string, object>> characters = ReadCollection(db, "characters", Equality("character_id", anchorValue));
            for (int i = 0; i < inventories.Count; i++)
            {
                Dictionary<string, object> inventory = inventories[i];
                bool match = query.RelationAnchorField.Equals("character_id", StringComparison.OrdinalIgnoreCase)
                    ? QueryRuntime.Compare(Value(inventory, "character_id"), anchorValue, QueryComparison.Equal)
                    : false;
                if (!match) continue;

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

    private static List<Dictionary<string, object>> ReadCollection(LiteDatabase db, string collectionName)
    {
        return ReadCollection(db, collectionName, null);
    }

    private static List<Dictionary<string, object>> ReadCollection(LiteDatabase db, string collectionName, List<QueryPredicate> predicates)
    {
        var rows = new List<Dictionary<string, object>>();
        ILiteCollection<BsonDocument> collection = db.GetCollection<BsonDocument>(collectionName);
        IEnumerable<BsonDocument> documents;
        if (predicates == null || predicates.Count == 0)
        {
            documents = collection.FindAll();
        }
        else
        {
            var expressions = new List<BsonExpression>();
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
            if (expressions.Count == 1)
            {
                // LiteDB 5.x Query.And requires at least two expressions.
                // A single predicate must be passed directly so PK/FK lookups work.
                documents = collection.Find(expressions[0]);
            }
            else if (expressions.Count > 1)
            {
                documents = collection.Find(Query.And(expressions.ToArray()));
            }
            else
            {
                throw new NotSupportedException("LiteDB로 변환할 수 있는 WHERE 조건이 없습니다.");
            }
        }

        foreach (BsonDocument document in documents)
        {
            var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in document.Keys)
            {
                if (key.Equals("_id", StringComparison.OrdinalIgnoreCase)) continue;
                row[key] = BsonValueToObject(document[key]);
            }
            rows.Add(row);
        }
        return rows;
    }

    private static List<QueryPredicate> Equality(string field, object value)
    {
        return new List<QueryPredicate>
        {
            new QueryPredicate { Field = field, Comparison = QueryComparison.Equal, Value = value }
        };
    }

    private static BsonValue ToBsonValue(object value)
    {
        if (value == null) return BsonValue.Null;
        if (value is int) return new BsonValue((int)value);
        if (value is long) return new BsonValue((long)value);
        if (value is double) return new BsonValue((double)value);
        if (value is float) return new BsonValue((double)(float)value);
        if (value is bool) return new BsonValue((bool)value);
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

    private static List<Dictionary<string, object>> FindAllById(List<Dictionary<string, object>> rows, string field, object value)
    {
        var matches = new List<Dictionary<string, object>>();
        for (int i = 0; i < rows.Count; i++)
        {
            if (QueryRuntime.Compare(Value(rows[i], field), value, QueryComparison.Equal)) matches.Add(rows[i]);
        }
        return matches;
    }

    private static object Value(Dictionary<string, object> row, string field)
    {
        if (row == null) return null;
        object value;
        return QueryRuntime.TryGetValue(row, field, out value) ? value : null;
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
        return value.ToString();
    }
}
