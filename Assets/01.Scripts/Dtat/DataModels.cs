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
    // 1. 유니티 / JSON / Binary / SO 직렬화용 순수 필드
    public int item_id;
    public string name;
    public string item_type;
    public int rarity;
    public int atk;
    public int price;

    // 2. SQLite ORM 속성 매핑용 프로퍼티 (직렬화 필드로 연결)
    [PrimaryKey]
    public int Sql_item_id
    {
        get => item_id;
        set => item_id = value;
    }
}

[Serializable]
[Table("Runtime_Character_C1")]
public class Runtime_Character_C1
{
    public int character_id;
    public string name;
    public int level;
    public int hp;
    public float pos_x;
    public float pos_y;

    [PrimaryKey]
    public int Sql_character_id
    {
        get => character_id;
        set => character_id = value;
    }
}

// --- C2 Models ---
[Serializable]
[Table("Master_Item_C2")]
public class Master_Item_C2
{
    public int item_id;
    public string item_code;
    public string name;
    public string item_type;
    public int rarity;
    public int req_level;
    public int base_atk;

    [PrimaryKey]
    public int Sql_item_id
    {
        get => item_id;
        set => item_id = value;
    }

    [Unique]
    public string Sql_item_code
    {
        get => item_code;
        set => item_code = value;
    }
}

[Serializable]
[Table("Item_Tag_C2")]
public class Item_Tag_C2
{
    public int item_id;
    public string tag_name;

    [Indexed(Name = "PK_Tag", Order = 1)]
    public int Sql_item_id
    {
        get => item_id;
        set => item_id = value;
    }

    [Indexed(Name = "PK_Tag", Order = 2)]
    public string Sql_tag_name
    {
        get => tag_name;
        set => tag_name = value;
    }
}

[Serializable]
[Table("Runtime_Character_C2")]
public class Runtime_Character_C2
{
    public int character_id;
    public string name;
    public int level;
    public int cur_hp;
    public float pos_x;
    public float pos_y;

    [PrimaryKey]
    public int Sql_character_id
    {
        get => character_id;
        set => character_id = value;
    }
}

[Serializable]
[Table("Runtime_Inventory_C2")]
public class Runtime_Inventory_C2
{
    public int instance_id;
    public int character_id;
    public int item_id;
    public int quantity;

    [PrimaryKey, AutoIncrement]
    public int Sql_instance_id
    {
        get => instance_id;
        set => instance_id = value;
    }

    [Indexed]
    public int Sql_character_id
    {
        get => character_id;
        set => character_id = value;
    }
}

// --- C3 Models ---
[Serializable]
[Table("System_User_C3")]
public class System_User_C3
{
    public int user_id;
    public string account_name;
    public long created_at;

    [PrimaryKey]
    public int Sql_user_id
    {
        get => user_id;
        set => user_id = value;
    }
}

[Serializable]
[Table("Runtime_Character_C3")]
public class Runtime_Character_C3
{
    public int character_id;
    public int user_id;
    public string name;
    public int guild_id;

    [PrimaryKey]
    public int Sql_character_id
    {
        get => character_id;
        set => character_id = value;
    }

    [Indexed]
    public int Sql_user_id
    {
        get => user_id;
        set => user_id = value;
    }
}

[Serializable]
[Table("Master_Item_C3")]
public class Master_Item_C3
{
    public int item_id;
    public string name;
    public int rarity;

    [PrimaryKey]
    public int Sql_item_id
    {
        get => item_id;
        set => item_id = value;
    }
}

[Serializable]
[Table("Master_Effect_C3")]
public class Master_Effect_C3
{
    public int effect_id;
    public string effect_type;
    public int value;

    [PrimaryKey]
    public int Sql_effect_id
    {
        get => effect_id;
        set => effect_id = value;
    }
}

[Serializable]
[Table("Item_Effect_Link_C3")]
public class Item_Effect_Link_C3
{
    public int item_id;
    public int effect_id;

    [Indexed(Name = "PK_Link", Order = 1)]
    public int Sql_item_id
    {
        get => item_id;
        set => item_id = value;
    }

    [Indexed(Name = "PK_Link", Order = 2)]
    public int Sql_effect_id
    {
        get => effect_id;
        set => effect_id = value;
    }
}

[Serializable]
[Table("Runtime_Inventory_C3")]
public class Runtime_Inventory_C3
{
    public int instance_id;
    public int character_id;
    public int item_id;
    public int durability;

    [PrimaryKey, AutoIncrement]
    public int Sql_instance_id
    {
        get => instance_id;
        set => instance_id = value;
    }

    [Indexed]
    public int Sql_character_id
    {
        get => character_id;
        set => character_id = value;
    }
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