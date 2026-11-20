using System;
using System.Diagnostics;

public interface IDataLoader<T>
{
    string FormatName { get; }
    T LoadData(string filePath);
}

public struct BenchmarkResult
{
    public string FormatName;
    public double ExecutionTimeMs;
    public long AllocatedMemoryBytes;
    public int CollectionCount;
    public object CustomData { get; set; }
}

public struct BenchmarkQueryStats
{
    public string FormatName;
    public int Iterations;
    public int RowCount;
    public double AvgMs;
    public double MinMs;
    public double MaxMs;
    public double P95Ms;
    public long AllocBytesPerOp;
    public int TotalGcCount;
    public bool IsSuccess;
    public string ErrorMessage;
}

public static class BenchmarkRunner
{
    public static BenchmarkResult Measure<T>(IDataLoader<T> loader, string filePath)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long memoryBefore = GC.GetTotalMemory(true);
        int gcCountBefore = GC.CollectionCount(0);

        Stopwatch sw = Stopwatch.StartNew();
        T data = loader.LoadData(filePath);
        sw.Stop();

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

    public static BenchmarkResult MeasureWithData<T>(IDataLoader<T> loader, string filePath, out T outputData)
    {
        var result = new BenchmarkResult { FormatName = loader.FormatName };

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long memBefore = GC.GetTotalMemory(true);
        int gcBefore0 = GC.CollectionCount(0);
        int gcBefore1 = GC.CollectionCount(1);
        int gcBefore2 = GC.CollectionCount(2);

        var stopwatch = Stopwatch.StartNew();
        T data = default;
        try
        {
            data = loader.LoadData(filePath);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.FormatName = $"{loader.FormatName} (FAILED: {ex.Message})";
            outputData = default;
            return result;
        }
        stopwatch.Stop();

        long memAfter = GC.GetTotalMemory(false);
        int gcAfter0 = GC.CollectionCount(0);
        int gcAfter1 = GC.CollectionCount(1);
        int gcAfter2 = GC.CollectionCount(2);

        result.ExecutionTimeMs = stopwatch.Elapsed.TotalMilliseconds;
        result.AllocatedMemoryBytes = Math.Max(0, memAfter - memBefore);
        result.CollectionCount = (gcAfter0 - gcBefore0) + (gcAfter1 - gcBefore1) + (gcAfter2 - gcBefore2);

        outputData = data;
        return result;
    }

    public static BenchmarkQueryStats MeasureQuerySession(
        IQueryBenchmarkSession session,
        QuerySpec query,
        int warmupIterations = 5,
        int measureIterations = 100)
    {
        var stats = new BenchmarkQueryStats
        {
            FormatName = session.FormatName,
            Iterations = measureIterations,
            IsSuccess = true
        };

        try
        {
            session.Prepare(query);

            // Warmup (JIT + 페이지 캐시 예열)
            for (int i = 0; i < warmupIterations; i++)
            {
                _ = session.ExecuteNoAlloc(query);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            double[] samples = new double[measureIterations];
            int gcBefore = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
            long allocBefore = GC.GetAllocatedBytesForCurrentThread();

            int lastRowCount = 0;
            var sw = new Stopwatch();

            for (int i = 0; i < measureIterations; i++)
            {
                sw.Restart();
                lastRowCount = session.ExecuteNoAlloc(query);
                sw.Stop();
                samples[i] = sw.Elapsed.TotalMilliseconds;
            }

            long allocAfter = GC.GetAllocatedBytesForCurrentThread();
            int gcAfter = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);

            Array.Sort(samples);
            double sum = 0;
            for (int i = 0; i < measureIterations; i++) sum += samples[i];

            int p95Index = Math.Min(measureIterations - 1, (int)(measureIterations * 0.95));

            stats.RowCount = lastRowCount;
            stats.AvgMs = sum / measureIterations;
            stats.MinMs = samples[0];
            stats.MaxMs = samples[measureIterations - 1];
            stats.P95Ms = samples[p95Index];
            stats.AllocBytesPerOp = Math.Max(0, (allocAfter - allocBefore) / measureIterations);
            stats.TotalGcCount = Math.Max(0, gcAfter - gcBefore);
        }
        catch (Exception ex)
        {
            stats.IsSuccess = false;
            stats.ErrorMessage = ex.Message;
        }

        return stats;
    }
}