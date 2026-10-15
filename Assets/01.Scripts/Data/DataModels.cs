using SQLite;
using MessagePack;
using System;
using System.Collections.Generic;

#region C1 복잡도 데이터 모델
[Serializable]
[Table("Master_Item_C1")]
[MessagePackObject(true)] // 또는 [MessagePackObject]와 함께 개별 [Key] 지정
public class Master_Item_C1
{
    [PrimaryKey]
    [Key(0)]
    public int item_id { get; set; }

    [Key(1)]
    public string name { get; set; }

    [Key(2)]
    public string item_type { get; set; }

    [Key(3)]
    public int rarity { get; set; }

    [Key(4)]
    public int atk { get; set; }

    [Key(5)]
    public int price { get; set; }
}

[Serializable]
[Table("Runtime_Character_C1")]
[MessagePackObject(true)]
public class Runtime_Character_C1
{
    [PrimaryKey]
    [Key(0)]
    public int character_id { get; set; }

    [Key(1)]
    public string name { get; set; }

    [Key(2)]
    public int level { get; set; }

    [Key(3)]
    public int hp { get; set; }

    [Key(4)]
    public float pos_x { get; set; }

    [Key(5)]
    public float pos_y { get; set; }
}

[Serializable]
[MessagePackObject(true)]
public class DataSet_C1
{
    [Key(0)] public List<Master_Item_C1> items = new List<Master_Item_C1>();
    [Key(1)] public List<Runtime_Character_C1> characters = new List<Runtime_Character_C1>();
}
#endregion

#region C2 복잡도 데이터 모델
[Serializable]
[Table("Master_Item_C2")]
[MessagePackObject(true)]
public class Master_Item_C2
{
    [PrimaryKey][Key(0)] public int item_id { get; set; }
    [Unique][Key(1)] public string item_code { get; set; }
    [Key(2)] public string name { get; set; }
    [Key(3)] public string item_type { get; set; }
    [Key(4)] public int rarity { get; set; }
    [Key(5)] public int req_level { get; set; }
    [Key(6)] public int base_atk { get; set; }
}

[Serializable]
[Table("Item_Tag_C2")]
[MessagePackObject(true)]
public class Item_Tag_C2
{
    [Indexed(Name = "PK_Tag", Order = 1)][Key(0)] public int item_id { get; set; }
    [Indexed(Name = "PK_Tag", Order = 2)][Key(1)] public string tag_name { get; set; }
}

[Serializable]
[Table("Runtime_Character_C2")]
[MessagePackObject(true)]
public class Runtime_Character_C2
{
    [PrimaryKey][Key(0)] public int character_id { get; set; }
    [Key(1)] public string name { get; set; }
    [Key(2)] public string level { get; set; }
    [Key(3)] public int cur_hp { get; set; }
    [Key(4)] public float pos_x { get; set; }
    [Key(5)] public float pos_y { get; set; }
}

[Serializable]
[Table("Runtime_Inventory_C2")]
[MessagePackObject(true)]
public class Runtime_Inventory_C2
{
    [PrimaryKey, AutoIncrement][Key(0)] public int instance_id { get; set; }
    [Indexed][Key(1)] public int character_id { get; set; }
    [Key(2)] public int item_id { get; set; }
    [Key(3)] public int quantity { get; set; }
}

[Serializable]
[MessagePackObject(true)]
public class DataSet_C2
{
    [Key(0)] public List<Master_Item_C2> items = new List<Master_Item_C2>();
    [Key(1)] public List<Item_Tag_C2> tags = new List<Item_Tag_C2>();
    [Key(2)] public List<Runtime_Character_C2> characters = new List<Runtime_Character_C2>();
    [Key(3)] public List<Runtime_Inventory_C2> inventories = new List<Runtime_Inventory_C2>();
}
#endregion

#region C3 복잡도 데이터 모델
[Serializable]
[Table("System_User_C3")]
[MessagePackObject(true)]
public class System_User_C3
{
    [PrimaryKey][Key(0)] public int user_id { get; set; }
    [Key(1)] public string account_name { get; set; }
    [Key(2)] public long created_at { get; set; }
}

[Serializable]
[Table("Runtime_Character_C3")]
[MessagePackObject(true)]
public class Runtime_Character_C3
{
    [PrimaryKey][Key(0)] public int character_id { get; set; }
    [Indexed][Key(1)] public int user_id { get; set; }
    [Key(2)] public string name { get; set; }
    [Key(3)] public int guild_id { get; set; }
}

[Serializable]
[Table("Master_Item_C3")]
[MessagePackObject(true)]
public class Master_Item_C3
{
    [PrimaryKey][Key(0)] public int item_id { get; set; }
    [Key(1)] public string name { get; set; }
    [Key(2)] public int rarity { get; set; }
}

[Serializable]
[Table("Master_Effect_C3")]
[MessagePackObject(true)]
public class Master_Effect_C3
{
    [PrimaryKey][Key(0)] public int effect_id { get; set; }
    [Key(1)] public string effect_type { get; set; }
    [Key(2)] public int value { get; set; }
}

[Serializable]
[Table("Item_Effect_Link_C3")]
[MessagePackObject(true)]
public class Item_Effect_Link_C3
{
    [Indexed(Name = "PK_Link", Order = 1)][Key(0)] public int item_id { get; set; }
    [Indexed(Name = "PK_Link", Order = 2)][Key(1)] public int effect_id { get; set; }
}

[Serializable]
[Table("Runtime_Inventory_C3")]
[MessagePackObject(true)]
public class Runtime_Inventory_C3
{
    [PrimaryKey, AutoIncrement][Key(0)] public int instance_id { get; set; }
    [Indexed][Key(1)] public int character_id { get; set; }
    [Key(2)] public int item_id { get; set; }
    [Key(3)] public int durability { get; set; }
}

[Serializable]
[MessagePackObject(true)]
public class DataSet_C3
{
    [Key(0)] public List<System_User_C3> users = new List<System_User_C3>();
    [Key(1)] public List<Runtime_Character_C3> characters = new List<Runtime_Character_C3>();
    [Key(2)] public List<Master_Item_C3> items = new List<Master_Item_C3>();
    [Key(3)] public List<Master_Effect_C3> effects = new List<Master_Effect_C3>();
    [Key(4)] public List<Item_Effect_Link_C3> links = new List<Item_Effect_Link_C3>();
    [Key(5)] public List<Runtime_Inventory_C3> inventories = new List<Runtime_Inventory_C3>();
}
#endregion