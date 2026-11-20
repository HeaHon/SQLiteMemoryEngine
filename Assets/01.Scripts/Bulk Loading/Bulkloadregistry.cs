using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

public class BulkLoadContext
{
    public string StreamingAssetsRoot { get; set; }
    public DataShape Shape { get; set; }
    public DataScale Scale { get; set; }
    public DataComplexity Complexity { get; set; }

    public string ShapeStr => Shape.ToString();
    public string ScaleStr => Scale.ToString();
    public string ComplexityStr => Complexity.ToString();
    public string BaseFileName => $"GameData_{ScaleStr}_{ComplexityStr}_{ShapeStr}";

    public string GetDbFilePath(string formatFolder, string ext = "db")
        => Path.Combine(StreamingAssetsRoot, formatFolder, ShapeStr, ScaleStr, $"{BaseFileName}.{ext}");

    public string GetDbFolderPath(string formatFolder)
        => Path.Combine(StreamingAssetsRoot, formatFolder, ShapeStr, ScaleStr, BaseFileName);
}

public interface IBulkLoadModule
{
    DataShape Shape { get; }
    bool TryMeasure(TargetFormat format, BulkLoadContext ctx, out BenchmarkResult result);
}

public abstract class BulkLoadModuleBase : IBulkLoadModule
{
    private readonly Dictionary<(TargetFormat, DataComplexity), Func<BulkLoadContext, BenchmarkResult>> _runners
        = new Dictionary<(TargetFormat, DataComplexity), Func<BulkLoadContext, BenchmarkResult>>();

    public abstract DataShape Shape { get; }

    protected void Register<TContainer>(
        TargetFormat format,
        DataComplexity complexity,
        Func<BulkLoadContext, (IDataLoader<TContainer> loader, string path)> factory)
    {
        _runners[(format, complexity)] = ctx =>
        {
            var (loader, path) = factory(ctx);
            return BenchmarkRunner.Measure(loader, path);
        };
    }

    public bool TryMeasure(TargetFormat format, BulkLoadContext ctx, out BenchmarkResult result)
    {
        if (_runners.TryGetValue((format, ctx.Complexity), out var runner))
        {
            result = runner(ctx);
            return true;
        }
        result = default;
        return false;
    }
}

public static class BulkLoadRegistry
{
    private static readonly Dictionary<DataShape, IBulkLoadModule> Modules = new Dictionary<DataShape, IBulkLoadModule>
    {
        { DataShape.Relational, new RelationalBulkLoadModule() },
        { DataShape.Document, new DocumentBulkLoadModule() },
        { DataShape.Sparse, new SparseBulkLoadModule() }
    };

    public static void RegisterModule(IBulkLoadModule module)
    {
        if (module != null) Modules[module.Shape] = module;
    }

    public static bool TryGetModule(DataShape shape, out IBulkLoadModule module)
        => Modules.TryGetValue(shape, out module);
}

#region Shape별 벌크 로드 모듈 구현

public class RelationalBulkLoadModule : BulkLoadModuleBase
{
    public override DataShape Shape => DataShape.Relational;

    public RelationalBulkLoadModule()
    {
        // C1
        Register(TargetFormat.SQLite_ORM, DataComplexity.C1_Low, ctx => (
            new SqliteOrmLoader<RelDataSet_C1>((db, c) =>
            {
                c.items = db.Table<RelMasterItem_C1>().ToList();
                c.characters = db.Table<RelRuntimeCharacter_C1>().ToList();
            }),
            ctx.GetDbFilePath("SQLite")));

        Register(TargetFormat.SQLite_NativeC, DataComplexity.C1_Low, ctx => (
            new SqliteNativeLoader<RelDataSet_C1>((db, c) =>
            {
                db.ExecuteQuery($"SELECT item_id, name, item_type, rarity, atk, price FROM {nameof(RelMasterItem_C1)}", stmt =>
                {
                    c.items.Add(new RelMasterItem_C1
                    {
                        item_id = SqliteNativeConnection.GetInt(stmt, 0),
                        name = SqliteNativeConnection.GetString(stmt, 1),
                        item_type = SqliteNativeConnection.GetString(stmt, 2),
                        rarity = SqliteNativeConnection.GetInt(stmt, 3),
                        atk = SqliteNativeConnection.GetInt(stmt, 4),
                        price = SqliteNativeConnection.GetInt(stmt, 5)
                    });
                });
                db.ExecuteQuery($"SELECT character_id, name, level, hp, pos_x, pos_y FROM {nameof(RelRuntimeCharacter_C1)}", stmt =>
                {
                    c.characters.Add(new RelRuntimeCharacter_C1
                    {
                        character_id = SqliteNativeConnection.GetInt(stmt, 0),
                        name = SqliteNativeConnection.GetString(stmt, 1),
                        level = SqliteNativeConnection.GetInt(stmt, 2),
                        hp = SqliteNativeConnection.GetInt(stmt, 3),
                        pos_x = (float)SqliteNativeConnection.GetDouble(stmt, 4),
                        pos_y = (float)SqliteNativeConnection.GetDouble(stmt, 5)
                    });
                });
            }),
            ctx.GetDbFilePath("SQLite")));

        Register(TargetFormat.LiteDB, DataComplexity.C1_Low, ctx => (
            new LiteDbLoader<RelDataSet_C1>((db, c) =>
            {
                c.items = db.GetCollection<RelMasterItem_C1>("items").FindAll().ToList();
                c.characters = db.GetCollection<RelRuntimeCharacter_C1>("characters").FindAll().ToList();
            }),
            ctx.GetDbFilePath("LiteDB")));

        Register(TargetFormat.RocksDB, DataComplexity.C1_Low, ctx => (
            new RocksDbLoader<RelDataSet_C1>((key, val, c) =>
            {
                using (var ms = new MemoryStream(val))
                using (var r = new BinaryReader(ms, Encoding.UTF8))
                {
                    if (key.StartsWith("item_"))
                        c.items.Add(new RelMasterItem_C1 { item_id = r.ReadInt32(), name = r.ReadString(), item_type = r.ReadString(), rarity = r.ReadInt32(), atk = r.ReadInt32(), price = r.ReadInt32() });
                    else if (key.StartsWith("char_"))
                        c.characters.Add(new RelRuntimeCharacter_C1 { character_id = r.ReadInt32(), name = r.ReadString(), level = r.ReadInt32(), hp = r.ReadInt32(), pos_x = r.ReadSingle(), pos_y = r.ReadSingle() });
                }
            }),
            ctx.GetDbFolderPath("RocksDB")));

        // C2
        Register(TargetFormat.SQLite_ORM, DataComplexity.C2_Mid, ctx => (
            new SqliteOrmLoader<RelDataSet_C2>((db, c) =>
            {
                c.items = db.Table<RelMasterItem_C2>().ToList();
                c.tags = db.Table<RelItemTag_C2>().ToList();
                c.characters = db.Table<RelRuntimeCharacter_C2>().ToList();
                c.inventories = db.Table<RelRuntimeInventory_C2>().ToList();
            }),
            ctx.GetDbFilePath("SQLite")));

        Register(TargetFormat.LiteDB, DataComplexity.C2_Mid, ctx => (
            new LiteDbLoader<RelDataSet_C2>((db, c) =>
            {
                c.items = db.GetCollection<RelMasterItem_C2>("items").FindAll().ToList();
                c.tags = db.GetCollection<RelItemTag_C2>("tags").FindAll().ToList();
                c.characters = db.GetCollection<RelRuntimeCharacter_C2>("characters").FindAll().ToList();
                c.inventories = db.GetCollection<RelRuntimeInventory_C2>("inventories").FindAll().ToList();
            }),
            ctx.GetDbFilePath("LiteDB")));

        Register(TargetFormat.RocksDB, DataComplexity.C2_Mid, ctx => (
            new RocksDbLoader<RelDataSet_C2>((key, val, c) =>
            {
                using (var ms = new MemoryStream(val))
                using (var r = new BinaryReader(ms, Encoding.UTF8))
                {
                    if (key.StartsWith("item_"))
                        c.items.Add(new RelMasterItem_C2 { item_id = r.ReadInt32(), item_code = r.ReadString(), name = r.ReadString(), item_type = r.ReadString(), rarity = r.ReadInt32(), req_level = r.ReadInt32(), base_atk = r.ReadInt32() });
                    else if (key.StartsWith("tag_"))
                        c.tags.Add(new RelItemTag_C2 { item_id = r.ReadInt32(), tag_name = r.ReadString() });
                    else if (key.StartsWith("char_"))
                        c.characters.Add(new RelRuntimeCharacter_C2 { character_id = r.ReadInt32(), name = r.ReadString(), level = r.ReadString(), cur_hp = r.ReadInt32(), pos_x = r.ReadSingle(), pos_y = r.ReadSingle() });
                    else if (key.StartsWith("inv_"))
                        c.inventories.Add(new RelRuntimeInventory_C2 { instance_id = r.ReadInt32(), character_id = r.ReadInt32(), item_id = r.ReadInt32(), quantity = r.ReadInt32() });
                }
            }),
            ctx.GetDbFolderPath("RocksDB")));

        // C3
        Register(TargetFormat.SQLite_ORM, DataComplexity.C3_High, ctx => (
            new SqliteOrmLoader<RelDataSet_C3>((db, c) =>
            {
                c.users = db.Table<RelSystemUser_C3>().ToList();
                c.characters = db.Table<RelRuntimeCharacter_C3>().ToList();
                c.items = db.Table<RelMasterItem_C3>().ToList();
                c.effects = db.Table<RelMasterEffect_C3>().ToList();
                c.links = db.Table<RelItemEffectLink_C3>().ToList();
                c.inventories = db.Table<RelRuntimeInventory_C3>().ToList();
            }),
            ctx.GetDbFilePath("SQLite")));

        Register(TargetFormat.LiteDB, DataComplexity.C3_High, ctx => (
            new LiteDbLoader<RelDataSet_C3>((db, c) =>
            {
                c.users = db.GetCollection<RelSystemUser_C3>("users").FindAll().ToList();
                c.characters = db.GetCollection<RelRuntimeCharacter_C3>("characters").FindAll().ToList();
                c.items = db.GetCollection<RelMasterItem_C3>("items").FindAll().ToList();
                c.effects = db.GetCollection<RelMasterEffect_C3>("effects").FindAll().ToList();
                c.links = db.GetCollection<RelItemEffectLink_C3>("links").FindAll().ToList();
                c.inventories = db.GetCollection<RelRuntimeInventory_C3>("inventories").FindAll().ToList();
            }),
            ctx.GetDbFilePath("LiteDB")));

        Register(TargetFormat.RocksDB, DataComplexity.C3_High, ctx => (
            new RocksDbLoader<RelDataSet_C3>((key, val, c) =>
            {
                using (var ms = new MemoryStream(val))
                using (var r = new BinaryReader(ms, Encoding.UTF8))
                {
                    if (key.StartsWith("user_"))
                        c.users.Add(new RelSystemUser_C3 { user_id = r.ReadInt32(), account_name = r.ReadString(), created_at = r.ReadInt64() });
                    else if (key.StartsWith("char_"))
                        c.characters.Add(new RelRuntimeCharacter_C3 { character_id = r.ReadInt32(), user_id = r.ReadInt32(), name = r.ReadString(), guild_id = r.ReadInt32() });
                    else if (key.StartsWith("item_"))
                        c.items.Add(new RelMasterItem_C3 { item_id = r.ReadInt32(), name = r.ReadString(), rarity = r.ReadInt32() });
                    else if (key.StartsWith("effect_"))
                        c.effects.Add(new RelMasterEffect_C3 { effect_id = r.ReadInt32(), effect_type = r.ReadString(), value = r.ReadInt32() });
                    else if (key.StartsWith("link_"))
                        c.links.Add(new RelItemEffectLink_C3 { item_id = r.ReadInt32(), effect_id = r.ReadInt32() });
                    else if (key.StartsWith("inv_"))
                        c.inventories.Add(new RelRuntimeInventory_C3 { instance_id = r.ReadInt32(), character_id = r.ReadInt32(), item_id = r.ReadInt32(), durability = r.ReadInt32() });
                }
            }),
            ctx.GetDbFolderPath("RocksDB")));
    }
}

public class DocumentBulkLoadModule : BulkLoadModuleBase
{
    public override DataShape Shape => DataShape.Document;

    public DocumentBulkLoadModule()
    {
        Register(TargetFormat.LiteDB, DataComplexity.C1_Low, ctx => (
            new LiteDbLoader<DocDataSet_C1>((db, c) => c.items = db.GetCollection<DocItem_C1>("items").FindAll().ToList()),
            ctx.GetDbFilePath("LiteDB")));
        Register(TargetFormat.RocksDB, DataComplexity.C1_Low, ctx => (
            new RocksDbLoader<DocDataSet_C1>((key, val, c) =>
            {
                if (key.StartsWith("doc_item_")) c.items.Add(JsonConvert.DeserializeObject<DocItem_C1>(Encoding.UTF8.GetString(val)));
            }),
            ctx.GetDbFolderPath("RocksDB")));

        Register(TargetFormat.LiteDB, DataComplexity.C2_Mid, ctx => (
            new LiteDbLoader<DocDataSet_C2>((db, c) => c.characters = db.GetCollection<DocCharacter_C2>("characters").FindAll().ToList()),
            ctx.GetDbFilePath("LiteDB")));
        Register(TargetFormat.RocksDB, DataComplexity.C2_Mid, ctx => (
            new RocksDbLoader<DocDataSet_C2>((key, val, c) =>
            {
                if (key.StartsWith("doc_char_")) c.characters.Add(JsonConvert.DeserializeObject<DocCharacter_C2>(Encoding.UTF8.GetString(val)));
            }),
            ctx.GetDbFolderPath("RocksDB")));

        Register(TargetFormat.LiteDB, DataComplexity.C3_High, ctx => (
            new LiteDbLoader<DocDataSet_C3>((db, c) => c.users = db.GetCollection<DocUser_C3>("users").FindAll().ToList()),
            ctx.GetDbFilePath("LiteDB")));
        Register(TargetFormat.RocksDB, DataComplexity.C3_High, ctx => (
            new RocksDbLoader<DocDataSet_C3>((key, val, c) =>
            {
                if (key.StartsWith("doc_user_")) c.users.Add(JsonConvert.DeserializeObject<DocUser_C3>(Encoding.UTF8.GetString(val)));
            }),
            ctx.GetDbFolderPath("RocksDB")));
    }
}

public class SparseBulkLoadModule : BulkLoadModuleBase
{
    public override DataShape Shape => DataShape.Sparse;

    public SparseBulkLoadModule()
    {
        Register(TargetFormat.LiteDB, DataComplexity.C1_Low, ctx => (
            new LiteDbLoader<SparseDataSet_C1>((db, c) => c.items = db.GetCollection<SparseItem_C1>("items").FindAll().ToList()),
            ctx.GetDbFilePath("LiteDB")));
        Register(TargetFormat.RocksDB, DataComplexity.C1_Low, ctx => (
            new RocksDbLoader<SparseDataSet_C1>((key, val, c) =>
            {
                if (key.StartsWith("sparse_item_")) c.items.Add(JsonConvert.DeserializeObject<SparseItem_C1>(Encoding.UTF8.GetString(val)));
            }),
            ctx.GetDbFolderPath("RocksDB")));

        Register(TargetFormat.LiteDB, DataComplexity.C2_Mid, ctx => (
            new LiteDbLoader<SparseDataSet_C2>((db, c) => c.entities = db.GetCollection<SparseEntity_C2>("entities").FindAll().ToList()),
            ctx.GetDbFilePath("LiteDB")));
        Register(TargetFormat.RocksDB, DataComplexity.C2_Mid, ctx => (
            new RocksDbLoader<SparseDataSet_C2>((key, val, c) =>
            {
                if (key.StartsWith("sparse_ent_")) c.entities.Add(JsonConvert.DeserializeObject<SparseEntity_C2>(Encoding.UTF8.GetString(val)));
            }),
            ctx.GetDbFolderPath("RocksDB")));

        Register(TargetFormat.LiteDB, DataComplexity.C3_High, ctx => (
            new LiteDbLoader<SparseDataSet_C3>((db, c) => c.events = db.GetCollection<SparseEventLog_C3>("events").FindAll().ToList()),
            ctx.GetDbFilePath("LiteDB")));
        Register(TargetFormat.RocksDB, DataComplexity.C3_High, ctx => (
            new RocksDbLoader<SparseDataSet_C3>((key, val, c) =>
            {
                if (key.StartsWith("sparse_ev_")) c.events.Add(JsonConvert.DeserializeObject<SparseEventLog_C3>(Encoding.UTF8.GetString(val)));
            }),
            ctx.GetDbFolderPath("RocksDB")));
    }
}

#endregion