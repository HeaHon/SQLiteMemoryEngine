// Mono.Data.Sqlite 대신 프로젝트에서 사용 중인 SQLite-net 클래스를 사용합니다.
using SQLite;
using System;
using System.Collections.Generic;
using UnityEngine;

#region SQLite-net ORM Data Models
// --- C1 Models ---
[Serializable]
[Table("Master_Item_C1")]
public class Master_Item_C1
{
    [PrimaryKey][field: SerializeField] public int item_id { get; set; }
    [field: SerializeField] public string name { get; set; }
    [field: SerializeField] public string item_type { get; set; }
    [field: SerializeField] public int rarity { get; set; }
    [field: SerializeField] public int atk { get; set; }
    [field: SerializeField] public int price { get; set; }
}

[Serializable]
[Table("Runtime_Character_C1")]
public class Runtime_Character_C1
{
    [PrimaryKey][field: SerializeField] public int character_id { get; set; }
    [field: SerializeField] public string name { get; set; }
    [field: SerializeField] public int level { get; set; }
    [field: SerializeField] public int hp { get; set; }
    [field: SerializeField] public float pos_x { get; set; }
    [field: SerializeField] public float pos_y { get; set; }
}

// --- C2 Models ---
[Serializable]
[Table("Master_Item_C2")]
public class Master_Item_C2
{
    [PrimaryKey][field: SerializeField] public int item_id { get; set; }
    [Unique][field: SerializeField] public string item_code { get; set; }
    [field: SerializeField] public string name { get; set; }
    [field: SerializeField] public string item_type { get; set; }
    [field: SerializeField] public int rarity { get; set; }
    [field: SerializeField] public int req_level { get; set; }
    [field: SerializeField] public int base_atk { get; set; }
}

[Serializable]
[Table("Item_Tag_C2")]
public class Item_Tag_C2
{
    [Indexed(Name = "PK_Tag", Order = 1)][field: SerializeField] public int item_id { get; set; }
    [Indexed(Name = "PK_Tag", Order = 2)][field: SerializeField] public string tag_name { get; set; }
}

[Serializable]
[Table("Runtime_Character_C2")]
public class Runtime_Character_C2
{
    [PrimaryKey][field: SerializeField] public int character_id { get; set; }
    [field: SerializeField] public string name { get; set; }
    [field: SerializeField] public int level { get; set; }
    [field: SerializeField] public int cur_hp { get; set; }
    [field: SerializeField] public float pos_x { get; set; }
    [field: SerializeField] public float pos_y { get; set; }
}

[Serializable]
[Table("Runtime_Inventory_C2")]
public class Runtime_Inventory_C2
{
    [PrimaryKey, AutoIncrement][field: SerializeField] public int instance_id { get; set; }
    [Indexed][field: SerializeField] public int character_id { get; set; }
    [field: SerializeField] public int item_id { get; set; }
    [field: SerializeField] public int quantity { get; set; }
}

// --- C3 Models ---
[Serializable]
[Table("System_User_C3")]
public class System_User_C3
{
    [PrimaryKey][field: SerializeField] public int user_id { get; set; }
    [field: SerializeField] public string account_name { get; set; }
    [field: SerializeField] public long created_at { get; set; }
}

[Serializable]
[Table("Runtime_Character_C3")]
public class Runtime_Character_C3
{
    [PrimaryKey][field: SerializeField] public int character_id { get; set; }
    [Indexed][field: SerializeField] public int user_id { get; set; }
    [field: SerializeField] public string name { get; set; }
    [field: SerializeField] public int guild_id { get; set; }
}

[Serializable]
[Table("Master_Item_C3")]
public class Master_Item_C3
{
    [PrimaryKey][field: SerializeField] public int item_id { get; set; }
    [field: SerializeField] public string name { get; set; }
    [field: SerializeField] public int rarity { get; set; }
}

[Serializable]
[Table("Master_Effect_C3")]
public class Master_Effect_C3
{
    [PrimaryKey][field: SerializeField] public int effect_id { get; set; }
    [field: SerializeField] public string effect_type { get; set; }
    [field: SerializeField] public int value { get; set; }
}

[Serializable]
[Table("Item_Effect_Link_C3")]
public class Item_Effect_Link_C3
{
    [Indexed(Name = "PK_Link", Order = 1)][field: SerializeField] public int item_id { get; set; }
    [Indexed(Name = "PK_Link", Order = 2)][field: SerializeField] public int effect_id { get; set; }
}

[Serializable]
[Table("Runtime_Inventory_C3")]
public class Runtime_Inventory_C3
{
    [PrimaryKey, AutoIncrement][field: SerializeField] public int instance_id { get; set; }
    [Indexed][field: SerializeField] public int character_id { get; set; }
    [field: SerializeField] public int item_id { get; set; }
    [field: SerializeField] public int durability { get; set; }
}
#endregion

#region JSON / Binary 직렬화용 데이터 컨테이너
[Serializable]
public class DataSet_C1
{
    public List<Master_Item_C1> items = new List<Master_Item_C1>();
    public List<Runtime_Character_C1> characters = new List<Runtime_Character_C1>();
}

[Serializable]
public class DataSet_C2
{
    public List<Master_Item_C2> items = new List<Master_Item_C2>();
    public List<Item_Tag_C2> tags = new List<Item_Tag_C2>();
    public List<Runtime_Character_C2> characters = new List<Runtime_Character_C2>();
    public List<Runtime_Inventory_C2> inventories = new List<Runtime_Inventory_C2>();
}

[Serializable]
public class DataSet_C3
{
    public List<System_User_C3> users = new List<System_User_C3>();
    public List<Runtime_Character_C3> characters = new List<Runtime_Character_C3>();
    public List<Master_Item_C3> items = new List<Master_Item_C3>();
    public List<Master_Effect_C3> effects = new List<Master_Effect_C3>();
    public List<Item_Effect_Link_C3> links = new List<Item_Effect_Link_C3>();
    public List<Runtime_Inventory_C3> inventories = new List<Runtime_Inventory_C3>();
}
#endregion

#region ScriptableObject 에셋 용 스크립트
public class SO_C1 : ScriptableObject { public DataSet_C1 data; }
public class SO_C2 : ScriptableObject { public DataSet_C2 data; }
public class SO_C3 : ScriptableObject { public DataSet_C3 data; }
#endregion