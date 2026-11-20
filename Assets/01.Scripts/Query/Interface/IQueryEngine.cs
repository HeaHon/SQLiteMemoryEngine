using System;

public interface IQueryEngine
{
    string FormatName { get; }

    // 검증 및 범용 콘솔 출력용 (QueryResult 반환)
    QueryResult Execute(QuerySpec query);
}

/// <summary>
/// 커넥션 Open/Close 및 쿼리 컴파일 비용을 측정에서 분리하기 위한 세션 인터페이스.
/// Warmup + N회 반복 측정 시 재사용한다.
/// </summary>
public interface IQueryBenchmarkSession : IDisposable
{
    string FormatName { get; }

    // 1. 정확도 검증용 (측정 루프 밖에서 1회 실행)
    QueryResult ExecuteForVerification(QuerySpec query);

    // 2. 순수 성능/GC 측정용 사전 준비 (SQL 컴파일, BsonExpression 빌드, 키 바이트 인코딩 등)
    void Prepare(QuerySpec query);

    // 3. 순수 성능/GC 측정용 실행 (Dictionary/Boxing 없이 네이티브 방식으로 결과 행 수 반환)
    int ExecuteNoAlloc(QuerySpec query);
}