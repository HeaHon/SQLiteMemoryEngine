using SQLite;
using MessagePack;
using System;
using System.Collections.Generic;

// 중첩 문서/임베디드 구조 (Document NoSQL 유리)

#region C1 복잡도 데이터 모델

[Serializable]
[MessagePackObject(true)]
public class DocItem_C1
{
    [Key(0)] public int item_id { get; set; }
    [Key(1)] public string name { get; set; }
    [Key(2)] public string item_type { get; set; }
    [Key(3)] public int rarity { get; set; }
    [Key(4)] public int atk { get; set; }
    [Key(5)] public int price { get; set; }
    [Key(6)] public List<string> tags { get; set; } = new List<string>();
}

[Serializable]
[MessagePackObject(true)]
public class DocDataSet_C1
{
    [Key(0)] public List<DocItem_C1> items { get; set; } = new List<DocItem_C1>();
}

#endregion

#region C2 복잡도 데이터 모델

[Serializable]
[MessagePackObject(true)]
public class DocInventoryEntry_C2
{
    [Key(0)] public int instance_id { get; set; }
    [Key(1)] public int item_id { get; set; }
    [Key(2)] public string item_name { get; set; }
    [Key(3)] public int rarity { get; set; }
    [Key(4)] public int quantity { get; set; }
    [Key(5)] public List<string> tags { get; set; } = new List<string>();
}

[Serializable]
[MessagePackObject(true)]
public class DocCharacter_C2
{
    [Key(0)] public int character_id { get; set; }
    [Key(1)] public string name { get; set; }
    [Key(2)] public int level { get; set; }
    [Key(3)] public int cur_hp { get; set; }
    [Key(4)] public List<DocInventoryEntry_C2> inventories { get; set; } = new List<DocInventoryEntry_C2>();
}

[Serializable]
[MessagePackObject(true)]
public class DocDataSet_C2
{
    [Key(0)] public List<DocCharacter_C2> characters { get; set; } = new List<DocCharacter_C2>();
}

#endregion

#region C3 복잡도 데이터 모델

[Serializable]
[MessagePackObject(true)]
public class DocEffect_C3
{
    [Key(0)] public int effect_id { get; set; }
    [Key(1)] public string effect_type { get; set; }
    [Key(2)] public int value { get; set; }
}

[Serializable]
[MessagePackObject(true)]
public class DocInventoryItem_C3
{
    [Key(0)] public int instance_id { get; set; }
    [Key(1)] public int item_id { get; set; }
    [Key(2)] public string item_name { get; set; }
    [Key(3)] public int rarity { get; set; }
    [Key(4)] public int durability { get; set; }
    [Key(5)] public List<DocEffect_C3> effects { get; set; } = new List<DocEffect_C3>();
}

[Serializable]
[MessagePackObject(true)]
public class DocCharacter_C3
{
    [Key(0)] public int character_id { get; set; }
    [Key(1)] public string name { get; set; }
    [Key(2)] public int guild_id { get; set; }
    [Key(3)] public List<DocInventoryItem_C3> inventories { get; set; } = new List<DocInventoryItem_C3>();
}

[Serializable]
[MessagePackObject(true)]
public class DocUser_C3
{
    [Key(0)] public int user_id { get; set; }
    [Key(1)] public string account_name { get; set; }
    [Key(2)] public long created_at { get; set; }
    [Key(3)] public List<DocCharacter_C3> characters { get; set; } = new List<DocCharacter_C3>();
}

[Serializable]
[MessagePackObject(true)]
public class DocDataSet_C3
{
    [Key(0)] public List<DocUser_C3> users { get; set; } = new List<DocUser_C3>();
}

#endregion