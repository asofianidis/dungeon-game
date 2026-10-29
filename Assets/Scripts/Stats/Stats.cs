using System.Collections.Generic;

namespace Depthbound
{
    public enum StatType
    {
        Strength, Dexterity, Intelligence, Vitality, Spirit, AllAttributes,
        MaxLife, MaxLifePct, MaxMana, MaxManaPct, LifeRegen, ManaRegen,
        Armor, ArmorPct, Evasion, EvasionPct, Accuracy, BlockChance,
        FlatAttackDamage, AttackDamage, SpellDamage, DotDamage,
        PhysicalDamage, FireDamage, ColdDamage, LightningDamage, PoisonDamage, HolyDamage, ShadowDamage,
        CritChance, CritDamage,
        FireRes, ColdRes, LightningRes, PoisonRes, HolyRes, ShadowRes, AllRes,
        LifeSteal, LifeOnKill, ManaOnKill, Thorns,
        CooldownReduction, ManaCostReduction, StatusChance,
        SummonDamage, SummonLife, AreaRadius, ExtraProjectiles, DoubleStrike,
        MagicFind, GoldFind, LightRadius, AllSkills, DamageReduction, XpBonus,
        ActionSpeed, MoveSpeed,
        COUNT
    }

    public struct StatMod
    {
        public StatType stat;
        public float value;
        public StatMod(StatType s, float v) { stat = s; value = v; }
    }

    public static class StatInfo
    {
        static readonly Dictionary<StatType, string> names = new Dictionary<StatType, string>
        {
            { StatType.Strength, "Strength" }, { StatType.Dexterity, "Dexterity" },
            { StatType.Intelligence, "Intelligence" }, { StatType.Vitality, "Vitality" },
            { StatType.Spirit, "Spirit" }, { StatType.AllAttributes, "All Attributes" },
            { StatType.MaxLife, "Maximum Life" }, { StatType.MaxLifePct, "increased Maximum Life" },
            { StatType.MaxMana, "Maximum Mana" }, { StatType.MaxManaPct, "increased Maximum Mana" },
            { StatType.LifeRegen, "Life per turn" }, { StatType.ManaRegen, "Mana per turn" },
            { StatType.Armor, "Armor" }, { StatType.ArmorPct, "increased Armor" },
            { StatType.Evasion, "Evasion" }, { StatType.EvasionPct, "increased Evasion" },
            { StatType.Accuracy, "Accuracy" }, { StatType.BlockChance, "Block Chance" },
            { StatType.FlatAttackDamage, "Damage to Attacks" }, { StatType.AttackDamage, "increased Attack Damage" },
            { StatType.SpellDamage, "increased Spell Damage" }, { StatType.DotDamage, "increased Damage over Time" },
            { StatType.PhysicalDamage, "increased Physical Damage" }, { StatType.FireDamage, "increased Fire Damage" },
            { StatType.ColdDamage, "increased Cold Damage" }, { StatType.LightningDamage, "increased Lightning Damage" },
            { StatType.PoisonDamage, "increased Poison Damage" }, { StatType.HolyDamage, "increased Holy Damage" },
            { StatType.ShadowDamage, "increased Shadow Damage" },
            { StatType.CritChance, "Critical Strike Chance" }, { StatType.CritDamage, "Critical Strike Damage" },
            { StatType.FireRes, "Fire Resistance" }, { StatType.ColdRes, "Cold Resistance" },
            { StatType.LightningRes, "Lightning Resistance" }, { StatType.PoisonRes, "Poison Resistance" },
            { StatType.HolyRes, "Holy Resistance" }, { StatType.ShadowRes, "Shadow Resistance" },
            { StatType.AllRes, "All Resistances" },
            { StatType.LifeSteal, "Life Steal" }, { StatType.LifeOnKill, "Life on Kill" },
            { StatType.ManaOnKill, "Mana on Kill" }, { StatType.Thorns, "Thorns Damage" },
            { StatType.CooldownReduction, "Cooldown Reduction" }, { StatType.ManaCostReduction, "Mana Cost Reduction" },
            { StatType.StatusChance, "Status Effect Chance" },
            { StatType.SummonDamage, "increased Minion Damage" }, { StatType.SummonLife, "increased Minion Life" },
            { StatType.AreaRadius, "Area of Effect Radius" }, { StatType.ExtraProjectiles, "Projectiles" },
            { StatType.DoubleStrike, "Double Strike Chance" },
            { StatType.MagicFind, "Magic Find" }, { StatType.GoldFind, "Gold Find" },
            { StatType.LightRadius, "Light Radius" }, { StatType.AllSkills, "to All Skills" },
            { StatType.DamageReduction, "Damage Reduction" }, { StatType.XpBonus, "Experience Gained" },
            { StatType.ActionSpeed, "increased Attack and Cast Speed" }, { StatType.MoveSpeed, "increased Movement Speed" },
        };

        static readonly HashSet<StatType> percent = new HashSet<StatType>
        {
            StatType.MaxLifePct, StatType.MaxManaPct, StatType.ArmorPct, StatType.EvasionPct, StatType.BlockChance,
            StatType.AttackDamage, StatType.SpellDamage, StatType.DotDamage, StatType.PhysicalDamage, StatType.FireDamage,
            StatType.ColdDamage, StatType.LightningDamage, StatType.PoisonDamage, StatType.HolyDamage, StatType.ShadowDamage,
            StatType.CritChance, StatType.CritDamage, StatType.FireRes, StatType.ColdRes, StatType.LightningRes,
            StatType.PoisonRes, StatType.HolyRes, StatType.ShadowRes, StatType.AllRes, StatType.LifeSteal,
            StatType.CooldownReduction, StatType.ManaCostReduction, StatType.StatusChance, StatType.SummonDamage,
            StatType.SummonLife, StatType.DoubleStrike, StatType.MagicFind, StatType.GoldFind,
            StatType.DamageReduction, StatType.XpBonus, StatType.ActionSpeed, StatType.MoveSpeed,
        };

        public static string Name(StatType s) => names.TryGetValue(s, out var n) ? n : s.ToString();
        public static bool IsPercent(StatType s) => percent.Contains(s);

        public static bool IsFractional(StatType s) => s == StatType.LifeRegen || s == StatType.ManaRegen;

        public static string FormatValue(StatType s, float v)
        {
            string num = IsFractional(s) ? v.ToString("0.#") : UnityEngine.Mathf.RoundToInt(v).ToString();
            return IsPercent(s) ? num + "%" : num;
        }

        public static string Describe(StatType s, float v)
        {
            string sign = v >= 0 ? "+" : "";
            return sign + FormatValue(s, v) + " " + Name(s);
        }

        public static StatType DamageStatFor(Element e)
        {
            switch (e)
            {
                case Element.Fire: return StatType.FireDamage;
                case Element.Cold: return StatType.ColdDamage;
                case Element.Lightning: return StatType.LightningDamage;
                case Element.Poison: return StatType.PoisonDamage;
                case Element.Holy: return StatType.HolyDamage;
                case Element.Shadow: return StatType.ShadowDamage;
                default: return StatType.PhysicalDamage;
            }
        }

        public static StatType ResFor(Element e)
        {
            switch (e)
            {
                case Element.Fire: return StatType.FireRes;
                case Element.Cold: return StatType.ColdRes;
                case Element.Lightning: return StatType.LightningRes;
                case Element.Poison: return StatType.PoisonRes;
                case Element.Holy: return StatType.HolyRes;
                case Element.Shadow: return StatType.ShadowRes;
                default: return StatType.Armor;
            }
        }
    }
}
