using System;
using System.IO;
using UnityEngine;

#region 인스펙터용 Enum 정의
public enum DataScale
{
    S100,
    S1K,
    S10K,
    S100K
}

public enum DataComplexity
{
    C1_Low,
    C2_Mid,
    C3_High
}

[Flags]
public enum TargetFormat
{
    None = 0,
    JSON = 1 << 0,
    Binary = 1 << 1,
    SQLite = 1 << 2,
    ScriptableObject = 1 << 3,
    All = JSON | Binary | SQLite | ScriptableObject
}
#endregion

public class BenchmarkTestManager : MonoBehaviour
{
    [Header("벤치마크 조건 설정")]
    [SerializeField] private DataScale scale = DataScale.S100K;
    [SerializeField] private DataComplexity complexity = DataComplexity.C1_Low;

    [Header("실행할 포맷 선택 (Flag)")]
    [SerializeField] private TargetFormat targetFormats = TargetFormat.All;

    private void Start()
    {
        RunBenchmark();
    }

    [ContextMenu("Run Benchmark")]
    public void RunBenchmark()
    {
        string scaleStr = scale.ToString();
        string compStr = complexity.ToString();
        string fileName = $"GameData_{scaleStr}_{compStr}";
        string saRoot = Application.streamingAssetsPath;

        Debug.Log($"<color=cyan>=== [{scaleStr} / {compStr}] 벤치마크 시작 ===</color>");

        // 복잡도(C1, C2, C3)에 따른 타입 분기 실행
        switch (complexity)
        {
            case DataComplexity.C1_Low:
                RunBenchmark_C1(saRoot, scaleStr, fileName);
                break;
            case DataComplexity.C2_Mid:
                RunBenchmark_C2(saRoot, scaleStr, fileName);
                break;
            case DataComplexity.C3_High:
                RunBenchmark_C3(saRoot, scaleStr, fileName);
                break;
        }
    }

    #region C1 복잡도 전용 로더 실행
    private void RunBenchmark_C1(string saRoot, string scaleStr, string fileName)
    {
        if (targetFormats.HasFlag(TargetFormat.JSON))
        {
            string path = Path.Combine(saRoot, "JSON", scaleStr, $"{fileName}.json");
            ExecuteAndPrint(new JsonFileLoader<DataSet_C1>(), path);
        }

        if (targetFormats.HasFlag(TargetFormat.Binary))
        {
            string path = Path.Combine(saRoot, "Binary", scaleStr, $"{fileName}.bytes");
            ExecuteAndPrint(new BinaryFileLoader<DataSet_C1>(), path);
        }

        if (targetFormats.HasFlag(TargetFormat.SQLite))
        {
            string path = Path.Combine(saRoot, "SQLite", scaleStr, $"{fileName}.db");
            var loader = new SqliteFileLoader<DataSet_C1>((db, container) =>
            {
                container.items = db.Table<Master_Item_C1>().ToList();
                container.characters = db.Table<Runtime_Character_C1>().ToList();
            });
            ExecuteAndPrint(loader, path);
        }

        if (targetFormats.HasFlag(TargetFormat.ScriptableObject))
        {
            string resourcePath = $"SO/{scaleStr}/{fileName}";
            var loader = new ScriptableObjectLoader<SO_C1, DataSet_C1>(so => so.data);
            ExecuteAndPrint(loader, resourcePath);
        }
    }
    #endregion

    #region C2 복잡도 전용 로더 실행
    private void RunBenchmark_C2(string saRoot, string scaleStr, string fileName)
    {
        if (targetFormats.HasFlag(TargetFormat.JSON))
        {
            string path = Path.Combine(saRoot, "JSON", scaleStr, $"{fileName}.json");
            ExecuteAndPrint(new JsonFileLoader<DataSet_C2>(), path);
        }

        if (targetFormats.HasFlag(TargetFormat.Binary))
        {
            string path = Path.Combine(saRoot, "Binary", scaleStr, $"{fileName}.bytes");
            ExecuteAndPrint(new BinaryFileLoader<DataSet_C2>(), path);
        }

        if (targetFormats.HasFlag(TargetFormat.SQLite))
        {
            string path = Path.Combine(saRoot, "SQLite", scaleStr, $"{fileName}.db");
            var loader = new SqliteFileLoader<DataSet_C2>((db, container) =>
            {
                container.items = db.Table<Master_Item_C2>().ToList();
                container.tags = db.Table<Item_Tag_C2>().ToList();
                container.characters = db.Table<Runtime_Character_C2>().ToList();
                container.inventories = db.Table<Runtime_Inventory_C2>().ToList();
            });
            ExecuteAndPrint(loader, path);
        }

        if (targetFormats.HasFlag(TargetFormat.ScriptableObject))
        {
            string resourcePath = $"SO/{scaleStr}/{fileName}";
            var loader = new ScriptableObjectLoader<SO_C2, DataSet_C2>(so => so.data);
            ExecuteAndPrint(loader, resourcePath);
        }
    }
    #endregion

    #region C3 복잡도 전용 로더 실행
    private void RunBenchmark_C3(string saRoot, string scaleStr, string fileName)
    {
        if (targetFormats.HasFlag(TargetFormat.JSON))
        {
            string path = Path.Combine(saRoot, "JSON", scaleStr, $"{fileName}.json");
            ExecuteAndPrint(new JsonFileLoader<DataSet_C3>(), path);
        }

        if (targetFormats.HasFlag(TargetFormat.Binary))
        {
            string path = Path.Combine(saRoot, "Binary", scaleStr, $"{fileName}.bytes");
            ExecuteAndPrint(new BinaryFileLoader<DataSet_C3>(), path);
        }

        if (targetFormats.HasFlag(TargetFormat.SQLite))
        {
            string path = Path.Combine(saRoot, "SQLite", scaleStr, $"{fileName}.db");
            var loader = new SqliteFileLoader<DataSet_C3>((db, container) =>
            {
                container.users = db.Table<System_User_C3>().ToList();
                container.characters = db.Table<Runtime_Character_C3>().ToList();
                container.items = db.Table<Master_Item_C3>().ToList();
                container.effects = db.Table<Master_Effect_C3>().ToList();
                container.links = db.Table<Item_Effect_Link_C3>().ToList();
                container.inventories = db.Table<Runtime_Inventory_C3>().ToList();
            });
            ExecuteAndPrint(loader, path);
        }

        if (targetFormats.HasFlag(TargetFormat.ScriptableObject))
        {
            string resourcePath = $"SO/{scaleStr}/{fileName}";
            var loader = new ScriptableObjectLoader<SO_C3, DataSet_C3>(so => so.data);
            ExecuteAndPrint(loader, resourcePath);
        }
    }
    #endregion

    private void ExecuteAndPrint<T>(IDataLoader<T> loader, string path)
    {
        var result = BenchmarkRunner.Measure(loader, path);
        PrintResult(result);
    }

    private void PrintResult(BenchmarkResult result)
    {
        if (result.FormatName.Contains("FAILED"))
        {
            Debug.LogWarning($"[{result.FormatName}] 데이터 로드 실패로 측정을 스킵합니다.");
            return;
        }

        Debug.Log($"<b>[{result.FormatName}]</b> " +
                  $"시간: <color=yellow>{result.ExecutionTimeMs:F2} ms</color> | " +
                  $"메모리 할당: <color=green>{result.AllocatedMemoryBytes / 1024.0 / 1024.0:F2} MB</color> | " +
                  $"GC 발생: {result.CollectionCount}회");

        BenchmarkLogger.AppendLog(result.FormatName, scale.ToString(), complexity.ToString(), result.ExecutionTimeMs, result.AllocatedMemoryBytes);
    }
}