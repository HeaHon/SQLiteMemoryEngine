// Mono.Data.Sqlite 대신 프로젝트에서 사용 중인 SQLite-net 클래스를 사용합니다.
using SQLite;
using Newtonsoft.Json;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public class DBGenerator : EditorWindow
{
    private static readonly string[] Scales = { "S100", "S1K", "S10K", "S100K" };
    private static readonly int[] ScaleCounts = { 100, 1000, 10000, 100000 };
    private static readonly string[] Complexities = { "C1_Low", "C2_Mid", "C3_High" };

    private static int Seed = 42; // 모든 데이터 세트에서 동일한 랜덤 시드를 사용하여 일관된 데이터 생성

    [MenuItem("Tools/Generate All Benchmark Data (SQL + JSON + SO + Binary)")]
    public static void GenerateAllData()
    {
        string saRoot = Path.Combine(Application.dataPath, "StreamingAssets");
        string soRoot = Path.Combine(Application.dataPath, "Resources/SO");

        for (int i = 0; i < Scales.Length; i++)
        {
            string scaleStr = Scales[i];
            int count = ScaleCounts[i];

            // 1. 포맷별 / 규모별 폴더 경로 정리
            string sqliteFolder = EnsureDirectory(Path.Combine(saRoot, "SQLite", scaleStr));
            string jsonFolder = EnsureDirectory(Path.Combine(saRoot, "JSON", scaleStr));
            string binaryFolder = EnsureDirectory(Path.Combine(saRoot, "Binary", scaleStr));
            string soFolder = EnsureDirectory(Path.Combine(soRoot, scaleStr));

            for (int j = 0; j < Complexities.Length; j++)
            {
                string compStr = Complexities[j];
                string baseFileName = $"GameData_{scaleStr}_{compStr}";

                // 1개의 원본 데이터 세트 생성
                object dataContainer = GenerateInMemoryData(count, compStr);

                // 1. 동일한 객체를 SQLite DB 파일로 내보내기
                string dbPath = Path.Combine(sqliteFolder, $"{baseFileName}.db");
                if (File.Exists(dbPath)) File.Delete(dbPath);
                BuildDatabaseFromContainer(dbPath, dataContainer, compStr);

                // 2. 동일한 객체를 JSON 파일로 내보내기
                ExportToJson(dataContainer, Path.Combine(jsonFolder, $"{baseFileName}.json"));

                // 3. 동일한 객체를 Binary (.bytes) 파일로 내보내기
                ExportToBinary(dataContainer, Path.Combine(binaryFolder, $"{baseFileName}.bytes"));

                // 4. 동일한 객체를 ScriptableObject (.asset)로 내보내기
                ExportToScriptableObject(dataContainer, compStr, $"Assets/Resources/SO/{scaleStr}/{baseFileName}.asset");
            }
        }

        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Data Generator", "SQLite, JSON, SO, Binary 4종 대조군 데이터가 성공적으로 빌드되었습니다!", "OK");
    }

    private static string EnsureDirectory(string path)
    {
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        return path;
    }

    #region 인메모리 데이터 생성
    private static object GenerateInMemoryData(int count, string complexity)
    {
        var random = new System.Random(Seed);

        switch (complexity)
        {
            case "C1_Low":
                {
                    var container = new DataSet_C1();
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
                    var container = new DataSet_C2();
                    var types = new[] { "WEAPON", "ARMOR", "CONSUMABLE" };
                    var tags = new[] { "MELEE", "RANGED", "ELEMENT_FIRE", "ELEMENT_ICE", "BLESSING", "HEAL" };
                    for (int i = 1; i <= count; i++)
                    {
                        container.items.Add(new Master_Item_C2 { item_id = i, item_code = $"ITM_{i:D6}", name = $"Item_C2_{i}", item_type = types[random.Next(types.Length)], rarity = random.Next(1, 6), req_level = random.Next(1, 80), base_atk = random.Next(10, 300) });
                        int tagCount = random.Next(1, 4);
                        for (int t = 0; t < tagCount; t++) container.tags.Add(new Item_Tag_C2 { item_id = i, tag_name = tags[(i + t) % tags.Length] });
                        container.characters.Add(new Runtime_Character_C2 { character_id = i, name = $"Hero_{i}", level = random.Next(1, 100), cur_hp = random.Next(100, 10000), pos_x = (float)(random.NextDouble() * 500.0), pos_y = (float)(random.NextDouble() * 500.0) });
                        container.inventories.Add(new Runtime_Inventory_C2 { instance_id = i, character_id = i, item_id = i, quantity = random.Next(1, 99) });
                    }
                    return container;
                }
            case "C3_High":
                {
                    var container = new DataSet_C3();
                    var effectTypes = new[] { "BUFF_ATK", "BUFF_DEF", "LIFESTEAL", "CRITICAL_UP" };
                    for (int i = 1; i <= count; i++)
                    {
                        container.users.Add(new System_User_C3 { user_id = i, account_name = $"User_{i}", created_at = 1700000000 + i });
                        container.characters.Add(new Runtime_Character_C3 { character_id = i, user_id = i, name = $"Hero_C3_{i}", guild_id = random.Next(1, 50) });
                        container.items.Add(new Master_Item_C3 { item_id = i, name = $"MasterItem_C3_{i}", rarity = random.Next(1, 6) });
                        container.effects.Add(new Master_Effect_C3 { effect_id = i, effect_type = effectTypes[random.Next(effectTypes.Length)], value = random.Next(5, 100) });
                        container.links.Add(new Item_Effect_Link_C3 { item_id = i, effect_id = i });
                        container.inventories.Add(new Runtime_Inventory_C3 { instance_id = i, character_id = i, item_id = i, durability = random.Next(1, 100) });
                    }
                    return container;
                }
        }
        return null;
    }
    #endregion

    #region SQLite DB 변환 로직 (원본 컨테이너 주입 방식)
    private static void BuildDatabaseFromContainer(string dbPath, object container, string complexity)
    {
        using (var db = new SQLiteConnection(dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create))
        {
            db.BeginTransaction();

            if (complexity == "C1_Low")
            {
                var data = (DataSet_C1)container;
                db.CreateTable<Master_Item_C1>();
                db.CreateTable<Runtime_Character_C1>();
                db.InsertAll(data.items);
                db.InsertAll(data.characters);
            }
            else if (complexity == "C2_Mid")
            {
                var data = (DataSet_C2)container;
                db.CreateTable<Master_Item_C2>();
                db.CreateTable<Item_Tag_C2>();
                db.CreateTable<Runtime_Character_C2>();
                db.CreateTable<Runtime_Inventory_C2>();
                db.InsertAll(data.items);
                db.InsertAll(data.tags);
                db.InsertAll(data.characters);
                db.InsertAll(data.inventories);
            }
            else if (complexity == "C3_High")
            {
                var data = (DataSet_C3)container;
                db.CreateTable<System_User_C3>();
                db.CreateTable<Runtime_Character_C3>();
                db.CreateTable<Master_Item_C3>();
                db.CreateTable<Master_Effect_C3>();
                db.CreateTable<Item_Effect_Link_C3>();
                db.CreateTable<Runtime_Inventory_C3>();
                db.InsertAll(data.users);
                db.InsertAll(data.characters);
                db.InsertAll(data.items);
                db.InsertAll(data.effects);
                db.InsertAll(data.links);
                db.InsertAll(data.inventories);
            }

            db.Commit();
        }
    }
    #endregion

    #region Export 헬퍼 메서드 (JSON, Binary, SO)
    private static void ExportToJson(object container, string path)
    {
        string json = JsonConvert.SerializeObject(container, Formatting.Indented);
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    private static void ExportToBinary(object container, string path)
    {
        string json = JsonConvert.SerializeObject(container, Formatting.None);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        File.WriteAllBytes(path, bytes);
    }

    private static void ExportToScriptableObject(object container, string complexity, string assetPath)
    {
        ScriptableObject so = null;
        if (complexity == "C1_Low")
        {
            var target = ScriptableObject.CreateInstance<SO_C1>();
            string json = JsonConvert.SerializeObject(container);
            target.data = JsonConvert.DeserializeObject<DataSet_C1>(json);
            so = target;
        }
        else if (complexity == "C2_Mid")
        {
            var target = ScriptableObject.CreateInstance<SO_C2>();
            string json = JsonConvert.SerializeObject(container);
            target.data = JsonConvert.DeserializeObject<DataSet_C2>(json);
            so = target;
        }
        else if (complexity == "C3_High")
        {
            var target = ScriptableObject.CreateInstance<SO_C3>();
            string json = JsonConvert.SerializeObject(container);
            target.data = JsonConvert.DeserializeObject<DataSet_C3>(json);
            so = target;
        }

        if (so != null)
        {
            AssetDatabase.CreateAsset(so, assetPath);
        }
    }
    #endregion
}

