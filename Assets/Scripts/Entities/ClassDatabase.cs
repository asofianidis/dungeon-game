using System.Collections.Generic;
using UnityEngine;

namespace Depthbound
{
    public class ClassDef
    {
        public string id, name, description, startSkill, startWeapon, startOffhand;
        public int[] attributes; // Str, Dex, Int, Vit, Spirit
        public School[] affinities;
        public Color color;
        public int unlockCost;
    }

    public static class ClassDatabase
    {
        public static readonly List<ClassDef> All = new List<ClassDef>
        {
            new ClassDef { id = "warrior", name = "Warrior", description = "A stalwart fighter who thrives in melee.",
                attributes = new[] { 10, 6, 3, 9, 4 }, startSkill = "blade_cleave", startWeapon = "rusty_sword", startOffhand = "buckler",
                affinities = new[] { School.Blade, School.Warcry }, color = new Color(0.75f, 0.2f, 0.2f), unlockCost = 0 },
            new ClassDef { id = "rogue", name = "Rogue", description = "A deadly opportunist who strikes from the shadows.",
                attributes = new[] { 6, 11, 4, 6, 5 }, startSkill = "as_backstab", startWeapon = "shiv",
                affinities = new[] { School.Assassination, School.Blade }, color = new Color(0.3f, 0.3f, 0.35f), unlockCost = 0 },
            new ClassDef { id = "pyromancer", name = "Pyromancer", description = "A volatile mage who commands fire.",
                attributes = new[] { 3, 5, 12, 6, 8 }, startSkill = "fire_bolt", startWeapon = "twig_wand", startOffhand = "glass_orb",
                affinities = new[] { School.Fire, School.Void }, color = new Color(0.9f, 0.4f, 0.1f), unlockCost = 0 },
            new ClassDef { id = "ranger", name = "Ranger", description = "A keen-eyed archer and trapper.",
                attributes = new[] { 6, 12, 4, 6, 4 }, startSkill = "arc_powershot", startWeapon = "short_bow",
                affinities = new[] { School.Archery, School.Venom }, color = new Color(0.25f, 0.55f, 0.25f), unlockCost = 40 },
            new ClassDef { id = "frostwitch", name = "Frost Witch", description = "A cold sorceress who freezes and poisons.",
                attributes = new[] { 3, 6, 12, 5, 8 }, startSkill = "frost_shard", startWeapon = "quarterstaff",
                affinities = new[] { School.Frost, School.Venom }, color = new Color(0.4f, 0.7f, 0.95f), unlockCost = 60 },
            new ClassDef { id = "paladin", name = "Paladin", description = "A holy warrior shielded by faith.",
                attributes = new[] { 9, 4, 5, 8, 8 }, startSkill = "holy_smite", startWeapon = "club", startOffhand = "buckler",
                affinities = new[] { School.Holy, School.Warcry }, color = new Color(0.95f, 0.85f, 0.45f), unlockCost = 60 },
            new ClassDef { id = "necromancer", name = "Necromancer", description = "A dark summoner who commands the dead.",
                attributes = new[] { 3, 4, 11, 6, 10 }, startSkill = "nec_skeleton", startWeapon = "twig_wand", startOffhand = "tome",
                affinities = new[] { School.Necromancy, School.Void }, color = new Color(0.35f, 0.75f, 0.6f), unlockCost = 100 },
            new ClassDef { id = "bloodknight", name = "Blood Knight", description = "A warrior who feeds on carnage.",
                attributes = new[] { 10, 6, 3, 11, 3 }, startSkill = "blood_rend", startWeapon = "hatchet",
                affinities = new[] { School.Blood, School.Blade }, color = new Color(0.55f, 0.05f, 0.1f), unlockCost = 100 },
            new ClassDef { id = "stormcaller", name = "Stormcaller", description = "A tempest given human form.",
                attributes = new[] { 4, 6, 11, 5, 8 }, startSkill = "storm_spark", startWeapon = "quarterstaff",
                affinities = new[] { School.Storm, School.Void }, color = new Color(0.95f, 0.9f, 0.3f), unlockCost = 150 },
            new ClassDef { id = "plaguebringer", name = "Plaguebringer", description = "A toxic alchemist of rot and ruin.",
                attributes = new[] { 5, 9, 9, 6, 5 }, startSkill = "ven_dart", startWeapon = "shiv", startOffhand = "glass_orb",
                affinities = new[] { School.Venom, School.Necromancy }, color = new Color(0.45f, 0.65f, 0.15f), unlockCost = 150 },
        };

        public static ClassDef Get(string id) => All.Find(c => c.id == id) ?? All[0];

        public static readonly string[] AttributeNames = { "Strength", "Dexterity", "Intelligence", "Vitality", "Spirit" };
        public static readonly string[] AttributeHelp =
        {
            "+0.8% Attack Damage, +0.5 Armor",
            "+1.5 Accuracy, +1 Evasion, +0.06% Crit, +0.3% Attack Damage",
            "+1% Spell Damage, +1 Mana",
            "+5 Life, +0.03 Life Regen",
            "+3 Mana, +0.06 Mana Regen",
        };
    }
}
