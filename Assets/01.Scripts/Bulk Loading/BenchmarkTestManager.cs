using Cysharp.Threading.Tasks;
using System;
using System.IO;
using UnityEngine;

public class BenchmarkTestManager : MonoBehaviour
{
    [Header("UI 연결 참조")]
    [SerializeField] private BenchmarkRuntimeUI runtimeUI;

    [Header("정밀 쿼리 측정 설정")]
    [SerializeField] private int warmupIterations = 5;
    [SerializeField] private int measureIterations = 100;

    private static readonly TargetFormat[] DbBulkFormats =
    {
        TargetFormat.SQLite_ORM,
        TargetFormat.SQLite_NativeC,
        TargetFormat.LiteDB,
        TargetFormat.RocksDB
    };

    private bool isRunningBenchmark = false;

    private void Start()
    {
        if (runtimeUI == null)
        {
            runtimeUI = FindFirstObjectByType<BenchmarkRuntimeUI>();
            if (runtimeUI == null)
            {
                var uiObj = new GameObject("BenchmarkRuntimeUI_Auto");
                runtimeUI = uiObj.AddComponent<BenchmarkRuntimeUI>();
            }
        }

        runtimeUI.OnRunAllRequested -= HandleRunAllRequested;
        runtimeUI.OnRunAllRequested += HandleRunAllRequested;

        runtimeUI.OnRunQueryRequested -= HandleRunQueryRequested;
        runtimeUI.OnRunQueryRequested += HandleRunQueryRequested;
    }

    private void OnDestroy()
    {
        if (runtimeUI != null)
        {
            runtimeUI.OnRunAllRequested -= HandleRunAllRequested;
            runtimeUI.OnRunQueryRequested -= HandleRunQueryRequested;
        }
    }

    private void HandleRunAllRequested(DataShape shape, DataScale scale, DataComplexity complexity, TargetFormat targetFormats)
    {
        if (isRunningBenchmark)
        {
            runtimeUI.AppendOutput("<color=orange>이미 벤치마크가 진행 중입니다. 잠시만 기다려주세요.</color>");
            return;
        }

        runtimeUI.AppendOutput($"<color=cyan>=== [{shape} / {scale} / {complexity}] 전체 로드 벤치마크 시작 ===</color>");
        RunAllBenchmarksAsync(Application.streamingAssetsPath, shape, scale, complexity, targetFormats).Forget();
    }

    private void HandleRunQueryRequested(TargetFormat targetFormats, DataShape shape, DataScale scale, DataComplexity complexity, string query)
    {
        if (isRunningBenchmark)
        {
            runtimeUI.AppendOutput("<color=orange>이미 벤치마크가 진행 중입니다. 잠시만 기다려주세요.</color>");
            return;
        }

        runtimeUI.AppendOutput($"<color=yellow>=== 쿼리 벤치마크 측정 [{shape} / {scale} / {complexity}] (Warmup {warmupIterations} / Iter {measureIterations}) ===</color>\nQuery: {query}");
        RunQueriesAsync(Application.streamingAssetsPath, shape.ToString(), scale.ToString(), complexity.ToString(),
            $"GameData_{scale}_{complexity}_{shape}", targetFormats, query).Forget();
    }

    private async UniTaskVoid RunAllBenchmarksAsync(string saRoot, DataShape shape, DataScale scale, DataComplexity complexity, TargetFormat formats)
    {
        isRunningBenchmark = true;
        try
        {
            if (!BulkLoadRegistry.TryGetModule(shape, out var module))
            {
                runtimeUI.AppendOutput($"<color=red>[전체 로드] 등록된 모듈 없음: {shape}</color>");
                return;
            }

            var ctx = new BulkLoadContext
            {
                StreamingAssetsRoot = saRoot,
                Shape = shape,
                Scale = scale,
                Complexity = complexity
            };

            foreach (var format in DbBulkFormats)
            {
                if (!formats.HasFlag(format)) continue;

                BenchmarkResult result = default;
                bool executed = await UniTask.RunOnThreadPool(() => module.TryMeasure(format, ctx, out result));
                if (executed)
                {
                    PrintResult(result, "[전체 로드]");
                    await UniTask.Yield();
                }
            }

            // TODO: 비-DB 포맷(JSON, RawBinary, MessagePack, FlatBuffers) 전체 로드 모듈은 별도 최적화 후 추가 예정

            runtimeUI.AppendOutput("<color=cyan>=== 전체 로드 벤치마크 완료 ===</color>");
        }
        finally
        {
            isRunningBenchmark = false;
        }
    }

    private async UniTaskVoid RunQueriesAsync(string saRoot, string shapeStr, string scaleStr, string compStr, string fileName, TargetFormat targetFormats, string query)
    {
        isRunningBenchmark = true;
        try
        {
            QueryParseResult parsed = QuerySpecParser.Parse(query);
            if (!parsed.IsSuccess)
            {
                runtimeUI.AppendOutput($"<color=red>[쿼리 검증 실패] {parsed.ErrorMessage}</color>");
                return;
            }

            QuerySpec querySpec = parsed.Spec;
            QueryResult baseline = null;

            if (targetFormats.HasFlag(TargetFormat.SQLite_ORM))
            {
                string path = Path.Combine(saRoot, "SQLite", shapeStr, scaleStr, $"{fileName}.db");
                using (var session = new SqliteORMQueryEngine(path))
                {
                    baseline = await ExecuteSessionAndPrintAsync(session, querySpec, shapeStr, scaleStr, compStr, baseline);
                }
            }

            if (targetFormats.HasFlag(TargetFormat.SQLite_NativeC))
            {
                string path = Path.Combine(saRoot, "SQLite", shapeStr, scaleStr, $"{fileName}.db");
                using (var session = new SqliteNativeQueryEngine(path))
                {
                    baseline = await ExecuteSessionAndPrintAsync(session, querySpec, shapeStr, scaleStr, compStr, baseline);
                }
            }

            if (targetFormats.HasFlag(TargetFormat.LiteDB))
            {
                string path = Path.Combine(saRoot, "LiteDB", shapeStr, scaleStr, $"{fileName}.db");
                using (var session = new LiteDbQueryEngine(path))
                {
                    baseline = await ExecuteSessionAndPrintAsync(session, querySpec, shapeStr, scaleStr, compStr, baseline);
                }
            }

            if (targetFormats.HasFlag(TargetFormat.RocksDB))
            {
                string path = Path.Combine(saRoot, "RocksDB", shapeStr, scaleStr, fileName);
                using (var session = new RocksDbQueryEngine(path))
                {
                    baseline = await ExecuteSessionAndPrintAsync(session, querySpec, shapeStr, scaleStr, compStr, baseline);
                }
            }

            runtimeUI.AppendOutput("<color=yellow>=== 쿼리 벤치마크 측정 완료 ===</color>");
        }
        finally
        {
            isRunningBenchmark = false;
        }
    }

    private async UniTask<QueryResult> ExecuteSessionAndPrintAsync(
        IQueryBenchmarkSession session,
        QuerySpec querySpec,
        string shapeStr,
        string scaleStr,
        string compStr,
        QueryResult baseline)
    {
        QueryResult verification = await UniTask.RunOnThreadPool(() => session.ExecuteForVerification(querySpec));
        if (verification == null || !verification.IsSuccess)
        {
            runtimeUI.AppendOutput($"<color=red>[검증 실패] {session.FormatName}: {verification?.ErrorMessage ?? "알 수 없는 오류"}</color>");
            return baseline;
        }

        if (baseline == null)
        {
            baseline = verification;
            runtimeUI.AppendOutput($"<color=green>[기준 결과 등록] {session.FormatName}</color> (Rows: {verification.Rows.Count})");
        }
        else if (verification.IsEquivalentTo(baseline, out string diffReason))
        {
            runtimeUI.AppendOutput($"<color=green>[검증 성공] {session.FormatName}</color> (Rows: {verification.Rows.Count})");
        }
        else
        {
            runtimeUI.AppendOutput($"<color=orange>[구조 차이/검증 불일치] {session.FormatName}: {diffReason}</color>");
        }

        BenchmarkQueryStats stats = await UniTask.RunOnThreadPool(() =>
            BenchmarkRunner.MeasureQuerySession(session, querySpec, warmupIterations, measureIterations));

        if (!stats.IsSuccess)
        {
            runtimeUI.AppendOutput($"<color=red>[측정 실패] {session.FormatName}: {stats.ErrorMessage}</color>");
            return baseline;
        }

        string log = $"<b>[정밀 쿼리 측정] [{stats.FormatName}]</b> (Rows: {stats.RowCount})\n" +
                     $"Avg: <color=yellow>{stats.AvgMs:F4} ms</color> | Min: {stats.MinMs:F4} ms | P95: {stats.P95Ms:F4} ms | Max: {stats.MaxMs:F4} ms\n" +
                     $"순수 할당: <color=green>{stats.AllocBytesPerOp:N0} Bytes/op</color> | GC 발생: {stats.TotalGcCount}회 ({stats.Iterations}회 기준)";
        runtimeUI.AppendOutput(log);

        BenchmarkLogger.AppendLog($"{stats.FormatName}/{shapeStr}", scaleStr, compStr, stats.AvgMs, stats.AllocBytesPerOp);
        await UniTask.Yield();
        return baseline;
    }

    private void PrintResult(BenchmarkResult result, string category)
    {
        if (result.FormatName.Contains("FAILED"))
        {
            runtimeUI.AppendOutput($"[{result.FormatName}] 측정 실패로 스킵합니다.");
            return;
        }

        string log = $"<b>{category} [{result.FormatName}]</b>\n" +
                     $"시간: <color=yellow>{result.ExecutionTimeMs:F2} ms</color> | " +
                     $"메모리 할당: <color=green>{result.AllocatedMemoryBytes / 1024.0 / 1024.0:F2} MB</color> | " +
                     $"GC 발생: {result.CollectionCount}회";

        runtimeUI.AppendOutput(log);
    }
}