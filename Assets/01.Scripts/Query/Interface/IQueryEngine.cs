public interface IQueryEngine
{
    string FormatName { get; }

    // SQL은 실행 전에 QuerySpec으로 한 번만 파싱되며, 각 포맷은 이 구조를 자기 방식으로 실행한다.
    QueryResult Execute(QuerySpec query);
}
