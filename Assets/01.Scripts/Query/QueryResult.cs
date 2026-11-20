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
                QueryRuntime.TryGetValue(left, orderField, out object leftValue);
                QueryRuntime.TryGetValue(right, orderField, out object rightValue);
                if (QueryRuntime.Compare(leftValue, rightValue, QueryComparison.Equal)) return 0;
                bool greater = QueryRuntime.Compare(leftValue, rightValue, QueryComparison.GreaterThan);
                if (greater) return query.OrderByDescending ? -1 : 1;
                return query.OrderByDescending ? 1 : -1;
            });
        }

        if (query.SelectAll || query.Projection.Count != 1 || Rows.Count != 1) return;

        if (Rows[0].TryGetValue(query.Projection[0].Name, out object value)) ScalarValue = value;
    }

    public bool IsEquivalentTo(QueryResult other)
    {
        return IsEquivalentTo(other, out _);
    }

    public bool IsEquivalentTo(QueryResult other, out string diffReason)
    {
        diffReason = null;
        if (other == null || !IsSuccess || !other.IsSuccess)
        {
            diffReason = "쿼리 실패 상태";
            return false;
        }

        if (!ValueEquals(ScalarValue, other.ScalarValue))
        {
            diffReason = $"Scalar 불일치 ({CanonicalValue(ScalarValue)} vs {CanonicalValue(other.ScalarValue)})";
            return false;
        }

        if (Rows.Count != other.Rows.Count)
        {
            diffReason = $"행 개수 불일치 ({Rows.Count} vs {other.Rows.Count})";
            return false;
        }

        if (Rows.Count == 0) return true;

        // 두 결과 집합에 공통으로 존재하는 컬럼만 추출 (NoSQL 임베디드 배열/딕셔너리 컬럼 제외)
        var myKeys = new HashSet<string>(Rows[0].Keys, StringComparer.OrdinalIgnoreCase);
        var otherKeys = new HashSet<string>(other.Rows[0].Keys, StringComparer.OrdinalIgnoreCase);
        myKeys.IntersectWith(otherKeys);

        if (myKeys.Count == 0)
        {
            diffReason = "공통 컬럼 없음";
            return false;
        }

        var sharedColumns = myKeys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        var leftRows = Rows.Select(r => CanonicalRow(r, sharedColumns)).OrderBy(v => v, StringComparer.Ordinal).ToList();
        var rightRows = other.Rows.Select(r => CanonicalRow(r, sharedColumns)).OrderBy(v => v, StringComparer.Ordinal).ToList();

        for (int i = 0; i < leftRows.Count; i++)
        {
            if (!leftRows[i].Equals(rightRows[i], StringComparison.Ordinal))
            {
                diffReason = $"Row[{i}] 값 불일치 ({leftRows[i]} vs {rightRows[i]})";
                return false;
            }
        }
        return true;
    }

    private static string CanonicalRow(Dictionary<string, object> row, List<string> columns)
    {
        var builder = new StringBuilder();
        for (int i = 0; i < columns.Count; i++)
        {
            string col = columns[i];
            QueryRuntime.TryGetValue(row, col, out object val);
            builder.Append(col.ToLowerInvariant()).Append('=').Append(CanonicalValue(val)).Append(';');
        }
        return builder.ToString();
    }

    private static string CanonicalValue(object value)
    {
        if (value == null) return "<null>";
        if (value is byte[] bytes) return Convert.ToBase64String(bytes);
        if (value is float f) return f.ToString("G6", CultureInfo.InvariantCulture);
        if (value is double d) return d.ToString("G6", CultureInfo.InvariantCulture);
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "<null>";
    }

    private static bool ValueEquals(object left, object right)
    {
        if (left == null || right == null) return left == null && right == null;
        return CanonicalValue(left).Equals(CanonicalValue(right), StringComparison.Ordinal);
    }
}