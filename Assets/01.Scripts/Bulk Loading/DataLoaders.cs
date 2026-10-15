using Google.FlatBuffers;
using LiteDB;
using MessagePack;
using Newtonsoft.Json;
using RocksDbSharp;
using SQLite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// JSON File IO 로더
public class JsonFileLoader<T> : IDataLoader<T>
{
    public string FormatName => "JSON (Newtonsoft)";

    public T LoadData(string filePath)
    {
        string jsonText = File.ReadAllText(filePath, Encoding.UTF8);
        return JsonConvert.DeserializeObject<T>(jsonText);
    }
}

// Binary File IO 로더
public class RawBinaryFileLoader<TContainer> : IDataLoader<TContainer> where TContainer : new()
{
    public string FormatName => "Raw Binary (Byte Stream)";

    public TContainer LoadData(string filePath)
    {
        var container = new TContainer();

        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        using (var reader = new BinaryReader(fs, Encoding.UTF8))
        {
            if (container is DataSet_C1 c1)
            {
                int itemCount = reader.ReadInt32();
                c1.items = new List<Master_Item_C1>(itemCount);
                for (int i = 0; i < itemCount; i++)
                {
                    c1.items.Add(new Master_Item_C1
                    {
                        item_id = reader.ReadInt32(),
                        name = reader.ReadString(),
                        item_type = reader.ReadString(),
                        rarity = reader.ReadInt32(),
                        atk = reader.ReadInt32(),
                        price = reader.ReadInt32()
                    });
                }

                int charCount = reader.ReadInt32();
                c1.characters = new List<Runtime_Character_C1>(charCount);
                for (int i = 0; i < charCount; i++)
                {
                    c1.characters.Add(new Runtime_Character_C1
                    {
                        character_id = reader.ReadInt32(),
                        name = reader.ReadString(),
                        level = reader.ReadInt32(),
                        hp = reader.ReadInt32(),
                        pos_x = reader.ReadSingle(),
                        pos_y = reader.ReadSingle()
                    });
                }
            }
            else if (container is DataSet_C2 c2)
            {
                int itemCount = reader.ReadInt32();
                c2.items = new List<Master_Item_C2>(itemCount);
                for (int i = 0; i < itemCount; i++)
                {
                    c2.items.Add(new Master_Item_C2
                    {
                        item_id = reader.ReadInt32(),
                        item_code = reader.ReadString(),
                        name = reader.ReadString(),
                        item_type = reader.ReadString(),
                        rarity = reader.ReadInt32(),
                        req_level = reader.ReadInt32(),
                        base_atk = reader.ReadInt32()
                    });
                }

                int tagCount = reader.ReadInt32();
                c2.tags = new List<Item_Tag_C2>(tagCount);
                for (int i = 0; i < tagCount; i++)
                {
                    c2.tags.Add(new Item_Tag_C2
                    {
                        item_id = reader.ReadInt32(),
                        tag_name = reader.ReadString()
                    });
                }

                int charCount = reader.ReadInt32();
                c2.characters = new List<Runtime_Character_C2>(charCount);
                for (int i = 0; i < charCount; i++)
                {
                    c2.characters.Add(new Runtime_Character_C2
                    {
                        character_id = reader.ReadInt32(),
                        name = reader.ReadString(),
                        level = reader.ReadString(),
                        cur_hp = reader.ReadInt32(),
                        pos_x = reader.ReadSingle(),
                        pos_y = reader.ReadSingle()
                    });
                }

                int invCount = reader.ReadInt32();
                c2.inventories = new List<Runtime_Inventory_C2>(invCount);
                for (int i = 0; i < invCount; i++)
                {
                    c2.inventories.Add(new Runtime_Inventory_C2
                    {
                        instance_id = reader.ReadInt32(),
                        character_id = reader.ReadInt32(),
                        item_id = reader.ReadInt32(),
                        quantity = reader.ReadInt32()
                    });
                }
            }
            else if (container is DataSet_C3 c3)
            {
                int userCount = reader.ReadInt32();
                c3.users = new List<System_User_C3>(userCount);
                for (int i = 0; i < userCount; i++)
                {
                    c3.users.Add(new System_User_C3
                    {
                        user_id = reader.ReadInt32(),
                        account_name = reader.ReadString(),
                        created_at = reader.ReadInt64()
                    });
                }

                int charCount = reader.ReadInt32();
                c3.characters = new List<Runtime_Character_C3>(charCount);
                for (int i = 0; i < charCount; i++)
                {
                    c3.characters.Add(new Runtime_Character_C3
                    {
                        character_id = reader.ReadInt32(),
                        user_id = reader.ReadInt32(),
                        name = reader.ReadString(),
                        guild_id = reader.ReadInt32()
                    });
                }

                int itemCount = reader.ReadInt32();
                c3.items = new List<Master_Item_C3>(itemCount);
                for (int i = 0; i < itemCount; i++)
                {
                    c3.items.Add(new Master_Item_C3
                    {
                        item_id = reader.ReadInt32(),
                        name = reader.ReadString(),
                        rarity = reader.ReadInt32()
                    });
                }

                int effectCount = reader.ReadInt32();
                c3.effects = new List<Master_Effect_C3>(effectCount);
                for (int i = 0; i < effectCount; i++)
                {
                    c3.effects.Add(new Master_Effect_C3
                    {
                        effect_id = reader.ReadInt32(),
                        effect_type = reader.ReadString(),
                        value = reader.ReadInt32()
                    });
                }

                int linkCount = reader.ReadInt32();
                c3.links = new List<Item_Effect_Link_C3>(linkCount);
                for (int i = 0; i < linkCount; i++)
                {
                    c3.links.Add(new Item_Effect_Link_C3
                    {
                        item_id = reader.ReadInt32(),
                        effect_id = reader.ReadInt32()
                    });
                }

                int invCount = reader.ReadInt32();
                c3.inventories = new List<Runtime_Inventory_C3>(invCount);
                for (int i = 0; i < invCount; i++)
                {
                    c3.inventories.Add(new Runtime_Inventory_C3
                    {
                        instance_id = reader.ReadInt32(),
                        character_id = reader.ReadInt32(),
                        item_id = reader.ReadInt32(),
                        durability = reader.ReadInt32()
                    });
                }
            }
        }

        return container;
    }
}

// SQLite (sqlite-net ORM) 로더
public class SqliteOrmLoader<TContainer> : IDataLoader<TContainer> where TContainer : new()
{
    public string FormatName => "SQLite (sqlite-net)";
    private readonly Action<SQLiteConnection, TContainer> fetchStrategy;

    // SQLite는 포맷 특성상 테이블별로 읽어와 컨테이너에 담아주는 전략을 주입받습니다.
    public SqliteOrmLoader(Action<SQLiteConnection, TContainer> fetchStrategy)
    {
        this.fetchStrategy = fetchStrategy;
    }

    public TContainer LoadData(string filePath)
    {
        var container = new TContainer();
        using (var db = new SQLiteConnection(filePath, SQLiteOpenFlags.ReadOnly))
        {
            fetchStrategy?.Invoke(db, container);
        }
        return container;
    }
}

// [4] SQLite (Native C API / P/Invoke) 로더
public class SqliteNativeLoader<TContainer> : IDataLoader<TContainer> where TContainer : new()
{
    public string FormatName => "SQLite (Native C)";
    private readonly Action<SqliteNativeConnection, TContainer> fetchStrategy;

    /// <summary>
    /// 복잡도(C1, C2, C3)별 테이블 조회 전략을 외부에서 주입받아 확장성을 확보합니다.
    /// </summary>
    public SqliteNativeLoader(Action<SqliteNativeConnection, TContainer> fetchStrategy)
    {
        this.fetchStrategy = fetchStrategy;
    }

    public TContainer LoadData(string filePath)
    {
        var container = new TContainer();

        using (var dbManager = new SqliteNativeConnection(filePath))
        {
            if (!dbManager.IsOpen) return container;
            fetchStrategy?.Invoke(dbManager, container);
        }

        return container;
    }
}

// LiteDB (NoSQL Document) 로더
public class LiteDbLoader<TContainer> : IDataLoader<TContainer> where TContainer : new()
{
    public string FormatName => "LiteDB";
    private readonly Action<LiteDatabase, TContainer> fetchStrategy;

    public LiteDbLoader(Action<LiteDatabase, TContainer> fetchStrategy)
    {
        this.fetchStrategy = fetchStrategy;
    }

    public TContainer LoadData(string filePath)
    {
        var container = new TContainer();
        using (var db = new LiteDatabase($"Filename={filePath};ReadOnly=true"))
        {
            fetchStrategy?.Invoke(db, container);
        }
        return container;
    }
}

// MessagePack (Cysharp) 바이너리 로더 확장 포인트
public class MessagePackLoader<T> : IDataLoader<T>
{
    public string FormatName => "MessagePack (Cysharp)";

    public T LoadData(string filePath)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        return MessagePackSerializer.Deserialize<T>(bytes, MessagePack.Resolvers.StandardResolver.Options);
    }
}

// FlatBuffers 제로파싱 메모리 맵 로더 확장 포인트
public class FlatBuffersLoader<TContainer> : IDataLoader<TContainer> where TContainer : new()
{
    public string FormatName => "FlatBuffers (Zero-Copy)";
    private readonly System.Func<ByteBuffer, TContainer> bindStrategy;

    public FlatBuffersLoader(Func<ByteBuffer, TContainer> bindStrategy)
    {
        this.bindStrategy = bindStrategy;
    }

    public TContainer LoadData(string filePath)
    {
        if (!File.Exists(filePath)) return default;

        // 파일을 메모리에 바이트 배열로 로드한 뒤 ByteBuffer 생성 (할당 최소화)
        byte[] fileBytes = File.ReadAllBytes(filePath);
        ByteBuffer bb = new ByteBuffer(fileBytes);

        // 객체를 새로 생성하고 파싱하는 대신, 버퍼 포인터만 매핑
        return bindStrategy != null ? bindStrategy.Invoke(bb) : default;
    }
}

// RocksDB (KV Storage 구조 로더)
public class RocksDbLoader_C1 : IDataLoader<DataSet_C1>
{
    public string FormatName => "RocksDB (KV Store)";
    public DataSet_C1 LoadData(string dbDirectory)
    {
        var container = new DataSet_C1();
        var options = new DbOptions().SetCreateIfMissing(false);
        using (var db = RocksDb.Open(options, dbDirectory))
        using (var iterator = db.NewIterator())
        {
            iterator.SeekToFirst();
            while (iterator.Valid())
            {
                string keyStr = Encoding.UTF8.GetString(iterator.Key());
                byte[] valBytes = iterator.Value();

                if (keyStr.StartsWith("item_"))
                    container.items.Add(MessagePackSerializer.Deserialize<Master_Item_C1>(valBytes));
                else if (keyStr.StartsWith("char_"))
                    container.characters.Add(MessagePackSerializer.Deserialize<Runtime_Character_C1>(valBytes));

                iterator.Next();
            }
        }
        return container;
    }
}

public class RocksDbLoader_C2 : IDataLoader<DataSet_C2>
{
    public string FormatName => "RocksDB (KV Store)";
    public DataSet_C2 LoadData(string dbDirectory)
    {
        var container = new DataSet_C2();
        var options = new DbOptions().SetCreateIfMissing(false);
        using (var db = RocksDb.Open(options, dbDirectory))
        using (var iterator = db.NewIterator())
        {
            iterator.SeekToFirst();
            while (iterator.Valid())
            {
                string keyStr = Encoding.UTF8.GetString(iterator.Key());
                byte[] valBytes = iterator.Value();

                if (keyStr.StartsWith("item_"))
                    container.items.Add(MessagePackSerializer.Deserialize<Master_Item_C2>(valBytes));
                else if (keyStr.StartsWith("tag_"))
                    container.tags.Add(MessagePackSerializer.Deserialize<Item_Tag_C2>(valBytes));
                else if (keyStr.StartsWith("char_"))
                    container.characters.Add(MessagePackSerializer.Deserialize<Runtime_Character_C2>(valBytes));
                else if (keyStr.StartsWith("inv_"))
                    container.inventories.Add(MessagePackSerializer.Deserialize<Runtime_Inventory_C2>(valBytes));

                iterator.Next();
            }
        }
        return container;
    }
}

public class RocksDbLoader_C3 : IDataLoader<DataSet_C3>
{
    public string FormatName => "RocksDB (KV Store)";
    public DataSet_C3 LoadData(string dbDirectory)
    {
        var container = new DataSet_C3();
        var options = new DbOptions().SetCreateIfMissing(false);
        using (var db = RocksDb.Open(options, dbDirectory))
        using (var iterator = db.NewIterator())
        {
            iterator.SeekToFirst();
            while (iterator.Valid())
            {
                string keyStr = Encoding.UTF8.GetString(iterator.Key());
                byte[] valBytes = iterator.Value();

                if (keyStr.StartsWith("user_"))
                    container.users.Add(MessagePackSerializer.Deserialize<System_User_C3>(valBytes));
                else if (keyStr.StartsWith("char_"))
                    container.characters.Add(MessagePackSerializer.Deserialize<Runtime_Character_C3>(valBytes));
                else if (keyStr.StartsWith("item_"))
                    container.items.Add(MessagePackSerializer.Deserialize<Master_Item_C3>(valBytes));
                else if (keyStr.StartsWith("effect_"))
                    container.effects.Add(MessagePackSerializer.Deserialize<Master_Effect_C3>(valBytes));
                else if (keyStr.StartsWith("link_"))
                    container.links.Add(MessagePackSerializer.Deserialize<Item_Effect_Link_C3>(valBytes));
                else if (keyStr.StartsWith("inv_"))
                    container.inventories.Add(MessagePackSerializer.Deserialize<Runtime_Inventory_C3>(valBytes));

                iterator.Next();
            }
        }
        return container;
    }
}