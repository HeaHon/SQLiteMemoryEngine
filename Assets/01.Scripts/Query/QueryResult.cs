using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

public class QueryResult
{
    public object ScalarValue { get; set; }
    public List<Dictionary<string, object>> Rows { get; set; } = new List<Dictionary<string, object>>();
    public bool IsSuccess { get; set; } = true;
    public string ErrorMessage { get; set; }

    public void AddProjectedRow(QuerySpec query, Dictionary<string, object> row)
    {
        Rows.Add(query == null ? row : query.ProjectRow(row));
    }

    public void Complete(QuerySpec query)
    {
        if (!IsSuccess || query == null) return;

        if (!string.IsNullOrEmpty(query.OrderByExpression) && Rows.Count > 1)
        {
            string orderField = QuerySpecParser.NormalizeField(query.OrderByExpression);
            Rows.Sort((left, right) =>
            {
                object leftValue;
                object rightValue;
                QueryRuntime.TryGetValue(left, orderField, out leftValue);
                QueryRuntime.TryGetValue(right, orderField, out rightValue);
                if (QueryRuntime.Compare(leftValue, rightValue, QueryComparison.Equal)) return 0;
                bool greater = QueryRuntime.Compare(leftValue, rightValue, QueryComparison.GreaterThan);
                if (greater) return query.OrderByDescending ? -1 : 1;
                return query.OrderByDescending ? 1 : -1;
            });
        }

        if (query.SelectAll || query.Projection.Count != 1 || Rows.Count != 1) return;

        object value;
        if (Rows[0].TryGetValue(query.Projection[0].Name, out value)) ScalarValue = value;
    }

    public bool IsEquivalentTo(QueryResult other)
    {
        if (other == null || !IsSuccess || !other.IsSuccess) return false;
        if (!ValueEquals(ScalarValue, other.ScalarValue) || Rows.Count != other.Rows.Count) return false;

        var leftRows = Rows.Select(CanonicalRow).OrderBy(value => value, StringComparer.Ordinal).ToList();
        var rightRows = other.Rows.Select(CanonicalRow).OrderBy(value => value, StringComparer.Ordinal).ToList();
        for (int i = 0; i < leftRows.Count; i++)
        {
            if (!leftRows[i].Equals(rightRows[i], StringComparison.Ordinal)) return false;
        }
        return true;
    }

    private static string CanonicalRow(Dictionary<string, object> row)
    {
        var builder = new StringBuilder();
        foreach (KeyValuePair<string, object> pair in row.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(pair.Key.ToLowerInvariant()).Append('=').Append(CanonicalValue(pair.Value)).Append(';');
        }
        return builder.ToString();
    }

    private static string CanonicalValue(object value)
    {
        if (value == null) return "<null>";
        if (value is byte[] bytes) return Convert.ToBase64String(bytes);
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "<null>";
    }

    private static bool ValueEquals(object left, object right)
    {
        if (left == null || right == null) return left == null && right == null;
        return CanonicalValue(left).Equals(CanonicalValue(right), StringComparison.Ordinal);
    }
}
