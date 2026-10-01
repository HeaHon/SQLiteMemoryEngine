using Newtonsoft.Json;
using SQLite;
using System;
using System.IO;
using System.Text;
using UnityEngine;

// [1] JSON File IO 로더
public class JsonFileLoader<T> : IDataLoader<T>
{
    public string FormatName => "JSON (Direct IO)";

    public T LoadData(string filePath)
    {
        string jsonText = File.ReadAllText(filePath, Encoding.UTF8);
        return JsonConvert.DeserializeObject<T>(jsonText);
    }
}

// [2] Binary File IO 로더
public class BinaryFileLoader<T> : IDataLoader<T>
{
    public string FormatName => "Binary (Direct IO)";

    public T LoadData(string filePath)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        string jsonText = Encoding.UTF8.GetString(bytes);
        return JsonConvert.DeserializeObject<T>(jsonText);
    }
}

// [3] SQLite File IO 및 벌크 조회 로더
public class SqliteFileLoader<TContainer> : IDataLoader<TContainer> where TContainer : new()
{
    public string FormatName => "SQLite (Direct IO)";
    private readonly Action<SQLiteConnection, TContainer> _fetchStrategy;

    // SQLite는 포맷 특성상 테이블별로 읽어와 컨테이너에 담아주는 전략을 주입받습니다.
    public SqliteFileLoader(Action<SQLiteConnection, TContainer> fetchStrategy)
    {
        _fetchStrategy = fetchStrategy;
    }

    public TContainer LoadData(string filePath)
    {
        var container = new TContainer();
        using (var db = new SQLiteConnection(filePath, SQLiteOpenFlags.ReadOnly))
        {
            _fetchStrategy?.Invoke(db, container);
        }
        return container;
    }
}

// [4] ScriptableObject 로더 (Direct Resource Loading)
public class ScriptableObjectLoader<TSO, TData> : IDataLoader<TData> where TSO : ScriptableObject
{
    public string FormatName => "ScriptableObject";
    private readonly Func<TSO, TData> _dataExtractor;

    public ScriptableObjectLoader(Func<TSO, TData> dataExtractor)
    {
        _dataExtractor = dataExtractor;
    }

    public TData LoadData(string resourcePath)
    {
        string normalizedPath = resourcePath.Replace('\\', '/');

        TSO so = Resources.Load<TSO>(normalizedPath);

        if (so == null)
        {
            Debug.LogError($"[ScriptableObjectLoader] 에셋을 찾을 수 없습니다! " +
                           $"경로를 확인하세요: Resources/{normalizedPath}.asset");
            return default;
        }

        TData data = _dataExtractor(so);

        if (data == null)
        {
            Debug.LogError($"[ScriptableObjectLoader] SO 에셋은 로드되었으나 내부 data가 null입니다: {normalizedPath}");
            return default;
        }

        return data;
    }
}