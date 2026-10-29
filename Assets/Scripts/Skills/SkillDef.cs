using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Depthbound
{
    public struct SkillStat
    {
        public StatType stat;
        public float baseValue, perRank;
        public float At(int rank) => baseValue + perRank * (rank - 1);
    }

    /// <summary>Data-driven definition of an active or passive skill. Players, monsters and item procs all share it.</summary>
    public class SkillDef
    {
        public static readonly int[] TierLevels = { 1, 1, 3, 6, 10, 15, 20 };
        public const int MaxLearnedRank = 10;

        public string id, name, flavor;
        public School school;
        public int tier = 1;
        public bool passive, hidden, monsterOnly;
        public Color color = Color.white;

        public Targeting targeting = Targeting.Self;
        public int range = 1, radius;
        public float mana, manaPerRank;
        public int cooldown;
        public float lifeCostPct;

        public bool weapon;
        public float dmg, dmgPerRank;
        public Element element = Element.Physical;
        public bool elementFromWeapon;

        public StatusType status;
        public float statusChance;
        public int statusTurns = 3;

        public int projectiles = 1, chains, hits = 1;
        public bool pierce, knockback, pull;
        public float bonusCrit, executeBonus;

        public float healPct, healPerRank, shieldPct, shieldPerRank;
        public readonly List<SkillStat> mods = new List<SkillStat>();
        public int buffTurns;

        public string summonId;
        public int summonMax = 1;

        public ProcTrigger trigger;
        public float procChance, procPerRank;
        public string procSkill;
        public Keystone keystone;

        public int RequiredLevel => TierLevels[Mathf.Clamp(tier, 0, TierLevels.Length - 1)];
        public bool IsAttack => weapon;
        public bool IsBuff => buffTurns > 0;
        public bool DealsDamage => dmg > 0 || dmgPerRank > 0;

        public float ManaCost(int rank) => mana + manaPerRank * (rank - 1);
        public float Damage(int rank) => dmg + dmgPerRank * (rank - 1);
        public float ProcChance(int rank) => procChance + procPerRank * (rank - 1);
        public int SummonCap(int rank) => summonMax + (rank - 1) / 3;

        public static float LevelScale(int level) => 1f + 0.09f * (level - 1);

        // ---- fluent builders ----
        public SkillDef Act(Targeting t, int range = 1, int radius = 0) { targeting = t; this.range = range; this.radius = radius; return this; }
        public SkillDef Cost(float m, float perRank, int cd) { mana = m; manaPerRank = perRank; cooldown = cd; return this; }
        public SkillDef LifeCost(float pct) { lifeCostPct = pct; return this; }
        public SkillDef Weap(float pct, float perRank) { weapon = true; dmg = pct; dmgPerRank = perRank; elementFromWeapon = true; return this; }
        public SkillDef Weap(float pct, float perRank, Element e) { weapon = true; dmg = pct; dmgPerRank = perRank; element = e; return this; }
        public SkillDef Spell(float b, float perRank, Element e) { dmg = b; dmgPerRank = perRank; element = e; return this; }
        public SkillDef Status(StatusType s, float chance, int turns = 3) { status = s; statusChance = chance; statusTurns = turns; return this; }
        public SkillDef Proj(int count, bool pierce = false) { projectiles = count; this.pierce = pierce; return this; }
        public SkillDef Chain(int n) { chains = n; return this; }
        public SkillDef Hits(int n) { hits = n; return this; }
        public SkillDef Crit(float bonus) { bonusCrit = bonus; return this; }
        public SkillDef Execute(float bonus) { executeBonus = bonus; return this; }
        public SkillDef Knock() { knockback = true; return this; }
        public SkillDef Pull() { pull = true; return this; }
        public SkillDef Heal(float b, float perRank) { healPct = b; healPerRank = perRank; return this; }
        public SkillDef Shield(float b, float perRank) { shieldPct = b; shieldPerRank = perRank; return this; }
        public SkillDef Mod(StatType s, float b, float perRank) { mods.Add(new SkillStat { stat = s, baseValue = b, perRank = perRank }); return this; }
        public SkillDef Buff(int turns) { buffTurns = turns; return this; }
        public SkillDef Summon(string id, int max) { targeting = Targeting.Summon; summonId = id; summonMax = max; return this; }
        public SkillDef Proc(ProcTrigger t, float chance, float perRank, string skill) { trigger = t; procChance = chance; procPerRank = perRank; procSkill = skill; return this; }
        public SkillDef Key(Keystone k) { keystone = k; return this; }
        public SkillDef Passive() { passive = true; return this; }
        public SkillDef Hidden() { hidden = true; return this; }
        public SkillDef MonsterOnly() { hidden = true; monsterOnly = true; return this; }

        public string TargetingLabel()
        {
            if (passive) return "Passive";
            switch (targeting)
            {
                case Targeting.Self: return radius > 0 ? $"Nova (radius {radius})" : "Self";
                case Targeting.Melee: return "Melee";
                case Targeting.Projectile: return $"Projectile (range {range})";
                case Targeting.Ground: return $"Area (range {range}, radius {radius})";
                case Targeting.Chain: return $"Chain (range {range})";
                case Targeting.Dash: return $"Leap (range {range})";
                case Targeting.Blink: return $"Teleport (range {range})";
                case Targeting.Summon: return "Summon";
                default: return $"Targeted (range {range})";
            }
        }

        /// <summary>Human readable effect lines for a given rank. Caster may be null.</summary>
        public string Describe(int rank, Actor caster)
        {
            rank = Mathf.Max(1, rank);
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(flavor)) sb.AppendLine(flavor);
            if (DealsDamage)
            {
                string el = elementFromWeapon ? "weapon element" : element.ToString();
                if (weapon) sb.AppendLine($"Deals {Mathf.RoundToInt(Damage(rank))}% weapon damage ({el}).");
                else
                {
                    int lvl = caster != null ? caster.level : 1;
                    sb.AppendLine($"Deals {Mathf.RoundToInt(Damage(rank) * LevelScale(lvl))} {el} damage (scales with level).");
                }
                if (hits > 1) sb.AppendLine($"Strikes {hits} times.");
                if (projectiles > 1) sb.AppendLine($"Fires {projectiles} projectiles.");
                if (pierce) sb.AppendLine("Pierces through enemies.");
                if (chains > 0) sb.AppendLine($"Chains to {chains + rank / 3} additional enemies.");
                if (bonusCrit > 0) sb.AppendLine($"+{bonusCrit}% critical chance.");
                if (executeBonus > 0) sb.AppendLine($"+{executeBonus}% damage to enemies below 30% life.");
            }
            if (status != StatusType.None) sb.AppendLine($"{Mathf.RoundToInt(statusChance)}% chance to inflict {status} ({statusTurns}s).");
            if (healPct > 0) sb.AppendLine($"Heals {healPct + healPerRank * (rank - 1):0}% of maximum life.");
            if (shieldPct > 0) sb.AppendLine($"Grants a shield equal to {shieldPct + shieldPerRank * (rank - 1):0}% of maximum life.");
            if (knockback) sb.AppendLine("Knocks enemies back.");
            if (pull) sb.AppendLine("Pulls enemies toward the center.");
            foreach (var m in mods) sb.AppendLine(StatInfo.Describe(m.stat, m.At(rank)) + (buffTurns > 0 ? $" for {buffTurns}s" : ""));
            if (!string.IsNullOrEmpty(summonId))
            {
                var md = MonsterDatabase.Get(summonId);
                sb.AppendLine($"Summons a {md?.name ?? summonId}. Maximum {SummonCap(rank)}.");
            }
            if (trigger != ProcTrigger.None && procSkill != null)
            {
                var ps = SkillDatabase.Get(procSkill);
                sb.AppendLine($"{ProcChance(rank):0}% chance {TriggerText(trigger)} to cast {ps?.name ?? procSkill}.");
            }
            if (keystone != Keystone.None) sb.AppendLine("KEYSTONE - " + EnumUtil.KeystoneDesc(keystone));
            if (!passive)
            {
                var cost = lifeCostPct > 0 ? $"{lifeCostPct:0}% life" : $"{Mathf.RoundToInt(ManaCost(rank))} mana";
                sb.Append($"Cost: {cost}");
                if (cooldown > 0) sb.Append($"   Cooldown: {cooldown}s");
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        public static string TriggerText(ProcTrigger t)
        {
            switch (t)
            {
                case ProcTrigger.OnHit: return "on hit";
                case ProcTrigger.OnCrit: return "on critical strike";
                case ProcTrigger.OnKill: return "on kill";
                case ProcTrigger.OnStruck: return "when struck";
                case ProcTrigger.OnCast: return "when you use a skill";
                default: return "";
            }
        }
    }
}
