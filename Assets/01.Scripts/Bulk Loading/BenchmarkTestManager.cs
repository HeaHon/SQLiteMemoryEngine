using Cysharp.Threading.Tasks;
using GameData.Schema;
using Google.FlatBuffers;
using System;
using System.IO;
using System.Linq;
using UnityEngine;

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
    RawBinary = 1 << 5,
    MessagePack = 1 << 6,
    JSON = 1 << 7,
    All = SQLite_NativeC | SQLite_ORM | RocksDB | LiteDB | FlatBuffers | RawBinary | MessagePack | JSON
}

public class BenchmarkTestManager : MonoBehaviour
{
    [Header("UI 연결 참조")]
    [SerializeField] private BenchmarkRuntimeUI runtimeUI;

    // 중복 실행 방지용 플래그
    private bool isRunningBenchmark = false;

    private void Start()
    {
        // 씬 내 UI 자동 탐색 및 연결
        if (runtimeUI == null)
        {
            runtimeUI = FindFirstObjectByType<BenchmarkRuntimeUI>();
            if (runtimeUI == null)
            {
                var uiObj = new GameObject("BenchmarkRuntimeUI_Auto");
                runtimeUI = uiObj.AddComponent<BenchmarkRuntimeUI>();
            }
        }

        // 벤치마크 UI 이벤트 바인딩
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

    /// <summary>
    /// UI에서 전체 로드 벤치마크 요청 시 호출
    /// </summary>
    private void HandleRunAllRequested(DataScale scale, DataComplexity complexity, TargetFormat targetFormats)
    {
        if (isRunningBenchmark)
        {
            runtimeUI.AppendOutput("<color=orange>이미 벤치마크가 진행 중입니다. 잠시만 기다려주세요.</color>");
            return;
        }

        string scaleStr = scale.ToString();
        string compStr = complexity.ToString();
        string fileName = $"GameData_{scaleStr}_{compStr}";
        string saRoot = Application.streamingAssetsPath;

        runtimeUI.AppendOutput($"<color=cyan>=== [{scaleStr} / {compStr}] 전체 로드 벤치마크 시작 ===</color>");

        RunAllBenchmarksAsync(saRoot, scaleStr, fileName, complexity, targetFormats).Forget();
    }

    /// <summary>
    /// UI에서 개별/조건부 SQL 쿼리 실행 요청 시 호출
    /// </summary>
    private void HandleRunQueryRequested(TargetFormat targetFormats, DataScale scale, DataComplexity complexity, string query)
    {
        if (isRunningBenchmark)
        {
            runtimeUI.AppendOutput("<color=orange>이미 벤치마크가 진행 중입니다. 잠시만 기다려주세요.</color>");
            return;
        }

        string scaleStr = scale.ToString();
        string compStr = complexity.ToString();
        string fileName = $"GameData_{scaleStr}_{compStr}";
        string saRoot = Application.streamingAssetsPath;

        runtimeUI.AppendOutput($"<color=yellow>=== 쿼리 벤치마크 측정 [{scaleStr} / {compStr}] ===</color>\nQuery: {query}");

        RunQueriesAsync(saRoot, scaleStr, fileName, targetFormats, query).Forget();
    }

    private async UniTaskVoid RunAllBenchmarksAsync(string saRoot, string scaleStr, string fileName, DataComplexity complexity, TargetFormat formats)
    {
        isRunningBenchmark = true;
        try
        {
            switch (complexity)
            {
                case DataComplexity.C1_Low:
                    await RunAll_C1_Async(saRoot, scaleStr, fileName, formats);
                    break;
                case DataComplexity.C2_Mid:
                    await RunAll_C2_Async(saRoot, scaleStr, fileName, formats);
                    break;
                case DataComplexity.C3_High:
                    await RunAll_C3_Async(saRoot, scaleStr, fileName, formats);
                    break;
            }

            runtimeUI.AppendOutput("<color=cyan>=== 전체 로드 벤치마크 완료 ===</color>");
        }
        finally
        {
            isRunningBenchmark = false;
        }
    }

    private async UniTaskVoid RunQueriesAsync(string saRoot, string scaleStr, string fileName, TargetFormat targetFormats, string query)
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
                string path = Path.Combine(saRoot, "SQLite", scaleStr, $"{fileName}.db");
                baseline = await ExecuteQueryAndPrintAsync(new SqliteQueryLoader(path, querySpec), path, baseline);
            }

            if (targetFormats.HasFlag(TargetFormat.SQLite_NativeC))
            {
                string path = Path.Combine(saRoot, "SQLite", scaleStr, $"{fileName}.db");
                baseline = await ExecuteQueryAndPrintAsync(new SqliteNativeQueryLoader(path, querySpec), path, baseline);
            }

            if (targetFormats.HasFlag(TargetFormat.LiteDB))
            {
                string path = Path.Combine(saRoot, "LiteDB", scaleStr, $"{fileName}.db");
                baseline = await ExecuteQueryAndPrintAsync(new LiteDbQueryLoader(path, querySpec), path, baseline);
            }

            if (targetFormats.HasFlag(TargetFormat.RocksDB))
            {
                string path = Path.Combine(saRoot, "RocksDB", scaleStr, fileName);
                baseline = await ExecuteQueryAndPrintAsync(new RocksDbQueryLoader(path, querySpec), path, baseline);
            }

            if (targetFormats.HasFlag(TargetFormat.FlatBuffers))
            {
                string path = Path.Combine(saRoot, "FlatBuffers", scaleStr, $"{fileName}.bytes");
                baseline = await ExecuteQueryAndPrintAsync(new FlatBuffersQueryLoader(path, querySpec), path, baseline);
            }

            if (targetFormats.HasFlag(TargetFormat.RawBinary))
            {
                string path = Path.Combine(saRoot, "Binary", scaleStr, $"{fileName}.bytes");
                baseline = await ExecuteQueryAndPrintAsync(new RawBinaryQueryLoader(path, querySpec), path, baseline);
            }

            runtimeUI.AppendOutput("<color=yellow>=== 쿼리 벤치마크 측정 완료 ===</color>");
        }
        finally
        {
            isRunningBenchmark = false;
        }
    }

    #region 복잡도별 전체 로더 실행 그룹
    private async UniTask RunAll_C1_Async(string saRoot, string scaleStr, string fileName, TargetFormat formats)
    {
        if (formats.HasFlag(TargetFormat.JSON))
            await ExecuteAndPrintAsync(new JsonFileLoader<DataSet_C1>(), Path.Combine(saRoot, "JSON", scaleStr, $"{fileName}.json"));

        if (formats.HasFlag(TargetFormat.RawBinary))
            await ExecuteAndPrintAsync(new RawBinaryFileLoader<DataSet_C1>(), Path.Combine(saRoot, "Binary", scaleStr, $"{fileName}.bytes"));

        if (formats.HasFlag(TargetFormat.MessagePack))
            await ExecuteAndPrintAsync(new MessagePackLoader<DataSet_C1>(), Path.Combine(saRoot, "MessagePack", scaleStr, $"{fileName}.mpk"));

        if (formats.HasFlag(TargetFormat.FlatBuffers))
        {
            var fbLoader = new FlatBuffersLoader<DataSet_C1>(bb => {
                var root = MasterGameDataSet.GetRootAsMasterGameDataSet(bb);
                var c1Data = root.C1Data;
                var container = new DataSet_C1();
                if (c1Data.HasValue)
                {
                    var data = c1Data.Value;
                    container.items = new System.Collections.Generic.List<Master_Item_C1>(data.ItemsLength);
                    for (int i = 0; i < data.ItemsLength; i++)
                    {
                        var item = data.Items(i).Value;
                        container.items.Add(new Master_Item_C1 { item_id = item.ItemId, name = item.Name, item_type = item.ItemType, rarity = item.Rarity, atk = item.Atk, price = item.Price });
                    }
                    container.characters = new System.Collections.Generic.List<Runtime_Character_C1>(data.CharactersLength);
                    for (int i = 0; i < data.CharactersLength; i++)
                    {
                        var ch = data.Characters(i).Value;
                        container.characters.Add(new Runtime_Character_C1 { character_id = ch.CharacterId, name = ch.Name, level = ch.Level, hp = ch.Hp, pos_x = ch.PosX, pos_y = ch.PosY });
                    }
                }
                return container;
            });
            await ExecuteAndPrintAsync(fbLoader, Path.Combine(saRoot, "FlatBuffers", scaleStr, $"{fileName}.bytes"));
        }

        if (formats.HasFlag(TargetFormat.SQLite_ORM))
        {
            var loader = new SqliteOrmLoader<DataSet_C1>((db, container) => {
                container.items = db.Table<Master_Item_C1>().ToList();
                container.characters = db.Table<Runtime_Character_C1>().ToList();
            });
            await ExecuteAndPrintAsync(loader, Path.Combine(saRoot, "SQLite", scaleStr, $"{fileName}.db"));
        }

        if (formats.HasFlag(TargetFormat.SQLite_NativeC))
        {
            var loader = new SqliteNativeLoader<DataSet_C1>((db, container) => {
                db.ExecuteQuery("SELECT item_id, name, item_type, rarity, atk, price FROM Master_Item_C1", stmt => {
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
                db.ExecuteQuery("SELECT character_id, name, level, hp, pos_x, pos_y FROM Runtime_Character_C1", stmt => {
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
            await ExecuteAndPrintAsync(loader, Path.Combine(saRoot, "SQLite", scaleStr, $"{fileName}.db"));
        }

        if (formats.HasFlag(TargetFormat.LiteDB))
        {
            var loader = new LiteDbLoader<DataSet_C1>((db, container) => {
                container.items = db.GetCollection<Master_Item_C1>("items").FindAll().ToList();
                container.characters = db.GetCollection<Runtime_Character_C1>("characters").FindAll().ToList();
            });
            await ExecuteAndPrintAsync(loader, Path.Combine(saRoot, "LiteDB", scaleStr, $"{fileName}.db"));
        }

        if (formats.HasFlag(TargetFormat.RocksDB))
        {
            await ExecuteAndPrintAsync(new RocksDbLoader_C1(), Path.Combine(saRoot, "RocksDB", scaleStr, fileName));
        }
    }

    private async UniTask RunAll_C2_Async(string saRoot, string scaleStr, string fileName, TargetFormat formats)
    {
        if (formats.HasFlag(TargetFormat.JSON))
            await ExecuteAndPrintAsync(new JsonFileLoader<DataSet_C2>(), Path.Combine(saRoot, "JSON", scaleStr, $"{fileName}.json"));

        if (formats.HasFlag(TargetFormat.RawBinary))
            await ExecuteAndPrintAsync(new RawBinaryFileLoader<DataSet_C2>(), Path.Combine(saRoot, "Binary", scaleStr, $"{fileName}.bytes"));

        if (formats.HasFlag(TargetFormat.MessagePack))
            await ExecuteAndPrintAsync(new MessagePackLoader<DataSet_C2>(), Path.Combine(saRoot, "MessagePack", scaleStr, $"{fileName}.mpk"));

        if (formats.HasFlag(TargetFormat.FlatBuffers))
        {
            var fbLoader = new FlatBuffersLoader<DataSet_C2>(bb => {
                var root = MasterGameDataSet.GetRootAsMasterGameDataSet(bb);
                var c2Data = root.C2Data;
                var container = new DataSet_C2();
                if (c2Data.HasValue)
                {
                    var data = c2Data.Value;
                    container.items = new System.Collections.Generic.List<Master_Item_C2>(data.ItemsLength);
                    for (int i = 0; i < data.ItemsLength; i++)
                    {
                        var item = data.Items(i).Value;
                        container.items.Add(new Master_Item_C2 { item_id = item.ItemId, item_code = item.ItemCode, name = item.Name, item_type = item.ItemType, rarity = item.Rarity, req_level = item.ReqLevel, base_atk = item.BaseAtk });
                    }
                    container.tags = new System.Collections.Generic.List<Item_Tag_C2>(data.TagsLength);
                    for (int i = 0; i < data.TagsLength; i++)
                    {
                        var tag = data.Tags(i).Value;
                        container.tags.Add(new Item_Tag_C2 { item_id = tag.ItemId, tag_name = tag.TagName });
                    }
                    container.characters = new System.Collections.Generic.List<Runtime_Character_C2>(data.CharactersLength);
                    for (int i = 0; i < data.CharactersLength; i++)
                    {
                        var ch = data.Characters(i).Value;
                        container.characters.Add(new Runtime_Character_C2 { character_id = ch.CharacterId, name = ch.Name, level = ch.Level, cur_hp = ch.CurHp, pos_x = ch.PosX, pos_y = ch.PosY });
                    }
                    container.inventories = new System.Collections.Generic.List<Runtime_Inventory_C2>(data.InventoriesLength);
                    for (int i = 0; i < data.InventoriesLength; i++)
                    {
                        var inv = data.Inventories(i).Value;
                        container.inventories.Add(new Runtime_Inventory_C2 { instance_id = inv.InstanceId, character_id = inv.CharacterId, item_id = inv.ItemId, quantity = inv.Quantity });
                    }
                }
                return container;
            });
            await ExecuteAndPrintAsync(fbLoader, Path.Combine(saRoot, "FlatBuffers", scaleStr, $"{fileName}.bytes"));
        }

        if (formats.HasFlag(TargetFormat.SQLite_ORM))
        {
            var loader = new SqliteOrmLoader<DataSet_C2>((db, container) => {
                container.items = db.Table<Master_Item_C2>().ToList();
                container.tags = db.Table<Item_Tag_C2>().ToList();
                container.characters = db.Table<Runtime_Character_C2>().ToList();
                container.inventories = db.Table<Runtime_Inventory_C2>().ToList();
            });
            await ExecuteAndPrintAsync(loader, Path.Combine(saRoot, "SQLite", scaleStr, $"{fileName}.db"));
        }

        if (formats.HasFlag(TargetFormat.LiteDB))
        {
            var loader = new LiteDbLoader<DataSet_C2>((db, container) => {
                container.items = db.GetCollection<Master_Item_C2>("items").FindAll().ToList();
                container.tags = db.GetCollection<Item_Tag_C2>("tags").FindAll().ToList();
                container.characters = db.GetCollection<Runtime_Character_C2>("characters").FindAll().ToList();
                container.inventories = db.GetCollection<Runtime_Inventory_C2>("inventories").FindAll().ToList();
            });
            await ExecuteAndPrintAsync(loader, Path.Combine(saRoot, "LiteDB", scaleStr, $"{fileName}.db"));
        }

        if (formats.HasFlag(TargetFormat.RocksDB))
        {
            await ExecuteAndPrintAsync(new RocksDbLoader_C2(), Path.Combine(saRoot, "RocksDB", scaleStr, fileName));
        }
    }

    private async UniTask RunAll_C3_Async(string saRoot, string scaleStr, string fileName, TargetFormat formats)
    {
        if (formats.HasFlag(TargetFormat.JSON))
            await ExecuteAndPrintAsync(new JsonFileLoader<DataSet_C3>(), Path.Combine(saRoot, "JSON", scaleStr, $"{fileName}.json"));

        if (formats.HasFlag(TargetFormat.RawBinary))
            await ExecuteAndPrintAsync(new RawBinaryFileLoader<DataSet_C3>(), Path.Combine(saRoot, "Binary", scaleStr, $"{fileName}.bytes"));

        if (formats.HasFlag(TargetFormat.MessagePack))
            await ExecuteAndPrintAsync(new MessagePackLoader<DataSet_C3>(), Path.Combine(saRoot, "MessagePack", scaleStr, $"{fileName}.mpk"));

        if (formats.HasFlag(TargetFormat.FlatBuffers))
        {
            var fbLoader = new FlatBuffersLoader<DataSet_C3>(bb => {
                var root = MasterGameDataSet.GetRootAsMasterGameDataSet(bb);
                var c3Data = root.C3Data;
                var container = new DataSet_C3();
                if (c3Data.HasValue)
                {
                    var data = c3Data.Value;
                    container.users = new System.Collections.Generic.List<System_User_C3>(data.UsersLength);
                    for (int i = 0; i < data.UsersLength; i++)
                    {
                        var user = data.Users(i).Value;
                        container.users.Add(new System_User_C3 { user_id = user.UserId, account_name = user.AccountName, created_at = user.CreatedAt });
                    }
                    container.characters = new System.Collections.Generic.List<Runtime_Character_C3>(data.CharactersLength);
                    for (int i = 0; i < data.CharactersLength; i++)
                    {
                        var ch = data.Characters(i).Value;
                        container.characters.Add(new Runtime_Character_C3 { character_id = ch.CharacterId, user_id = ch.UserId, name = ch.Name, guild_id = ch.GuildId });
                    }
                    container.items = new System.Collections.Generic.List<Master_Item_C3>(data.ItemsLength);
                    for (int i = 0; i < data.ItemsLength; i++)
                    {
                        var item = data.Items(i).Value;
                        container.items.Add(new Master_Item_C3 { item_id = item.ItemId, name = item.Name, rarity = item.Rarity });
                    }
                    container.effects = new System.Collections.Generic.List<Master_Effect_C3>(data.EffectsLength);
                    for (int i = 0; i < data.EffectsLength; i++)
                    {
                        var eff = data.Effects(i).Value;
                        container.effects.Add(new Master_Effect_C3 { effect_id = eff.EffectId, effect_type = eff.EffectType, value = eff.Value });
                    }
                    container.links = new System.Collections.Generic.List<Item_Effect_Link_C3>(data.LinksLength);
                    for (int i = 0; i < data.LinksLength; i++)
                    {
                        var link = data.Links(i).Value;
                        container.links.Add(new Item_Effect_Link_C3 { item_id = link.ItemId, effect_id = link.EffectId });
                    }
                    container.inventories = new System.Collections.Generic.List<Runtime_Inventory_C3>(data.InventoriesLength);
                    for (int i = 0; i < data.InventoriesLength; i++)
                    {
                        var inv = data.Inventories(i).Value;
                        container.inventories.Add(new Runtime_Inventory_C3 { instance_id = inv.InstanceId, character_id = inv.CharacterId, item_id = inv.ItemId, durability = inv.Durability });
                    }
                }
                return container;
            });
            await ExecuteAndPrintAsync(fbLoader, Path.Combine(saRoot, "FlatBuffers", scaleStr, $"{fileName}.bytes"));
        }

        if (formats.HasFlag(TargetFormat.SQLite_ORM))
        {
            var loader = new SqliteOrmLoader<DataSet_C3>((db, container) => {
                container.users = db.Table<System_User_C3>().ToList();
                container.characters = db.Table<Runtime_Character_C3>().ToList();
                container.items = db.Table<Master_Item_C3>().ToList();
                container.effects = db.Table<Master_Effect_C3>().ToList();
                container.links = db.Table<Item_Effect_Link_C3>().ToList();
                container.inventories = db.Table<Runtime_Inventory_C3>().ToList();
            });
            await ExecuteAndPrintAsync(loader, Path.Combine(saRoot, "SQLite", scaleStr, $"{fileName}.db"));
        }

        if (formats.HasFlag(TargetFormat.LiteDB))
        {
            var loader = new LiteDbLoader<DataSet_C3>((db, container) => {
                container.users = db.GetCollection<System_User_C3>("users").FindAll().ToList();
                container.characters = db.GetCollection<Runtime_Character_C3>("characters").FindAll().ToList();
                container.items = db.GetCollection<Master_Item_C3>("items").FindAll().ToList();
                container.effects = db.GetCollection<Master_Effect_C3>("effects").FindAll().ToList();
                container.links = db.GetCollection<Item_Effect_Link_C3>("links").FindAll().ToList();
                container.inventories = db.GetCollection<Runtime_Inventory_C3>("inventories").FindAll().ToList();
            });
            await ExecuteAndPrintAsync(loader, Path.Combine(saRoot, "LiteDB", scaleStr, $"{fileName}.db"));
        }

        if (formats.HasFlag(TargetFormat.RocksDB))
        {
            await ExecuteAndPrintAsync(new RocksDbLoader_C3(), Path.Combine(saRoot, "RocksDB", scaleStr, fileName));
        }
    }
    #endregion

    private async UniTask ExecuteAndPrintAsync<T>(IDataLoader<T> loader, string path)
    {
        BenchmarkResult result = await UniTask.RunOnThreadPool(() => BenchmarkRunner.Measure(loader, path));
        PrintResult(result, "[전체 로드]");
        await UniTask.Yield();
    }

    private async UniTask<QueryResult> ExecuteQueryAndPrintAsync(IDataLoader<QueryResult> queryLoader, string path, QueryResult baseline)
    {
        QueryResult queryResult = null;

        BenchmarkResult result = await UniTask.RunOnThreadPool(() => {
            return BenchmarkRunner.MeasureWithData(queryLoader, path, out queryResult);
        });

        if (result.FormatName.Contains("FAILED"))
        {
            runtimeUI.AppendOutput($"[{result.FormatName}] 쿼리 측정 실패로 스킵합니다.");
            return baseline;
        }

        if (queryResult != null && queryResult.IsSuccess)
        {
            bool hasData = (queryResult.ScalarValue != null) || (queryResult.Rows != null && queryResult.Rows.Count > 0);

            if (hasData)
            {
                string contentLog = "";
                if (queryResult.ScalarValue != null)
                {
                    contentLog = $"Scalar Value: <color=cyan>{queryResult.ScalarValue}</color>";
                }
                else if (queryResult.Rows != null && queryResult.Rows.Count > 0)
                {
                    var firstRow = queryResult.Rows[0];
                    var pairs = firstRow.Select(kv => $"{kv.Key} = {kv.Value}");
                    contentLog = $"Row[0] ({firstRow.Count} columns): <color=cyan>{string.Join(", ", pairs)}</color> (Total Rows: {queryResult.Rows.Count})";
                }

                if (baseline == null)
                {
                    runtimeUI.AppendOutput($"<color=green>[기준 결과 등록] {queryLoader.FormatName}</color>\n↳ {contentLog}");
                    baseline = queryResult;
                }
                else if (queryResult.IsEquivalentTo(baseline))
                {
                    runtimeUI.AppendOutput($"<color=green>[검증 성공] {queryLoader.FormatName} 결과가 기준 결과와 일치합니다.</color>\n↳ {contentLog}");
                }
                else
                {
                    runtimeUI.AppendOutput($"<color=red>[검증 실패] {queryLoader.FormatName} 결과가 기준 결과와 다릅니다.</color>\n↳ {contentLog}");
                }
            }
            else
            {
                runtimeUI.AppendOutput($"<color=orange>[검증 경고] {queryLoader.FormatName}: 쿼리는 성공했으나 반환된 데이터(Rows/Scalar)가 비어 있습니다.</color>");
                if (baseline == null) baseline = queryResult;
            }
        }
        else
        {
            string errReason = queryResult?.ErrorMessage ?? "알 수 없는 오류";
            runtimeUI.AppendOutput($"<color=red>[검증 실패] {queryLoader.FormatName} 쿼리 실행 오류: {errReason}</color>");
        }

        PrintResult(result, "[쿼리 로드]");
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
