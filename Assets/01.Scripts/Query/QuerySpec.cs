using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

public enum QueryOperation
{
    PrimaryKeyLookup,
    RangeFilter,
    RelationTraversal,
    PartialFieldAccess
}

public enum QueryComparison
{
    Equal,
    NotEqual,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual
}

public sealed class QueryPredicate
{
    public string Expression { get; set; }
    public string Field { get; set; }
    public QueryComparison Comparison { get; set; }
    public object Value { get; set; }
}

public sealed class QueryProjection
{
    public string Expression { get; set; }
    public string Field { get; set; }
    public string Name { get; set; }
}

public sealed class QueryJoin
{
    public bool IsLeftJoin { get; set; }
    public string Table { get; set; }
    public string Alias { get; set; }
    public string LeftExpression { get; set; }
    public string RightExpression { get; set; }
}

public sealed class QuerySpec
{
    public string OriginalSql { get; internal set; }
    public string Table { get; internal set; }
    public string Alias { get; internal set; }
    public bool SelectAll { get; internal set; }
    public QueryOperation Operation { get; internal set; }
    public string OrderByExpression { get; internal set; }
    public bool OrderByDescending { get; internal set; }
    public List<QueryProjection> Projection { get; } = new List<QueryProjection>();
    public List<QueryPredicate> Predicates { get; } = new List<QueryPredicate>();
    public List<QueryJoin> Joins { get; } = new List<QueryJoin>();

    public string PrimaryKeyField => GetPrimaryKeyField(Table);

    public int ComplexityLevel
    {
        get
        {
            if (Table.IndexOf("C3", StringComparison.OrdinalIgnoreCase) >= 0) return 3;
            if (Table.IndexOf("C2", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            return 1;
        }
    }

    public string RelationAnchorField
    {
        get
        {
            for (int i = 0; i < Predicates.Count; i++)
            {
                QueryPredicate predicate = Predicates[i];
                if (predicate.Comparison == QueryComparison.Equal &&
                    (predicate.Field.Equals("character_id", StringComparison.OrdinalIgnoreCase) ||
                     predicate.Field.Equals("user_id", StringComparison.OrdinalIgnoreCase)))
                {
                    return predicate.Field.ToLowerInvariant();
                }
            }

            return string.Empty;
        }
    }

    public bool TryGetEquality(string field, out object value)
    {
        for (int i = 0; i < Predicates.Count; i++)
        {
            QueryPredicate predicate = Predicates[i];
            if (predicate.Comparison == QueryComparison.Equal &&
                predicate.Field.Equals(field, StringComparison.OrdinalIgnoreCase))
            {
                value = predicate.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    public static string GetPrimaryKeyField(string table)
    {
        if (string.IsNullOrEmpty(table)) return "item_id";
        if (table.IndexOf("USER", StringComparison.OrdinalIgnoreCase) >= 0) return "user_id";
        if (table.IndexOf("CHARACTER", StringComparison.OrdinalIgnoreCase) >= 0) return "character_id";
        if (table.IndexOf("INVENTORY", StringComparison.OrdinalIgnoreCase) >= 0) return "instance_id";
        if (table.IndexOf("EFFECT", StringComparison.OrdinalIgnoreCase) >= 0 &&
            table.IndexOf("LINK", StringComparison.OrdinalIgnoreCase) < 0) return "effect_id";
        return "item_id";
    }

    public static string GetCollectionName(string table)
    {
        if (table.IndexOf("ITEM_EFFECT_LINK", StringComparison.OrdinalIgnoreCase) >= 0) return "links";
        if (table.IndexOf("ITEM_TAG", StringComparison.OrdinalIgnoreCase) >= 0) return "tags";
        if (table.IndexOf("INVENTORY", StringComparison.OrdinalIgnoreCase) >= 0) return "inventories";
        if (table.IndexOf("CHARACTER", StringComparison.OrdinalIgnoreCase) >= 0) return "characters";
        if (table.IndexOf("USER", StringComparison.OrdinalIgnoreCase) >= 0) return "users";
        if (table.IndexOf("EFFECT", StringComparison.OrdinalIgnoreCase) >= 0) return "effects";
        return "items";
    }

    public Dictionary<string, object> ProjectRow(Dictionary<string, object> source)
    {
        var projected = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (SelectAll)
        {
            foreach (KeyValuePair<string, object> pair in source) projected[pair.Key] = pair.Value;
            return projected;
        }

        for (int i = 0; i < Projection.Count; i++)
        {
            QueryProjection projection = Projection[i];
            object value;
            if (!QueryRuntime.TryGetValue(source, projection.Name, out value) &&
                !QueryRuntime.TryGetValue(source, projection.Field, out value)) value = null;
            projected[projection.Name] = value;
        }

        return projected;
    }
}

public sealed class QueryParseResult
{
    public QuerySpec Spec { get; private set; }
    public string ErrorMessage { get; private set; }
    public bool IsSuccess => Spec != null && string.IsNullOrEmpty(ErrorMessage);

    private QueryParseResult(QuerySpec spec, string errorMessage)
    {
        Spec = spec;
        ErrorMessage = errorMessage;
    }

    public static QueryParseResult Success(QuerySpec spec) => new QueryParseResult(spec, null);
    public static QueryParseResult Failure(string message) => new QueryParseResult(null, message);
}

public static class QuerySpecParser
{
    private static readonly Regex SelectFromRegex = new Regex(
        @"^\s*SELECT\s+(?<select>.+?)\s+FROM\s+(?<rest>.+?)\s*;?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex PredicateRegex = new Regex(
        @"^(?<field>[A-Za-z_][A-Za-z0-9_\.]*|\[[^\]]+\]|`[^`]+`)\s*(?<operator>>=|<=|<>|!=|=|>|<)\s*(?<value>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static QueryParseResult Parse(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return QueryParseResult.Failure("쿼리가 비어 있습니다.");

        string trimmed = sql.Trim().TrimEnd(';').Trim();
        Match selectMatch = SelectFromRegex.Match(trimmed);
        if (!selectMatch.Success) return QueryParseResult.Failure("지원되는 쿼리 형식은 SELECT ... FROM ... 입니다.");

        string rest = selectMatch.Groups["rest"].Value.Trim();
        int whereIndex = FindKeyword(rest, "WHERE");
        int orderIndex = FindKeyword(rest, "ORDER BY");
        int fromEnd = rest.Length;
        if (whereIndex >= 0) fromEnd = Math.Min(fromEnd, whereIndex);
        if (orderIndex >= 0) fromEnd = Math.Min(fromEnd, orderIndex);

        string fromPart = rest.Substring(0, fromEnd).Trim();
        string wherePart = whereIndex >= 0
            ? rest.Substring(whereIndex + 5, (orderIndex >= 0 ? orderIndex : rest.Length) - whereIndex - 5).Trim()
            : string.Empty;
        string orderPart = orderIndex >= 0 ? rest.Substring(orderIndex + 8).Trim() : string.Empty;

        var spec = new QuerySpec { OriginalSql = trimmed };
        string selectPart = selectMatch.Groups["select"].Value.Trim();
        if (selectPart == "*")
        {
            spec.SelectAll = true;
        }
        else
        {
            List<string> projectionParts = SplitCommaSeparated(selectPart);
            for (int i = 0; i < projectionParts.Count; i++)
            {
                string projectionText = projectionParts[i].Trim();
                Match aliasMatch = Regex.Match(projectionText,
                    @"^(?<expression>[A-Za-z_][A-Za-z0-9_\.]*)(?:\s+AS\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*))?$",
                    RegexOptions.IgnoreCase);
                if (!aliasMatch.Success) return QueryParseResult.Failure($"지원하지 않는 SELECT 표현식입니다: {projectionText}");

                string expression = aliasMatch.Groups["expression"].Value;
                string field = NormalizeField(expression);
                string alias = aliasMatch.Groups["alias"].Success ? aliasMatch.Groups["alias"].Value : field;
                spec.Projection.Add(new QueryProjection { Expression = expression, Field = field, Name = alias });
            }
        }

        string fromError = ParseFromPart(fromPart, spec);
        if (!string.IsNullOrEmpty(fromError)) return QueryParseResult.Failure(fromError);

        if (!string.IsNullOrEmpty(wherePart))
        {
            if (Regex.IsMatch(wherePart, @"\bOR\b", RegexOptions.IgnoreCase))
                return QueryParseResult.Failure("공통 쿼리 모드에서는 OR 조건을 지원하지 않습니다. AND 조건으로 분리해 주세요.");

            List<string> predicateParts = SplitByKeyword(wherePart, "AND");
            for (int i = 0; i < predicateParts.Count; i++)
            {
                Match predicateMatch = PredicateRegex.Match(predicateParts[i].Trim());
                if (!predicateMatch.Success) return QueryParseResult.Failure($"지원하지 않는 WHERE 조건입니다: {predicateParts[i].Trim()}");

                object value;
                string valueError;
                if (!TryParseValue(predicateMatch.Groups["value"].Value.Trim(), out value, out valueError))
                    return QueryParseResult.Failure(valueError);

                spec.Predicates.Add(new QueryPredicate
                {
                    Expression = predicateMatch.Groups["field"].Value,
                    Field = NormalizeField(predicateMatch.Groups["field"].Value),
                    Comparison = ParseComparison(predicateMatch.Groups["operator"].Value),
                    Value = value
                });
            }
        }

        if (!string.IsNullOrEmpty(orderPart))
        {
            string[] orderTokens = orderPart.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (orderTokens.Length > 2 || orderTokens.Length == 0) return QueryParseResult.Failure("지원하지 않는 ORDER BY 형식입니다.");
            spec.OrderByExpression = orderTokens[0];
            spec.OrderByDescending = orderTokens.Length == 2 && orderTokens[1].Equals("DESC", StringComparison.OrdinalIgnoreCase);
        }

        spec.Operation = InferOperation(spec);
        return QueryParseResult.Success(spec);
    }

    private static QueryOperation InferOperation(QuerySpec spec)
    {
        if (spec.Joins.Count > 0) return QueryOperation.RelationTraversal;
        if (spec.Table.IndexOf("INVENTORY", StringComparison.OrdinalIgnoreCase) >= 0 && spec.RelationAnchorField.Length > 0)
            return QueryOperation.RelationTraversal;

        object primaryKey;
        if (spec.TryGetEquality(spec.PrimaryKeyField, out primaryKey))
            return !spec.SelectAll && spec.Projection.Count == 1 ? QueryOperation.PartialFieldAccess : QueryOperation.PrimaryKeyLookup;
        return QueryOperation.RangeFilter;
    }

    private static string ParseFromPart(string fromPart, QuerySpec spec)
    {
        List<string> tokens = Tokenize(fromPart);
        if (tokens.Count == 0) return "FROM 뒤에 테이블이 없습니다.";

        int index = 0;
        spec.Table = NormalizeIdentifier(tokens[index++]);
        if (index < tokens.Count && tokens[index].Equals("AS", StringComparison.OrdinalIgnoreCase))
        {
            index++;
            if (index >= tokens.Count) return "FROM 별칭이 없습니다.";
            spec.Alias = NormalizeIdentifier(tokens[index++]);
        }
        else if (index < tokens.Count && !IsJoinKeyword(tokens[index]))
        {
            spec.Alias = NormalizeIdentifier(tokens[index++]);
        }

        while (index < tokens.Count)
        {
            bool leftJoin = false;
            if (tokens[index].Equals("LEFT", StringComparison.OrdinalIgnoreCase))
            {
                leftJoin = true;
                index++;
                if (index < tokens.Count && tokens[index].Equals("OUTER", StringComparison.OrdinalIgnoreCase)) index++;
            }
            else if (tokens[index].Equals("INNER", StringComparison.OrdinalIgnoreCase)) index++;

            if (index >= tokens.Count || !tokens[index].Equals("JOIN", StringComparison.OrdinalIgnoreCase)) return "지원하지 않는 FROM/JOIN 구문입니다.";
            index++;
            if (index >= tokens.Count) return "JOIN 대상 테이블이 없습니다.";
            string joinTable = NormalizeIdentifier(tokens[index++]);
            string joinAlias = string.Empty;

            if (index < tokens.Count && tokens[index].Equals("AS", StringComparison.OrdinalIgnoreCase))
            {
                index++;
                if (index >= tokens.Count) return "JOIN 별칭이 없습니다.";
                joinAlias = NormalizeIdentifier(tokens[index++]);
            }
            else if (index < tokens.Count && !tokens[index].Equals("ON", StringComparison.OrdinalIgnoreCase))
            {
                joinAlias = NormalizeIdentifier(tokens[index++]);
            }

            if (index >= tokens.Count || !tokens[index].Equals("ON", StringComparison.OrdinalIgnoreCase)) return "JOIN 조건에 ON이 없습니다.";
            index++;
            if (index + 2 >= tokens.Count || !tokens[index + 1].Equals("=", StringComparison.Ordinal)) return "JOIN은 현재 등가 조건만 지원합니다.";

            string leftExpression = tokens[index++];
            index++;
            string rightExpression = tokens[index++];
            spec.Joins.Add(new QueryJoin
            {
                IsLeftJoin = leftJoin,
                Table = joinTable,
                Alias = joinAlias,
                LeftExpression = leftExpression,
                RightExpression = rightExpression
            });
        }

        return null;
    }

    private static QueryComparison ParseComparison(string value)
    {
        switch (value)
        {
            case "=": return QueryComparison.Equal;
            case "!=":
            case "<>": return QueryComparison.NotEqual;
            case ">": return QueryComparison.GreaterThan;
            case ">=": return QueryComparison.GreaterThanOrEqual;
            case "<": return QueryComparison.LessThan;
            case "<=": return QueryComparison.LessThanOrEqual;
            default: throw new InvalidOperationException($"지원하지 않는 비교 연산자입니다: {value}");
        }
    }

    private static bool TryParseValue(string text, out object value, out string error)
    {
        error = null;
        if (text.Equals("NULL", StringComparison.OrdinalIgnoreCase)) { value = null; return true; }
        if (text.Length >= 2 && text[0] == '\'' && text[text.Length - 1] == '\'')
        {
            value = text.Substring(1, text.Length - 2).Replace("''", "'");
            return true;
        }

        int intValue;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue)) { value = intValue; return true; }
        long longValue;
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out longValue)) { value = longValue; return true; }
        double doubleValue;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out doubleValue)) { value = doubleValue; return true; }

        value = null;
        error = $"지원하지 않는 WHERE 값입니다: {text}";
        return false;
    }

    public static string NormalizeField(string expression)
    {
        string value = NormalizeIdentifier(expression);
        int dot = value.LastIndexOf('.');
        return dot >= 0 ? value.Substring(dot + 1) : value;
    }

    private static string NormalizeIdentifier(string value) => value.Trim().Trim('[', ']', '`', '"');

    private static bool IsJoinKeyword(string token) =>
        token.Equals("JOIN", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("LEFT", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("INNER", StringComparison.OrdinalIgnoreCase);

    private static int FindKeyword(string text, string keyword)
    {
        bool quoted = false;
        for (int i = 0; i <= text.Length - keyword.Length; i++)
        {
            if (text[i] == '\'' && (i == 0 || text[i - 1] != '\\')) quoted = !quoted;
            if (quoted || !text.Substring(i, keyword.Length).Equals(keyword, StringComparison.OrdinalIgnoreCase)) continue;
            bool leftBoundary = i == 0 || char.IsWhiteSpace(text[i - 1]);
            int end = i + keyword.Length;
            bool rightBoundary = end == text.Length || char.IsWhiteSpace(text[end]);
            if (leftBoundary && rightBoundary) return i;
        }
        return -1;
    }

    private static List<string> SplitByKeyword(string text, string keyword)
    {
        var parts = new List<string>();
        int start = 0;
        int index;
        while ((index = FindKeyword(text.Substring(start), keyword)) >= 0)
        {
            parts.Add(text.Substring(start, index).Trim());
            start += index + keyword.Length;
        }
        parts.Add(text.Substring(start).Trim());
        return parts;
    }

    private static List<string> SplitCommaSeparated(string text)
    {
        var parts = new List<string>();
        int start = 0;
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\'') quoted = !quoted;
            if (text[i] == ',' && !quoted)
            {
                parts.Add(text.Substring(start, i - start));
                start = i + 1;
            }
        }
        parts.Add(text.Substring(start));
        return parts;
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\'') { quoted = !quoted; current.Append(c); }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (current.Length > 0) { tokens.Add(current.ToString()); current.Length = 0; }
            }
            else if (c == '=' && !quoted)
            {
                if (current.Length > 0) { tokens.Add(current.ToString()); current.Length = 0; }
                tokens.Add("=");
            }
            else current.Append(c);
        }
        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }
}

public sealed class SqlCompilation
{
    public string Sql { get; set; }
    public object[] Parameters { get; set; }
}

public static class SqlQueryCompiler
{
    public static SqlCompilation Compile(QuerySpec spec)
    {
        var sql = new StringBuilder("SELECT ");
        if (spec.SelectAll) sql.Append("*");
        else
        {
            for (int i = 0; i < spec.Projection.Count; i++)
            {
                if (i > 0) sql.Append(", ");
                QueryProjection projection = spec.Projection[i];
                sql.Append(projection.Expression);
                if (!projection.Name.Equals(projection.Field, StringComparison.OrdinalIgnoreCase)) sql.Append(" AS ").Append(projection.Name);
            }
        }

        sql.Append(" FROM ").Append(spec.Table);
        if (!string.IsNullOrEmpty(spec.Alias)) sql.Append(' ').Append(spec.Alias);
        for (int i = 0; i < spec.Joins.Count; i++)
        {
            QueryJoin join = spec.Joins[i];
            sql.Append(join.IsLeftJoin ? " LEFT JOIN " : " JOIN ").Append(join.Table);
            if (!string.IsNullOrEmpty(join.Alias)) sql.Append(' ').Append(join.Alias);
            sql.Append(" ON ").Append(join.LeftExpression).Append(" = ").Append(join.RightExpression);
        }

        var parameters = new List<object>();
        if (spec.Predicates.Count > 0)
        {
            sql.Append(" WHERE ");
            for (int i = 0; i < spec.Predicates.Count; i++)
            {
                if (i > 0) sql.Append(" AND ");
                QueryPredicate predicate = spec.Predicates[i];
                sql.Append(predicate.Expression).Append(' ').Append(ToSqlOperator(predicate.Comparison)).Append(" ?");
                parameters.Add(predicate.Value);
            }
        }

        if (!string.IsNullOrEmpty(spec.OrderByExpression))
        {
            sql.Append(" ORDER BY ").Append(spec.OrderByExpression);
            if (spec.OrderByDescending) sql.Append(" DESC");
        }

        return new SqlCompilation { Sql = sql.ToString(), Parameters = parameters.ToArray() };
    }

    private static string ToSqlOperator(QueryComparison comparison)
    {
        switch (comparison)
        {
            case QueryComparison.Equal: return "=";
            case QueryComparison.NotEqual: return "<>";
            case QueryComparison.GreaterThan: return ">";
            case QueryComparison.GreaterThanOrEqual: return ">=";
            case QueryComparison.LessThan: return "<";
            case QueryComparison.LessThanOrEqual: return "<=";
            default: throw new ArgumentOutOfRangeException(nameof(comparison), comparison, null);
        }
    }
}

public static class QueryRuntime
{
    public static bool Matches(Dictionary<string, object> row, List<QueryPredicate> predicates)
    {
        for (int i = 0; i < predicates.Count; i++)
        {
            object actual;
            if (!TryGetValue(row, predicates[i].Field, out actual)) return false;
            if (!Compare(actual, predicates[i].Value, predicates[i].Comparison)) return false;
        }
        return true;
    }

    public static bool TryGetValue(Dictionary<string, object> row, string field, out object value)
    {
        if (row.TryGetValue(field, out value)) return true;
        foreach (KeyValuePair<string, object> pair in row)
        {
            if (pair.Key.Equals(field, StringComparison.OrdinalIgnoreCase)) { value = pair.Value; return true; }
        }
        value = null;
        return false;
    }

    public static bool Compare(object left, object right, QueryComparison comparison)
    {
        if (left == null || right == null)
        {
            bool equal = left == null && right == null;
            return comparison == QueryComparison.Equal ? equal : comparison == QueryComparison.NotEqual && !equal;
        }

        double leftNumber;
        double rightNumber = 0d;
        bool numeric = TryToDouble(left, out leftNumber) && TryToDouble(right, out rightNumber);
        int compare = numeric
            ? leftNumber.CompareTo(rightNumber)
            : string.Compare(Convert.ToString(left, CultureInfo.InvariantCulture), Convert.ToString(right, CultureInfo.InvariantCulture), StringComparison.Ordinal);

        switch (comparison)
        {
            case QueryComparison.Equal: return compare == 0;
            case QueryComparison.NotEqual: return compare != 0;
            case QueryComparison.GreaterThan: return compare > 0;
            case QueryComparison.GreaterThanOrEqual: return compare >= 0;
            case QueryComparison.LessThan: return compare < 0;
            case QueryComparison.LessThanOrEqual: return compare <= 0;
            default: return false;
        }
    }

    private static bool TryToDouble(object value, out double result)
    {
        if (value is byte || value is short || value is int || value is long || value is float || value is double || value is decimal)
        {
            result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return true;
        }
        return double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }
}
