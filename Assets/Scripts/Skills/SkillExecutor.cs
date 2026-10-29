using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Depthbound
{
    public static class SkillExecutor
    {
        static World W => World.Current;

        /// <summary>Skill radii are authored in tiles; this converts them to a world-space radius.</summary>
        public static float WorldRadius(int r) => r + 0.6f;
        const float TargetSnap = 1.1f;

        public static float Cost(Actor c, SkillDef s, int rank, out bool usesLife)
        {
            usesLife = s.lifeCostPct > 0 || c.keystones.Contains(Keystone.BloodMagic);
            if (s.lifeCostPct > 0) return c.MaxLife * s.lifeCostPct / 100f;
            float m = s.ManaCost(rank) * (1f - Mathf.Min(c.Get(StatType.ManaCostReduction), 60f) / 100f);
            return usesLife ? m * 1.2f : m;
        }

        public static float CooldownFor(Actor c, SkillDef s)
        {
            if (s.cooldown <= 0) return 0;
            float cdr = Mathf.Min(c.Get(StatType.CooldownReduction), 50f);
            return Mathf.Max(0.5f, s.cooldown * (1f - cdr / 100f));
        }

        static bool RequiresHostile(Targeting t) => t == Targeting.Target || t == Targeting.Chain || t == Targeting.Dash;

        /// <summary>Returns null if castable, otherwise a reason.</summary>
        public static string CanCast(Actor c, SkillDef s, int rank, Vector2 target)
        {
            if (rank <= 0) return "You don't know that skill";
            if (s.passive) return "Passive skill";
            if (c.Cooldown(s.id) > 0) return $"{s.name} is on cooldown";
            float cost = Cost(c, s, rank, out bool life);
            if (life ? c.life <= cost + 1 : c.mana < cost) return life ? "Not enough life" : "Not enough mana";

            var map = W.map;
            float d = Vector2.Distance(c.pos, target);
            var tt = new Vector2Int(Mathf.RoundToInt(target.x), Mathf.RoundToInt(target.y));
            switch (s.targeting)
            {
                case Targeting.Self:
                case Targeting.Summon:
                case Targeting.Projectile:
                    return null;
                case Targeting.Melee:
                    if (s.radius > 0) return Hostiles(c, c.pos, Combat.Reach(c, c) + 0.6f).Any() ? null : "No enemy in reach";
                    return Combat.MeleeTarget(c, target - c.pos) != null ? null : "No enemy in reach";
                case Targeting.Blink:
                    if (d > s.range + 0.5f) return "Out of range";
                    if (!map.InBounds(tt.x, tt.y) || W.Blocked(target, c.radius, false)) return "Can't teleport there";
                    if (c == W.player && !map.visible[tt.x, tt.y]) return "Can't see there";
                    if (!map.HasLOS(c.x, c.y, tt.x, tt.y)) return "No line of sight";
                    return null;
                default:
                {
                    if (d > s.range + 0.6f) return "Out of range";
                    if (!map.InBounds(tt.x, tt.y) || !map.HasLOS(c.x, c.y, tt.x, tt.y)) return "No line of sight";
                    if (RequiresHostile(s.targeting))
                    {
                        var t = W.HostileNear(c, target, TargetSnap);
                        if (t == null) return "No enemy there";
                        if (s.targeting == Targeting.Dash && DashLanding(c, t) == null) return "No room to land";
                    }
                    return null;
                }
            }
        }

        public static bool TryCast(Actor c, SkillDef s, int rank, Vector2 target)
        {
            var reason = CanCast(c, s, rank, target);
            if (reason != null)
            {
                if (c == W.player) W.Log(reason, new Color(0.8f, 0.6f, 0.6f));
                return false;
            }
            float cost = Cost(c, s, rank, out bool life);
            if (life) c.life -= cost; else c.mana -= cost;
            float cd = CooldownFor(c, s);
            if (cd > 0) c.cooldowns[s.id] = cd;
            var dir = target - c.pos;
            if (dir.sqrMagnitude > 0.01f) c.facing = dir.normalized;
            Cast(c, s, rank, target, false);
            return true;
        }

        public static void Cast(Actor c, SkillDef s, int rank, Vector2 target, bool isProc)
        {
            if (c.dead) return;
            var elem = s.elementFromWeapon ? c.attackElement : s.element;
            var color = s.DealsDamage ? SkillDatabase.ElementColor(Combat.Convert(c, elem)) : s.color;
            int area = Mathf.RoundToInt(c.Get(StatType.AreaRadius));

            switch (s.targeting)
            {
                case Targeting.Self:
                    if (s.radius > 0)
                    {
                        float r = WorldRadius(s.radius + area);
                        Fx.Burst(c.pos, r, color);
                        foreach (var t in Hostiles(c, c.pos, r)) HitWith(c, s, rank, t, isProc);
                    }
                    break;

                case Targeting.Melee:
                {
                    if (s.radius > 0)
                    {
                        float r = Combat.Reach(c, c) + 0.6f;
                        Fx.Burst(c.pos, r, color);
                        foreach (var t in Hostiles(c, c.pos, r).ToList()) HitWith(c, s, rank, t, isProc);
                    }
                    else
                    {
                        var main = isProc ? W.HostileNear(c, target, TargetSnap) : Combat.MeleeTarget(c, target - c.pos);
                        if (main != null) { Fx.Flash(main.pos, color); HitWith(c, s, rank, main, isProc); }
                    }
                    break;
                }

                case Targeting.Projectile:
                {
                    int count = s.projectiles + Mathf.RoundToInt(c.Get(StatType.ExtraProjectiles));
                    var dir = target - c.pos;
                    if (dir.sqrMagnitude < 0.01f) dir = c.facing;
                    dir.Normalize();
                    float spread = count > 1 ? Mathf.Min(14f, 60f / count) : 0;
                    for (int i = 0; i < count; i++)
                    {
                        float angle = (i - (count - 1) / 2f) * spread;
                        W.SpawnSkillProjectile(c, s, rank, Combat.Rotate(dir, angle), color, s.radius > 0 ? s.radius + area : 0, isProc);
                    }
                    break;
                }

                case Targeting.Ground:
                {
                    float r = WorldRadius(s.radius + area);
                    Fx.Burst(target, r, color);
                    var victims = Hostiles(c, target, r);
                    if (s.pull) foreach (var t in victims) Pull(t, target);
                    foreach (var t in victims) HitWith(c, s, rank, t, isProc);
                    break;
                }

                case Targeting.Chain:
                {
                    var first = W.HostileNear(c, target, TargetSnap + 0.5f);
                    if (first == null) break;
                    int chains = s.chains + rank / 3;
                    var hit = new HashSet<Actor>();
                    var from = c.pos;
                    var cur = first;
                    for (int i = 0; i <= chains && cur != null; i++)
                    {
                        Fx.Line(from, cur.pos, color);
                        hit.Add(cur);
                        from = cur.pos;
                        var at = cur.pos;
                        HitWith(c, s, rank, cur, isProc);
                        cur = Hostiles(c, at, 4.5f).Where(t => !hit.Contains(t)).OrderBy(t => Vector2.Distance(t.pos, at)).FirstOrDefault();
                    }
                    break;
                }

                case Targeting.Dash:
                {
                    var t = W.HostileNear(c, target, TargetSnap);
                    if (t == null) break;
                    var land = DashLanding(c, t);
                    if (land == null) break;
                    Fx.Line(c.pos, land.Value, color);
                    c.pos = land.Value;
                    if (s.radius > 0) foreach (var v in Hostiles(c, c.pos, WorldRadius(s.radius))) HitWith(c, s, rank, v, isProc);
                    else HitWith(c, s, rank, t, isProc);
                    break;
                }

                case Targeting.Blink:
                    if (!W.Blocked(target, c.radius, false))
                    {
                        Fx.Burst(c.pos, 0.5f, s.color);
                        c.pos = target;
                        Fx.Burst(c.pos, 0.5f, s.color);
                    }
                    break;

                case Targeting.Summon:
                    SummonMinion(c, s, rank);
                    break;

                case Targeting.Target:
                {
                    var t = W.HostileNear(c, target, TargetSnap);
                    if (t != null)
                    {
                        Fx.Projectile(c.pos, t.pos, color);
                        HitWith(c, s, rank, t, isProc);
                    }
                    break;
                }
            }

            ApplySelfEffects(c, s, rank);

            if (!isProc)
            {
                if (c.keystones.Contains(Keystone.Echo) && !s.weapon && s.DealsDamage && W.rng.Chance(0.25f))
                {
                    Fx.Text(c.pos, "Echo!", s.color, 0.8f);
                    Cast(c, s, rank, target, true);
                }
                Combat.TriggerProcs(c, ProcTrigger.OnCast, W.HostileNear(c, target, TargetSnap), target);
            }
        }

        public static IEnumerable<Actor> Hostiles(Actor c, Vector2 center, float radius)
        {
            var list = new List<Actor>();
            foreach (var a in W.ActorsInRadius(center, radius))
                if (!a.dead && c.IsHostileTo(a)) list.Add(a);
            return list;
        }

        /// <summary>Applies a skill's damage/status to one target.</summary>
        public static void HitWith(Actor c, SkillDef s, int rank, Actor t, bool isProc)
        {
            if (t.dead) return;
            if (!s.DealsDamage)
            {
                if (s.status != StatusType.None && W.rng.Chance((s.statusChance + c.Get(StatType.StatusChance)) / 100f))
                    Combat.ApplyStatus(c, t, s.status, s.statusTurns, 0, s.element);
                return;
            }
            int hits = s.hits;
            if (s.weapon && W.rng.Chance(c.Get(StatType.DoubleStrike) / 100f)) hits++;
            var elem = s.elementFromWeapon ? c.attackElement : s.element;
            for (int i = 0; i < hits && !t.dead && !c.dead; i++)
            {
                float baseD = s.weapon ? Combat.RollWeapon(c) * s.Damage(rank) / 100f : s.Damage(rank) * SkillDef.LevelScale(c.level);
                Combat.Deal(new HitInfo
                {
                    source = c, target = t, baseDamage = baseD, element = elem,
                    isAttack = s.weapon, melee = s.weapon && Combat.CanMelee(c, t), isProc = isProc,
                    status = s.status, statusChance = s.statusChance, statusTime = s.statusTurns,
                    bonusCrit = s.bonusCrit, executeBonus = s.executeBonus,
                });
            }
            if (s.knockback && !t.dead && !t.isBoss) Push(t, c.pos);
        }

        /// <summary>Area burst used by exploding projectiles.</summary>
        public static void Explosion(Actor c, SkillDef s, int rank, Vector2 center, int radius, bool isProc)
        {
            float r = WorldRadius(radius);
            var elem = s.elementFromWeapon ? c.attackElement : s.element;
            Fx.Burst(center, r, SkillDatabase.ElementColor(Combat.Convert(c, elem)));
            foreach (var t in Hostiles(c, center, r)) HitWith(c, s, rank, t, isProc);
        }

        public static Vector2? DashLanding(Actor c, Actor t)
        {
            if (Combat.CanMelee(c, t)) return c.pos;
            var delta = t.pos - c.pos;
            float len = delta.magnitude;
            var dir = delta / len;
            float stop = len - (c.radius + t.radius + 0.25f);
            Vector2? best = null;
            for (float d = 0.25f; d <= stop; d += 0.25f)
            {
                var p = c.pos + dir * d;
                if (W.Blocked(p, c.radius, false)) break;
                best = p;
            }
            if (best == null || Vector2.Distance(best.Value, t.pos) > Combat.Reach(c, t)) return null;
            return best;
        }

        static void Push(Actor t, Vector2 from)
        {
            var dir = t.pos - from;
            if (dir.sqrMagnitude < 0.001f) return;
            W.TryMove(t, dir.normalized * 1.3f);
        }

        static void Pull(Actor t, Vector2 center)
        {
            var d = center - t.pos;
            float len = d.magnitude;
            if (len < 0.7f) return;
            W.TryMove(t, d / len * Mathf.Min(len - 0.6f, 2f));
        }

        static void ApplySelfEffects(Actor c, SkillDef s, int rank)
        {
            if (s.healPct > 0)
            {
                float amt = c.MaxLife * (s.healPct + s.healPerRank * (rank - 1)) / 100f;
                c.Heal(amt);
                Fx.Text(c.pos, "+" + Mathf.RoundToInt(amt), new Color(0.4f, 1f, 0.4f));
            }
            if (s.shieldPct > 0)
            {
                c.shield = Mathf.Max(c.shield, c.MaxLife * (s.shieldPct + s.shieldPerRank * (rank - 1)) / 100f);
                Fx.Text(c.pos, "Shield", new Color(1f, 0.95f, 0.6f));
            }
            if (s.buffTurns > 0 && s.mods.Count > 0)
            {
                var b = new Buff { id = s.id, name = s.name, remaining = s.buffTurns };
                foreach (var m in s.mods) b.mods.Add(new StatMod(m.stat, m.At(rank)));
                c.AddBuff(b);
                Fx.Burst(c.pos, 0.6f, s.color);
            }
        }

        static void SummonMinion(Actor c, SkillDef s, int rank)
        {
            var def = MonsterDatabase.Get(s.summonId);
            if (def == null) return;
            int cap = s.SummonCap(rank) + (c.keystones.Contains(Keystone.Necrolord) ? 2 : 0);
            var mine = W.actors.OfType<Monster>().Where(m => m.owner == c && m.def == def && !m.dead).ToList();
            if (mine.Count >= cap) W.Kill(mine[0], null);
            var spot = W.FindFreeNear(c.Pos, 3);
            if (spot == null) return;
            var minion = W.CreateMonster(def, c.level, c.faction, 0);
            minion.owner = c;
            minion.isSummon = true;
            minion.awake = true;
            minion.skillRank = rank;
            if (c.faction == Faction.Player)
            {
                minion.lifeMult = (1f + 0.15f * (rank - 1)) * (1f + c.Get(StatType.SummonLife) / 100f);
                minion.dmgMult = (1f + 0.12f * (rank - 1)) * (1f + c.Get(StatType.SummonDamage) / 100f);
            }
            minion.Recalc();
            minion.life = minion.MaxLife;
            W.Place(minion, spot.Value);
            Fx.Burst(spot.Value, 0.5f, s.color);
        }
    }
}
