using LiteDB;
using Newtonsoft.Json;
using RocksDbSharp;
using SQLite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

#region SQLite Format Builder (Relational / Document(정규화) / Sparse(EAV))

public class SqliteFormatBuilder : FormatBuilderBase
{
    public override string FormatName => "SQLite";
    protected override string GetExtension() => "db";

    public SqliteFormatBuilder()
    {
        // Relational (기존)
        Register(DataShape.Relational, DataComplexity.C1_Low, BuildRelationalC1);
        Register(DataShape.Relational, DataComplexity.C2_Mid, BuildRelationalC2);
        Register(DataShape.Relational, DataComplexity.C3_High, BuildRelationalC3);

        // Document (SQLite 대조용: 중첩 구조를 정규화 테이블로 분해 저장)
        Register(DataShape.Document, DataComplexity.C1_Low, BuildDocumentC1);
        Register(DataShape.Document, DataComplexity.C2_Mid, BuildDocumentC2);
        Register(DataShape.Document, DataComplexity.C3_High, BuildDocumentC3);

        // Sparse (SQLite 대조용: EAV 패턴으로 가변 속성 분해 저장)
        Register(DataShape.Sparse, DataComplexity.C1_Low, BuildSparseC1);
        Register(DataShape.Sparse, DataComplexity.C2_Mid, BuildSparseC2);
        Register(DataShape.Sparse, DataComplexity.C3_High, BuildSparseC3);
    }

    private static SQLiteConnection OpenFreshDb(string targetPath)
    {
        if (File.Exists(targetPath)) File.Delete(targetPath);
        return new SQLiteConnection(targetPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create);
    }

    #region Relational
    private void BuildRelationalC1(object container, string targetPath)
    {
        if (!(container is RelDataSet_C1 c1)) return;
        using (var db = OpenFreshDb(targetPath))
        {
            db.BeginTransaction();
            db.CreateTable<RelMasterItem_C1>();
            db.CreateTable<RelRuntimeCharacter_C1>();
            db.InsertAll(c1.items);
            db.InsertAll(c1.characters);
            db.CreateIndex("idx_c1_rarity_atk", nameof(RelMasterItem_C1), new[] { "rarity", "atk" }, false);
            db.Commit();
        }
    }

    private void BuildRelationalC2(object container, string targetPath)
    {
        if (!(container is RelDataSet_C2 c2)) return;
        using (var db = OpenFreshDb(targetPath))
        {
            db.BeginTransaction();
            db.CreateTable<RelMasterItem_C2>();
            db.CreateTable<RelItemTag_C2>();
            db.CreateTable<RelRuntimeCharacter_C2>();
            db.CreateTable<RelRuntimeInventory_C2>();
            db.InsertAll(c2.items);
            db.InsertAll(c2.tags);
            db.InsertAll(c2.characters);
            db.InsertAll(c2.inventories);
            db.CreateIndex("idx_c2_req_level_rarity", nameof(RelMasterItem_C2), new[] { "req_level", "rarity" }, false);
            db.Commit();
        }
    }

    private void BuildRelationalC3(object container, string targetPath)
    {
        if (!(container is RelDataSet_C3 c3)) return;
        using (var db = OpenFreshDb(targetPath))
        {
            db.BeginTransaction();
            db.CreateTable<RelSystemUser_C3>();
            db.CreateTable<RelRuntimeCharacter_C3>();
            db.CreateTable<RelMasterItem_C3>();
            db.CreateTable<RelMasterEffect_C3>();
            db.CreateTable<RelItemEffectLink_C3>();
            db.CreateTable<RelRuntimeInventory_C3>();
            db.InsertAll(c3.users);
            db.InsertAll(c3.characters);
            db.InsertAll(c3.items);
            db.InsertAll(c3.effects);
            db.InsertAll(c3.links);
            db.InsertAll(c3.inventories);
            db.CreateIndex("idx_c3_rarity", nameof(RelMasterItem_C3), new[] { "rarity" }, false);
            db.Commit();
        }
    }
    #endregion

    #region Document -> Normalized Tables
    private void BuildDocumentC1(object container, string targetPath)
    {
        if (!(container is DocDataSet_C1 doc)) return;
        var items = new List<SqlDocItemRow_C1>(doc.items.Count);
        var tags = new List<SqlDocItemTagRow_C1>();

        foreach (var item in doc.items)
        {
            items.Add(new SqlDocItemRow_C1
            {
                item_id = item.item_id,
                name = item.name,
                item_type = item.item_type,
                rarity = item.rarity,
                atk = item.atk,
                price = item.price
            });
            foreach (var tag in item.tags)
            {
                tags.Add(new SqlDocItemTagRow_C1 { item_id = item.item_id, tag_name = tag });
            }
        }

        using (var db = OpenFreshDb(targetPath))
        {
            db.BeginTransaction();
            db.CreateTable<SqlDocItemRow_C1>();
            db.CreateTable<SqlDocItemTagRow_C1>();
            db.InsertAll(items);
            db.InsertAll(tags);
            db.CreateIndex("idx_doc_c1_rarity", "Doc_Item_C1", new[] { "rarity" }, false);
            db.Commit();
        }
    }

    private void BuildDocumentC2(object container, string targetPath)
    {
        if (!(container is DocDataSet_C2 doc)) return;
        var chars = new List<SqlDocCharRow_C2>(doc.characters.Count);
        var invs = new List<SqlDocInvRow_C2>();
        var tags = new List<SqlDocInvTagRow_C2>();

        foreach (var ch in doc.characters)
        {
            chars.Add(new SqlDocCharRow_C2
            {
                character_id = ch.character_id,
                name = ch.name,
                level = ch.level,
                cur_hp = ch.cur_hp
            });
            foreach (var inv in ch.inventories)
            {
                invs.Add(new SqlDocInvRow_C2
                {
                    instance_id = inv.instance_id,
                    character_id = ch.character_id,
                    item_id = inv.item_id,
                    item_name = inv.item_name,
                    rarity = inv.rarity,
                    quantity = inv.quantity
                });
                foreach (var tag in inv.tags)
                {
                    tags.Add(new SqlDocInvTagRow_C2 { instance_id = inv.instance_id, tag_name = tag });
                }
            }
        }

        using (var db = OpenFreshDb(targetPath))
        {
            db.BeginTransaction();
            db.CreateTable<SqlDocCharRow_C2>();
            db.CreateTable<SqlDocInvRow_C2>();
            db.CreateTable<SqlDocInvTagRow_C2>();
            db.InsertAll(chars);
            db.InsertAll(invs);
            db.InsertAll(tags);
            db.Commit();
        }
    }

    private void BuildDocumentC3(object container, string targetPath)
    {
        if (!(container is DocDataSet_C3 doc)) return;
        var users = new List<SqlDocUserRow_C3>(doc.users.Count);
        var chars = new List<SqlDocCharRow_C3>();
        var invs = new List<SqlDocInvRow_C3>();
        var effs = new List<SqlDocEffRow_C3>();

        foreach (var u in doc.users)
        {
            users.Add(new SqlDocUserRow_C3 { user_id = u.user_id, account_name = u.account_name, created_at = u.created_at });
            foreach (var ch in u.characters)
            {
                chars.Add(new SqlDocCharRow_C3 { character_id = ch.character_id, user_id = u.user_id, name = ch.name, guild_id = ch.guild_id });
                foreach (var inv in ch.inventories)
                {
                    invs.Add(new SqlDocInvRow_C3
                    {
                        instance_id = inv.instance_id,
                        character_id = ch.character_id,
                        item_id = inv.item_id,
                        item_name = inv.item_name,
                        rarity = inv.rarity,
                        durability = inv.durability
                    });
                    foreach (var eff in inv.effects)
                    {
                        effs.Add(new SqlDocEffRow_C3
                        {
                            effect_id = eff.effect_id,
                            instance_id = inv.instance_id,
                            effect_type = eff.effect_type,
                            value = eff.value
                        });
                    }
                }
            }
        }

        using (var db = OpenFreshDb(targetPath))
        {
            db.BeginTransaction();
            db.CreateTable<SqlDocUserRow_C3>();
            db.CreateTable<SqlDocCharRow_C3>();
            db.CreateTable<SqlDocInvRow_C3>();
            db.CreateTable<SqlDocEffRow_C3>();
            db.InsertAll(users);
            db.InsertAll(chars);
            db.InsertAll(invs);
            db.InsertAll(effs);
            db.Commit();
        }
    }
    #endregion

    #region Sparse -> EAV Tables
    private void BuildSparseC1(object container, string targetPath)
    {
        if (!(container is SparseDataSet_C1 sparse)) return;
        var items = new List<SqlSparseItemRow_C1>(sparse.items.Count);
        var attrs = new List<SqlSparseAttrRow>();

        foreach (var item in sparse.items)
        {
            items.Add(new SqlSparseItemRow_C1 { item_id = item.item_id, item_type = item.item_type, name = item.name });
            foreach (var kv in item.attributes)
            {
                attrs.Add(new SqlSparseAttrRow { owner_id = item.item_id, attr_key = kv.Key, attr_value = kv.Value?.ToString() ?? string.Empty });
            }
        }

        using (var db = OpenFreshDb(targetPath))
        {
            db.BeginTransaction();
            db.CreateTable<SqlSparseItemRow_C1>();
            db.CreateTable<SqlSparseAttrRow>();
            db.InsertAll(items);
            db.InsertAll(attrs);
            db.CreateIndex("idx_sparse_c1_type", "Sparse_Item_C1", new[] { "item_type" }, false);
            db.Commit();
        }
    }

    private void BuildSparseC2(object container, string targetPath)
    {
        if (!(container is SparseDataSet_C2 sparse)) return;
        var entities = new List<SqlSparseEntityRow_C2>(sparse.entities.Count);
        var stats = new List<SqlSparseAttrRow>();
        var metas = new List<SqlSparseMetaRow>();

        foreach (var ent in sparse.entities)
        {
            entities.Add(new SqlSparseEntityRow_C2 { entity_id = ent.entity_id, category = ent.category, name = ent.name });
            foreach (var kv in ent.stats)
                stats.Add(new SqlSparseAttrRow { owner_id = ent.entity_id, attr_key = kv.Key, attr_value = kv.Value?.ToString() ?? string.Empty });
            foreach (var kv in ent.metadata)
                metas.Add(new SqlSparseMetaRow { owner_id = ent.entity_id, meta_key = kv.Key, meta_value = kv.Value ?? string.Empty });
        }

        using (var db = OpenFreshDb(targetPath))
        {
            db.BeginTransaction();
            db.CreateTable<SqlSparseEntityRow_C2>();
            db.CreateTable<SqlSparseAttrRow>();
            db.CreateTable<SqlSparseMetaRow>();
            db.InsertAll(entities);
            db.InsertAll(stats);
            db.InsertAll(metas);
            db.CreateIndex("idx_sparse_c2_cat", "Sparse_Entity_C2", new[] { "category" }, false);
            db.Commit();
        }
    }

    private void BuildSparseC3(object container, string targetPath)
    {
        if (!(container is SparseDataSet_C3 sparse)) return;
        var events = new List<SqlSparseEventRow_C3>(sparse.events.Count);
        var payloads = new List<SqlSparseEventPayloadRow>();

        foreach (var ev in sparse.events)
        {
            events.Add(new SqlSparseEventRow_C3
            {
                event_id = ev.event_id,
                user_id = ev.user_id,
                event_type = ev.event_type,
                timestamp = ev.timestamp
            });
            foreach (var kv in ev.payload)
            {
                payloads.Add(new SqlSparseEventPayloadRow
                {
                    event_id = ev.event_id,
                    payload_key = kv.Key,
                    payload_value = kv.Value?.ToString() ?? string.Empty
                });
            }
        }

        using (var db = OpenFreshDb(targetPath))
        {
            db.BeginTransaction();
            db.CreateTable<SqlSparseEventRow_C3>();
            db.CreateTable<SqlSparseEventPayloadRow>();
            db.InsertAll(events);
            db.InsertAll(payloads);
            db.CreateIndex("idx_sparse_c3_user_type", "Sparse_Event_C3", new[] { "user_id", "event_type" }, false);
            db.Commit();
        }
    }
    #endregion

    #region SQLite Helper Table Definitions
    [Table("Doc_Item_C1")]
    private class SqlDocItemRow_C1
    {
        [PrimaryKey] public int item_id { get; set; }
        public string name { get; set; }
        public string item_type { get; set; }
        public int rarity { get; set; }
        public int atk { get; set; }
        public int price { get; set; }
    }

    [Table("Doc_Item_Tag_C1")]
    private class SqlDocItemTagRow_C1
    {
        [Indexed] public int item_id { get; set; }
        public string tag_name { get; set; }
    }

    [Table("Doc_Char_C2")]
    private class SqlDocCharRow_C2
    {
        [PrimaryKey] public int character_id { get; set; }
        public string name { get; set; }
        public int level { get; set; }
        public int cur_hp { get; set; }
    }

    [Table("Doc_Inv_C2")]
    private class SqlDocInvRow_C2
    {
        [PrimaryKey] public int instance_id { get; set; }
        [Indexed] public int character_id { get; set; }
        public int item_id { get; set; }
        public string item_name { get; set; }
        public int rarity { get; set; }
        public int quantity { get; set; }
    }

    [Table("Doc_Inv_Tag_C2")]
    private class SqlDocInvTagRow_C2
    {
        [Indexed] public int instance_id { get; set; }
        public string tag_name { get; set; }
    }

    [Table("Doc_User_C3")]
    private class SqlDocUserRow_C3
    {
        [PrimaryKey] public int user_id { get; set; }
        public string account_name { get; set; }
        public long created_at { get; set; }
    }

    [Table("Doc_Char_C3")]
    private class SqlDocCharRow_C3
    {
        [PrimaryKey] public int character_id { get; set; }
        [Indexed] public int user_id { get; set; }
        public string name { get; set; }
        [Indexed] public int guild_id { get; set; }
    }

    [Table("Doc_Inv_C3")]
    private class SqlDocInvRow_C3
    {
        [PrimaryKey] public int instance_id { get; set; }
        [Indexed] public int character_id { get; set; }
        public int item_id { get; set; }
        public string item_name { get; set; }
        public int rarity { get; set; }
        public int durability { get; set; }
    }

    [Table("Doc_Eff_C3")]
    private class SqlDocEffRow_C3
    {
        [PrimaryKey] public int effect_id { get; set; }
        [Indexed] public int instance_id { get; set; }
        public string effect_type { get; set; }
        public int value { get; set; }
    }

    [Table("Sparse_Item_C1")]
    private class SqlSparseItemRow_C1
    {
        [PrimaryKey] public int item_id { get; set; }
        public string item_type { get; set; }
        public string name { get; set; }
    }

    [Table("Sparse_Entity_C2")]
    private class SqlSparseEntityRow_C2
    {
        [PrimaryKey] public int entity_id { get; set; }
        public string category { get; set; }
        public string name { get; set; }
    }

    [Table("Sparse_Attr")]
    private class SqlSparseAttrRow
    {
        [Indexed] public int owner_id { get; set; }
        [Indexed] public string attr_key { get; set; }
        public string attr_value { get; set; }
    }

    [Table("Sparse_Meta")]
    private class SqlSparseMetaRow
    {
        [Indexed] public int owner_id { get; set; }
        public string meta_key { get; set; }
        public string meta_value { get; set; }
    }

    [Table("Sparse_Event_C3")]
    private class SqlSparseEventRow_C3
    {
        [PrimaryKey] public string event_id { get; set; }
        [Indexed] public int user_id { get; set; }
        public string event_type { get; set; }
        public long timestamp { get; set; }
    }

    [Table("Sparse_Event_Payload")]
    private class SqlSparseEventPayloadRow
    {
        [Indexed] public string event_id { get; set; }
        public string payload_key { get; set; }
        public string payload_value { get; set; }
    }
    #endregion
}

#endregion

#region LiteDB Format Builder (Document NoSQL)

public class LiteDbFormatBuilder : FormatBuilderBase
{
    private const int InsertBatchSize = 20000;

    public override string FormatName => "LiteDB";
    protected override string GetExtension() => "db";

    public LiteDbFormatBuilder()
    {
        // Relational
        Register(DataShape.Relational, DataComplexity.C1_Low, BuildRelationalC1);
        Register(DataShape.Relational, DataComplexity.C2_Mid, BuildRelationalC2);
        Register(DataShape.Relational, DataComplexity.C3_High, BuildRelationalC3);

        // Document (네이티브 임베디드 문서 저장)
        Register(DataShape.Document, DataComplexity.C1_Low, BuildDocumentC1);
        Register(DataShape.Document, DataComplexity.C2_Mid, BuildDocumentC2);
        Register(DataShape.Document, DataComplexity.C3_High, BuildDocumentC3);

        // Sparse (BSON 동적 딕셔너리 저장)
        Register(DataShape.Sparse, DataComplexity.C1_Low, BuildSparseC1);
        Register(DataShape.Sparse, DataComplexity.C2_Mid, BuildSparseC2);
        Register(DataShape.Sparse, DataComplexity.C3_High, BuildSparseC3);
    }

    private static LiteDatabase OpenFreshDb(string targetPath)
    {
        if (File.Exists(targetPath)) File.Delete(targetPath);
        return new LiteDatabase($"Filename={targetPath};");
    }

    #region Relational
    private void BuildRelationalC1(object container, string targetPath)
    {
        if (!(container is RelDataSet_C1 c1)) return;
        using (var db = OpenFreshDb(targetPath))
        {
            var items = db.GetCollection<RelMasterItem_C1>("items");
            var characters = db.GetCollection<RelRuntimeCharacter_C1>("characters");
            items.InsertBulk(c1.items, InsertBatchSize);
            items.EnsureIndex(item => item.item_id, true);
            items.EnsureIndex(item => item.rarity);
            items.EnsureIndex(item => item.atk);
            characters.InsertBulk(c1.characters, InsertBatchSize);
        }
    }

    private void BuildRelationalC2(object container, string targetPath)
    {
        if (!(container is RelDataSet_C2 c2)) return;
        using (var db = OpenFreshDb(targetPath))
        {
            var items = db.GetCollection<RelMasterItem_C2>("items");
            var tags = db.GetCollection<RelItemTag_C2>("tags");
            var characters = db.GetCollection<RelRuntimeCharacter_C2>("characters");
            var inventories = db.GetCollection<RelRuntimeInventory_C2>("inventories");
            items.InsertBulk(c2.items, InsertBatchSize);
            tags.InsertBulk(c2.tags, InsertBatchSize);
            characters.InsertBulk(c2.characters, InsertBatchSize);
            inventories.InsertBulk(c2.inventories, InsertBatchSize);
            items.EnsureIndex(item => item.item_id, true);
            items.EnsureIndex(item => item.req_level);
            items.EnsureIndex(item => item.rarity);
            characters.EnsureIndex(ch => ch.character_id, true);
            inventories.EnsureIndex(inv => inv.character_id);
        }
    }

    private void BuildRelationalC3(object container, string targetPath)
    {
        if (!(container is RelDataSet_C3 c3)) return;
        using (var db = OpenFreshDb(targetPath))
        {
            var users = db.GetCollection<RelSystemUser_C3>("users");
            var characters = db.GetCollection<RelRuntimeCharacter_C3>("characters");
            var items = db.GetCollection<RelMasterItem_C3>("items");
            var effects = db.GetCollection<RelMasterEffect_C3>("effects");
            var links = db.GetCollection<RelItemEffectLink_C3>("links");
            var inventories = db.GetCollection<RelRuntimeInventory_C3>("inventories");
            users.InsertBulk(c3.users, InsertBatchSize);
            characters.InsertBulk(c3.characters, InsertBatchSize);
            items.InsertBulk(c3.items, InsertBatchSize);
            effects.InsertBulk(c3.effects, InsertBatchSize);
            links.InsertBulk(c3.links, InsertBatchSize);
            inventories.InsertBulk(c3.inventories, InsertBatchSize);
            users.EnsureIndex(u => u.user_id, true);
            characters.EnsureIndex(ch => ch.character_id, true);
            characters.EnsureIndex(ch => ch.user_id);
            items.EnsureIndex(item => item.item_id, true);
            items.EnsureIndex(item => item.rarity);
            effects.EnsureIndex(eff => eff.effect_id, true);
            links.EnsureIndex(link => link.item_id);
            inventories.EnsureIndex(inv => inv.character_id);
        }
    }
    #endregion

    #region Document
    private void BuildDocumentC1(object container, string targetPath)
    {
        if (!(container is DocDataSet_C1 doc)) return;
        using (var db = OpenFreshDb(targetPath))
        {
            var items = db.GetCollection<DocItem_C1>("items");
            items.InsertBulk(doc.items, InsertBatchSize);
            items.EnsureIndex(x => x.item_id, true);
            items.EnsureIndex(x => x.rarity);
            items.EnsureIndex(x => x.item_type);
        }
    }

    private void BuildDocumentC2(object container, string targetPath)
    {
        if (!(container is DocDataSet_C2 doc)) return;
        using (var db = OpenFreshDb(targetPath))
        {
            var characters = db.GetCollection<DocCharacter_C2>("characters");
            characters.InsertBulk(doc.characters, InsertBatchSize);
            characters.EnsureIndex(x => x.character_id, true);
            characters.EnsureIndex(x => x.level);
        }
    }

    private void BuildDocumentC3(object container, string targetPath)
    {
        if (!(container is DocDataSet_C3 doc)) return;
        using (var db = OpenFreshDb(targetPath))
        {
            var users = db.GetCollection<DocUser_C3>("users");
            users.InsertBulk(doc.users, InsertBatchSize);
            users.EnsureIndex(x => x.user_id, true);
            users.EnsureIndex(x => x.account_name);
        }
    }
    #endregion

    #region Sparse
    private void BuildSparseC1(object container, string targetPath)
    {
        if (!(container is SparseDataSet_C1 sparse)) return;
        using (var db = OpenFreshDb(targetPath))
        {
            var items = db.GetCollection<SparseItem_C1>("items");
            items.InsertBulk(sparse.items, InsertBatchSize);
            items.EnsureIndex(x => x.item_id, true);
            items.EnsureIndex(x => x.item_type);
        }
    }

    private void BuildSparseC2(object container, string targetPath)
    {
        if (!(container is SparseDataSet_C2 sparse)) return;
        using (var db = OpenFreshDb(targetPath))
        {
            var entities = db.GetCollection<SparseEntity_C2>("entities");
            entities.InsertBulk(sparse.entities, InsertBatchSize);
            entities.EnsureIndex(x => x.entity_id, true);
            entities.EnsureIndex(x => x.category);
        }
    }

    private void BuildSparseC3(object container, string targetPath)
    {
        if (!(container is SparseDataSet_C3 sparse)) return;
        using (var db = OpenFreshDb(targetPath))
        {
            var events = db.GetCollection<SparseEventLog_C3>("events");
            events.InsertBulk(sparse.events, InsertBatchSize);
            events.EnsureIndex(x => x.event_id, true);
            events.EnsureIndex(x => x.user_id);
            events.EnsureIndex(x => x.event_type);
        }
    }
    #endregion
}

#endregion

#region RocksDB Format Builder (Key-Value Store)

public class RocksDbFormatBuilder : FormatBuilderBase
{
    public override string FormatName => "RocksDB";
    protected override string GetExtension() => string.Empty;

    public RocksDbFormatBuilder()
    {
        // Relational
        Register(DataShape.Relational, DataComplexity.C1_Low, BuildRelationalC1);
        Register(DataShape.Relational, DataComplexity.C2_Mid, BuildRelationalC2);
        Register(DataShape.Relational, DataComplexity.C3_High, BuildRelationalC3);

        // Document
        Register(DataShape.Document, DataComplexity.C1_Low, BuildDocumentC1);
        Register(DataShape.Document, DataComplexity.C2_Mid, BuildDocumentC2);
        Register(DataShape.Document, DataComplexity.C3_High, BuildDocumentC3);

        // Sparse
        Register(DataShape.Sparse, DataComplexity.C1_Low, BuildSparseC1);
        Register(DataShape.Sparse, DataComplexity.C2_Mid, BuildSparseC2);
        Register(DataShape.Sparse, DataComplexity.C3_High, BuildSparseC3);
    }

    public override string GetTargetPath(string formatShapeScaleDir, BenchmarkDataSpec spec)
    {
        string dbFolder = Path.Combine(formatShapeScaleDir, spec.BaseFileName);
        if (!Directory.Exists(dbFolder)) Directory.CreateDirectory(dbFolder);
        return dbFolder;
    }

    private static RocksDb OpenFreshDb(string dbFolder)
    {
        if (Directory.Exists(dbFolder)) Directory.Delete(dbFolder, true);
        var dbOptions = new DbOptions().SetCreateIfMissing(true);
        return RocksDb.Open(dbOptions, dbFolder);
    }

    private static byte[] WriteBinary(Action<BinaryWriter> writeAction)
    {
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms, Encoding.UTF8))
        {
            writeAction(writer);
            return ms.ToArray();
        }
    }

    #region Relational
    private void BuildRelationalC1(object container, string dbFolder)
    {
        if (!(container is RelDataSet_C1 c1)) return;
        using (var db = OpenFreshDb(dbFolder))
        using (var batch = new WriteBatch())
        {
            foreach (var item in c1.items)
            {
                var val = WriteBinary(w =>
                {
                    w.Write(item.item_id);
                    w.Write(item.name ?? string.Empty);
                    w.Write(item.item_type ?? string.Empty);
                    w.Write(item.rarity);
                    w.Write(item.atk);
                    w.Write(item.price);
                });
                batch.Put(Encoding.UTF8.GetBytes($"item_{item.item_id}"), val);
            }
            foreach (var ch in c1.characters)
            {
                var val = WriteBinary(w =>
                {
                    w.Write(ch.character_id);
                    w.Write(ch.name ?? string.Empty);
                    w.Write(ch.level);
                    w.Write(ch.hp);
                    w.Write(ch.pos_x);
                    w.Write(ch.pos_y);
                });
                batch.Put(Encoding.UTF8.GetBytes($"char_{ch.character_id}"), val);
            }
            db.Write(batch);
        }
    }

    private void BuildRelationalC2(object container, string dbFolder)
    {
        if (!(container is RelDataSet_C2 c2)) return;
        using (var db = OpenFreshDb(dbFolder))
        using (var batch = new WriteBatch())
        {
            foreach (var item in c2.items)
            {
                var val = WriteBinary(w =>
                {
                    w.Write(item.item_id);
                    w.Write(item.item_code ?? string.Empty);
                    w.Write(item.name ?? string.Empty);
                    w.Write(item.item_type ?? string.Empty);
                    w.Write(item.rarity);
                    w.Write(item.req_level);
                    w.Write(item.base_atk);
                });
                batch.Put(Encoding.UTF8.GetBytes($"item_{item.item_id}"), val);
            }
            foreach (var tag in c2.tags)
            {
                var val = WriteBinary(w =>
                {
                    w.Write(tag.item_id);
                    w.Write(tag.tag_name ?? string.Empty);
                });
                batch.Put(Encoding.UTF8.GetBytes($"tag_{tag.item_id}_{tag.tag_name}"), val);
            }
            foreach (var ch in c2.characters)
            {
                var val = WriteBinary(w =>
                {
                    w.Write(ch.character_id);
                    w.Write(ch.name ?? string.Empty);
                    w.Write(ch.level ?? string.Empty);
                    w.Write(ch.cur_hp);
                    w.Write(ch.pos_x);
                    w.Write(ch.pos_y);
                });
                batch.Put(Encoding.UTF8.GetBytes($"char_{ch.character_id}"), val);
            }
            foreach (var inv in c2.inventories)
            {
                var val = WriteBinary(w =>
                {
                    w.Write(inv.instance_id);
                    w.Write(inv.character_id);
                    w.Write(inv.item_id);
                    w.Write(inv.quantity);
                });
                batch.Put(Encoding.UTF8.GetBytes($"inv_{inv.instance_id}"), val);
                batch.Put(Encoding.UTF8.GetBytes($"inv_by_char_{inv.character_id}_{inv.instance_id}"), val);
            }
            db.Write(batch);
        }
    }

    private void BuildRelationalC3(object container, string dbFolder)
    {
        if (!(container is RelDataSet_C3 c3)) return;
        using (var db = OpenFreshDb(dbFolder))
        using (var batch = new WriteBatch())
        {
            foreach (var user in c3.users)
            {
                var val = WriteBinary(w =>
                {
                    w.Write(user.user_id);
                    w.Write(user.account_name ?? string.Empty);
                    w.Write(user.created_at);
                });
                batch.Put(Encoding.UTF8.GetBytes($"user_{user.user_id}"), val);
            }
            foreach (var ch in c3.characters)
            {
                var val = WriteBinary(w =>
                {
                    w.Write(ch.character_id);
                    w.Write(ch.user_id);
                    w.Write(ch.name ?? string.Empty);
                    w.Write(ch.guild_id);
                });
                batch.Put(Encoding.UTF8.GetBytes($"char_{ch.character_id}"), val);
                batch.Put(Encoding.UTF8.GetBytes($"char_by_user_{ch.user_id}_{ch.character_id}"), val);
            }
            foreach (var item in c3.items)
            {
                var val = WriteBinary(w =>
                {
                    w.Write(item.item_id);
                    w.Write(item.name ?? string.Empty);
                    w.Write(item.rarity);
                });
                batch.Put(Encoding.UTF8.GetBytes($"item_{item.item_id}"), val);
            }
            foreach (var eff in c3.effects)
            {
                var val = WriteBinary(w =>
                {
                    w.Write(eff.effect_id);
                    w.Write(eff.effect_type ?? string.Empty);
                    w.Write(eff.value);
                });
                batch.Put(Encoding.UTF8.GetBytes($"effect_{eff.effect_id}"), val);
            }
            foreach (var link in c3.links)
            {
                var val = WriteBinary(w =>
                {
                    w.Write(link.item_id);
                    w.Write(link.effect_id);
                });
                batch.Put(Encoding.UTF8.GetBytes($"link_{link.item_id}_{link.effect_id}"), val);
            }
            foreach (var inv in c3.inventories)
            {
                var val = WriteBinary(w =>
                {
                    w.Write(inv.instance_id);
                    w.Write(inv.character_id);
                    w.Write(inv.item_id);
                    w.Write(inv.durability);
                });
                batch.Put(Encoding.UTF8.GetBytes($"inv_{inv.instance_id}"), val);
                batch.Put(Encoding.UTF8.GetBytes($"inv_by_char_{inv.character_id}_{inv.instance_id}"), val);
            }
            db.Write(batch);
        }
    }
    #endregion

    #region Document
    private void BuildDocumentC1(object container, string dbFolder)
    {
        if (!(container is DocDataSet_C1 doc)) return;
        using (var db = OpenFreshDb(dbFolder))
        using (var batch = new WriteBatch())
        {
            foreach (var item in doc.items)
            {
                byte[] val = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(item));
                batch.Put(Encoding.UTF8.GetBytes($"doc_item_{item.item_id}"), val);
            }
            db.Write(batch);
        }
    }

    private void BuildDocumentC2(object container, string dbFolder)
    {
        if (!(container is DocDataSet_C2 doc)) return;
        using (var db = OpenFreshDb(dbFolder))
        using (var batch = new WriteBatch())
        {
            foreach (var ch in doc.characters)
            {
                byte[] val = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(ch));
                batch.Put(Encoding.UTF8.GetBytes($"doc_char_{ch.character_id}"), val);
            }
            db.Write(batch);
        }
    }

    private void BuildDocumentC3(object container, string dbFolder)
    {
        if (!(container is DocDataSet_C3 doc)) return;
        using (var db = OpenFreshDb(dbFolder))
        using (var batch = new WriteBatch())
        {
            foreach (var u in doc.users)
            {
                byte[] val = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(u));
                batch.Put(Encoding.UTF8.GetBytes($"doc_user_{u.user_id}"), val);
            }
            db.Write(batch);
        }
    }
    #endregion

    #region Sparse
    private void BuildSparseC1(object container, string dbFolder)
    {
        if (!(container is SparseDataSet_C1 sparse)) return;
        using (var db = OpenFreshDb(dbFolder))
        using (var batch = new WriteBatch())
        {
            foreach (var item in sparse.items)
            {
                byte[] val = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(item));
                batch.Put(Encoding.UTF8.GetBytes($"sparse_item_{item.item_id}"), val);
                batch.Put(Encoding.UTF8.GetBytes($"sparse_item_by_type_{item.item_type}_{item.item_id}"), val);
            }
            db.Write(batch);
        }
    }

    private void BuildSparseC2(object container, string dbFolder)
    {
        if (!(container is SparseDataSet_C2 sparse)) return;
        using (var db = OpenFreshDb(dbFolder))
        using (var batch = new WriteBatch())
        {
            foreach (var ent in sparse.entities)
            {
                byte[] val = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(ent));
                batch.Put(Encoding.UTF8.GetBytes($"sparse_ent_{ent.entity_id}"), val);
                batch.Put(Encoding.UTF8.GetBytes($"sparse_ent_by_cat_{ent.category}_{ent.entity_id}"), val);
            }
            db.Write(batch);
        }
    }

    private void BuildSparseC3(object container, string dbFolder)
    {
        if (!(container is SparseDataSet_C3 sparse)) return;
        using (var db = OpenFreshDb(dbFolder))
        using (var batch = new WriteBatch())
        {
            foreach (var ev in sparse.events)
            {
                byte[] val = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(ev));
                batch.Put(Encoding.UTF8.GetBytes($"sparse_ev_{ev.event_id}"), val);
                batch.Put(Encoding.UTF8.GetBytes($"sparse_ev_by_user_{ev.user_id}_{ev.timestamp}_{ev.event_id}"), val);
            }
            db.Write(batch);
        }
    }
    #endregion
}

#endregion

#region Non-DB Format Builders (별도 최적화 예정 - 현재 비워둠)

public class FlatBuffersFormatBuilder : FormatBuilderBase
{
    public override string FormatName => "FlatBuffers";
    protected override string GetExtension() => "bytes";

    public FlatBuffersFormatBuilder()
    {
        // TODO: 별도 최적화 후 Shape/Complexity 핸들러 등록 예정
    }
}

public class RawBinaryFormatBuilder : FormatBuilderBase
{
    public override string FormatName => "Binary";
    protected override string GetExtension() => "bytes";

    public RawBinaryFormatBuilder()
    {
        // TODO: 별도 최적화 후 Shape/Complexity 핸들러 등록 예정
    }
}

public class MessagePackFormatBuilder : FormatBuilderBase
{
    public override string FormatName => "MessagePack";
    public override bool IsLegacyFormat => true;
    protected override string GetExtension() => "mpk";

    public MessagePackFormatBuilder()
    {
        // TODO: 별도 최적화 후 Shape/Complexity 핸들러 등록 예정
    }
}

public class JsonFormatBuilder : FormatBuilderBase
{
    public override string FormatName => "JSON";
    public override bool IsLegacyFormat => true;
    protected override string GetExtension() => "json";

    public JsonFormatBuilder()
    {
        // TODO: 별도 최적화 후 Shape/Complexity 핸들러 등록 예정
    }
}

#endregion