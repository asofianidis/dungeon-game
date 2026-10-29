using System.Collections.Generic;
using UnityEngine;

namespace Depthbound
{
    public class StatusEffect
    {
        public StatusType type;
        public float remaining;
        public float power;     // damage per second for damage-over-time effects
        public Element element;
        public Actor source;
        public float tick;
    }

    public class Buff
    {
        public string id, name;
        public readonly List<StatMod> mods = new List<StatMod>();
        public float remaining;
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
        public const float MeleeReach = 0.6f;

        static int nextId;
        public readonly int id = ++nextId;

        public string name;
        public Faction faction;
        public Vector2 pos;
        public Vector2 facing = new Vector2(0, -1);
        public float radius = 0.34f;
        public int level = 1;
        public float life, mana, shield;
        public readonly float[] stats = new float[(int)StatType.COUNT];

        public float weaponMin = 1, weaponMax = 3;
        public int attackRange = 1;
        public Element attackElement = Element.Physical;

        public readonly List<StatusEffect> statuses = new List<StatusEffect>();
        public readonly List<Buff> buffs = new List<Buff>();
        public readonly Dictionary<string, float> cooldowns = new Dictionary<string, float>();
        public readonly HashSet<Keystone> keystones = new HashSet<Keystone>();
        public readonly List<ProcEntry> procs = new List<ProcEntry>();

        /// <summary>Seconds until this actor may attack or cast again.</summary>
        public float actionLock;
        public float baseSpeed = 100;
        public bool dead, isBoss;
        public Actor owner;

        public Sprite sprite;
        public Color tint = Color.white;
        public float scale = 1f;

        /// <summary>Tile the actor's center is in.</summary>
        public int x => Mathf.RoundToInt(pos.x);
        public int y => Mathf.RoundToInt(pos.y);
        public Vector2Int Pos => new Vector2Int(x, y);

        public float Get(StatType s) => stats[(int)s];
        public float MaxLife => Mathf.Max(1, Get(StatType.MaxLife));
        public float MaxMana => Mathf.Max(0, Get(StatType.MaxMana));
        public float LifePct => life / MaxLife;
        public bool IsPlayerSide => faction == Faction.Player;
        public bool IsHostileTo(Actor o) => o != null && o.faction != faction;
        public bool Incapacitated => Has(StatusType.Stun) || Has(StatusType.Freeze);

        public float SpeedMult => baseSpeed / 100f * (Has(StatusType.Chill) ? 0.6f : 1f);
        public float MoveSpeed => (this is Player ? 4.4f : 2.7f) * SpeedMult * Mathf.Max(0.3f, 1f + Get(StatType.MoveSpeed) / 100f);

        /// <summary>Scales a base action duration by speed and attack/cast speed.</summary>
        public float ActionTime(float baseSeconds) => baseSeconds / Mathf.Max(0.25f, SpeedMult * (1f + Get(StatType.ActionSpeed) / 100f));

        public virtual int SkillRank(string id) => 0;
        public abstract void Recalc();

        public bool Has(StatusType t)
        {
            for (int i = 0; i < statuses.Count; i++) if (statuses[i].type == t) return true;
            return false;
        }

        public float Cooldown(string id) => cooldowns.TryGetValue(id, out var c) ? c : 0;

        readonly List<string> cdKeys = new List<string>();
        public void TickCooldowns(float dt)
        {
            if (cooldowns.Count == 0) return;
            cdKeys.Clear();
            cdKeys.AddRange(cooldowns.Keys);
            foreach (var k in cdKeys)
            {
                float v = cooldowns[k] - dt;
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

        public void AddStatus(StatusType t, float duration, float power, Element e, Actor src)
        {
            if (t == StatusType.None || dead) return;
            if ((t == StatusType.Stun || t == StatusType.Freeze) && keystones.Contains(Keystone.Juggernaut)) return;
            if (isBoss && (t == StatusType.Stun || t == StatusType.Freeze || t == StatusType.Fear)) duration = Mathf.Max(0.5f, duration / 2f);
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
                statuses.Add(new StatusEffect { type = t, remaining = duration, power = power, element = e, source = src });
                return;
            }
            foreach (var s in statuses)
            {
                if (s.type != t) continue;
                s.remaining = Mathf.Max(s.remaining, duration);
                s.power = Mathf.Max(s.power, power);
                s.source = src;
                return;
            }
            statuses.Add(new StatusEffect { type = t, remaining = duration, power = power, element = e, source = src });
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
            Set(StatType.ActionSpeed, raw[(int)StatType.ActionSpeed] + dex * 0.2f);
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
