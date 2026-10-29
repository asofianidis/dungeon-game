using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Depthbound
{
    public class Player : Actor
    {
        public const int BackpackSize = 40;
        public const int HotbarSize = 8;

        public ClassDef cls;
        public int xp, attrPoints, skillPoints;
        public readonly int[] allocated = new int[5];
        public readonly Dictionary<string, int> learned = new Dictionary<string, int>();
        public readonly Item[] equipment = new Item[10];
        public readonly List<Item> backpack = new List<Item>();
        public readonly string[] hotbar = new string[HotbarSize];
        public int gold, healthPotions, manaPotions, kills;
        public readonly List<StatMod> metaMods = new List<StatMod>();

        // Real-time control state
        public Vector2 moveInput;
        public float dodgeTime, dodgeCooldown, potionLock;
        public Vector2 dodgeDir;
        public bool Dodging => dodgeTime > 0;
        public const float DodgeDuration = 0.22f, DodgeCooldownTime = 1.1f, DodgeSpeed = 13f;

        readonly Dictionary<string, int> skillBonus = new Dictionary<string, int>();
        readonly Dictionary<School, int> schoolBonus = new Dictionary<School, int>();
        public readonly Dictionary<string, int> granted = new Dictionary<string, int>();
        int allSkills;

        public int XpToNext => Mathf.RoundToInt(40 * Mathf.Pow(level, 1.65f));

        public Player(ClassDef c)
        {
            cls = c;
            name = c.name;
            faction = Faction.Player;
            tint = Color.white;
        }

        public Item Equipped(EquipSlot s) => equipment[(int)s];

        public int Learned(string id) => learned.TryGetValue(id, out var r) ? r : 0;
        public int GrantedRank(string id) => granted.TryGetValue(id, out var r) ? r : 0;
        public bool Knows(string id) => Learned(id) > 0 || GrantedRank(id) > 0;

        public int BonusRanks(SkillDef def)
        {
            schoolBonus.TryGetValue(def.school, out var sb);
            skillBonus.TryGetValue(def.id, out var kb);
            return allSkills + sb + kb;
        }

        public override int SkillRank(string id)
        {
            var def = SkillDatabase.Get(id);
            if (def == null) return 0;
            int r = Mathf.Max(Learned(id), GrantedRank(id));
            if (r <= 0) return 0;
            return r + BonusRanks(def);
        }

        public int PointsInSchool(School s) => learned.Where(kv => SkillDatabase.Get(kv.Key)?.school == s).Sum(kv => kv.Value);

        public string CannotLearnReason(SkillDef def)
        {
            if (skillPoints <= 0) return "No skill points";
            if (Learned(def.id) >= SkillDef.MaxLearnedRank) return "Max rank";
            if (level < def.RequiredLevel) return $"Requires level {def.RequiredLevel}";
            int need = def.tier - 1;
            if (PointsInSchool(def.school) < need) return $"Requires {need} points in {def.school}";
            return null;
        }

        public bool LearnSkill(SkillDef def)
        {
            if (CannotLearnReason(def) != null) return false;
            learned[def.id] = Learned(def.id) + 1;
            skillPoints--;
            if (!def.passive && !hotbar.Contains(def.id)) AssignToHotbar(def.id);
            Recalc();
            return true;
        }

        public void AssignToHotbar(string id)
        {
            for (int i = 0; i < HotbarSize; i++)
                if (string.IsNullOrEmpty(hotbar[i])) { hotbar[i] = id; return; }
        }

        public override void Recalc()
        {
            var raw = new float[(int)StatType.COUNT];
            for (int i = 0; i < 5; i++) raw[i] = cls.attributes[i] + allocated[i];

            keystones.Clear(); procs.Clear(); skillBonus.Clear(); schoolBonus.Clear(); granted.Clear();
            weaponMin = 1; weaponMax = 3; attackRange = 1; attackElement = Element.Physical;

            foreach (var item in equipment)
            {
                if (item == null) continue;
                if (item.IsWeapon)
                {
                    weaponMin = item.dmgMin; weaponMax = item.dmgMax;
                    attackRange = item.baseDef.ranged ? item.baseDef.range : 1;
                    attackElement = item.baseDef.element;
                }
                raw[(int)StatType.Armor] += item.armor;
                raw[(int)StatType.BlockChance] += item.block;
                AddMods(raw, item.baseDef.implicits);
                foreach (var p in item.powers) ApplyPower(raw, p);
            }
            foreach (var s in cls.affinities) schoolBonus[s] = (schoolBonus.TryGetValue(s, out var v) ? v : 0) + 1;
            allSkills = Mathf.RoundToInt(raw[(int)StatType.AllSkills]);

            foreach (var id in learned.Keys.Union(granted.Keys).ToList())
            {
                var def = SkillDatabase.Get(id);
                if (def == null || !def.passive) continue;
                int rank = SkillRank(id);
                foreach (var m in def.mods) raw[(int)m.stat] += m.At(rank);
                if (def.keystone != Keystone.None) keystones.Add(def.keystone);
                if (def.trigger != ProcTrigger.None)
                    procs.Add(new ProcEntry { trigger = def.trigger, chance = def.ProcChance(rank), skillId = def.procSkill, rank = rank });
            }
            foreach (var b in buffs) AddMods(raw, b.mods);
            AddMods(raw, metaMods);
            Derive(raw);
            ClampVitals();
        }

        void ApplyPower(float[] raw, ItemPower p)
        {
            switch (p.kind)
            {
                case PowerKind.Stat: raw[(int)p.stat] += p.value; break;
                case PowerKind.SchoolBonus:
                    schoolBonus[p.school] = (schoolBonus.TryGetValue(p.school, out var s) ? s : 0) + (int)p.value; break;
                case PowerKind.SkillBonus:
                    skillBonus[p.skillId] = (skillBonus.TryGetValue(p.skillId, out var k) ? k : 0) + (int)p.value; break;
                case PowerKind.GrantSkill:
                    granted[p.skillId] = Mathf.Max(GrantedRank(p.skillId), p.rank); break;
                case PowerKind.Proc:
                    procs.Add(new ProcEntry { trigger = p.trigger, chance = p.value, skillId = p.skillId, rank = p.rank }); break;
                case PowerKind.Keystone: keystones.Add(p.keystone); break;
            }
        }

        // ---------------- Inventory ----------------
        public bool AddToBackpack(Item item)
        {
            if (backpack.Count >= BackpackSize) return false;
            backpack.Add(item);
            return true;
        }

        public EquipSlot SlotFor(Item item)
        {
            switch (item.Kind)
            {
                case ItemKind.Weapon: return EquipSlot.Weapon;
                case ItemKind.Offhand: return EquipSlot.Offhand;
                case ItemKind.Helm: return EquipSlot.Helm;
                case ItemKind.Chest: return EquipSlot.Chest;
                case ItemKind.Gloves: return EquipSlot.Gloves;
                case ItemKind.Boots: return EquipSlot.Boots;
                case ItemKind.Belt: return EquipSlot.Belt;
                case ItemKind.Amulet: return EquipSlot.Amulet;
                default: return Equipped(EquipSlot.Ring1) == null ? EquipSlot.Ring1 : Equipped(EquipSlot.Ring2) == null ? EquipSlot.Ring2 : EquipSlot.Ring1;
            }
        }

        public void Equip(Item item)
        {
            var slot = SlotFor(item);
            backpack.Remove(item);
            if (item.IsWeapon && item.baseDef.twoHanded && Equipped(EquipSlot.Offhand) != null) Unequip(EquipSlot.Offhand, true);
            if (item.Kind == ItemKind.Offhand)
            {
                var w = Equipped(EquipSlot.Weapon);
                if (w != null && w.baseDef.twoHanded) Unequip(EquipSlot.Weapon, true);
            }
            var prev = equipment[(int)slot];
            equipment[(int)slot] = item;
            if (prev != null) backpack.Add(prev);
            float lifePct = LifePct;
            Recalc();
            life = Mathf.Max(1, MaxLife * lifePct);
            foreach (var p in item.powers)
            {
                if (p.kind != PowerKind.GrantSkill) continue;
                var def = SkillDatabase.Get(p.skillId);
                if (def != null && !def.passive && !hotbar.Contains(p.skillId)) AssignToHotbar(p.skillId);
            }
        }

        public void Unequip(EquipSlot slot, bool force = false)
        {
            var it = equipment[(int)slot];
            if (it == null) return;
            if (!force && backpack.Count >= BackpackSize) return;
            equipment[(int)slot] = null;
            backpack.Add(it);
            Recalc();
        }

        public IEnumerable<string> UsableActiveSkills()
        {
            foreach (var id in learned.Keys.Union(granted.Keys))
            {
                var d = SkillDatabase.Get(id);
                if (d != null && !d.passive && SkillRank(id) > 0) yield return id;
            }
        }

        public int GainXp(float amount)
        {
            xp += Mathf.Max(1, Mathf.RoundToInt(amount * (1 + Get(StatType.XpBonus) / 100f)));
            int gained = 0;
            while (xp >= XpToNext)
            {
                xp -= XpToNext;
                level++;
                gained++;
                attrPoints += 5;
                skillPoints += 1;
            }
            if (gained > 0)
            {
                Recalc();
                life = MaxLife;
                mana = MaxMana;
            }
            return gained;
        }
    }
}
