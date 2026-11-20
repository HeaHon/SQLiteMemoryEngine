using System;
using System.Collections.Generic;

/// <summary>
/// 기존 정규화 RDB 형태(DataSet_C1 ~ C3) 생성기
/// </summary>
public class RelationalDataSetGenerator : DataSetGeneratorBase
{
    public override DataShape Shape => DataShape.Relational;

    public RelationalDataSetGenerator()
    {
        Register(DataComplexity.C1_Low, GenerateC1);
        Register(DataComplexity.C2_Mid, GenerateC2);
        Register(DataComplexity.C3_High, GenerateC3);
    }

    private object GenerateC1(BenchmarkDataSpec spec)
    {
        var random = new Random(spec.Seed);
        int count = spec.Count;
        var container = new RelDataSet_C1
        {
            items = new List<RelMasterItem_C1>(count),
            characters = new List<RelRuntimeCharacter_C1>(count)
        };
        var types = new[] { "WEAPON", "ARMOR", "CONSUMABLE" };

        for (int i = 1; i <= count; i++)
        {
            container.items.Add(new RelMasterItem_C1
            {
                item_id = i,
                name = $"Item_C1_{i}",
                item_type = types[random.Next(types.Length)],
                rarity = random.Next(1, 6),
                atk = random.Next(10, 200),
                price = random.Next(100, 50000)
            });
            container.characters.Add(new RelRuntimeCharacter_C1
            {
                character_id = i,
                name = $"Hero_{i}",
                level = random.Next(1, 100),
                hp = random.Next(100, 10000),
                pos_x = (float)(random.NextDouble() * 500.0),
                pos_y = (float)(random.NextDouble() * 500.0)
            });
        }
        return container;
    }

    private object GenerateC2(BenchmarkDataSpec spec)
    {
        var random = new Random(spec.Seed);
        int count = spec.Count;
        var container = new RelDataSet_C2
        {
            items = new List<RelMasterItem_C2>(count),
            tags = new List<RelItemTag_C2>(count),
            characters = new List<RelRuntimeCharacter_C2>(count),
            inventories = new List<RelRuntimeInventory_C2>(count * 2)
        };
        var types = new[] { "WEAPON", "ARMOR", "CONSUMABLE" };
        var tags = new[] { "MELEE", "RANGED", "ELEMENT_FIRE", "ELEMENT_ICE", "BLESSING", "HEAL" };

        for (int i = 1; i <= count; i++)
        {
            container.items.Add(new RelMasterItem_C2
            {
                item_id = i,
                item_code = $"ITM_{i:D6}",
                name = $"Item_C2_{i}",
                item_type = types[random.Next(types.Length)],
                rarity = random.Next(1, 6),
                req_level = random.Next(1, 80),
                base_atk = random.Next(10, 300)
            });
            container.tags.Add(new RelItemTag_C2 { item_id = i, tag_name = tags[i % tags.Length] });
            container.characters.Add(new RelRuntimeCharacter_C2
            {
                character_id = i,
                name = $"Hero_{i}",
                level = random.Next(1, 100).ToString(),
                cur_hp = random.Next(100, 10000),
                pos_x = (float)(random.NextDouble() * 500.0),
                pos_y = (float)(random.NextDouble() * 500.0)
            });
            container.inventories.Add(new RelRuntimeInventory_C2 { instance_id = i, character_id = i, item_id = i, quantity = random.Next(1, 99) });
            container.inventories.Add(new RelRuntimeInventory_C2 { instance_id = count + i, character_id = i, item_id = i, quantity = random.Next(1, 99) });
        }
        return container;
    }

    private object GenerateC3(BenchmarkDataSpec spec)
    {
        var random = new Random(spec.Seed);
        int count = spec.Count;
        var container = new RelDataSet_C3
        {
            users = new List<RelSystemUser_C3>(count),
            characters = new List<RelRuntimeCharacter_C3>(count * 2),
            items = new List<RelMasterItem_C3>(count),
            effects = new List<RelMasterEffect_C3>(count),
            links = new List<RelItemEffectLink_C3>(count),
            inventories = new List<RelRuntimeInventory_C3>(count * 2)
        };
        var effectTypes = new[] { "BUFF_ATK", "BUFF_DEF", "LIFESTEAL", "CRITICAL_UP" };

        for (int i = 1; i <= count; i++)
        {
            container.users.Add(new RelSystemUser_C3 { user_id = i, account_name = $"User_{i}", created_at = 1700000000 + i });
            int firstCharacterId = i * 2 - 1;
            int secondCharacterId = i * 2;
            container.characters.Add(new RelRuntimeCharacter_C3 { character_id = firstCharacterId, user_id = i, name = $"Hero_C3_{firstCharacterId}", guild_id = random.Next(1, 50) });
            container.characters.Add(new RelRuntimeCharacter_C3 { character_id = secondCharacterId, user_id = i, name = $"Hero_C3_{secondCharacterId}", guild_id = random.Next(1, 50) });
            container.items.Add(new RelMasterItem_C3 { item_id = i, name = $"MasterItem_C3_{i}", rarity = random.Next(1, 6) });
            container.effects.Add(new RelMasterEffect_C3 { effect_id = i, effect_type = effectTypes[random.Next(effectTypes.Length)], value = random.Next(5, 100) });
            container.links.Add(new RelItemEffectLink_C3 { item_id = i, effect_id = i });
            container.inventories.Add(new RelRuntimeInventory_C3 { instance_id = firstCharacterId, character_id = firstCharacterId, item_id = i, durability = random.Next(1, 100) });
            container.inventories.Add(new RelRuntimeInventory_C3 { instance_id = secondCharacterId, character_id = secondCharacterId, item_id = i, durability = random.Next(1, 100) });
        }
        return container;
    }
}