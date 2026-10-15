using System;
using System.Linq;
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
    SQLite_NativeC = 1 << 0,
    SQLite_ORM = 1 << 1,
    RocksDB = 1 << 2,
    LiteDB = 1 << 3,
    FlatBuffers = 1 << 4,
    MessagePack = 1 << 5,
    RawBinary = 1 << 6,
    JSON = 1 << 7,
    All = SQLite_NativeC | SQLite_ORM | RocksDB | LiteDB | FlatBuffers | MessagePack | RawBinary | JSON
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

        if (targetFormats.HasFlag(TargetFormat.RawBinary))
        {
            string path = Path.Combine(saRoot, "Binary", scaleStr, $"{fileName}.bytes");
            ExecuteAndPrint(new RawBinaryFileLoader<DataSet_C1>(), path);
        }

        if (targetFormats.HasFlag(TargetFormat.MessagePack))
        {
            string path = Path.Combine(saRoot, "MessagePack", scaleStr, $"{fileName}.mpk");
            ExecuteAndPrint(new MessagePackLoader<DataSet_C1>(), path);
        }

        if (targetFormats.HasFlag(TargetFormat.SQLite_ORM))
        {
            string path = Path.Combine(saRoot, "SQLite", scaleStr, $"{fileName}.db");
            var loader = new SqliteOrmLoader<DataSet_C1>((db, container) =>
            {
                container.items = db.Table<Master_Item_C1>().ToList();
                container.characters = db.Table<Runtime_Character_C1>().ToList();
            });
            ExecuteAndPrint(loader, path);
        }

        if (targetFormats.HasFlag(TargetFormat.SQLite_NativeC))
        {
            string path = Path.Combine(saRoot, "SQLite", scaleStr, $"{fileName}.db");
            var loader = new SqliteNativeLoader<DataSet_C1>((db, container) =>
            {
                db.ExecuteQuery("SELECT item_id, name, item_type, rarity, atk, price FROM Master_Item_C1", stmt =>
                {
                    container.items.Add(new Master_Item_C1
                    {
                        item_id = SqliteNativeConnection.GetInt(stmt, 0),
                        name = SqliteNativeConnection.GetString(stmt, 1),
                        item_type = SqliteNativeConnection.GetString(stmt, 2),
                        rarity = SqliteNativeConnection.GetInt(stmt, 3),
                        atk = SqliteNativeConnection.GetInt(stmt, 4),
                        price = SqliteNativeConnection.GetInt(stmt, 5)
                    });
                });
                db.ExecuteQuery("SELECT character_id, name, level, hp, pos_x, pos_y FROM Runtime_Character_C1", stmt =>
                {
                    container.characters.Add(new Runtime_Character_C1
                    {
                        character_id = SqliteNativeConnection.GetInt(stmt, 0),
                        name = SqliteNativeConnection.GetString(stmt, 1),
                        level = SqliteNativeConnection.GetInt(stmt, 2),
                        hp = SqliteNativeConnection.GetInt(stmt, 3),
                        pos_x = (float)SqliteNativeConnection.GetDouble(stmt, 4),
                        pos_y = (float)SqliteNativeConnection.GetDouble(stmt, 5)
                    });
                });
            });
            ExecuteAndPrint(loader, path);
        }

        if (targetFormats.HasFlag(TargetFormat.LiteDB))
        {
            string path = Path.Combine(saRoot, "LiteDB", scaleStr, $"{fileName}.db");
            var loader = new LiteDbLoader<DataSet_C1>((db, container) =>
            {
                container.items = db.GetCollection<Master_Item_C1>("items").FindAll().ToList();
                container.characters = db.GetCollection<Runtime_Character_C1>("characters").FindAll().ToList();
            });
            ExecuteAndPrint(loader, path);
        }

        if (targetFormats.HasFlag(TargetFormat.RocksDB))
        {
            string path = Path.Combine(saRoot, "RocksDB", scaleStr, fileName);
            ExecuteAndPrint(new RocksDbLoader_C1(), path);
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

        if (targetFormats.HasFlag(TargetFormat.RawBinary))
        {
            string path = Path.Combine(saRoot, "Binary", scaleStr, $"{fileName}.bytes");
            ExecuteAndPrint(new RawBinaryFileLoader<DataSet_C2>(), path);
        }

        if (targetFormats.HasFlag(TargetFormat.MessagePack))
        {
            string path = Path.Combine(saRoot, "MessagePack", scaleStr, $"{fileName}.mpk");
            ExecuteAndPrint(new MessagePackLoader<DataSet_C2>(), path);
        }

        if (targetFormats.HasFlag(TargetFormat.SQLite_ORM))
        {
            string path = Path.Combine(saRoot, "SQLite", scaleStr, $"{fileName}.db");
            var loader = new SqliteOrmLoader<DataSet_C2>((db, container) =>
            {
                container.items = db.Table<Master_Item_C2>().ToList();
                container.tags = db.Table<Item_Tag_C2>().ToList();
                container.characters = db.Table<Runtime_Character_C2>().ToList();
                container.inventories = db.Table<Runtime_Inventory_C2>().ToList();
            });
            ExecuteAndPrint(loader, path);
        }

        if (targetFormats.HasFlag(TargetFormat.LiteDB))
        {
            string path = Path.Combine(saRoot, "LiteDB", scaleStr, $"{fileName}.db");
            var loader = new LiteDbLoader<DataSet_C2>((db, container) =>
            {
                container.items = db.GetCollection<Master_Item_C2>("items").FindAll().ToList();
                container.tags = db.GetCollection<Item_Tag_C2>("tags").FindAll().ToList();
                container.characters = db.GetCollection<Runtime_Character_C2>("characters").FindAll().ToList();
                container.inventories = db.GetCollection<Runtime_Inventory_C2>("inventories").FindAll().ToList();
            });
            ExecuteAndPrint(loader, path);
        }

        if (targetFormats.HasFlag(TargetFormat.RocksDB))
        {
            string path = Path.Combine(saRoot, "RocksDB", scaleStr, fileName);
            ExecuteAndPrint(new RocksDbLoader_C2(), path);
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

        if (targetFormats.HasFlag(TargetFormat.RawBinary))
        {
            string path = Path.Combine(saRoot, "Binary", scaleStr, $"{fileName}.bytes");
            ExecuteAndPrint(new RawBinaryFileLoader<DataSet_C3>(), path);
        }

        if (targetFormats.HasFlag(TargetFormat.MessagePack))
        {
            string path = Path.Combine(saRoot, "MessagePack", scaleStr, $"{fileName}.mpk");
            ExecuteAndPrint(new MessagePackLoader<DataSet_C3>(), path);
        }

        if (targetFormats.HasFlag(TargetFormat.SQLite_ORM))
        {
            string path = Path.Combine(saRoot, "SQLite", scaleStr, $"{fileName}.db");
            var loader = new SqliteOrmLoader<DataSet_C3>((db, container) =>
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

        if (targetFormats.HasFlag(TargetFormat.LiteDB))
        {
            string path = Path.Combine(saRoot, "LiteDB", scaleStr, $"{fileName}.db");
            var loader = new LiteDbLoader<DataSet_C3>((db, container) =>
            {
                container.users = db.GetCollection<System_User_C3>("users").FindAll().ToList();
                container.characters = db.GetCollection<Runtime_Character_C3>("characters").FindAll().ToList();
                container.items = db.GetCollection<Master_Item_C3>("items").FindAll().ToList();
                container.effects = db.GetCollection<Master_Effect_C3>("effects").FindAll().ToList();
                container.links = db.GetCollection<Item_Effect_Link_C3>("links").FindAll().ToList();
                container.inventories = db.GetCollection<Runtime_Inventory_C3>("inventories").FindAll().ToList();
            });
            ExecuteAndPrint(loader, path);
        }

        if (targetFormats.HasFlag(TargetFormat.RocksDB))
        {
            string path = Path.Combine(saRoot, "RocksDB", scaleStr, fileName);
            ExecuteAndPrint(new RocksDbLoader_C3(), path);
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