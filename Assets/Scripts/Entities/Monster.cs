using System.Collections.Generic;
using UnityEngine;

namespace Depthbound
{
    public enum MonsterAffix { Swift, Vampiric, Fiery, Frozen, Shocking, Venomous, Armored, Regenerating, Thorny, Teleporter, Volatile, Berserk, Shielded, Resilient, Mighty }

    public class Monster : Actor
    {
        public MonsterDef def;
        public int rank;              // 0 normal, 1 champion, 2 rare elite, 3 boss
        public bool awake;
        public bool isSummon;
        public float lifetime = -1;

        // Real-time AI state
        public float thinkTimer, windup = -1;
        public Actor windupTarget;
        public Vector2 moveDir;
        public int skillRank = 1;
        public float lifeMult = 1, dmgMult = 1, xpValue;
        public StatusType onHit;
        public float onHitChance;
        public readonly List<MonsterAffix> affixes = new List<MonsterAffix>();
        public readonly List<string> skills = new List<string>();

        public Monster(MonsterDef d, int lvl, Faction f)
        {
            def = d;
            level = Mathf.Max(1, lvl);
            faction = f;
            name = d.name;
            baseSpeed = d.speed;
            attackRange = d.range;
            attackElement = d.element;
            onHit = d.onHit;
            onHitChance = d.onHitChance;
            if (d.skills != null) skills.AddRange(d.skills);
            isBoss = d.boss;
            scale = d.boss ? 1.6f : d.scale;
            radius = Mathf.Clamp(0.32f * scale, 0.22f, 0.48f);
        }

        public override int SkillRank(string id) => skillRank;

        public void AddAffix(MonsterAffix a)
        {
            if (affixes.Contains(a)) return;
            affixes.Add(a);
            switch (a)
            {
                case MonsterAffix.Swift: baseSpeed *= 1.5f; break;
                case MonsterAffix.Fiery: attackElement = Element.Fire; onHit = StatusType.Burn; onHitChance = 30; break;
                case MonsterAffix.Frozen: attackElement = Element.Cold; onHit = StatusType.Chill; onHitChance = 40; break;
                case MonsterAffix.Shocking: attackElement = Element.Lightning; onHit = StatusType.Shock; onHitChance = 35; break;
                case MonsterAffix.Venomous: onHit = StatusType.Poison; onHitChance = 60; break;
                case MonsterAffix.Teleporter: if (!skills.Contains("m_blink")) skills.Add("m_blink"); break;
                case MonsterAffix.Berserk: keystones.Add(Keystone.Berserker); break;
                case MonsterAffix.Mighty: dmgMult *= 1.4f; break;
            }
        }

        public string DisplayName()
        {
            if (rank == 0 || isSummon) return name;
            if (rank == 3) return name;
            var prefix = new List<string>();
            foreach (var a in affixes) prefix.Add(a.ToString());
            return string.Join(" ", prefix) + " " + name;
        }

        public override void Recalc()
        {
            System.Array.Clear(stats, 0, stats.Length);
            float L = level - 1;
            Set(StatType.MaxLife, def.life * 0.7f * (1 + 0.22f * L) * lifeMult);
            Set(StatType.MaxMana, 100);
            weaponMin = def.damage * 0.85f * (1 + 0.14f * L) * dmgMult * 0.8f;
            weaponMax = def.damage * 0.85f * (1 + 0.14f * L) * dmgMult * 1.2f;
            Set(StatType.Armor, def.armor * (1 + 0.12f * L));
            Set(StatType.Evasion, def.evasion * (1 + 0.1f * L));
            Set(StatType.Accuracy, 55 + level * 4);
            Set(StatType.CritChance, 5);
            Set(StatType.CritDamage, 50);
            Set(StatType.SpellDamage, (dmgMult - 1) * 100);
            Set(StatType.LifeRegen, def.regen * MaxLife);
            Set(StatType.FireRes, def.res[0]); Set(StatType.ColdRes, def.res[1]); Set(StatType.LightningRes, def.res[2]);
            Set(StatType.PoisonRes, def.res[3]); Set(StatType.HolyRes, def.res[4]); Set(StatType.ShadowRes, def.res[5]);

            foreach (var a in affixes)
            {
                switch (a)
                {
                    case MonsterAffix.Vampiric: stats[(int)StatType.LifeSteal] += 25; break;
                    case MonsterAffix.Armored: stats[(int)StatType.Armor] *= 2.5f; stats[(int)StatType.DamageReduction] += 20; break;
                    case MonsterAffix.Regenerating: stats[(int)StatType.LifeRegen] += MaxLife * 0.03f; break;
                    case MonsterAffix.Thorny: stats[(int)StatType.Thorns] += 4 + level * 1.5f; break;
                    case MonsterAffix.Resilient:
                        foreach (var r in new[] { StatType.FireRes, StatType.ColdRes, StatType.LightningRes, StatType.PoisonRes, StatType.HolyRes, StatType.ShadowRes })
                            stats[(int)r] += 35;
                        break;
                }
            }
            if (owner != null)
            {
                // Minions inherit some of their master's offense.
                stats[(int)StatType.CritChance] += owner.Get(StatType.CritChance) * 0.5f;
                stats[(int)StatType.Accuracy] += owner.Get(StatType.Accuracy) * 0.5f;
            }
            foreach (var b in buffs) foreach (var m in b.mods) stats[(int)m.stat] += m.value;
            ClampVitals();
        }
    }
}
