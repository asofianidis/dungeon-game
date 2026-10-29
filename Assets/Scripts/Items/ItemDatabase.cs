using System.Collections.Generic;
using System.Linq;

namespace Depthbound
{
    public class ItemBase
    {
        public string id, name, icon;
        public ItemKind kind;
        public int minLevel;
        public float dmgMin, dmgMax, armor, block;
        public bool twoHanded, ranged;
        public int range = 1;
        public Element element = Element.Physical;
        public readonly List<StatMod> implicits = new List<StatMod>();

        public ItemBase Imp(StatType s, float v) { implicits.Add(new StatMod(s, v)); return this; }
    }

    public class AffixDef
    {
        public string name;
        public bool prefix;
        public StatType stat;
        public float min, max, growth;
        public ItemKind[] kinds;
        public int weight = 10, minLevel = 1;
        public bool integer;

        public bool Allows(ItemKind k) => kinds == null || System.Array.IndexOf(kinds, k) >= 0;
    }

    public class UniqueDef
    {
        public string id, name, baseId, flavor;
        public readonly List<ItemPower> powers = new List<ItemPower>();
        public UniqueDef P(ItemPower p) { powers.Add(p); return this; }
    }

    public static class ItemDatabase
    {
        public static readonly List<ItemBase> Bases = new List<ItemBase>();
        public static readonly List<AffixDef> Affixes = new List<AffixDef>();
        public static readonly List<UniqueDef> Uniques = new List<UniqueDef>();
        static readonly Dictionary<string, ItemBase> byId = new Dictionary<string, ItemBase>();

        public static ItemBase Base(string id) => byId.TryGetValue(id, out var b) ? b : null;
        public static UniqueDef Unique(string id) => Uniques.FirstOrDefault(u => u.id == id);

        static ItemKind[] K(params ItemKind[] k) => k;
        static readonly ItemKind[] Armor = { ItemKind.Helm, ItemKind.Chest, ItemKind.Gloves, ItemKind.Boots, ItemKind.Belt, ItemKind.Offhand };
        static readonly ItemKind[] Jewelry = { ItemKind.Amulet, ItemKind.Ring };
        static readonly ItemKind[] ArmorJewelry = { ItemKind.Helm, ItemKind.Chest, ItemKind.Gloves, ItemKind.Boots, ItemKind.Belt, ItemKind.Offhand, ItemKind.Amulet, ItemKind.Ring };

        static ItemBase B(string id, string name, ItemKind kind, int lvl, string icon)
        {
            var b = new ItemBase { id = id, name = name, kind = kind, minLevel = lvl, icon = icon };
            Bases.Add(b); byId[id] = b;
            return b;
        }

        static ItemBase W(string id, string name, int lvl, string icon, float min, float max, bool twoHanded = false)
        {
            var b = B(id, name, ItemKind.Weapon, lvl, icon);
            b.dmgMin = min; b.dmgMax = max; b.twoHanded = twoHanded;
            return b;
        }

        static ItemBase R(ItemBase b, int range) { b.ranged = true; b.range = range; return b; }
        static ItemBase A(string id, string name, ItemKind kind, int lvl, string icon, float armor) { var b = B(id, name, kind, lvl, icon); b.armor = armor; return b; }

        static void Af(string name, bool prefix, StatType s, float min, float max, float growth, ItemKind[] kinds, int weight = 10, int minLevel = 1, bool integer = false)
        {
            Affixes.Add(new AffixDef { name = name, prefix = prefix, stat = s, min = min, max = max, growth = growth, kinds = kinds, weight = weight, minLevel = minLevel, integer = integer });
        }

        static UniqueDef U(string id, string name, string baseId, string flavor)
        {
            var u = new UniqueDef { id = id, name = name, baseId = baseId, flavor = flavor };
            Uniques.Add(u);
            return u;
        }

        static ItemDatabase()
        {
            // ---------------- WEAPONS ----------------
            W("rusty_sword", "Rusty Sword", 1, "sword", 3, 6).Imp(StatType.Accuracy, 5);
            W("longsword", "Longsword", 8, "sword", 6, 11).Imp(StatType.Accuracy, 10);
            W("bastard_sword", "Bastard Sword", 18, "sword", 10, 18).Imp(StatType.Accuracy, 15);
            W("runeblade", "Runeblade", 30, "sword", 15, 26).Imp(StatType.Accuracy, 20).Imp(StatType.SpellDamage, 10);
            W("starsteel", "Starsteel Blade", 45, "sword", 22, 36).Imp(StatType.Accuracy, 30);
            W("hatchet", "Hatchet", 1, "axe", 2, 8).Imp(StatType.CritDamage, 10);
            W("battle_axe", "Battle Axe", 10, "axe", 5, 16).Imp(StatType.CritDamage, 15);
            W("great_axe", "Great Axe", 20, "axe", 12, 34, true).Imp(StatType.CritDamage, 25);
            W("executioner", "Executioner's Axe", 38, "axe", 20, 52, true).Imp(StatType.CritDamage, 35);
            W("club", "Club", 1, "mace", 3, 7).Imp(StatType.StatusChance, 5);
            W("flanged_mace", "Flanged Mace", 12, "mace", 7, 13).Imp(StatType.StatusChance, 8);
            W("war_hammer", "War Hammer", 25, "mace", 14, 30, true).Imp(StatType.StatusChance, 12);
            W("shiv", "Shiv", 1, "dagger", 2, 5).Imp(StatType.CritChance, 4);
            W("kris", "Kris", 10, "dagger", 4, 9).Imp(StatType.CritChance, 6);
            W("dirk", "Assassin's Dirk", 28, "dagger", 8, 16).Imp(StatType.CritChance, 8);
            W("spear", "Spear", 5, "spear", 5, 12, true).Imp(StatType.AttackDamage, 10);
            W("halberd", "Halberd", 24, "spear", 12, 28, true).Imp(StatType.AttackDamage, 18);
            W("quarterstaff", "Quarterstaff", 1, "staff", 3, 7, true).Imp(StatType.SpellDamage, 12);
            W("battle_staff", "Battle Staff", 12, "staff", 6, 12, true).Imp(StatType.SpellDamage, 22);
            W("archon_staff", "Archon Staff", 30, "staff", 10, 20, true).Imp(StatType.SpellDamage, 35);
            R(W("twig_wand", "Twig Wand", 1, "wand", 2, 4), 5).Imp(StatType.SpellDamage, 8).element = Element.Shadow;
            R(W("bone_wand", "Bone Wand", 14, "wand", 4, 8), 5).Imp(StatType.SpellDamage, 16).element = Element.Shadow;
            R(W("lich_wand", "Lich Wand", 32, "wand", 7, 14), 5).Imp(StatType.SpellDamage, 26).element = Element.Shadow;
            R(W("short_bow", "Short Bow", 1, "bow", 3, 6, true), 7);
            R(W("long_bow", "Long Bow", 10, "bow", 5, 11, true), 7).Imp(StatType.Accuracy, 10);
            R(W("composite_bow", "Composite Bow", 22, "bow", 8, 17, true), 7).Imp(StatType.Accuracy, 20);
            R(W("dragon_bow", "Dragonbone Bow", 40, "bow", 14, 28, true), 8).Imp(StatType.Accuracy, 30);

            // ---------------- OFFHANDS ----------------
            var buckler = A("buckler", "Buckler", ItemKind.Offhand, 1, "shield", 5); buckler.block = 10;
            var kite = A("kite_shield", "Kite Shield", ItemKind.Offhand, 12, "shield", 15); kite.block = 15;
            var tower = A("tower_shield", "Tower Shield", ItemKind.Offhand, 28, "shield", 30); tower.block = 20;
            B("glass_orb", "Glass Orb", ItemKind.Offhand, 1, "orb").Imp(StatType.SpellDamage, 8).Imp(StatType.MaxMana, 10);
            B("star_orb", "Star Orb", ItemKind.Offhand, 20, "orb").Imp(StatType.SpellDamage, 16).Imp(StatType.MaxMana, 25);
            B("quiver", "Quiver", ItemKind.Offhand, 1, "quiver").Imp(StatType.AttackDamage, 8);
            B("war_quiver", "War Quiver", ItemKind.Offhand, 18, "quiver").Imp(StatType.AttackDamage, 16);
            B("tome", "Grimoire", ItemKind.Offhand, 6, "tome").Imp(StatType.ManaRegen, 0.5f).Imp(StatType.CooldownReduction, 3);

            // ---------------- ARMOR ----------------
            A("cap", "Leather Cap", ItemKind.Helm, 1, "helm", 4);
            A("helm", "Iron Helm", ItemKind.Helm, 10, "helm", 10);
            A("great_helm", "Great Helm", ItemKind.Helm, 25, "helm", 20);
            A("circlet", "Circlet", ItemKind.Helm, 15, "crown", 6).Imp(StatType.SpellDamage, 10);
            A("crown", "Crown", ItemKind.Helm, 40, "crown", 22).Imp(StatType.MagicFind, 10);
            A("robe", "Robe", ItemKind.Chest, 1, "robe", 4).Imp(StatType.MaxMana, 15);
            A("leather_armor", "Leather Armor", ItemKind.Chest, 1, "chest", 8);
            A("chainmail", "Chainmail", ItemKind.Chest, 10, "chest", 18);
            A("plate", "Plate Armor", ItemKind.Chest, 24, "chest", 32);
            A("mage_robe", "Archmage Robe", ItemKind.Chest, 28, "robe", 14).Imp(StatType.SpellDamage, 15);
            A("dragonscale", "Dragonscale", ItemKind.Chest, 40, "chest", 45).Imp(StatType.AllRes, 8);
            A("wraps", "Wraps", ItemKind.Gloves, 1, "gloves", 2);
            A("gauntlets", "Gauntlets", ItemKind.Gloves, 12, "gloves", 8);
            A("war_gauntlets", "War Gauntlets", ItemKind.Gloves, 30, "gloves", 16).Imp(StatType.AttackDamage, 6);
            A("sandals", "Sandals", ItemKind.Boots, 1, "boots", 2).Imp(StatType.Evasion, 5);
            A("greaves", "Greaves", ItemKind.Boots, 12, "boots", 8);
            A("war_boots", "Battle Boots", ItemKind.Boots, 30, "boots", 16).Imp(StatType.Evasion, 15);
            A("sash", "Sash", ItemKind.Belt, 1, "belt", 1).Imp(StatType.MaxLife, 5);
            A("heavy_belt", "Heavy Belt", ItemKind.Belt, 14, "belt", 6).Imp(StatType.MaxLife, 20);
            A("titan_belt", "Titan Girdle", ItemKind.Belt, 34, "belt", 12).Imp(StatType.MaxLife, 40);
            B("amulet", "Amulet", ItemKind.Amulet, 1, "amulet");
            B("pendant", "Pendant", ItemKind.Amulet, 10, "amulet").Imp(StatType.MaxLife, 10);
            B("talisman", "Talisman", ItemKind.Amulet, 10, "amulet").Imp(StatType.MaxMana, 10);
            B("ring", "Ring", ItemKind.Ring, 1, "ring");
            B("band", "Band", ItemKind.Ring, 15, "ring").Imp(StatType.AllRes, 3);

            // ---------------- AFFIXES (prefixes) ----------------
            var W_ = K(ItemKind.Weapon);
            Af("Sturdy", true, StatType.MaxLife, 5, 12, 0.12f, ArmorJewelry, 14);
            Af("Vital", true, StatType.MaxLifePct, 3, 8, 0.01f, K(ItemKind.Chest, ItemKind.Belt, ItemKind.Amulet), 6, 5);
            Af("Arcane", true, StatType.MaxMana, 5, 12, 0.1f, K(ItemKind.Helm, ItemKind.Amulet, ItemKind.Ring, ItemKind.Offhand, ItemKind.Weapon), 10);
            Af("Reinforced", true, StatType.Armor, 4, 10, 0.12f, Armor, 12);
            Af("Fortified", true, StatType.ArmorPct, 10, 25, 0.01f, K(ItemKind.Chest, ItemKind.Helm, ItemKind.Offhand), 7);
            Af("Nimble", true, StatType.Evasion, 4, 10, 0.12f, K(ItemKind.Boots, ItemKind.Gloves, ItemKind.Chest, ItemKind.Helm), 10);
            Af("Cruel", true, StatType.AttackDamage, 8, 20, 0.015f, K(ItemKind.Weapon, ItemKind.Gloves, ItemKind.Amulet, ItemKind.Ring, ItemKind.Offhand), 12);
            Af("Heavy", true, StatType.FlatAttackDamage, 1, 4, 0.1f, K(ItemKind.Weapon, ItemKind.Gloves, ItemKind.Ring), 10);
            Af("Sorcerous", true, StatType.SpellDamage, 8, 20, 0.015f, K(ItemKind.Weapon, ItemKind.Offhand, ItemKind.Amulet, ItemKind.Ring, ItemKind.Helm), 12);
            Af("Brutal", true, StatType.PhysicalDamage, 8, 22, 0.015f, K(ItemKind.Weapon, ItemKind.Amulet, ItemKind.Ring, ItemKind.Gloves), 8);
            Af("Searing", true, StatType.FireDamage, 8, 22, 0.015f, K(ItemKind.Weapon, ItemKind.Amulet, ItemKind.Ring, ItemKind.Offhand, ItemKind.Gloves), 8);
            Af("Freezing", true, StatType.ColdDamage, 8, 22, 0.015f, K(ItemKind.Weapon, ItemKind.Amulet, ItemKind.Ring, ItemKind.Offhand, ItemKind.Gloves), 8);
            Af("Shocking", true, StatType.LightningDamage, 8, 22, 0.015f, K(ItemKind.Weapon, ItemKind.Amulet, ItemKind.Ring, ItemKind.Offhand, ItemKind.Gloves), 8);
            Af("Toxic", true, StatType.PoisonDamage, 8, 22, 0.015f, K(ItemKind.Weapon, ItemKind.Amulet, ItemKind.Ring, ItemKind.Offhand, ItemKind.Gloves), 8);
            Af("Hallowed", true, StatType.HolyDamage, 8, 22, 0.015f, K(ItemKind.Weapon, ItemKind.Amulet, ItemKind.Ring, ItemKind.Offhand, ItemKind.Gloves), 8);
            Af("Abyssal", true, StatType.ShadowDamage, 8, 22, 0.015f, K(ItemKind.Weapon, ItemKind.Amulet, ItemKind.Ring, ItemKind.Offhand, ItemKind.Gloves), 8);
            Af("Festering", true, StatType.DotDamage, 10, 25, 0.015f, K(ItemKind.Weapon, ItemKind.Amulet, ItemKind.Gloves), 6);
            Af("Keen", true, StatType.CritChance, 2, 5, 0.005f, K(ItemKind.Weapon, ItemKind.Gloves, ItemKind.Ring, ItemKind.Amulet, ItemKind.Helm), 9);
            Af("Deadly", true, StatType.CritDamage, 10, 25, 0.015f, K(ItemKind.Weapon, ItemKind.Gloves, ItemKind.Amulet), 8);
            Af("Vampiric", true, StatType.LifeSteal, 1, 3, 0.004f, K(ItemKind.Weapon, ItemKind.Ring, ItemKind.Amulet, ItemKind.Gloves), 6, 4);
            Af("Spiked", true, StatType.Thorns, 3, 8, 0.12f, K(ItemKind.Chest, ItemKind.Offhand, ItemKind.Gloves), 8);
            Af("Commanding", true, StatType.SummonDamage, 10, 25, 0.015f, K(ItemKind.Weapon, ItemKind.Offhand, ItemKind.Helm, ItemKind.Amulet), 7);
            Af("Swift", true, StatType.DoubleStrike, 3, 8, 0.005f, K(ItemKind.Gloves, ItemKind.Weapon, ItemKind.Boots), 6, 6);
            Af("Masterful", true, StatType.AllSkills, 1, 1, 0, K(ItemKind.Amulet, ItemKind.Weapon, ItemKind.Helm), 2, 15, true);
            Af("Expansive", true, StatType.AreaRadius, 1, 1, 0, K(ItemKind.Weapon, ItemKind.Offhand, ItemKind.Amulet), 2, 20, true);
            Af("Splitting", true, StatType.ExtraProjectiles, 1, 1, 0, K(ItemKind.Weapon, ItemKind.Offhand), 2, 15, true);

            // ---------------- AFFIXES (suffixes) ----------------
            Af("of the Bear", false, StatType.Strength, 2, 5, 0.08f, null, 10, 1, true);
            Af("of the Fox", false, StatType.Dexterity, 2, 5, 0.08f, null, 10, 1, true);
            Af("of the Owl", false, StatType.Intelligence, 2, 5, 0.08f, null, 10, 1, true);
            Af("of the Whale", false, StatType.Vitality, 2, 5, 0.08f, null, 10, 1, true);
            Af("of the Sage", false, StatType.Spirit, 2, 5, 0.08f, null, 10, 1, true);
            Af("of the Heavens", false, StatType.AllAttributes, 1, 3, 0.06f, Jewelry, 4, 8, true);
            Af("of Regeneration", false, StatType.LifeRegen, 0.3f, 1f, 0.06f, K(ItemKind.Helm, ItemKind.Chest, ItemKind.Belt, ItemKind.Ring, ItemKind.Amulet), 8);
            Af("of Clarity", false, StatType.ManaRegen, 0.3f, 1f, 0.06f, K(ItemKind.Helm, ItemKind.Ring, ItemKind.Amulet, ItemKind.Offhand), 8);
            Af("of Embers", false, StatType.FireRes, 6, 15, 0.005f, ArmorJewelry, 9);
            Af("of Frost Ward", false, StatType.ColdRes, 6, 15, 0.005f, ArmorJewelry, 9);
            Af("of Grounding", false, StatType.LightningRes, 6, 15, 0.005f, ArmorJewelry, 9);
            Af("of Antidotes", false, StatType.PoisonRes, 6, 15, 0.005f, ArmorJewelry, 9);
            Af("of Sanctity", false, StatType.HolyRes, 6, 15, 0.005f, ArmorJewelry, 6);
            Af("of the Void", false, StatType.ShadowRes, 6, 15, 0.005f, ArmorJewelry, 6);
            Af("of Warding", false, StatType.AllRes, 3, 8, 0.004f, ArmorJewelry, 5, 6);
            Af("of Precision", false, StatType.Accuracy, 5, 15, 0.1f, K(ItemKind.Weapon, ItemKind.Gloves, ItemKind.Helm, ItemKind.Ring), 8);
            Af("of Blocking", false, StatType.BlockChance, 3, 7, 0.003f, K(ItemKind.Offhand), 8);
            Af("of Slaughter", false, StatType.LifeOnKill, 2, 5, 0.1f, K(ItemKind.Weapon, ItemKind.Gloves, ItemKind.Ring), 8);
            Af("of Absorption", false, StatType.ManaOnKill, 1, 4, 0.1f, K(ItemKind.Weapon, ItemKind.Ring, ItemKind.Helm), 8);
            Af("of Haste", false, StatType.CooldownReduction, 3, 7, 0.002f, K(ItemKind.Helm, ItemKind.Amulet, ItemKind.Ring, ItemKind.Gloves, ItemKind.Boots), 6, 5);
            Af("of Efficiency", false, StatType.ManaCostReduction, 4, 10, 0.003f, K(ItemKind.Helm, ItemKind.Amulet, ItemKind.Ring, ItemKind.Offhand), 6);
            Af("of Affliction", false, StatType.StatusChance, 4, 10, 0.005f, K(ItemKind.Weapon, ItemKind.Gloves, ItemKind.Amulet), 7);
            Af("of Fortune", false, StatType.MagicFind, 5, 15, 0.01f, K(ItemKind.Helm, ItemKind.Ring, ItemKind.Amulet, ItemKind.Boots), 7);
            Af("of Greed", false, StatType.GoldFind, 10, 30, 0.01f, K(ItemKind.Ring, ItemKind.Amulet, ItemKind.Boots, ItemKind.Belt, ItemKind.Gloves), 7);
            Af("of Light", false, StatType.LightRadius, 1, 2, 0, K(ItemKind.Helm, ItemKind.Amulet), 4, 1, true);
            Af("of the Legion", false, StatType.SummonLife, 10, 25, 0.015f, K(ItemKind.Helm, ItemKind.Chest, ItemKind.Offhand, ItemKind.Amulet), 7);
            Af("of Protection", false, StatType.DamageReduction, 2, 5, 0.003f, K(ItemKind.Chest, ItemKind.Offhand, ItemKind.Belt), 6, 8);
            Af("of Learning", false, StatType.XpBonus, 3, 8, 0.004f, K(ItemKind.Helm, ItemKind.Amulet, ItemKind.Ring), 5);
            Af("of the Thorn", false, StatType.Thorns, 3, 8, 0.12f, K(ItemKind.Boots, ItemKind.Belt, ItemKind.Helm), 5);
            Af("of Power", false, StatType.FlatAttackDamage, 1, 3, 0.1f, K(ItemKind.Amulet, ItemKind.Gloves, ItemKind.Offhand), 6);

            // ---------------- LEGENDARIES ----------------
            U("emberheart", "Emberheart", "amulet", "It still beats, slow and hot.")
                .P(ItemPower.Stat(StatType.FireDamage, 30)).P(ItemPower.Key(Keystone.Pyre)).P(ItemPower.Grant("fire_nova", 3));
            U("frostbite", "Frostbite", "longsword", "The blade is cold enough to burn.")
                .P(ItemPower.Key(Keystone.Permafrost)).P(ItemPower.Proc(ProcTrigger.OnHit, 20, "proc_frostburst", 3)).P(ItemPower.Stat(StatType.ColdDamage, 25));
            U("stormcrown", "Stormcaller's Crown", "crown", "Thunder follows the one who wears it.")
                .P(ItemPower.SchoolBonus(School.Storm, 2)).P(ItemPower.Key(Keystone.Conduit)).P(ItemPower.Proc(ProcTrigger.OnCrit, 15, "storm_chain", 4));
            U("butcher", "The Butcher's Cleaver", "great_axe", "Fresh meat!")
                .P(ItemPower.Stat(StatType.AttackDamage, 60)).P(ItemPower.Stat(StatType.LifeSteal, 4)).P(ItemPower.Grant("blood_rend", 5)).P(ItemPower.Proc(ProcTrigger.OnKill, 25, "proc_bloodspike", 4));
            U("lichveil", "Veil of the Lich", "robe", "Death is a door. You hold the key.")
                .P(ItemPower.SchoolBonus(School.Necromancy, 2)).P(ItemPower.Key(Keystone.Necrolord)).P(ItemPower.Stat(StatType.SummonLife, 40));
            U("needles", "Thousand Needles", "composite_bow", "Count them as they fall.")
                .P(ItemPower.Stat(StatType.ExtraProjectiles, 2)).P(ItemPower.Stat(StatType.AttackDamage, 30)).P(ItemPower.Grant("arc_multishot", 4));
            U("bloodband", "Bloodpact Band", "ring", "Paid in full.")
                .P(ItemPower.Key(Keystone.BloodMagic)).P(ItemPower.Key(Keystone.Vampirism)).P(ItemPower.Stat(StatType.SpellDamage, 25)).P(ItemPower.Stat(StatType.LifeSteal, 3));
            U("glasswing", "Glasswing", "bone_wand", "Beautiful. Fragile. Lethal.")
                .P(ItemPower.Key(Keystone.GlassCannon)).P(ItemPower.SchoolBonus(School.Void, 3)).P(ItemPower.Stat(StatType.CritChance, 5));
            U("aegis", "Aegis of Dawn", "tower_shield", "The sun does not yield.")
                .P(ItemPower.Key(Keystone.Bulwark)).P(ItemPower.Stat(StatType.AllRes, 20)).P(ItemPower.Proc(ProcTrigger.OnStruck, 20, "proc_holybolt", 4));
            U("plaguewalk", "Plaguewalker Treads", "greaves", "Where they step, nothing grows.")
                .P(ItemPower.Key(Keystone.PlagueBearer)).P(ItemPower.Stat(StatType.PoisonDamage, 25)).P(ItemPower.Grant("ven_cloud", 3));
            U("echo_orb", "Echo of Eternity", "star_orb", "Every word spoken twice.")
                .P(ItemPower.Key(Keystone.Echo)).P(ItemPower.Stat(StatType.CooldownReduction, 10)).P(ItemPower.SchoolBonus(School.Void, 2));
            U("ironclad", "Ironclad Will", "plate", "Unmoved. Unbroken.")
                .P(ItemPower.Key(Keystone.Juggernaut)).P(ItemPower.Stat(StatType.ArmorPct, 40)).P(ItemPower.Stat(StatType.MaxLifePct, 15));
            U("berserk_ring", "Ring of Fury", "ring", "Rage is a gift.")
                .P(ItemPower.Key(Keystone.Berserker)).P(ItemPower.Stat(StatType.DoubleStrike, 15)).P(ItemPower.Stat(StatType.AttackDamage, 15));
            U("shatterglass", "Shatterglass Gauntlets", "gauntlets", "Fragile things are made to break.")
                .P(ItemPower.Key(Keystone.Shatter)).P(ItemPower.Grant("frost_shard", 4)).P(ItemPower.Stat(StatType.ColdDamage, 15));
            U("flamecrown", "Crown of Flames", "circlet", "A crown that is never cool.")
                .P(ItemPower.SchoolBonus(School.Fire, 3)).P(ItemPower.Key(Keystone.Inferno)).P(ItemPower.Stat(StatType.FireRes, 25));
            U("soulreaper", "Soulreaper", "halberd", "It harvests more than wheat.")
                .P(ItemPower.Proc(ProcTrigger.OnHit, 12, "proc_shadowburst", 5)).P(ItemPower.Stat(StatType.LifeOnKill, 10)).P(ItemPower.Stat(StatType.ManaOnKill, 8)).P(ItemPower.Grant("as_deathmark", 3));
            U("wraithstep", "Wraithstep", "sandals", "You were never there.")
                .P(ItemPower.Grant("void_blink", 5)).P(ItemPower.Stat(StatType.EvasionPct, 30)).P(ItemPower.Stat(StatType.DoubleStrike, 10));
            U("manypaths", "Sigil of Many Paths", "band", "All roads lead deeper.")
                .P(ItemPower.Stat(StatType.AllSkills, 1)).P(ItemPower.Stat(StatType.AllAttributes, 8));
            U("mountain", "Heart of the Mountain", "titan_belt", "Endure.")
                .P(ItemPower.Stat(StatType.MaxLife, 60)).P(ItemPower.Stat(StatType.DamageReduction, 10)).P(ItemPower.Stat(StatType.Thorns, 25));
            U("tempest", "Tempest Edge", "kris", "Lightning in the shape of a knife.")
                .P(ItemPower.Key(Keystone.Conduit)).P(ItemPower.Proc(ProcTrigger.OnCrit, 25, "proc_lightning", 5)).P(ItemPower.Stat(StatType.CritChance, 8));
            U("gravecaller", "Gravecaller", "bone_wand", "The dead remember who called them.")
                .P(ItemPower.Grant("nec_golem", 3)).P(ItemPower.SchoolBonus(School.Necromancy, 3)).P(ItemPower.Stat(StatType.SummonDamage, 30));
            U("sunforged", "Sunforged Hammer", "war_hammer", "Struck on the anvil of the sun.")
                .P(ItemPower.SchoolBonus(School.Holy, 3)).P(ItemPower.Proc(ProcTrigger.OnHit, 12, "holy_judgement", 3)).P(ItemPower.Stat(StatType.HolyDamage, 30));
            U("venom_embrace", "Venomous Embrace", "wraps", "A lover's touch, a viper's kiss.")
                .P(ItemPower.Proc(ProcTrigger.OnHit, 30, "proc_venom", 6)).P(ItemPower.Stat(StatType.DotDamage, 25)).P(ItemPower.Stat(StatType.PoisonDamage, 15));
            U("meteorfall", "Meteorfall", "archon_staff", "The sky is a weapon.")
                .P(ItemPower.Grant("fire_meteor", 5)).P(ItemPower.Stat(StatType.FireDamage, 40)).P(ItemPower.Stat(StatType.AreaRadius, 1));
            U("lightbringer", "Lightbringer", "pendant", "Carry the dawn into the dark.")
                .P(ItemPower.Stat(StatType.LightRadius, 2)).P(ItemPower.Stat(StatType.HolyDamage, 30)).P(ItemPower.Key(Keystone.Vampirism));
            U("splitter", "Quiver of Echoes", "war_quiver", "Each arrow casts a shadow.")
                .P(ItemPower.Stat(StatType.ExtraProjectiles, 1)).P(ItemPower.Proc(ProcTrigger.OnHit, 10, "arc_powershot", 4)).P(ItemPower.Stat(StatType.AttackDamage, 20));
            U("banner", "Warlord's Banner", "kite_shield", "Rally to me!")
                .P(ItemPower.Grant("war_battlecry", 5)).P(ItemPower.Grant("war_avatar", 1)).P(ItemPower.SchoolBonus(School.Warcry, 2));
            U("grin", "Gambler's Grin", "ring", "Fortune favors the reckless.")
                .P(ItemPower.Stat(StatType.MagicFind, 50)).P(ItemPower.Stat(StatType.GoldFind, 50)).P(ItemPower.Stat(StatType.CritChance, 5));
            U("deathgrip", "Deathgrip", "war_gauntlets", "It does not let go.")
                .P(ItemPower.Proc(ProcTrigger.OnHit, 15, "nec_bonespear", 4)).P(ItemPower.Stat(StatType.LifeSteal, 3)).P(ItemPower.Stat(StatType.AttackDamage, 25));
            U("thundergod", "Thundergod's Vigor", "heavy_belt", "Lightning made flesh.")
                .P(ItemPower.Proc(ProcTrigger.OnStruck, 25, "storm_thunderclap", 4)).P(ItemPower.Stat(StatType.LightningRes, 30)).P(ItemPower.Stat(StatType.LightningDamage, 25));
            U("shadowmantle", "Shadow Mantle", "leather_armor", "Darkness clings to it like smoke.")
                .P(ItemPower.SchoolBonus(School.Assassination, 2)).P(ItemPower.Stat(StatType.EvasionPct, 40)).P(ItemPower.Grant("as_shadowstep", 4));
            U("hydra", "Hydra's Tongue", "lich_wand", "Three heads. Three bolts.")
                .P(ItemPower.Stat(StatType.ExtraProjectiles, 2)).P(ItemPower.Grant("fire_bolt", 6)).P(ItemPower.Stat(StatType.SpellDamage, 30));
        }
    }
}
