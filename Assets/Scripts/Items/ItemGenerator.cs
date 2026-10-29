using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Depthbound
{
    public static class ItemGenerator
    {
        static readonly (ItemKind kind, float w)[] kindWeights =
        {
            (ItemKind.Weapon, 20), (ItemKind.Offhand, 8), (ItemKind.Helm, 9), (ItemKind.Chest, 9), (ItemKind.Gloves, 9),
            (ItemKind.Boots, 9), (ItemKind.Belt, 7), (ItemKind.Amulet, 6), (ItemKind.Ring, 10),
        };

        static readonly string[] rareFirst = { "Doom", "Grim", "Storm", "Blood", "Shadow", "Bone", "Soul", "Dread", "Ember", "Frost", "Rune", "Wraith", "Star", "Death", "Void", "Iron", "Viper", "Raven", "Dusk", "Ash", "Gloom", "Hate", "Plague", "Sun", "Moon", "Thunder" };
        static readonly Dictionary<ItemKind, string[]> rareSecond = new Dictionary<ItemKind, string[]>
        {
            { ItemKind.Weapon, new[] { "Bite", "Edge", "Fang", "Song", "Thirst", "Reaver", "Cleaver", "Spike", "Call", "Scourge" } },
            { ItemKind.Offhand, new[] { "Bulwark", "Ward", "Guard", "Aegis", "Wall", "Focus" } },
            { ItemKind.Helm, new[] { "Visage", "Crown", "Brow", "Mask", "Cowl", "Horn" } },
            { ItemKind.Chest, new[] { "Shell", "Coat", "Carapace", "Mantle", "Hide", "Husk" } },
            { ItemKind.Gloves, new[] { "Grasp", "Fist", "Hand", "Claw", "Grip", "Touch" } },
            { ItemKind.Boots, new[] { "Stride", "Path", "Trail", "Tread", "March", "Step" } },
            { ItemKind.Belt, new[] { "Cord", "Coil", "Girdle", "Clasp", "Lash", "Chain" } },
            { ItemKind.Amulet, new[] { "Heart", "Eye", "Charm", "Ward", "Talon", "Star" } },
            { ItemKind.Ring, new[] { "Loop", "Circle", "Band", "Spiral", "Knot", "Whorl" } },
        };

        static List<SkillDef> procPool;

        static List<SkillDef> ProcPool()
        {
            if (procPool != null) return procPool;
            procPool = SkillDatabase.All.Where(s => !s.passive && !s.monsterOnly && s.DealsDamage &&
                (s.targeting == Targeting.Projectile || s.targeting == Targeting.Ground || s.targeting == Targeting.Chain ||
                 s.targeting == Targeting.Target || (s.targeting == Targeting.Self && s.radius > 0))).ToList();
            return procPool;
        }

        public static Rarity RollRarity(Rng rng, float magicFind, float quality)
        {
            float m = 1f + Mathf.Max(0, magicFind) / 100f;
            float common = 60f / (1f + quality * 2f);
            var table = new List<(Rarity r, float w)>
            {
                (Rarity.Common, common), (Rarity.Magic, 30 * m), (Rarity.Rare, 8 * m * (1 + quality)),
                (Rarity.Epic, 1.6f * m * (1 + quality * 1.5f)), (Rarity.Legendary, 0.35f * m * (1 + quality * 2f)),
            };
            return rng.Weighted(table, t => t.w).r;
        }

        public static ItemKind RollKind(Rng rng) => rng.Weighted(kindWeights, k => k.w).kind;

        public static ItemBase RollBase(Rng rng, ItemKind kind, int ilvl)
        {
            var list = ItemDatabase.Bases.Where(b => b.kind == kind && b.minLevel <= ilvl).ToList();
            if (list.Count == 0) list = ItemDatabase.Bases.Where(b => b.kind == kind).ToList();
            return rng.Weighted(list, b => 1f + 3f * b.minLevel / (ilvl + 1f));
        }

        public static Item Generate(Rng rng, int ilvl, float magicFind, float quality)
        {
            var rarity = RollRarity(rng, magicFind, quality);
            if (rarity == Rarity.Legendary) return GenerateUnique(rng, rng.Pick(ItemDatabase.Uniques), ilvl);
            return Generate(rng, ilvl, rarity, RollBase(rng, RollKind(rng), ilvl));
        }

        public static Item CreateBase(ItemBase b, int ilvl)
        {
            ilvl = Mathf.Max(1, ilvl);
            float scale = 1f + 0.05f * Mathf.Max(0, ilvl - b.minLevel);
            return new Item
            {
                baseDef = b, name = b.name, rarity = Rarity.Common, ilvl = ilvl,
                dmgMin = b.dmgMin * scale * 1.6f, dmgMax = b.dmgMax * scale * 1.6f,
                armor = b.armor * scale, block = b.block,
            };
        }

        public static Item Generate(Rng rng, int ilvl, Rarity rarity, ItemBase b)
        {
            if (rarity == Rarity.Legendary)
            {
                var matching = ItemDatabase.Uniques.Where(u => ItemDatabase.Base(u.baseId).kind == b.kind).ToList();
                return GenerateUnique(rng, matching.Count > 0 ? rng.Pick(matching) : rng.Pick(ItemDatabase.Uniques), ilvl);
            }
            var item = CreateBase(b, ilvl);
            item.rarity = rarity;
            int count = 0, maxPre = 0, maxSuf = 0, skillPowers = 0;
            switch (rarity)
            {
                case Rarity.Magic: count = rng.Range(1, 3); maxPre = 1; maxSuf = 1; break;
                case Rarity.Rare: count = rng.Range(3, 5); maxPre = 2; maxSuf = 2; skillPowers = rng.Chance(0.25f) ? 1 : 0; break;
                case Rarity.Epic: count = rng.Range(4, 6); maxPre = 3; maxSuf = 3; skillPowers = rng.Chance(0.35f) ? 2 : 1; break;
            }
            AddAffixes(rng, item, count, maxPre, maxSuf);
            for (int i = 0; i < skillPowers; i++) item.powers.Add(RollSkillPower(rng, ilvl, rarity));

            if (rarity == Rarity.Magic)
            {
                var pre = item.powers.FirstOrDefault(p => p.prefix);
                var suf = item.powers.FirstOrDefault(p => !p.prefix && p.affixName != null);
                item.name = (pre != null ? pre.affixName + " " : "") + b.name + (suf != null ? " " + suf.affixName : "");
            }
            else if (rarity >= Rarity.Rare)
            {
                var second = rareSecond[b.kind];
                item.name = rng.Pick(rareFirst) + " " + rng.Pick(second);
            }
            return item;
        }

        static void AddAffixes(Rng rng, Item item, int count, int maxPre, int maxSuf)
        {
            int pre = 0, suf = 0;
            var used = new HashSet<AffixDef>();
            for (int attempt = 0; attempt < 40 && pre + suf < count; attempt++)
            {
                bool wantPrefix = pre < maxPre && (suf >= maxSuf || rng.Chance(0.5f));
                var pool = ItemDatabase.Affixes.Where(a => a.prefix == wantPrefix && a.Allows(item.Kind) && a.minLevel <= item.ilvl && !used.Contains(a)).ToList();
                if (pool.Count == 0) continue;
                var a = rng.Weighted(pool, x => x.weight);
                used.Add(a);
                item.powers.Add(RollAffix(rng, a, item.ilvl, item.rarity));
                if (wantPrefix) pre++; else suf++;
            }
        }

        public static ItemPower RollAffix(Rng rng, AffixDef a, int ilvl, Rarity rarity)
        {
            float v = rng.Range(a.min, a.max) * (1f + a.growth * (ilvl - 1));
            if (rarity >= Rarity.Epic) v *= 1.15f;
            if (a.integer) v = Mathf.Max(1, Mathf.Round(v));
            else if (!StatInfo.IsFractional(a.stat)) v = Mathf.Max(1, Mathf.Round(v));
            else v = Mathf.Round(v * 10f) / 10f;
            var p = ItemPower.Stat(a.stat, v);
            p.affixName = a.name;
            p.prefix = a.prefix;
            return p;
        }

        public static ItemPower RollSkillPower(Rng rng, int ilvl, Rarity rarity)
        {
            bool epic = rarity >= Rarity.Epic;
            var playerSkills = SkillDatabase.PlayerSkills();
            int roll = rng.Range(0, 100);
            if (roll < 28)
            {
                var school = (School)rng.Range(0, 12);
                return ItemPower.SchoolBonus(school, epic && rng.Chance(0.35f) ? 2 : 1);
            }
            if (roll < 56)
            {
                var s = rng.Pick(playerSkills);
                return ItemPower.SkillBonus(s.id, rng.Range(1, epic ? 4 : 3));
            }
            if (roll < (epic ? 78 : 85))
            {
                var eligible = playerSkills.Where(s => s.RequiredLevel <= ilvl + 6).ToList();
                var s = rng.Pick(eligible);
                return ItemPower.Grant(s.id, Mathf.Clamp(1 + ilvl / 10 + rng.Range(0, 3), 1, 12));
            }
            var ps = rng.Pick(ProcPool());
            var trig = (ProcTrigger)rng.Range(1, 6);
            float chance;
            switch (trig)
            {
                case ProcTrigger.OnHit: chance = rng.Range(6, 15); break;
                case ProcTrigger.OnCrit: chance = rng.Range(15, 31); break;
                case ProcTrigger.OnKill: chance = rng.Range(20, 41); break;
                case ProcTrigger.OnStruck: chance = rng.Range(8, 19); break;
                default: chance = rng.Range(8, 16); break;
            }
            return ItemPower.Proc(trig, chance, ps.id, Mathf.Clamp(1 + ilvl / 10 + rng.Range(0, 2), 1, 12));
        }

        static readonly HashSet<StatType> scalingFlat = new HashSet<StatType>
        {
            StatType.MaxLife, StatType.MaxMana, StatType.Thorns, StatType.LifeOnKill, StatType.ManaOnKill, StatType.FlatAttackDamage, StatType.Armor, StatType.Evasion,
        };

        public static Item GenerateUnique(Rng rng, UniqueDef u, int ilvl)
        {
            var b = ItemDatabase.Base(u.baseId);
            var item = CreateBase(b, Mathf.Max(ilvl, b.minLevel));
            item.rarity = Rarity.Legendary;
            item.name = u.name;
            item.uniqueId = u.id;
            item.flavor = u.flavor;
            item.dmgMin *= 1.2f; item.dmgMax *= 1.2f; item.armor *= 1.2f;
            foreach (var p in u.powers)
            {
                var c = p.Clone();
                if (c.kind == PowerKind.Stat && scalingFlat.Contains(c.stat)) c.value = Mathf.Round(c.value * (1f + 0.06f * (item.ilvl - 1)));
                if (c.kind == PowerKind.GrantSkill || c.kind == PowerKind.Proc) c.rank += item.ilvl / 12;
                item.powers.Add(c);
            }
            AddAffixes(rng, item, 2, 1, 1);
            return item;
        }
    }
}
