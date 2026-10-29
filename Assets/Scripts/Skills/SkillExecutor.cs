using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Depthbound
{
    public static class SkillExecutor
    {
        static World W => World.Current;

        public static float Cost(Actor c, SkillDef s, int rank, out bool usesLife)
        {
            usesLife = s.lifeCostPct > 0 || c.keystones.Contains(Keystone.BloodMagic);
            if (s.lifeCostPct > 0) return c.MaxLife * s.lifeCostPct / 100f;
            float m = s.ManaCost(rank) * (1f - Mathf.Min(c.Get(StatType.ManaCostReduction), 60f) / 100f);
            return usesLife ? m * 1.2f : m;
        }

        public static int CooldownFor(Actor c, SkillDef s)
        {
            if (s.cooldown <= 0) return 0;
            float cdr = Mathf.Min(c.Get(StatType.CooldownReduction), 50f);
            return Mathf.Max(1, Mathf.RoundToInt(s.cooldown * (1f - cdr / 100f)));
        }

        static bool RequiresHostile(Targeting t) => t == Targeting.Melee || t == Targeting.Target || t == Targeting.Chain || t == Targeting.Dash;

        /// <summary>Returns null if castable, otherwise a reason.</summary>
        public static string CanCast(Actor c, SkillDef s, int rank, Vector2Int target)
        {
            if (rank <= 0) return "You don't know that skill";
            if (s.passive) return "Passive skill";
            if (c.Cooldown(s.id) > 0) return $"{s.name} is on cooldown ({c.Cooldown(s.id)})";
            float cost = Cost(c, s, rank, out bool life);
            if (life ? c.life <= cost + 1 : c.mana < cost) return life ? "Not enough life" : "Not enough mana";

            var map = W.map;
            int d = Combat.Dist(c.Pos, target);
            switch (s.targeting)
            {
                case Targeting.Self:
                case Targeting.Summon:
                    return null;
                case Targeting.Melee:
                {
                    var t = W.ActorAt(target);
                    if (t == null || !c.IsHostileTo(t)) return "No enemy there";
                    if (!Combat.CanMelee(c, t)) return "Target is not adjacent";
                    return null;
                }
                case Targeting.Blink:
                    if (d > s.range) return "Out of range";
                    if (!map.Walkable(target.x, target.y) || W.ActorAt(target) != null) return "Can't teleport there";
                    if (c == W.player && !map.visible[target.x, target.y]) return "Can't see there";
                    if (!map.HasLOS(c.x, c.y, target.x, target.y)) return "No line of sight";
                    return null;
                default:
                {
                    if (target == c.Pos) return "Choose a target";
                    if (d > s.range) return "Out of range";
                    if (!map.HasLOS(c.x, c.y, target.x, target.y)) return "No line of sight";
                    if (RequiresHostile(s.targeting))
                    {
                        var t = W.ActorAt(target);
                        if (t == null || !c.IsHostileTo(t)) return "No enemy there";
                        if (s.targeting == Targeting.Dash && DashLanding(c, t) == null) return "No room to land";
                    }
                    return null;
                }
            }
        }

        public static bool TryCast(Actor c, SkillDef s, int rank, Vector2Int target)
        {
            var reason = CanCast(c, s, rank, target);
            if (reason != null)
            {
                if (c == W.player) W.Log(reason, new Color(0.8f, 0.6f, 0.6f));
                return false;
            }
            float cost = Cost(c, s, rank, out bool life);
            if (life) c.life -= cost; else c.mana -= cost;
            int cd = CooldownFor(c, s);
            if (cd > 0) c.cooldowns[s.id] = cd;
            Cast(c, s, rank, target, false);
            return true;
        }

        public static void Cast(Actor c, SkillDef s, int rank, Vector2Int target, bool isProc)
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
                        int r = s.radius + area;
                        Fx.Burst(c.Pos, r, color);
                        foreach (var t in Hostiles(c, c.Pos, r)) HitWith(c, s, rank, t, isProc);
                    }
                    break;

                case Targeting.Melee:
                {
                    var main = W.ActorAt(target);
                    if (s.radius > 0)
                    {
                        Fx.Burst(c.Pos, 1, color);
                        foreach (var t in Hostiles(c, c.Pos, 1).Where(t => Combat.CanMelee(c, t)).ToList()) HitWith(c, s, rank, t, isProc);
                    }
                    else if (main != null && c.IsHostileTo(main))
                    {
                        Fx.Flash(main.Pos, color);
                        HitWith(c, s, rank, main, isProc);
                    }
                    break;
                }

                case Targeting.Projectile:
                {
                    int count = s.projectiles + Mathf.RoundToInt(c.Get(StatType.ExtraProjectiles));
                    var aims = new List<Vector2Int> { target };
                    if (count > 1)
                    {
                        var others = Hostiles(c, c.Pos, s.range).Where(t => t.Pos != target && W.map.HasLOS(c.x, c.y, t.x, t.y)).ToList();
                        W.rng.Shuffle(others);
                        for (int i = 1; i < count; i++)
                            aims.Add(i - 1 < others.Count ? others[i - 1].Pos : target);
                    }
                    foreach (var aim in aims) FireProjectile(c, s, rank, aim, color, area, isProc);
                    break;
                }

                case Targeting.Ground:
                {
                    int r = s.radius + area;
                    Fx.Burst(target, r, color);
                    var victims = Hostiles(c, target, r);
                    if (s.pull)
                        foreach (var t in victims.OrderBy(v => Combat.Dist(v.Pos, target))) Pull(t, target);
                    foreach (var t in victims) HitWith(c, s, rank, t, isProc);
                    break;
                }

                case Targeting.Chain:
                {
                    var first = W.ActorAt(target);
                    if (first == null || !c.IsHostileTo(first))
                        first = Hostiles(c, target, 1).OrderBy(t => Combat.Dist(t.Pos, target)).FirstOrDefault();
                    if (first == null) break;
                    int chains = s.chains + rank / 3;
                    var hit = new HashSet<Actor>();
                    var from = c.Pos;
                    var cur = first;
                    for (int i = 0; i <= chains && cur != null; i++)
                    {
                        Fx.Line(from, cur.Pos, color);
                        hit.Add(cur);
                        from = cur.Pos;
                        var at = cur.Pos;
                        HitWith(c, s, rank, cur, isProc);
                        cur = Hostiles(c, at, 4).Where(t => !hit.Contains(t) && W.map.HasLOS(at.x, at.y, t.x, t.y))
                            .OrderBy(t => Combat.Dist(t.Pos, at)).FirstOrDefault();
                    }
                    break;
                }

                case Targeting.Dash:
                {
                    var t = W.ActorAt(target);
                    if (t == null) break;
                    var land = DashLanding(c, t);
                    if (land == null) break;
                    Fx.Line(c.Pos, land.Value, color);
                    W.MoveActor(c, land.Value.x, land.Value.y);
                    if (s.radius > 0) foreach (var v in Hostiles(c, c.Pos, s.radius)) HitWith(c, s, rank, v, isProc);
                    else HitWith(c, s, rank, t, isProc);
                    break;
                }

                case Targeting.Blink:
                    if (W.map.Walkable(target.x, target.y) && W.ActorAt(target) == null)
                    {
                        Fx.Burst(c.Pos, 0, s.color);
                        W.MoveActor(c, target.x, target.y);
                        Fx.Burst(c.Pos, 0, s.color);
                    }
                    break;

                case Targeting.Summon:
                    SummonMinion(c, s, rank);
                    break;

                case Targeting.Target:
                {
                    var t = W.ActorAt(target);
                    if (t != null && c.IsHostileTo(t))
                    {
                        Fx.Projectile(c.Pos, t.Pos, color);
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
                    Fx.Text(c.Pos, "Echo!", s.color, 0.8f);
                    Cast(c, s, rank, target, true);
                }
                Combat.TriggerProcs(c, ProcTrigger.OnCast, W.ActorAt(target), target);
            }
        }

        static List<Actor> Hostiles(Actor c, Vector2Int center, int radius)
        {
            var list = new List<Actor>();
            foreach (var a in W.ActorsInRadius(center, radius))
                if (!a.dead && c.IsHostileTo(a)) list.Add(a);
            return list;
        }

        static void HitWith(Actor c, SkillDef s, int rank, Actor t, bool isProc)
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
                    isAttack = s.weapon, melee = s.weapon && Combat.Dist(c, t) <= 1, isProc = isProc,
                    status = s.status, statusChance = s.statusChance, statusTurns = s.statusTurns,
                    bonusCrit = s.bonusCrit, executeBonus = s.executeBonus,
                });
            }
            if (s.knockback && !t.dead && !t.isBoss) Push(t, c.Pos);
        }

        static void FireProjectile(Actor c, SkillDef s, int rank, Vector2Int aim, Color color, int area, bool isProc)
        {
            int dx = aim.x - c.x, dy = aim.y - c.y;
            int n = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
            if (n == 0) return;
            var end = new Vector2Int(c.x + Mathf.RoundToInt(dx * (float)s.range / n), c.y + Mathf.RoundToInt(dy * (float)s.range / n));
            var line = DungeonMap.Line(c.Pos, end);
            var impact = c.Pos;
            Actor explodeOn = null;
            var hitSet = new List<Actor>();
            for (int i = 1; i < line.Count; i++)
            {
                var p = line[i];
                if (!W.map.InBounds(p.x, p.y) || !W.map.Transparent(p.x, p.y)) break;
                impact = p;
                var a = W.ActorAt(p);
                if (a == null || !c.IsHostileTo(a)) continue;
                if (s.radius > 0) { explodeOn = a; break; }
                hitSet.Add(a);
                if (!s.pierce) break;
            }
            Fx.Projectile(c.Pos, impact, color);
            foreach (var a in hitSet) HitWith(c, s, rank, a, isProc);
            if (s.radius > 0 && (explodeOn != null || impact != c.Pos))
            {
                int r = s.radius + area;
                Fx.Burst(impact, r, color);
                foreach (var t in Hostiles(c, impact, r)) HitWith(c, s, rank, t, isProc);
            }
        }

        public static Vector2Int? DashLanding(Actor c, Actor t)
        {
            if (Combat.CanMelee(c, t)) return c.Pos;
            var line = DungeonMap.Line(c.Pos, t.Pos);
            Vector2Int? best = null;
            for (int i = 1; i < line.Count - 1; i++)
            {
                var p = line[i];
                if (!W.map.Walkable(p.x, p.y)) break;
                var a = W.ActorAt(p);
                if (a != null) break;
                best = p;
            }
            if (best == null || Combat.Dist(best.Value, t.Pos) > 1) return null;
            return best;
        }

        static void Push(Actor t, Vector2Int from)
        {
            var dir = new Vector2Int(System.Math.Sign(t.x - from.x), System.Math.Sign(t.y - from.y));
            var dest = t.Pos + dir;
            if (W.IsFree(dest.x, dest.y) && W.map.CanStep(t.x, t.y, dir.x, dir.y)) W.MoveActor(t, dest.x, dest.y);
        }

        static void Pull(Actor t, Vector2Int center)
        {
            for (int step = 0; step < 2; step++)
            {
                var dir = new Vector2Int(System.Math.Sign(center.x - t.x), System.Math.Sign(center.y - t.y));
                if (dir == Vector2Int.zero) return;
                var dest = t.Pos + dir;
                if (!W.IsFree(dest.x, dest.y) || !W.map.CanStep(t.x, t.y, dir.x, dir.y)) return;
                W.MoveActor(t, dest.x, dest.y);
            }
        }

        static void ApplySelfEffects(Actor c, SkillDef s, int rank)
        {
            if (s.healPct > 0)
            {
                float amt = c.MaxLife * (s.healPct + s.healPerRank * (rank - 1)) / 100f;
                c.Heal(amt);
                Fx.Text(c.Pos, "+" + Mathf.RoundToInt(amt), new Color(0.4f, 1f, 0.4f));
            }
            if (s.shieldPct > 0)
            {
                c.shield = Mathf.Max(c.shield, c.MaxLife * (s.shieldPct + s.shieldPerRank * (rank - 1)) / 100f);
                Fx.Text(c.Pos, "Shield", new Color(1f, 0.95f, 0.6f));
            }
            if (s.buffTurns > 0 && s.mods.Count > 0)
            {
                var b = new Buff { id = s.id, name = s.name, turns = s.buffTurns };
                foreach (var m in s.mods) b.mods.Add(new StatMod(m.stat, m.At(rank)));
                c.AddBuff(b);
                Fx.Burst(c.Pos, 0, s.color);
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
            Fx.Burst(spot.Value, 0, s.color);
        }
    }
}
