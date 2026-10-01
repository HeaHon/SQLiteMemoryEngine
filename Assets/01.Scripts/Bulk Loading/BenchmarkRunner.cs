using System;
using System.Diagnostics;
using UnityEngine;

// 1. 공통 데이터 로더 인터페이스 (DIP)
public interface IDataLoader<T>
{
    string FormatName { get; }
    T LoadData(string filePath);
}

// 2. 측정 결과 데이터 구조체
public struct BenchmarkResult
{
    public string FormatName;
    public double ExecutionTimeMs;  // 소요 시간 (ms)
    public long AllocatedMemoryBytes; // 로딩 전후 메모리 증가량 (Bytes)
    public int CollectionCount;     // 발생한 GC 횟수
}

// 3. 부하 측정 전담 클래스 (SRP)
public static class BenchmarkRunner
{
    public static BenchmarkResult Measure<T>(IDataLoader<T> loader, string filePath)
    {
        // 측정 전 GC 정리를 통해 정확한 메모리 오버헤드 측정 준비
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long memoryBefore = GC.GetTotalMemory(true);
        int gcCountBefore = GC.CollectionCount(0);

        Stopwatch sw = Stopwatch.StartNew();

        // 벌크 로딩 실행
        T data = loader.LoadData(filePath);

        sw.Stop();

        // 데이터 로드 실패 시 (null일 때) 0ms 결과 반환 또는 처리
        if (data == null)
        {
            return new BenchmarkResult
            {
                FormatName = $"{loader.FormatName} (FAILED)",
                ExecutionTimeMs = 0,
                AllocatedMemoryBytes = 0,
                CollectionCount = 0
            };
        }

        long memoryAfter = GC.GetTotalMemory(false);
        int gcCountAfter = GC.CollectionCount(0);

        return new BenchmarkResult
        {
            FormatName = loader.FormatName,
            ExecutionTimeMs = sw.Elapsed.TotalMilliseconds,
            AllocatedMemoryBytes = Math.Max(0, memoryAfter - memoryBefore),
            CollectionCount = gcCountAfter - gcCountBefore
        };
    }
}