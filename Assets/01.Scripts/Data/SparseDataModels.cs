using SQLite;
using MessagePack;
using System;
using System.Collections.Generic;

// 가변/다형성 스키마 구조 (Schema-less NoSQL 유리)

#region C1 복잡도 데이터 모델

[Serializable]
[MessagePackObject(true)]
public class SparseItem_C1
{
    [Key(0)] public int item_id { get; set; }
    [Key(1)] public string item_type { get; set; } // WEAPON, ARMOR, CONSUMABLE, MATERIAL
    [Key(2)] public string name { get; set; }
    [Key(3)] public Dictionary<string, object> attributes { get; set; } = new Dictionary<string, object>();
}

[Serializable]
[MessagePackObject(true)]
public class SparseDataSet_C1
{
    [Key(0)] public List<SparseItem_C1> items { get; set; } = new List<SparseItem_C1>();
}

#endregion

#region C2 복잡도 데이터 모델

[Serializable]
[MessagePackObject(true)]
public class SparseEntity_C2
{
    [Key(0)] public int entity_id { get; set; }
    [Key(1)] public string category { get; set; } // HERO, MONSTER, NPC
    [Key(2)] public string name { get; set; }
    [Key(3)] public Dictionary<string, object> stats { get; set; } = new Dictionary<string, object>();
    [Key(4)] public Dictionary<string, string> metadata { get; set; } = new Dictionary<string, string>();
}

[Serializable]
[MessagePackObject(true)]
public class SparseDataSet_C2
{
    [Key(0)] public List<SparseEntity_C2> entities { get; set; } = new List<SparseEntity_C2>();
}


#endregion

#region C3 복잡도 데이터 모델

[Serializable]
[MessagePackObject(true)]
public class SparseEventLog_C3
{
    [Key(0)] public string event_id { get; set; }
    [Key(1)] public int user_id { get; set; }
    [Key(2)] public string event_type { get; set; } // COMBAT, GACHA, TRADE, QUEST
    [Key(3)] public long timestamp { get; set; }
    [Key(4)] public Dictionary<string, object> payload { get; set; } = new Dictionary<string, object>();
}

[Serializable]
[MessagePackObject(true)]
public class SparseDataSet_C3
{
    [Key(0)] public List<SparseEventLog_C3> events { get; set; } = new List<SparseEventLog_C3>();
}

#endregion