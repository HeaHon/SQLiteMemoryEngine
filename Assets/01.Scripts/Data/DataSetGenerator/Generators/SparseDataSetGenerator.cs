using System;
using System.Collections.Generic;

/// <summary>
/// 가변/다형성 스키마형(Sparse Shape) 생성기
/// </summary>
public class SparseDataSetGenerator : DataSetGeneratorBase
{
    public override DataShape Shape => DataShape.Sparse;

    public SparseDataSetGenerator()
    {
        Register(DataComplexity.C1_Low, GenerateC1);
        Register(DataComplexity.C2_Mid, GenerateC2);
        Register(DataComplexity.C3_High, GenerateC3);
    }

    private object GenerateC1(BenchmarkDataSpec spec)
    {
        var random = new Random(spec.Seed);
        int count = spec.Count;
        var types = new[] { "WEAPON", "ARMOR", "CONSUMABLE", "MATERIAL" };
        var container = new SparseDataSet_C1 { items = new List<SparseItem_C1>(count) };

        for (int i = 1; i <= count; i++)
        {
            string type = types[random.Next(types.Length)];
            var attrs = new Dictionary<string, object>();

            switch (type)
            {
                case "WEAPON":
                    attrs["atk"] = random.Next(15, 250);
                    attrs["crit_rate"] = Math.Round(random.NextDouble() * 0.4, 2);
                    break;
                case "ARMOR":
                    attrs["def"] = random.Next(10, 180);
                    attrs["element_resist"] = random.Next(0, 50);
                    break;
                case "CONSUMABLE":
                    attrs["heal_hp"] = random.Next(50, 2000);
                    attrs["cooldown_sec"] = random.Next(5, 60);
                    break;
                case "MATERIAL":
                    attrs["stack_limit"] = 999;
                    attrs["craft_tier"] = random.Next(1, 5);
                    break;
            }

            container.items.Add(new SparseItem_C1
            {
                item_id = i,
                item_type = type,
                name = $"SparseItem_{i}",
                attributes = attrs
            });
        }
        return container;
    }

    private object GenerateC2(BenchmarkDataSpec spec)
    {
        var random = new Random(spec.Seed);
        int count = spec.Count;
        var categories = new[] { "HERO", "MONSTER", "NPC" };
        var container = new SparseDataSet_C2 { entities = new List<SparseEntity_C2>(count) };

        for (int i = 1; i <= count; i++)
        {
            string cat = categories[random.Next(categories.Length)];
            var stats = new Dictionary<string, object> { ["hp"] = random.Next(100, 10000) };
            var meta = new Dictionary<string, string>();

            if (cat == "HERO")
            {
                stats["mp"] = random.Next(50, 5000);
                stats["guild_rank"] = random.Next(1, 10);
                meta["class"] = random.Next(0, 2) == 0 ? "KNIGHT" : "MAGE";
            }
            else if (cat == "MONSTER")
            {
                stats["aggro_range"] = random.Next(5, 30);
                stats["is_boss"] = random.Next(0, 10) == 0;
                meta["drop_table"] = $"DROP_{random.Next(1, 20)}";
            }
            else
            {
                stats["shop_discount"] = Math.Round(random.NextDouble() * 0.2, 2);
                meta["region"] = $"TOWN_{random.Next(1, 8)}";
            }

            container.entities.Add(new SparseEntity_C2
            {
                entity_id = i,
                category = cat,
                name = $"{cat}_{i}",
                stats = stats,
                metadata = meta
            });
        }
        return container;
    }

    private object GenerateC3(BenchmarkDataSpec spec)
    {
        var random = new Random(spec.Seed);
        int count = spec.Count;
        var eventTypes = new[] { "COMBAT", "GACHA", "TRADE", "QUEST" };
        var container = new SparseDataSet_C3 { events = new List<SparseEventLog_C3>(count) };

        for (int i = 1; i <= count; i++)
        {
            string evType = eventTypes[random.Next(eventTypes.Length)];
            var payload = new Dictionary<string, object>();

            if (evType == "COMBAT")
            {
                payload["damage"] = random.Next(100, 50000);
                payload["is_crit"] = random.Next(0, 2) == 1;
                payload["skill_id"] = random.Next(1000, 1050);
            }
            else if (evType == "GACHA")
            {
                payload["banner_id"] = random.Next(1, 10);
                payload["pity_count"] = random.Next(1, 90);
                payload["pulled_rarity"] = random.Next(3, 6);
            }
            else if (evType == "TRADE")
            {
                payload["target_user_id"] = random.Next(1, count + 1);
                payload["gold_amount"] = random.Next(100, 1000000);
            }
            else
            {
                payload["quest_id"] = random.Next(1, 500);
                payload["elapsed_sec"] = random.Next(10, 3600);
            }

            container.events.Add(new SparseEventLog_C3
            {
                event_id = $"EV_{i:D8}",
                user_id = random.Next(1, Math.Max(2, count / 10)),
                event_type = evType,
                timestamp = 1700000000L + i,
                payload = payload
            });
        }
        return container;
    }
}