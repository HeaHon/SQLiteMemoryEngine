// Mono.Data.Sqlite 대신 프로젝트에서 사용 중인 SQLite-net 클래스를 사용합니다.
using SQLite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public class MatrixDBGenerator : EditorWindow
{
    private static readonly string[] Scales = { "S100", "S1K", "S10K", "S100K" };
    private static readonly int[] ScaleCounts = { 100, 1000, 10000, 100000 };
    private static readonly string[] Complexities = { "C1_Low", "C2_Mid", "C3_High" };

    [MenuItem("Tools/Generate Matrix GameData DBs")]
    public static void GenerateAllDBs()
    {
        string rootFolder = Path.Combine(Application.dataPath, "StreamingAssets");
        if (!Directory.Exists(rootFolder))
        {
            Directory.CreateDirectory(rootFolder);
        }

        for (int i = 0; i < Scales.Length; i++)
        {
            string scaleStr = Scales[i];
            int dataCount = ScaleCounts[i];

            // Scale 단위 하위 폴더 생성 (StreamingAssets/S100, StreamingAssets/S1K ...)
            string targetFolder = Path.Combine(rootFolder, scaleStr);
            if (!Directory.Exists(targetFolder))
            {
                Directory.CreateDirectory(targetFolder);
            }

            for (int j = 0; j < Complexities.Length; j++)
            {
                string complexityStr = Complexities[j];
                string dbName = $"GameData_{scaleStr}_{complexityStr}.db";
                string dbPath = Path.Combine(targetFolder, dbName);

                // 기존 파일 존재 시 삭제 후 재생성
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }

                BuildDatabase(dbPath, dataCount, complexityStr);
                Debug.Log($"[MatrixDBGenerator] Successfully Generated: {scaleStr}/{dbName} (Scale: {dataCount})");
            }
        }

        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("DB Generator", "All 12 Matrix Databases have been organized into Scale folders in StreamingAssets!", "OK");
    }

    private static void BuildDatabase(string dbPath, int count, string complexity)
    {
        using (var db = new SQLiteConnection(dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create))
        {
            db.BeginTransaction(); // 대용량 Insert 속도 향상을 위한 Transaction 처리

            switch (complexity)
            {
                case "C1_Low":
                    BuildC1(db, count);
                    break;
                case "C2_Mid":
                    BuildC2(db, count);
                    break;
                case "C3_High":
                    BuildC3(db, count);
                    break;
            }

            db.Commit();
        }
    }

    #region Level 1: C1_Low Schema & Seeding
    private static void BuildC1(SQLiteConnection db, int count)
    {
        db.CreateTable<Master_Item_C1>();
        db.CreateTable<Runtime_Character_C1>();

        var types = new[] { "WEAPON", "ARMOR", "CONSUMABLE" };
        var random = new System.Random(42);

        for (int i = 1; i <= count; i++)
        {
            db.Insert(new Master_Item_C1
            {
                item_id = i,
                name = $"Item_C1_{i}",
                item_type = types[random.Next(types.Length)],
                rarity = random.Next(1, 6),
                atk = random.Next(10, 200),
                price = random.Next(100, 50000)
            });

            db.Insert(new Runtime_Character_C1
            {
                character_id = i,
                name = $"Hero_{i}",
                level = random.Next(1, 100),
                hp = random.Next(100, 10000),
                pos_x = (float)(random.NextDouble() * 500.0),
                pos_y = (float)(random.NextDouble() * 500.0)
            });
        }
    }
    #endregion

    #region Level 2: C2_Mid Schema & Seeding
    private static void BuildC2(SQLiteConnection db, int count)
    {
        db.CreateTable<Master_Item_C2>();
        db.CreateTable<Item_Tag_C2>();
        db.CreateTable<Runtime_Character_C2>();
        db.CreateTable<Runtime_Inventory_C2>();

        var types = new[] { "WEAPON", "ARMOR", "CONSUMABLE" };
        var tags = new[] { "MELEE", "RANGED", "ELEMENT_FIRE", "ELEMENT_ICE", "BLESSING", "HEAL" };
        var random = new System.Random(42);

        for (int i = 1; i <= count; i++)
        {
            db.Insert(new Master_Item_C2
            {
                item_id = i,
                item_code = $"ITM_{i:D6}",
                name = $"Item_C2_{i}",
                item_type = types[random.Next(types.Length)],
                rarity = random.Next(1, 6),
                req_level = random.Next(1, 80),
                base_atk = random.Next(10, 300)
            });

            // N:M Tag (아이템당 1~3개 태그 부여)
            int tagCount = random.Next(1, 4);
            for (int t = 0; t < tagCount; t++)
            {
                try
                {
                    db.Insert(new Item_Tag_C2
                    {
                        item_id = i,
                        tag_name = tags[(i + t) % tags.Length]
                    });
                }
                catch { /* PK 중복 방지 */ }
            }

            db.Insert(new Runtime_Character_C2
            {
                character_id = i,
                name = $"Hero_{i}",
                level = random.Next(1, 100),
                cur_hp = random.Next(100, 10000),
                pos_x = (float)(random.NextDouble() * 500.0),
                pos_y = (float)(random.NextDouble() * 500.0)
            });

            db.Insert(new Runtime_Inventory_C2
            {
                character_id = i,
                item_id = i, // 1:1 대응 기본 보장
                quantity = random.Next(1, 99)
            });
        }
    }
    #endregion

    #region Level 3: C3_High Schema & Seeding
    private static void BuildC3(SQLiteConnection db, int count)
    {
        db.CreateTable<System_User_C3>();
        db.CreateTable<Runtime_Character_C3>();
        db.CreateTable<Master_Item_C3>();
        db.CreateTable<Master_Effect_C3>();
        db.CreateTable<Item_Effect_Link_C3>();
        db.CreateTable<Runtime_Inventory_C3>();

        var random = new System.Random(42);
        var effectTypes = new[] { "BUFF_ATK", "BUFF_DEF", "LIFESTEAL", "CRITICAL_UP" };

        for (int i = 1; i <= count; i++)
        {
            db.Insert(new System_User_C3
            {
                user_id = i,
                account_name = $"User_{i}",
                created_at = 1700000000 + i
            });

            db.Insert(new Runtime_Character_C3
            {
                character_id = i,
                user_id = i,
                name = $"Hero_C3_{i}",
                guild_id = random.Next(1, 50)
            });

            db.Insert(new Master_Item_C3
            {
                item_id = i,
                name = $"MasterItem_C3_{i}",
                rarity = random.Next(1, 6)
            });

            db.Insert(new Master_Effect_C3
            {
                effect_id = i,
                effect_type = effectTypes[random.Next(effectTypes.Length)],
                value = random.Next(5, 100)
            });

            // Fan-out 유발용 N:M 효과 매핑
            db.Insert(new Item_Effect_Link_C3
            {
                item_id = i,
                effect_id = i
            });

            db.Insert(new Runtime_Inventory_C3
            {
                character_id = i,
                item_id = i,
                durability = random.Next(1, 100)
            });
        }
    }
    #endregion
}

#region SQLite-net ORM Data Models
// --- C1 Models ---
[Table("Master_Item_C1")]
public class Master_Item_C1
{
    [PrimaryKey] public int item_id { get; set; }
    public string name { get; set; }
    public string item_type { get; set; }
    public int rarity { get; set; }
    public int atk { get; set; }
    public int price { get; set; }
}

[Table("Runtime_Character_C1")]
public class Runtime_Character_C1
{
    [PrimaryKey] public int character_id { get; set; }
    public string name { get; set; }
    public int level { get; set; }
    public int hp { get; set; }
    public float pos_x { get; set; }
    public float pos_y { get; set; }
}

// --- C2 Models ---
[Table("Master_Item_C2")]
public class Master_Item_C2
{
    [PrimaryKey] public int item_id { get; set; }
    [Unique] public string item_code { get; set; }
    public string name { get; set; }
    public string item_type { get; set; }
    public int rarity { get; set; }
    public int req_level { get; set; }
    public int base_atk { get; set; }
}

[Table("Item_Tag_C2")]
public class Item_Tag_C2
{
    [Indexed(Name = "PK_Tag", Order = 1)] public int item_id { get; set; }
    [Indexed(Name = "PK_Tag", Order = 2)] public string tag_name { get; set; }
}

[Table("Runtime_Character_C2")]
public class Runtime_Character_C2
{
    [PrimaryKey] public int character_id { get; set; }
    public string name { get; set; }
    public int level { get; set; }
    public int cur_hp { get; set; }
    public float pos_x { get; set; }
    public float pos_y { get; set; }
}

[Table("Runtime_Inventory_C2")]
public class Runtime_Inventory_C2
{
    [PrimaryKey, AutoIncrement] public int instance_id { get; set; }
    [Indexed] public int character_id { get; set; }
    public int item_id { get; set; }
    public int quantity { get; set; }
}

// --- C3 Models ---
[Table("System_User_C3")]
public class System_User_C3
{
    [PrimaryKey] public int user_id { get; set; }
    public string account_name { get; set; }
    public long created_at { get; set; }
}

[Table("Runtime_Character_C3")]
public class Runtime_Character_C3
{
    [PrimaryKey] public int character_id { get; set; }
    [Indexed] public int user_id { get; set; }
    public string name { get; set; }
    public int guild_id { get; set; }
}

[Table("Master_Item_C3")]
public class Master_Item_C3
{
    [PrimaryKey] public int item_id { get; set; }
    public string name { get; set; }
    public int rarity { get; set; }
}

[Table("Master_Effect_C3")]
public class Master_Effect_C3
{
    [PrimaryKey] public int effect_id { get; set; }
    public string effect_type { get; set; }
    public int value { get; set; }
}

[Table("Item_Effect_Link_C3")]
public class Item_Effect_Link_C3
{
    [Indexed(Name = "PK_Link", Order = 1)] public int item_id { get; set; }
    [Indexed(Name = "PK_Link", Order = 2)] public int effect_id { get; set; }
}

[Table("Runtime_Inventory_C3")]
public class Runtime_Inventory_C3
{
    [PrimaryKey, AutoIncrement] public int instance_id { get; set; }
    [Indexed] public int character_id { get; set; }
    public int item_id { get; set; }
    public int durability { get; set; }
}
#endregion