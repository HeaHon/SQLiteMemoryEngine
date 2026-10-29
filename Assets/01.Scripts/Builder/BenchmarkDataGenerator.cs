using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using UnityEngine;

public static class BenchmarkDataGenerator
{
    private static readonly string[] Scales = { "S100", "S1K", "S10K", "S100K" };
    private static readonly int[] ScaleCounts = { 100, 1000, 10000, 100000 };
    private static readonly string[] Complexities = { "C1_Low", "C2_Mid", "C3_High" };
    private static readonly int Seed = 42;

    // SQL 쿼리 벤치마크 포맷을 먼저 배치하고, 레거시 포맷은 옵션으로 추가 생성한다.
    // SQLite Native C는 SQLite 파일을 공유하므로 별도 파일 생성이 필요 없다.
    private static readonly Dictionary<string, IDataFormatBuilder> Builders = new Dictionary<string, IDataFormatBuilder>
    {
        { "SQLite", new SqliteFormatBuilder() },
        { "LiteDB", new LiteDbFormatBuilder() },
        { "RocksDB", new RocksDbFormatBuilder() },
        { "FlatBuffers", new FlatBuffersFormatBuilder() },
        { "Binary", new RawBinaryFormatBuilder() },
        { "MessagePack", new MessagePackFormatBuilder() },
        { "JSON", new JsonFormatBuilder() }
    };

    public static void ExecuteGeneration(bool includeLegacyFormats = false)
    {
        string saRoot = Path.Combine(Application.dataPath, "StreamingAssets");
        Stopwatch totalStopwatch = Stopwatch.StartNew();

        for (int i = 0; i < Scales.Length; i++)
        {
            string scaleStr = Scales[i];
            int count = ScaleCounts[i];

            for (int j = 0; j < Complexities.Length; j++)
            {
                string compStr = Complexities[j];
                string baseFileName = $"GameData_{scaleStr}_{compStr}";

                // 1. 공통 인메모리 데이터 생성 (단일 책임: 데이터 팩토리)
                Stopwatch dataStopwatch = Stopwatch.StartNew();
                object dataContainer = GenerateInMemoryData(count, compStr);
                if (dataContainer == null) continue;
                UnityEngine.Debug.Log($"[BenchmarkDataGenerator] {baseFileName} 데이터 생성: {dataStopwatch.Elapsed.TotalSeconds:F2}s");

                // 2. 각 포맷별 빌더 위임 (전략 패턴 적용)
                foreach (var pair in Builders)
                {
                    string formatName = pair.Key;
                    if (!includeLegacyFormats && (formatName == "MessagePack" || formatName == "JSON")) continue;
                    var builder = pair.Value;

                    string formatDir = EnsureDirectory(Path.Combine(saRoot, formatName, scaleStr));
                    string targetPath = formatName == "RocksDB"
                        ? EnsureDirectory(Path.Combine(formatDir, baseFileName))
                        : Path.Combine(formatDir, $"{baseFileName}.{GetExtension(formatName)}");

                    Stopwatch buildStopwatch = Stopwatch.StartNew();
                    builder.Build(dataContainer, targetPath, compStr);
                    UnityEngine.Debug.Log($"[BenchmarkDataGenerator] {baseFileName}/{formatName} 저장: {buildStopwatch.Elapsed.TotalSeconds:F2}s");
                }
            }
        }
        UnityEngine.Debug.Log($"[BenchmarkDataGenerator] 전체 생성 완료: {totalStopwatch.Elapsed.TotalMinutes:F2}분");
    }

    private static string EnsureDirectory(string path)
    {
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        return path;
    }

    private static string GetExtension(string formatName)
    {
        switch (formatName)
        {
            case "SQLite": return "db";
            case "LiteDB": return "db";
            case "FlatBuffers": return "bytes";
            case "MessagePack": return "mpk";
            case "Binary": return "bytes";
            case "JSON": return "json";
            default: return "dat";
        }
    }

    private static object GenerateInMemoryData(int count, string complexity)
    {
        var random = new System.Random(Seed);

        switch (complexity)
        {
            case "C1_Low":
                {
                    var container = new DataSet_C1
                    {
                        items = new List<Master_Item_C1>(count),
                        characters = new List<Runtime_Character_C1>(count)
                    };
                    var types = new[] { "WEAPON", "ARMOR", "CONSUMABLE" };
                    for (int i = 1; i <= count; i++)
                    {
                        container.items.Add(new Master_Item_C1 { item_id = i, name = $"Item_C1_{i}", item_type = types[random.Next(types.Length)], rarity = random.Next(1, 6), atk = random.Next(10, 200), price = random.Next(100, 50000) });
                        container.characters.Add(new Runtime_Character_C1 { character_id = i, name = $"Hero_{i}", level = random.Next(1, 100), hp = random.Next(100, 10000), pos_x = (float)(random.NextDouble() * 500.0), pos_y = (float)(random.NextDouble() * 500.0) });
                    }
                    return container;
                }
            case "C2_Mid":
                {
                    var container = new DataSet_C2
                    {
                        items = new List<Master_Item_C2>(count),
                        tags = new List<Item_Tag_C2>(count),
                        characters = new List<Runtime_Character_C2>(count),
                        inventories = new List<Runtime_Inventory_C2>(count * 2)
                    };
                    var types = new[] { "WEAPON", "ARMOR", "CONSUMABLE" };
                    var tags = new[] { "MELEE", "RANGED", "ELEMENT_FIRE", "ELEMENT_ICE", "BLESSING", "HEAL" };
                    for (int i = 1; i <= count; i++)
                    {
                        container.items.Add(new Master_Item_C2 { item_id = i, item_code = $"ITM_{i:D6}", name = $"Item_C2_{i}", item_type = types[random.Next(types.Length)], rarity = random.Next(1, 6), req_level = random.Next(1, 80), base_atk = random.Next(10, 300) });
                        container.tags.Add(new Item_Tag_C2 { item_id = i, tag_name = tags[i % tags.Length] });
                        container.characters.Add(new Runtime_Character_C2 { character_id = i, name = $"Hero_{i}", level = random.Next(1, 100).ToString(), cur_hp = random.Next(100, 10000), pos_x = (float)(random.NextDouble() * 500.0), pos_y = (float)(random.NextDouble() * 500.0) });
                        container.inventories.Add(new Runtime_Inventory_C2 { instance_id = i, character_id = i, item_id = i, quantity = random.Next(1, 99) });
                        container.inventories.Add(new Runtime_Inventory_C2 { instance_id = count + i, character_id = i, item_id = i, quantity = random.Next(1, 99) });
                    }
                    return container;
                }
            case "C3_High":
                {
                    var container = new DataSet_C3
                    {
                        users = new List<System_User_C3>(count),
                        characters = new List<Runtime_Character_C3>(count * 2),
                        items = new List<Master_Item_C3>(count),
                        effects = new List<Master_Effect_C3>(count),
                        links = new List<Item_Effect_Link_C3>(count),
                        inventories = new List<Runtime_Inventory_C3>(count * 2)
                    };
                    var effectTypes = new[] { "BUFF_ATK", "BUFF_DEF", "LIFESTEAL", "CRITICAL_UP" };
                    for (int i = 1; i <= count; i++)
                    {
                        container.users.Add(new System_User_C3 { user_id = i, account_name = $"User_{i}", created_at = 1700000000 + i });
                        int firstCharacterId = i * 2 - 1;
                        int secondCharacterId = i * 2;
                        container.characters.Add(new Runtime_Character_C3 { character_id = firstCharacterId, user_id = i, name = $"Hero_C3_{firstCharacterId}", guild_id = random.Next(1, 50) });
                        container.characters.Add(new Runtime_Character_C3 { character_id = secondCharacterId, user_id = i, name = $"Hero_C3_{secondCharacterId}", guild_id = random.Next(1, 50) });
                        container.items.Add(new Master_Item_C3 { item_id = i, name = $"MasterItem_C3_{i}", rarity = random.Next(1, 6) });
                        container.effects.Add(new Master_Effect_C3 { effect_id = i, effect_type = effectTypes[random.Next(effectTypes.Length)], value = random.Next(5, 100) });
                        container.links.Add(new Item_Effect_Link_C3 { item_id = i, effect_id = i });
                        container.inventories.Add(new Runtime_Inventory_C3 { instance_id = firstCharacterId, character_id = firstCharacterId, item_id = i, durability = random.Next(1, 100) });
                        container.inventories.Add(new Runtime_Inventory_C3 { instance_id = secondCharacterId, character_id = secondCharacterId, item_id = i, durability = random.Next(1, 100) });
                    }
                    return container;
                }
        }
        return null;
    }
}
