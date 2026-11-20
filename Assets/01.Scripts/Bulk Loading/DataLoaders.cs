using Google.FlatBuffers;
using LiteDB;
using Newtonsoft.Json;
using RocksDbSharp;
using SQLite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

#region DB 벌크 로더 (SQLite ORM / Native C / LiteDB / RocksDB)

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

/// <summary>
/// RocksDB 범용 벌크 로더: 테이블/Shape별 파싱 전략을 주입받아 로드
/// </summary>
public class RocksDbLoader<TContainer> : IDataLoader<TContainer> where TContainer : new()
{
    public string FormatName => "RocksDB (KV Store)";
    private readonly Action<string, byte[], TContainer> entryHandler;

    public RocksDbLoader(Action<string, byte[], TContainer> entryHandler)
    {
        this.entryHandler = entryHandler;
    }

    public TContainer LoadData(string dbDirectory)
    {
        var container = new TContainer();
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
                if (!keyStr.StartsWith("idx_", StringComparison.Ordinal) && !keyStr.Contains("_by_"))
                {
                    entryHandler?.Invoke(keyStr, iterator.Value(), container);
                }
                iterator.Next();
            }
        }
        return container;
    }
}

#endregion

#region DB 쿼리 로더 어댑터

public class SqliteOrmQueryLoader : IDataLoader<QueryResult>
{
    public string FormatName => "SQLite (sqlite-net Query)";
    private readonly string dbPath;
    private readonly QuerySpec query;

    public SqliteOrmQueryLoader(string dbPath, QuerySpec query)
    {
        this.dbPath = dbPath;
        this.query = query;
    }

    public QueryResult LoadData(string filePath)
    {
        var engine = new SqliteORMQueryEngine(dbPath);
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

#endregion

#region Non-DB 로더 (별도 최적화 예정 - 현재 비워둠)

public class JsonFileLoader<T> : IDataLoader<T>
{
    public string FormatName => "JSON (Newtonsoft)";
    public T LoadData(string filePath)
    {
        // TODO: 별도 최적화 예정
        return default;
    }
}

public class FlatBuffersLoader<TContainer> : IDataLoader<TContainer> where TContainer : new()
{
    public string FormatName => "FlatBuffers (Zero-Copy)";
    public FlatBuffersLoader(Func<ByteBuffer, TContainer> bindStrategy) { }
    public TContainer LoadData(string filePath)
    {
        // TODO: 별도 최적화 예정
        return default;
    }
}

public class RawBinaryFileLoader<TContainer> : IDataLoader<TContainer> where TContainer : new()
{
    public string FormatName => "Raw Binary (Byte Stream)";
    public TContainer LoadData(string filePath)
    {
        // TODO: 별도 최적화 예정
        return default;
    }
}

public class MessagePackLoader<T> : IDataLoader<T>
{
    public string FormatName => "MessagePack (Cysharp)";
    public T LoadData(string filePath)
    {
        // TODO: 별도 최적화 예정
        return default;
    }
}

public class FlatBuffersQueryLoader : IDataLoader<QueryResult>
{
    public string FormatName => "FlatBuffers (Query)";
    public FlatBuffersQueryLoader(string filePath, QuerySpec query) { }
    public QueryResult LoadData(string filePathIgnored)
    {
        // TODO: 별도 최적화 예정
        return new QueryResult { IsSuccess = false, ErrorMessage = "비-DB 포맷은 별도 최적화 예정입니다." };
    }
}

public class RawBinaryQueryLoader : IDataLoader<QueryResult>
{
    public string FormatName => "RawBinary (Query)";
    public RawBinaryQueryLoader(string filePath, QuerySpec query) { }
    public QueryResult LoadData(string filePathIgnored)
    {
        // TODO: 별도 최적화 예정
        return new QueryResult { IsSuccess = false, ErrorMessage = "비-DB 포맷은 별도 최적화 예정입니다." };
    }
}

#endregion