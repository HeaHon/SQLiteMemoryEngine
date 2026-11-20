using System;
using System.Collections.Generic;

/// <summary>
/// 중첩 문서형(Document Shape) 생성기: 가변 길이 임베디드 배열 포함
/// </summary>
public class DocumentDataSetGenerator : DataSetGeneratorBase
{
    public override DataShape Shape => DataShape.Document;

    public DocumentDataSetGenerator()
    {
        Register(DataComplexity.C1_Low, GenerateC1);
        Register(DataComplexity.C2_Mid, GenerateC2);
        Register(DataComplexity.C3_High, GenerateC3);
    }

    private object GenerateC1(BenchmarkDataSpec spec)
    {
        var random = new Random(spec.Seed);
        int count = spec.Count;
        var types = new[] { "WEAPON", "ARMOR", "CONSUMABLE" };
        var tagPool = new[] { "MELEE", "RANGED", "FIRE", "ICE", "BOUND", "TRADEABLE" };
        var container = new DocDataSet_C1 { items = new List<DocItem_C1>(count) };

        for (int i = 1; i <= count; i++)
        {
            int tagCount = random.Next(1, 5);
            var itemTags = new List<string>(tagCount);
            for (int t = 0; t < tagCount; t++)
            {
                itemTags.Add(tagPool[random.Next(tagPool.Length)]);
            }

            container.items.Add(new DocItem_C1
            {
                item_id = i,
                name = $"DocItem_C1_{i}",
                item_type = types[random.Next(types.Length)],
                rarity = random.Next(1, 6),
                atk = random.Next(10, 200),
                price = random.Next(100, 50000),
                tags = itemTags
            });
        }
        return container;
    }

    private object GenerateC2(BenchmarkDataSpec spec)
    {
        var random = new Random(spec.Seed);
        int count = spec.Count;
        var tagPool = new[] { "MELEE", "RANGED", "FIRE", "ICE", "BLESSING", "HEAL" };
        var container = new DocDataSet_C2 { characters = new List<DocCharacter_C2>(count) };
        int instanceSeq = 1;

        for (int i = 1; i <= count; i++)
        {
            int invCount = random.Next(spec.MinChildrenPerEntity, spec.MaxChildrenPerEntity + 1);
            var invList = new List<DocInventoryEntry_C2>(invCount);
            for (int j = 0; j < invCount; j++)
            {
                invList.Add(new DocInventoryEntry_C2
                {
                    instance_id = instanceSeq++,
                    item_id = random.Next(1, count + 1),
                    item_name = $"Item_{i}_{j}",
                    rarity = random.Next(1, 6),
                    quantity = random.Next(1, 99),
                    tags = new List<string> { tagPool[random.Next(tagPool.Length)], tagPool[random.Next(tagPool.Length)] }
                });
            }

            container.characters.Add(new DocCharacter_C2
            {
                character_id = i,
                name = $"DocHero_{i}",
                level = random.Next(1, 100),
                cur_hp = random.Next(100, 10000),
                inventories = invList
            });
        }
        return container;
    }

    private object GenerateC3(BenchmarkDataSpec spec)
    {
        var random = new Random(spec.Seed);
        int count = spec.Count;
        var effectTypes = new[] { "BUFF_ATK", "BUFF_DEF", "LIFESTEAL", "CRITICAL_UP" };
        var container = new DocDataSet_C3 { users = new List<DocUser_C3>(count) };
        int charSeq = 1, invSeq = 1, effSeq = 1;

        for (int i = 1; i <= count; i++)
        {
            int charCount = random.Next(1, 4);
            var chars = new List<DocCharacter_C3>(charCount);
            for (int c = 0; c < charCount; c++)
            {
                int invCount = random.Next(spec.MinChildrenPerEntity, Math.Max(2, spec.MaxChildrenPerEntity / 2));
                var invs = new List<DocInventoryItem_C3>(invCount);
                for (int k = 0; k < invCount; k++)
                {
                    int effCount = random.Next(1, 4);
                    var effs = new List<DocEffect_C3>(effCount);
                    for (int e = 0; e < effCount; e++)
                    {
                        effs.Add(new DocEffect_C3
                        {
                            effect_id = effSeq++,
                            effect_type = effectTypes[random.Next(effectTypes.Length)],
                            value = random.Next(5, 100)
                        });
                    }

                    invs.Add(new DocInventoryItem_C3
                    {
                        instance_id = invSeq++,
                        item_id = random.Next(1, count + 1),
                        item_name = $"MasterItem_C3_{i}_{k}",
                        rarity = random.Next(1, 6),
                        durability = random.Next(1, 100),
                        effects = effs
                    });
                }

                chars.Add(new DocCharacter_C3
                {
                    character_id = charSeq++,
                    name = $"Hero_C3_{charSeq}",
                    guild_id = random.Next(1, 50),
                    inventories = invs
                });
            }

            container.users.Add(new DocUser_C3
            {
                user_id = i,
                account_name = $"DocUser_{i}",
                created_at = 1700000000 + i,
                characters = chars
            });
        }
        return container;
    }
}