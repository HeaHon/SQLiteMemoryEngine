using GameData.Schema;
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

// SQLite 포맷 빌더
public class SqliteFormatBuilder : IDataFormatBuilder
{
    public void Build(object container, string targetPath, string complexity)
    {
        if (File.Exists(targetPath)) File.Delete(targetPath);

        using (var db = new SQLiteConnection(targetPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create))
        {
            db.BeginTransaction();
            if (complexity == "C1_Low" && container is DataSet_C1 c1)
            {
                db.CreateTable<Master_Item_C1>();
                db.CreateTable<Runtime_Character_C1>();
                db.InsertAll(c1.items);
                db.InsertAll(c1.characters);
            }
            else if (complexity == "C2_Mid" && container is DataSet_C2 c2)
            {
                db.CreateTable<Master_Item_C2>();
                db.CreateTable<Item_Tag_C2>();
                db.CreateTable<Runtime_Character_C2>();
                db.CreateTable<Runtime_Inventory_C2>();
                db.InsertAll(c2.items);
                db.InsertAll(c2.tags);
                db.InsertAll(c2.characters);
                db.InsertAll(c2.inventories);
            }
            else if (complexity == "C3_High" && container is DataSet_C3 c3)
            {
                db.CreateTable<System_User_C3>();
                db.CreateTable<Runtime_Character_C3>();
                db.CreateTable<Master_Item_C3>();
                db.CreateTable<Master_Effect_C3>();
                db.CreateTable<Item_Effect_Link_C3>();
                db.CreateTable<Runtime_Inventory_C3>();
                db.InsertAll(c3.users);
                db.InsertAll(c3.characters);
                db.InsertAll(c3.items);
                db.InsertAll(c3.effects);
                db.InsertAll(c3.links);
                db.InsertAll(c3.inventories);
            }
            db.Commit();
        }
    }
}

// LiteDB 포맷 빌더
public class LiteDbFormatBuilder : IDataFormatBuilder
{
    public void Build(object container, string targetPath, string complexity)
    {
        if (File.Exists(targetPath))
        {
            try { File.Delete(targetPath); } catch { }
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
        }

        string connStr = $"Filename={targetPath};";

        using (var db = new LiteDatabase(connStr))
        {
            if (container is DataSet_C1 c1)
            {
                db.GetCollection<Master_Item_C1>("items").InsertBulk(c1.items);
                db.GetCollection<Runtime_Character_C1>("characters").InsertBulk(c1.characters);
            }
            else if (container is DataSet_C2 c2)
            {
                db.GetCollection<Master_Item_C2>("items").InsertBulk(c2.items);
                db.GetCollection<Item_Tag_C2>("tags").InsertBulk(c2.tags);
                db.GetCollection<Runtime_Character_C2>("characters").InsertBulk(c2.characters);
                db.GetCollection<Runtime_Inventory_C2>("inventories").InsertBulk(c2.inventories);
            }
            else if (container is DataSet_C3 c3)
            {
                db.GetCollection<System_User_C3>("users").InsertBulk(c3.users);
                db.GetCollection<Runtime_Character_C3>("characters").InsertBulk(c3.characters);
                db.GetCollection<Master_Item_C3>("items").InsertBulk(c3.items);
                db.GetCollection<Master_Effect_C3>("effects").InsertBulk(c3.effects);
                db.GetCollection<Item_Effect_Link_C3>("links").InsertBulk(c3.links);
                db.GetCollection<Runtime_Inventory_C3>("inventories").InsertBulk(c3.inventories);
            }
        }
    }
}

// RocksDB (KV Store) 포맷 빌더
public class RocksDbFormatBuilder : IDataFormatBuilder
{
    public void Build(object container, string dbFolder, string complexity)
    {
        if (Directory.Exists(dbFolder))
        {
            Directory.Delete(dbFolder, true);
        }

        // 1. RocksDB 엔진 옵션 설정
        var dbOptions = new DbOptions().SetCreateIfMissing(true);
        var msgPackOptions = MessagePack.Resolvers.StandardResolver.Options;

        using (var db = RocksDb.Open(dbOptions, dbFolder))
        using (var writeBatch = new WriteBatch())
        {
            if (container is DataSet_C1 c1)
            {
                AddBatch(writeBatch, c1.items, item => $"item_{item.item_id}", msgPackOptions);
                AddBatch(writeBatch, c1.characters, ch => $"char_{ch.character_id}", msgPackOptions);
            }
            else if (container is DataSet_C2 c2)
            {
                AddBatch(writeBatch, c2.items, item => $"item_{item.item_id}", msgPackOptions);
                AddBatch(writeBatch, c2.tags, tag => $"tag_{tag.item_id}_{tag.tag_name}", msgPackOptions);
                AddBatch(writeBatch, c2.characters, ch => $"char_{ch.character_id}", msgPackOptions);
                AddBatch(writeBatch, c2.inventories, inv => $"inv_{inv.instance_id}", msgPackOptions);
            }
            else if (container is DataSet_C3 c3)
            {
                AddBatch(writeBatch, c3.users, user => $"user_{user.user_id}", msgPackOptions);
                AddBatch(writeBatch, c3.characters, ch => $"char_{ch.character_id}", msgPackOptions);
                AddBatch(writeBatch, c3.items, item => $"item_{item.item_id}", msgPackOptions);
                AddBatch(writeBatch, c3.effects, eff => $"effect_{eff.effect_id}", msgPackOptions);
                AddBatch(writeBatch, c3.links, link => $"link_{link.item_id}_{link.effect_id}", msgPackOptions);
                AddBatch(writeBatch, c3.inventories, inv => $"inv_{ inv.instance_id}", msgPackOptions);
            }

            // 일괄 배치 커밋 (디스크 쓰기 최적화)
            db.Write(writeBatch);
        }
    }

    private void AddBatch<T>(WriteBatch batch, IEnumerable<T> items, Func<T, string> keySelector, MessagePackSerializerOptions options)
    {
        foreach (var item in items)
        {
            string keyStr = keySelector(item);
            byte[] keyBytes = Encoding.UTF8.GetBytes(keyStr);
            byte[] valueBytes = MessagePackSerializer.Serialize(item, options);

            // RocksDB WriteBatch.Put 오버로드 규격에 맞게 바이트 배열 전달
            batch.Put(keyBytes, valueBytes);
        }
    }
}

// FlatBuffers 포맷 빌더
public class FlatBuffersFormatBuilder : IDataFormatBuilder
{
    public void Build(object container, string filePath, string complexity)
    {
        var builder = new FlatBufferBuilder(1024);

        if (complexity == "C1_Low" && container is DataSet_C1 c1)
        {
            var itemOffsets = new Offset<Item_C1_Table>[c1.items.Count];
            for (int i = 0; i < c1.items.Count; i++)
            {
                var item = c1.items[i];
                var nameOff = builder.CreateString(item.name);
                var typeOff = builder.CreateString(item.item_type);

                Item_C1_Table.StartItem_C1_Table(builder);
                Item_C1_Table.AddItemId(builder, item.item_id);
                Item_C1_Table.AddName(builder, nameOff);
                Item_C1_Table.AddItemType(builder, typeOff);
                Item_C1_Table.AddRarity(builder, item.rarity);
                Item_C1_Table.AddAtk(builder, item.atk);
                Item_C1_Table.AddPrice(builder, item.price);
                itemOffsets[i] = Item_C1_Table.EndItem_C1_Table(builder);
            }
            var itemsVec = DataSet_C1_Schema.CreateItemsVector(builder, itemOffsets);

            var charOffsets = new Offset<Character_C1_Table>[c1.characters.Count];
            for (int i = 0; i < c1.characters.Count; i++)
            {
                var ch = c1.characters[i];
                var nameOff = builder.CreateString(ch.name);

                Character_C1_Table.StartCharacter_C1_Table(builder);
                Character_C1_Table.AddCharacterId(builder, ch.character_id);
                Character_C1_Table.AddName(builder, nameOff);
                Character_C1_Table.AddLevel(builder, ch.level);
                Character_C1_Table.AddHp(builder, ch.hp);
                Character_C1_Table.AddPosX(builder, ch.pos_x);
                Character_C1_Table.AddPosY(builder, ch.pos_y);
                charOffsets[i] = Character_C1_Table.EndCharacter_C1_Table(builder);
            }
            var charsVec = DataSet_C1_Schema.CreateCharactersVector(builder, charOffsets);

            DataSet_C1_Schema.StartDataSet_C1_Schema(builder);
            DataSet_C1_Schema.AddItems(builder, itemsVec);
            DataSet_C1_Schema.AddCharacters(builder, charsVec);
            var c1SchemaOffset = DataSet_C1_Schema.EndDataSet_C1_Schema(builder);

            MasterGameDataSet.StartMasterGameDataSet(builder);
            MasterGameDataSet.AddC1Data(builder, c1SchemaOffset);
            var root = MasterGameDataSet.EndMasterGameDataSet(builder);

            MasterGameDataSet.FinishMasterGameDataSetBuffer(builder, root);
            File.WriteAllBytes(filePath, builder.SizedByteArray());
        }
        else if (complexity == "C2_Mid" && container is DataSet_C2 c2)
        {
            var itemOffsets = new Offset<Item_C2_Table>[c2.items.Count];
            for (int i = 0; i < c2.items.Count; i++)
            {
                var item = c2.items[i];
                var codeOff = builder.CreateString(item.item_code);
                var nameOff = builder.CreateString(item.name);
                var typeOff = builder.CreateString(item.item_type);

                Item_C2_Table.StartItem_C2_Table(builder);
                Item_C2_Table.AddItemId(builder, item.item_id);
                Item_C2_Table.AddItemCode(builder, codeOff);
                Item_C2_Table.AddName(builder, nameOff);
                Item_C2_Table.AddItemType(builder, typeOff);
                Item_C2_Table.AddRarity(builder, item.rarity);
                Item_C2_Table.AddReqLevel(builder, item.req_level);
                Item_C2_Table.AddBaseAtk(builder, item.base_atk);
                itemOffsets[i] = Item_C2_Table.EndItem_C2_Table(builder);
            }
            var itemsVec = DataSet_C2_Schema.CreateItemsVector(builder, itemOffsets);

            var tagOffsets = new Offset<ItemTag_C2_Table>[c2.tags.Count];
            for (int i = 0; i < c2.tags.Count; i++)
            {
                var tag = c2.tags[i];
                var nameOff = builder.CreateString(tag.tag_name);

                ItemTag_C2_Table.StartItemTag_C2_Table(builder);
                ItemTag_C2_Table.AddItemId(builder, tag.item_id);
                ItemTag_C2_Table.AddTagName(builder, nameOff);
                tagOffsets[i] = ItemTag_C2_Table.EndItemTag_C2_Table(builder);
            }
            var tagsVec = DataSet_C2_Schema.CreateTagsVector(builder, tagOffsets);

            var charOffsets = new Offset<Character_C2_Table>[c2.characters.Count];
            for (int i = 0; i < c2.characters.Count; i++)
            {
                var ch = c2.characters[i];
                var nameOff = builder.CreateString(ch.name);
                var levelOff = builder.CreateString(ch.level);

                Character_C2_Table.StartCharacter_C2_Table(builder);
                Character_C2_Table.AddCharacterId(builder, ch.character_id);
                Character_C2_Table.AddName(builder, nameOff);
                Character_C2_Table.AddLevel(builder, levelOff);
                Character_C2_Table.AddCurHp(builder, ch.cur_hp);
                Character_C2_Table.AddPosX(builder, ch.pos_x);
                Character_C2_Table.AddPosY(builder, ch.pos_y);
                charOffsets[i] = Character_C2_Table.EndCharacter_C2_Table(builder);
            }
            var charsVec = DataSet_C2_Schema.CreateCharactersVector(builder, charOffsets);

            var invOffsets = new Offset<Inventory_C2_Table>[c2.inventories.Count];
            for (int i = 0; i < c2.inventories.Count; i++)
            {
                var inv = c2.inventories[i];
                Inventory_C2_Table.StartInventory_C2_Table(builder);
                Inventory_C2_Table.AddInstanceId(builder, inv.instance_id);
                Inventory_C2_Table.AddCharacterId(builder, inv.character_id);
                Inventory_C2_Table.AddItemId(builder, inv.item_id);
                Inventory_C2_Table.AddQuantity(builder, inv.quantity);
                invOffsets[i] = Inventory_C2_Table.EndInventory_C2_Table(builder);
            }
            var invsVec = DataSet_C2_Schema.CreateInventoriesVector(builder, invOffsets);

            DataSet_C2_Schema.StartDataSet_C2_Schema(builder);
            DataSet_C2_Schema.AddItems(builder, itemsVec);
            DataSet_C2_Schema.AddTags(builder, tagsVec);
            DataSet_C2_Schema.AddCharacters(builder, charsVec);
            DataSet_C2_Schema.AddInventories(builder, invsVec);
            var c2SchemaOffset = DataSet_C2_Schema.EndDataSet_C2_Schema(builder);

            MasterGameDataSet.StartMasterGameDataSet(builder);
            MasterGameDataSet.AddC2Data(builder, c2SchemaOffset);
            var root = MasterGameDataSet.EndMasterGameDataSet(builder);

            MasterGameDataSet.FinishMasterGameDataSetBuffer(builder, root);
            File.WriteAllBytes(filePath, builder.SizedByteArray());
        }
        else if (complexity == "C3_High" && container is DataSet_C3 c3)
        {
            var userOffsets = new Offset<User_C3_Table>[c3.users.Count];
            for (int i = 0; i < c3.users.Count; i++)
            {
                var user = c3.users[i];
                var nameOff = builder.CreateString(user.account_name);

                User_C3_Table.StartUser_C3_Table(builder);
                User_C3_Table.AddUserId(builder, user.user_id);
                User_C3_Table.AddAccountName(builder, nameOff);
                User_C3_Table.AddCreatedAt(builder, user.created_at);
                userOffsets[i] = User_C3_Table.EndUser_C3_Table(builder);
            }
            var usersVec = DataSet_C3_Schema.CreateUsersVector(builder, userOffsets);

            var charOffsets = new Offset<Character_C3_Table>[c3.characters.Count];
            for (int i = 0; i < c3.characters.Count; i++)
            {
                var ch = c3.characters[i];
                var nameOff = builder.CreateString(ch.name);

                Character_C3_Table.StartCharacter_C3_Table(builder);
                Character_C3_Table.AddCharacterId(builder, ch.character_id);
                Character_C3_Table.AddUserId(builder, ch.user_id);
                Character_C3_Table.AddName(builder, nameOff);
                Character_C3_Table.AddGuildId(builder, ch.guild_id);
                charOffsets[i] = Character_C3_Table.EndCharacter_C3_Table(builder);
            }
            var charsVec = DataSet_C3_Schema.CreateCharactersVector(builder, charOffsets);

            var itemOffsets = new Offset<Item_C3_Table>[c3.items.Count];
            for (int i = 0; i < c3.items.Count; i++)
            {
                var item = c3.items[i];
                var nameOff = builder.CreateString(item.name);

                Item_C3_Table.StartItem_C3_Table(builder);
                Item_C3_Table.AddItemId(builder, item.item_id);
                Item_C3_Table.AddName(builder, nameOff);
                Item_C3_Table.AddRarity(builder, item.rarity);
                itemOffsets[i] = Item_C3_Table.EndItem_C3_Table(builder);
            }
            var itemsVec = DataSet_C3_Schema.CreateItemsVector(builder, itemOffsets);

            var effectOffsets = new Offset<Effect_C3_Table>[c3.effects.Count];
            for (int i = 0; i < c3.effects.Count; i++)
            {
                var eff = c3.effects[i];
                var typeOff = builder.CreateString(eff.effect_type);

                Effect_C3_Table.StartEffect_C3_Table(builder);
                Effect_C3_Table.AddEffectId(builder, eff.effect_id);
                Effect_C3_Table.AddEffectType(builder, typeOff);
                Effect_C3_Table.AddValue(builder, eff.value);
                effectOffsets[i] = Effect_C3_Table.EndEffect_C3_Table(builder);
            }
            var effectsVec = DataSet_C3_Schema.CreateEffectsVector(builder, effectOffsets);

            var linkOffsets = new Offset<EffectLink_C3_Table>[c3.links.Count];
            for (int i = 0; i < c3.links.Count; i++)
            {
                var link = c3.links[i];
                EffectLink_C3_Table.StartEffectLink_C3_Table(builder);
                EffectLink_C3_Table.AddItemId(builder, link.item_id);
                EffectLink_C3_Table.AddEffectId(builder, link.effect_id);
                linkOffsets[i] = EffectLink_C3_Table.EndEffectLink_C3_Table(builder);
            }
            var linksVec = DataSet_C3_Schema.CreateLinksVector(builder, linkOffsets);

            var invOffsets = new Offset<Inventory_C3_Table>[c3.inventories.Count];
            for (int i = 0; i < c3.inventories.Count; i++)
            {
                var inv = c3.inventories[i];
                Inventory_C3_Table.StartInventory_C3_Table(builder);
                Inventory_C3_Table.AddInstanceId(builder, inv.instance_id);
                Inventory_C3_Table.AddCharacterId(builder, inv.character_id);
                Inventory_C3_Table.AddItemId(builder, inv.item_id);
                Inventory_C3_Table.AddDurability(builder, inv.durability);
                invOffsets[i] = Inventory_C3_Table.EndInventory_C3_Table(builder);
            }
            var invsVec = DataSet_C3_Schema.CreateInventoriesVector(builder, invOffsets);

            DataSet_C3_Schema.StartDataSet_C3_Schema(builder);
            DataSet_C3_Schema.AddUsers(builder, usersVec);
            DataSet_C3_Schema.AddCharacters(builder, charsVec);
            DataSet_C3_Schema.AddItems(builder, itemsVec);
            DataSet_C3_Schema.AddEffects(builder, effectsVec);
            DataSet_C3_Schema.AddLinks(builder, linksVec);
            DataSet_C3_Schema.AddInventories(builder, invsVec);
            var c3SchemaOffset = DataSet_C3_Schema.EndDataSet_C3_Schema(builder);

            MasterGameDataSet.StartMasterGameDataSet(builder);
            MasterGameDataSet.AddC3Data(builder, c3SchemaOffset);
            var root = MasterGameDataSet.EndMasterGameDataSet(builder);

            MasterGameDataSet.FinishMasterGameDataSetBuffer(builder, root);
            File.WriteAllBytes(filePath, builder.SizedByteArray());
        }
    }
}

// MessagePack 포맷 빌더
public class MessagePackFormatBuilder : IDataFormatBuilder
{
    public void Build(object container, string filePath, string complexity)
    {
        var options = MessagePack.Resolvers.StandardResolver.Options;
        byte[] bytes = MessagePackSerializer.Serialize(container.GetType(), container, options);
        File.WriteAllBytes(filePath, bytes);
    }
}

// Raw Binary 포맷 빌더
public class RawBinaryFormatBuilder : IDataFormatBuilder
{
    public void Build(object container, string filePath, string complexity)
    {
        using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(fs, Encoding.UTF8))
        {
            if (complexity == "C1_Low" && container is DataSet_C1 c1)
            {
                // 1. Items 직렬화
                writer.Write(c1.items.Count);
                foreach (var item in c1.items)
                {
                    writer.Write(item.item_id);
                    writer.Write(item.name ?? string.Empty);
                    writer.Write(item.item_type ?? string.Empty);
                    writer.Write(item.rarity);
                    writer.Write(item.atk);
                    writer.Write(item.price);
                }

                // 2. Characters 직렬화
                writer.Write(c1.characters.Count);
                foreach (var ch in c1.characters)
                {
                    writer.Write(ch.character_id);
                    writer.Write(ch.name ?? string.Empty);
                    writer.Write(ch.level);
                    writer.Write(ch.hp);
                    writer.Write(ch.pos_x);
                    writer.Write(ch.pos_y);
                }
            }
            else if (complexity == "C2_Mid" && container is DataSet_C2 c2)
            {
                // 1. Items
                writer.Write(c2.items.Count);
                foreach (var item in c2.items)
                {
                    writer.Write(item.item_id);
                    writer.Write(item.item_code ?? string.Empty);
                    writer.Write(item.name ?? string.Empty);
                    writer.Write(item.item_type ?? string.Empty);
                    writer.Write(item.rarity);
                    writer.Write(item.req_level);
                    writer.Write(item.base_atk);
                }

                // 2. Tags
                writer.Write(c2.tags.Count);
                foreach (var tag in c2.tags)
                {
                    writer.Write(tag.item_id);
                    writer.Write(tag.tag_name ?? string.Empty);
                }

                // 3. Characters
                writer.Write(c2.characters.Count);
                foreach (var ch in c2.characters)
                {
                    writer.Write(ch.character_id);
                    writer.Write(ch.name ?? string.Empty);
                    writer.Write(ch.level ?? string.Empty);
                    writer.Write(ch.cur_hp);
                    writer.Write(ch.pos_x);
                    writer.Write(ch.pos_y);
                }

                // 4. Inventories
                writer.Write(c2.inventories.Count);
                foreach (var inv in c2.inventories)
                {
                    writer.Write(inv.instance_id);
                    writer.Write(inv.character_id);
                    writer.Write(inv.item_id);
                    writer.Write(inv.quantity);
                }
            }
            else if (complexity == "C3_High" && container is DataSet_C3 c3)
            {
                // 1. Users
                writer.Write(c3.users.Count);
                foreach (var user in c3.users)
                {
                    writer.Write(user.user_id);
                    writer.Write(user.account_name ?? string.Empty);
                    writer.Write(user.created_at);
                }

                // 2. Characters
                writer.Write(c3.characters.Count);
                foreach (var ch in c3.characters)
                {
                    writer.Write(ch.character_id);
                    writer.Write(ch.user_id);
                    writer.Write(ch.name ?? string.Empty);
                    writer.Write(ch.guild_id);
                }

                // 3. Items
                writer.Write(c3.items.Count);
                foreach (var item in c3.items)
                {
                    writer.Write(item.item_id);
                    writer.Write(item.name ?? string.Empty);
                    writer.Write(item.rarity);
                }

                // 4. Effects
                writer.Write(c3.effects.Count);
                foreach (var eff in c3.effects)
                {
                    writer.Write(eff.effect_id);
                    writer.Write(eff.effect_type ?? string.Empty);
                    writer.Write(eff.value);
                }

                // 5. Links
                writer.Write(c3.links.Count);
                foreach (var link in c3.links)
                {
                    writer.Write(link.item_id);
                    writer.Write(link.effect_id);
                }

                // 6. Inventories
                writer.Write(c3.inventories.Count);
                foreach (var inv in c3.inventories)
                {
                    writer.Write(inv.instance_id);
                    writer.Write(inv.character_id);
                    writer.Write(inv.item_id);
                    writer.Write(inv.durability);
                }
            }
        }
    
    }
}

// JSON 포맷 빌더
public class JsonFormatBuilder : IDataFormatBuilder
{
    public void Build(object container, string filePath, string complexity)
    {
        string json = JsonConvert.SerializeObject(container, Formatting.Indented);
        File.WriteAllText(filePath, json, System.Text.Encoding.UTF8);
    }
}