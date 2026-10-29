using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Depthbound
{
    public enum PowerKind { Stat, SchoolBonus, SkillBonus, GrantSkill, Proc, Keystone }

    /// <summary>One line of power on an item: a stat roll, skill bonus, granted skill, proc, or keystone.</summary>
    public class ItemPower
    {
        public PowerKind kind;
        public StatType stat;
        public float value;
        public School school;
        public string skillId;
        public int rank = 1;
        public ProcTrigger trigger;
        public Keystone keystone;
        public string affixName;
        public bool prefix;

        public static ItemPower Stat(StatType s, float v) => new ItemPower { kind = PowerKind.Stat, stat = s, value = v };
        public static ItemPower SchoolBonus(School s, int n) => new ItemPower { kind = PowerKind.SchoolBonus, school = s, value = n };
        public static ItemPower SkillBonus(string id, int n) => new ItemPower { kind = PowerKind.SkillBonus, skillId = id, value = n };
        public static ItemPower Grant(string id, int rank) => new ItemPower { kind = PowerKind.GrantSkill, skillId = id, rank = rank };
        public static ItemPower Proc(ProcTrigger t, float chance, string id, int rank) => new ItemPower { kind = PowerKind.Proc, trigger = t, value = chance, skillId = id, rank = rank };
        public static ItemPower Key(Keystone k) => new ItemPower { kind = PowerKind.Keystone, keystone = k };

        public ItemPower Clone() => (ItemPower)MemberwiseClone();

        public bool IsSkillPower => kind != PowerKind.Stat;

        public string Describe()
        {
            switch (kind)
            {
                case PowerKind.Stat: return StatInfo.Describe(stat, value);
                case PowerKind.SchoolBonus: return $"+{value:0} to {school} Skills";
                case PowerKind.SkillBonus: return $"+{value:0} to {SkillDatabase.Get(skillId)?.name ?? skillId}";
                case PowerKind.GrantSkill: return $"Grants Skill: {SkillDatabase.Get(skillId)?.name ?? skillId} (Rank {rank})";
                case PowerKind.Proc: return $"{value:0}% chance {SkillDef.TriggerText(trigger)} to cast {SkillDatabase.Get(skillId)?.name ?? skillId} (Rank {rank})";
                case PowerKind.Keystone: return EnumUtil.KeystoneDesc(keystone);
            }
            return "";
        }
    }

    public class Item
    {
        public ItemBase baseDef;
        public string name;
        public Rarity rarity;
        public int ilvl;
        public float dmgMin, dmgMax, armor, block;
        public readonly List<ItemPower> powers = new List<ItemPower>();
        public string uniqueId, flavor;

        public ItemKind Kind => baseDef.kind;
        public bool IsWeapon => baseDef.kind == ItemKind.Weapon;
        public int Value => Mathf.RoundToInt((6 + ilvl * 3) * RarityValue(rarity));

        static float RarityValue(Rarity r)
        {
            switch (r)
            {
                case Rarity.Magic: return 2f;
                case Rarity.Rare: return 4f;
                case Rarity.Epic: return 8f;
                case Rarity.Legendary: return 15f;
                default: return 1f;
            }
        }

        public static Color RarityColor(Rarity r)
        {
            switch (r)
            {
                case Rarity.Magic: return new Color(0.4f, 0.6f, 1f);
                case Rarity.Rare: return new Color(1f, 0.9f, 0.3f);
                case Rarity.Epic: return new Color(0.75f, 0.4f, 1f);
                case Rarity.Legendary: return new Color(1f, 0.55f, 0.1f);
                default: return new Color(0.85f, 0.85f, 0.85f);
            }
        }

        public Color Color => RarityColor(rarity);

        public string Tooltip()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<b><color=#{ColorUtility.ToHtmlStringRGB(Color)}>{name}</color></b>");
            sb.AppendLine($"<color=#aaaaaa>{rarity} {baseDef.name}  -  Item Level {ilvl}</color>");
            if (IsWeapon)
            {
                sb.AppendLine($"Damage: {Mathf.RoundToInt(dmgMin)}-{Mathf.RoundToInt(dmgMax)} ({baseDef.element})");
                sb.AppendLine(baseDef.ranged ? $"Ranged attack (range {baseDef.range})" : "Melee attack");
                if (baseDef.twoHanded) sb.AppendLine("Two-Handed");
            }
            if (armor > 0) sb.AppendLine($"Armor: {Mathf.RoundToInt(armor)}");
            if (block > 0) sb.AppendLine($"Block: {Mathf.RoundToInt(block)}%");
            foreach (var m in baseDef.implicits)
                sb.AppendLine($"<color=#cccccc>{StatInfo.Describe(m.stat, m.value)}</color>");
            if (powers.Count > 0) sb.AppendLine();
            foreach (var p in powers)
            {
                string col = p.kind == PowerKind.Keystone ? "ff9933" : p.IsSkillPower ? "66ddff" : "8888ff";
                sb.AppendLine($"<color=#{col}>{p.Describe()}</color>");
            }
            if (!string.IsNullOrEmpty(flavor)) sb.AppendLine($"\n<i><color=#c08040>\"{flavor}\"</color></i>");
            sb.Append($"<color=#ddcc66>Salvage value: {Value / 3} gold</color>");
            return sb.ToString();
        }
    }
}
