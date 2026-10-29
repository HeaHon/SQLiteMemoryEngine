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
using UnityEngine;

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

// FlatBuffers 전체 로드 로더 (제로파싱 버퍼 바인딩)
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
        if (!File.Exists(filePath))
        {
            Debug.LogWarning($"[FlatBuffersLoader] 파일을 찾을 수 없습니다: {filePath}");
            return default;
        }

        try
        {
            byte[] fileBytes = File.ReadAllBytes(filePath);
            if (fileBytes == null || fileBytes.Length == 0) return default;

            ByteBuffer bb = new ByteBuffer(fileBytes);
            return bindStrategy != null ? bindStrategy.Invoke(bb) : default;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[FlatBuffersLoader] 파싱 중 에러 발생: {ex.Message}");
            return default;
        }
    }
}

// Binary File IO 로더 (벌크 로드용)
public class RawBinaryFileLoader<TContainer> : IDataLoader<TContainer> where TContainer : new()
{
    public string FormatName => "Raw Binary (Byte Stream)";

    public TContainer LoadData(string filePath)
    {
        var container = new TContainer();

        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        using (var reader = new BinaryReader(fs, Encoding.UTF8))
        {
            if (fs.Length == 0) return container;

            if (container is DataSet_C1 c1)
            {
                int itemCount = reader.ReadInt32();
                // PK 인덱스 헤더 영역 건너뛰기 (itemCount * 12바이트)
                fs.Seek(itemCount * 12, SeekOrigin.Current);

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
                fs.Seek(itemCount * 12, SeekOrigin.Current);

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
                fs.Seek(userCount * 12, SeekOrigin.Current);

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

// SQLite (Native C API / P/Invoke) 로더
public class SqliteNativeLoader<TContainer> : IDataLoader<TContainer> where TContainer : new()
{
    public string FormatName => "SQLite (Native C)";
    private readonly Action<SqliteNativeConnection, TContainer> fetchStrategy;

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

// MessagePack 바이너리 로더
public class MessagePackLoader<T> : IDataLoader<T>
{
    public string FormatName => "MessagePack (Cysharp)";

    public T LoadData(string filePath)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        return MessagePackSerializer.Deserialize<T>(bytes, MessagePack.Resolvers.StandardResolver.Options);
    }
}

// RocksDB 벌크 로더 (새로운 포맷 빌더의 이진 페이로드 구조 대응)


public class RocksDbLoader_C1 : IDataLoader<DataSet_C1>
{
    public string FormatName => "RocksDB (KV Store)";
    public DataSet_C1 LoadData(string dbDirectory)
    {
        var container = new DataSet_C1();
        string absolutePath = Path.GetFullPath(dbDirectory);
        if (!Directory.Exists(absolutePath)) return container;

        var options = new DbOptions().SetCreateIfMissing(false);
        using (var db = RocksDb.Open(options, absolutePath))
        using (var iterator = db.NewIterator())
        {
            iterator.SeekToFirst();
            while (iterator.Valid())
            {
                string keyStr = Encoding.UTF8.GetString(iterator.Key());

                // 보조 인덱스 키는 전체 로드 대상이 아니므로 제외
                if (keyStr.StartsWith("idx_"))
                {
                    iterator.Next();
                    continue;
                }

                byte[] valBytes = iterator.Value();

                if (keyStr.StartsWith("item_"))
                {
                    using (var ms = new MemoryStream(valBytes))
                    using (var reader = new BinaryReader(ms, Encoding.UTF8))
                    {
                        container.items.Add(new Master_Item_C1
                        {
                            item_id = reader.ReadInt32(),
                            name = reader.ReadString(),
                            item_type = reader.ReadString(),
                            rarity = reader.ReadInt32(),
                            atk = reader.ReadInt32(),
                            price = reader.ReadInt32()
                        });
                    }
                }
                else if (keyStr.StartsWith("char_"))
                {
                    using (var ms = new MemoryStream(valBytes))
                    using (var reader = new BinaryReader(ms, Encoding.UTF8))
                    {
                        container.characters.Add(new Runtime_Character_C1
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
        string absolutePath = Path.GetFullPath(dbDirectory);
        if (!Directory.Exists(absolutePath)) return container;

        var options = new DbOptions().SetCreateIfMissing(false);
        using (var db = RocksDb.Open(options, absolutePath))
        using (var iterator = db.NewIterator())
        {
            iterator.SeekToFirst();
            while (iterator.Valid())
            {
                string keyStr = Encoding.UTF8.GetString(iterator.Key());
                if (keyStr.StartsWith("idx_")) { iterator.Next(); continue; }

                byte[] valBytes = iterator.Value();
                if (keyStr.StartsWith("item_"))
                {
                    using (var ms = new MemoryStream(valBytes))
                    using (var reader = new BinaryReader(ms, Encoding.UTF8))
                    {
                        container.items.Add(new Master_Item_C2
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
                }
                else if (keyStr.StartsWith("tag_"))
                {
                    using (var ms = new MemoryStream(valBytes))
                    using (var reader = new BinaryReader(ms, Encoding.UTF8))
                    {
                        container.tags.Add(new Item_Tag_C2 { item_id = reader.ReadInt32(), tag_name = reader.ReadString() });
                    }
                }
                else if (keyStr.StartsWith("char_"))
                {
                    using (var ms = new MemoryStream(valBytes))
                    using (var reader = new BinaryReader(ms, Encoding.UTF8))
                    {
                        container.characters.Add(new Runtime_Character_C2
                        {
                            character_id = reader.ReadInt32(),
                            name = reader.ReadString(),
                            level = reader.ReadString(),
                            cur_hp = reader.ReadInt32(),
                            pos_x = reader.ReadSingle(),
                            pos_y = reader.ReadSingle()
                        });
                    }
                }
                else if (keyStr.StartsWith("inv_") && !keyStr.StartsWith("inv_by_char_"))
                {
                    using (var ms = new MemoryStream(valBytes))
                    using (var reader = new BinaryReader(ms, Encoding.UTF8))
                    {
                        container.inventories.Add(new Runtime_Inventory_C2
                        {
                            instance_id = reader.ReadInt32(),
                            character_id = reader.ReadInt32(),
                            item_id = reader.ReadInt32(),
                            quantity = reader.ReadInt32()
                        });
                    }
                }
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
        string absolutePath = Path.GetFullPath(dbDirectory);
        if (!Directory.Exists(absolutePath)) return container;

        var options = new DbOptions().SetCreateIfMissing(false);
        using (var db = RocksDb.Open(options, absolutePath))
        using (var iterator = db.NewIterator())
        {
            iterator.SeekToFirst();
            while (iterator.Valid())
            {
                string keyStr = Encoding.UTF8.GetString(iterator.Key());
                if (keyStr.StartsWith("idx_")) { iterator.Next(); continue; }

                byte[] valBytes = iterator.Value();
                if (keyStr.StartsWith("user_"))
                {
                    using (var ms = new MemoryStream(valBytes))
                    using (var reader = new BinaryReader(ms, Encoding.UTF8))
                    {
                        container.users.Add(new System_User_C3 { user_id = reader.ReadInt32(), account_name = reader.ReadString(), created_at = reader.ReadInt64() });
                    }
                }
                else if (keyStr.StartsWith("char_") && !keyStr.StartsWith("char_by_user_"))
                {
                    using (var ms = new MemoryStream(valBytes))
                    using (var reader = new BinaryReader(ms, Encoding.UTF8))
                    {
                        container.characters.Add(new Runtime_Character_C3 { character_id = reader.ReadInt32(), user_id = reader.ReadInt32(), name = reader.ReadString(), guild_id = reader.ReadInt32() });
                    }
                }
                else if (keyStr.StartsWith("item_"))
                {
                    using (var ms = new MemoryStream(valBytes))
                    using (var reader = new BinaryReader(ms, Encoding.UTF8))
                    {
                        container.items.Add(new Master_Item_C3 { item_id = reader.ReadInt32(), name = reader.ReadString(), rarity = reader.ReadInt32() });
                    }
                }
                else if (keyStr.StartsWith("effect_"))
                {
                    using (var ms = new MemoryStream(valBytes))
                    using (var reader = new BinaryReader(ms, Encoding.UTF8))
                    {
                        container.effects.Add(new Master_Effect_C3 { effect_id = reader.ReadInt32(), effect_type = reader.ReadString(), value = reader.ReadInt32() });
                    }
                }
                else if (keyStr.StartsWith("link_"))
                {
                    using (var ms = new MemoryStream(valBytes))
                    using (var reader = new BinaryReader(ms, Encoding.UTF8))
                    {
                        container.links.Add(new Item_Effect_Link_C3 { item_id = reader.ReadInt32(), effect_id = reader.ReadInt32() });
                    }
                }
                else if (keyStr.StartsWith("inv_") && !keyStr.StartsWith("inv_by_char_"))
                {
                    using (var ms = new MemoryStream(valBytes))
                    using (var reader = new BinaryReader(ms, Encoding.UTF8))
                    {
                        container.inventories.Add(new Runtime_Inventory_C3 { instance_id = reader.ReadInt32(), character_id = reader.ReadInt32(), item_id = reader.ReadInt32(), durability = reader.ReadInt32() });
                    }
                }
                iterator.Next();
            }
        }
        return container;
    }
}

// ------- 쿼리 로더 어댑터 -------

public class SqliteQueryLoader : IDataLoader<QueryResult>
{
    public string FormatName => "SQLite (Query)";
    private readonly string dbPath;
    private readonly QuerySpec query;

    public SqliteQueryLoader(string dbPath, QuerySpec query)
    {
        this.dbPath = dbPath;
        this.query = query;
    }

    public QueryResult LoadData(string filePath)
    {
        var engine = new SqliteQueryEngine(dbPath);
        return engine.Execute(query);
    }
}

public class SqliteNativeQueryLoader : IDataLoader<QueryResult>
{
    public string FormatName => "SQLite (Native C Query)";
    private readonly string dbPath;
    private readonly QuerySpec query;

    public SqliteNativeQueryLoader(string dbPath, QuerySpec query)
    {
        this.dbPath = dbPath;
        this.query = query;
    }

    public QueryResult LoadData(string filePath)
    {
        var engine = new SqliteNativeQueryEngine(dbPath);
        return engine.Execute(query);
    }
}

public class LiteDbQueryLoader : IDataLoader<QueryResult>
{
    public string FormatName => "LiteDB (Query)";
    private readonly string dbPath;
    private readonly QuerySpec query;

    public LiteDbQueryLoader(string dbPath, QuerySpec query)
    {
        this.dbPath = dbPath;
        this.query = query;
    }

    public QueryResult LoadData(string filePath)
    {
        var engine = new LiteDbQueryEngine(dbPath);
        return engine.Execute(query);
    }
}
public class RocksDbQueryLoader : IDataLoader<QueryResult>
{
    public string FormatName => "RocksDB (Query)";
    private readonly string dbFolder;
    private readonly QuerySpec query;

    public RocksDbQueryLoader(string dbFolder, QuerySpec query)
    {
        this.dbFolder = dbFolder;
        this.query = query;
    }

    public QueryResult LoadData(string filePath)
    {
        var engine = new RocksDbQueryEngine(dbFolder);
        return engine.Execute(query);
    }
}

public class FlatBuffersQueryLoader : IDataLoader<QueryResult>
{
    public string FormatName => "FlatBuffers (Query)";
    private readonly string filePath;
    private readonly QuerySpec query;

    public FlatBuffersQueryLoader(string filePath, QuerySpec query)
    {
        this.filePath = filePath;
        this.query = query;
    }

    public QueryResult LoadData(string filePathIgnored)
    {
        var engine = new FlatBuffersQueryEngine(filePath);
        return engine.Execute(query);
    }
}

public class RawBinaryQueryLoader : IDataLoader<QueryResult>
{
    public string FormatName => "RawBinary (Query)";
    private readonly string filePath;
    private readonly QuerySpec query;

    public RawBinaryQueryLoader(string filePath, QuerySpec query)
    {
        this.filePath = filePath;
        this.query = query;
    }

    public QueryResult LoadData(string filePathIgnored)
    {
        var engine = new RawBinaryQueryEngine(filePath);
        return engine.Execute(query);
    }
}
