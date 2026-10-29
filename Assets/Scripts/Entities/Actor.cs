using System.Collections.Generic;
using UnityEngine;

namespace Depthbound
{
    public class StatusEffect
    {
        public StatusType type;
        public int turns;
        public float power;
        public Element element;
        public Actor source;
    }

    public class Buff
    {
        public string id, name;
        public readonly List<StatMod> mods = new List<StatMod>();
        public int turns;
    }

    public class ProcEntry
    {
        public ProcTrigger trigger;
        public float chance;
        public string skillId;
        public int rank;
    }

    public abstract class Actor
    {
        static int nextId;
        public readonly int id = ++nextId;

        public string name;
        public Faction faction;
        public int x, y;
        public int level = 1;
        public float life, mana, shield;
        public readonly float[] stats = new float[(int)StatType.COUNT];

        public float weaponMin = 1, weaponMax = 3;
        public int attackRange = 1;
        public Element attackElement = Element.Physical;

        public readonly List<StatusEffect> statuses = new List<StatusEffect>();
        public readonly List<Buff> buffs = new List<Buff>();
        public readonly Dictionary<string, int> cooldowns = new Dictionary<string, int>();
        public readonly HashSet<Keystone> keystones = new HashSet<Keystone>();
        public readonly List<ProcEntry> procs = new List<ProcEntry>();

        public float energy, baseSpeed = 100;
        public bool dead, isBoss;
        public Actor owner;

        public Sprite sprite;
        public Color tint = Color.white;
        public float scale = 1f;

        public Vector2Int Pos => new Vector2Int(x, y);
        public float Get(StatType s) => stats[(int)s];
        public float MaxLife => Mathf.Max(1, Get(StatType.MaxLife));
        public float MaxMana => Mathf.Max(0, Get(StatType.MaxMana));
        public float LifePct => life / MaxLife;
        public bool IsPlayerSide => faction == Faction.Player;
        public bool IsHostileTo(Actor o) => o != null && o.faction != faction;
        public bool Incapacitated => Has(StatusType.Stun) || Has(StatusType.Freeze);
        public float Speed => baseSpeed * (Has(StatusType.Chill) ? 0.6f : 1f);

        public virtual int SkillRank(string id) => 0;
        public abstract void Recalc();

        public bool Has(StatusType t)
        {
            for (int i = 0; i < statuses.Count; i++) if (statuses[i].type == t) return true;
            return false;
        }

        public int Cooldown(string id) => cooldowns.TryGetValue(id, out var c) ? c : 0;

        public void TickCooldowns()
        {
            if (cooldowns.Count == 0) return;
            var keys = new List<string>(cooldowns.Keys);
            foreach (var k in keys)
            {
                int v = cooldowns[k] - 1;
                if (v <= 0) cooldowns.Remove(k); else cooldowns[k] = v;
            }
        }

        public void ClampVitals()
        {
            life = Mathf.Min(life, MaxLife);
            mana = Mathf.Min(mana, MaxMana);
        }

        public void Heal(float amount)
        {
            if (dead || amount <= 0) return;
            life = Mathf.Min(MaxLife, life + amount);
        }

        public void AddStatus(StatusType t, int turns, float power, Element e, Actor src)
        {
            if (t == StatusType.None || dead) return;
            if ((t == StatusType.Stun || t == StatusType.Freeze) && keystones.Contains(Keystone.Juggernaut)) return;
            if (isBoss && (t == StatusType.Stun || t == StatusType.Freeze || t == StatusType.Fear)) turns = Mathf.Max(1, turns / 2);
            if (t == StatusType.Poison)
            {
                int count = 0; StatusEffect weakest = null;
                foreach (var s in statuses)
                {
                    if (s.type != StatusType.Poison) continue;
                    count++;
                    if (weakest == null || s.power < weakest.power) weakest = s;
                }
                if (count >= 8 && weakest != null) statuses.Remove(weakest);
                statuses.Add(new StatusEffect { type = t, turns = turns, power = power, element = e, source = src });
                return;
            }
            foreach (var s in statuses)
            {
                if (s.type != t) continue;
                s.turns = Mathf.Max(s.turns, turns);
                s.power = Mathf.Max(s.power, power);
                s.source = src;
                return;
            }
            statuses.Add(new StatusEffect { type = t, turns = turns, power = power, element = e, source = src });
        }

        public void AddBuff(Buff b)
        {
            buffs.RemoveAll(x => x.id == b.id);
            buffs.Add(b);
            Recalc();
        }

        /// <summary>Turns raw summed modifiers into final stats. Shared by players and minions.</summary>
        protected void Derive(float[] raw)
        {
            for (int i = 0; i < raw.Length; i++) stats[i] = raw[i];
            float aa = raw[(int)StatType.AllAttributes];
            float str = raw[(int)StatType.Strength] + aa;
            float dex = raw[(int)StatType.Dexterity] + aa;
            float intel = raw[(int)StatType.Intelligence] + aa;
            float vit = raw[(int)StatType.Vitality] + aa;
            float spi = raw[(int)StatType.Spirit] + aa;
            Set(StatType.Strength, str); Set(StatType.Dexterity, dex); Set(StatType.Intelligence, intel);
            Set(StatType.Vitality, vit); Set(StatType.Spirit, spi);

            float life = (70 + vit * 5 + (level - 1) * 8 + raw[(int)StatType.MaxLife]) * (1 + raw[(int)StatType.MaxLifePct] / 100f);
            if (keystones.Contains(Keystone.GlassCannon)) life *= 0.65f;
            Set(StatType.MaxLife, life);
            Set(StatType.MaxMana, (25 + spi * 3 + intel + (level - 1) * 2 + raw[(int)StatType.MaxMana]) * (1 + raw[(int)StatType.MaxManaPct] / 100f));
            Set(StatType.LifeRegen, life * 0.004f + vit * 0.03f + raw[(int)StatType.LifeRegen]);
            Set(StatType.ManaRegen, 0.6f + spi * 0.06f + raw[(int)StatType.ManaRegen]);
            Set(StatType.Armor, (raw[(int)StatType.Armor] + str * 0.5f) * (1 + raw[(int)StatType.ArmorPct] / 100f));
            Set(StatType.Evasion, (10 + raw[(int)StatType.Evasion] + dex) * (1 + raw[(int)StatType.EvasionPct] / 100f));
            Set(StatType.Accuracy, 60 + dex * 1.5f + level * 3 + raw[(int)StatType.Accuracy]);
            Set(StatType.BlockChance, raw[(int)StatType.BlockChance] + (keystones.Contains(Keystone.Bulwark) ? 10 : 0));
            Set(StatType.AttackDamage, raw[(int)StatType.AttackDamage] + str * 0.8f + dex * 0.3f);
            Set(StatType.SpellDamage, raw[(int)StatType.SpellDamage] + intel);
            Set(StatType.CritChance, 5 + dex * 0.06f + raw[(int)StatType.CritChance]);
            Set(StatType.CritDamage, 50 + raw[(int)StatType.CritDamage]);
            float allRes = raw[(int)StatType.AllRes];
            foreach (var r in new[] { StatType.FireRes, StatType.ColdRes, StatType.LightningRes, StatType.PoisonRes, StatType.HolyRes, StatType.ShadowRes })
                Set(r, raw[(int)r] + allRes);
            Set(StatType.LightRadius, 7 + raw[(int)StatType.LightRadius]);
        }

        protected void Set(StatType s, float v) => stats[(int)s] = v;

        protected static void AddMods(float[] raw, IEnumerable<StatMod> mods)
        {
            foreach (var m in mods) raw[(int)m.stat] += m.value;
        }
    }
}
