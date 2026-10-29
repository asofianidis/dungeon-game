using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Depthbound
{
    public static class MonsterAI
    {
        static readonly Vector2Int[] Dirs =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
            new Vector2Int(1, 1), new Vector2Int(-1, 1), new Vector2Int(1, -1), new Vector2Int(-1, -1),
        };

        public static void Act(World w, Monster m)
        {
            if (m.faction == Faction.Player) { ActAlly(w, m); return; }

            var p = w.player;
            if (!m.awake)
            {
                if (w.map.visible[m.x, m.y] && Combat.Dist(m, p) <= 9)
                {
                    m.awake = true;
                    foreach (var o in w.ActorsInRadius(m.Pos, 5))
                        if (o is Monster om && om.faction == Faction.Monster) om.awake = true;
                }
                else
                {
                    if (w.rng.Chance(0.15f)) RandomStep(w, m);
                    return;
                }
            }

            // Pick a target: the player, or a player minion that is in the way.
            Actor target = p;
            if (!Combat.CanMelee(m, p))
            {
                var ally = w.ActorsInRadius(m.Pos, 1).FirstOrDefault(a => a.faction == Faction.Player && a != p && Combat.CanMelee(m, a));
                if (ally != null) target = ally;
            }
            int d = Combat.Dist(m, target);

            if (m.Has(StatusType.Fear)) { StepAway(w, m, target); return; }
            if (TryUseSkill(w, m, target, d)) return;

            switch (m.def.ai)
            {
                case AiType.Exploder:
                    if (Combat.CanMelee(m, target))
                    {
                        Combat.Explode(m, Faction.Monster, m.Pos, 1, m.weaponMax * 1.5f, m.attackElement);
                        w.Kill(m, null);
                    }
                    else StepToward(w, m, target);
                    break;
                case AiType.Ranged:
                case AiType.Caster:
                case AiType.Summoner:
                {
                    bool los = w.map.HasLOS(m.x, m.y, target.x, target.y);
                    if (d <= m.attackRange && los)
                    {
                        if (d <= 2 && w.rng.Chance(0.5f) && StepAway(w, m, target)) break;
                        Combat.BasicAttack(m, target);
                    }
                    else StepToward(w, m, target);
                    break;
                }
                default:
                    if (Combat.CanMelee(m, target)) Combat.BasicAttack(m, target);
                    else StepToward(w, m, target);
                    break;
            }
        }

        static void ActAlly(World w, Monster m)
        {
            var p = w.player;
            Monster target = null; int best = int.MaxValue;
            foreach (var a in w.actors)
            {
                if (a.dead || a.faction != Faction.Monster || !(a is Monster e)) continue;
                int d = Combat.Dist(m, e);
                if (d > 8 || d >= best || !w.map.HasLOS(m.x, m.y, e.x, e.y)) continue;
                best = d; target = e;
            }
            if (target != null)
            {
                if (TryUseSkill(w, m, target, best)) return;
                if (Combat.CanMelee(m, target)) Combat.BasicAttack(m, target);
                else StepToward(w, m, target);
                return;
            }
            if (Combat.Dist(m, p) > 2) StepToward(w, m, p);
            else if (w.rng.Chance(0.2f)) RandomStep(w, m);
        }

        static bool TryUseSkill(World w, Monster m, Actor target, int d)
        {
            if (m.skills.Count == 0 || !w.rng.Chance(0.45f)) return false;
            var order = m.skills.ToList();
            w.rng.Shuffle(order);
            foreach (var id in order)
            {
                var s = SkillDatabase.Get(id);
                if (s == null || m.Cooldown(id) > 0) continue;
                var aim = target.Pos;
                bool los = w.map.HasLOS(m.x, m.y, target.x, target.y);
                switch (s.targeting)
                {
                    case Targeting.Summon:
                    {
                        var def = MonsterDatabase.Get(s.summonId);
                        int mine = w.actors.Count(a => a is Monster o && o.owner == m && o.def == def && !o.dead);
                        if (mine >= s.SummonCap(m.skillRank) || d > 8) continue;
                        break;
                    }
                    case Targeting.Self:
                        if (s.healPct > 0) { if (m.LifePct > 0.5f) continue; }
                        else if (s.radius > 0 && d > s.radius) continue;
                        aim = m.Pos;
                        break;
                    case Targeting.Blink:
                    {
                        if (d > 3 && m.def.ai == AiType.Melee) continue;
                        var spot = BlinkSpot(w, m, target, s.range);
                        if (spot == null) continue;
                        aim = spot.Value;
                        break;
                    }
                    case Targeting.Dash:
                        if (d < 2 || !los) continue;
                        break;
                    default:
                        if (!los || d > s.range) continue;
                        break;
                }
                if (SkillExecutor.TryCast(m, s, m.skillRank, aim)) return true;
            }
            return false;
        }

        static Vector2Int? BlinkSpot(World w, Monster m, Actor target, int range)
        {
            var opts = new List<Vector2Int>();
            for (int dx = -range; dx <= range; dx++)
                for (int dy = -range; dy <= range; dy++)
                {
                    int x = m.x + dx, y = m.y + dy;
                    if (!w.IsFree(x, y) || w.map.Get(x, y) == Tile.Lava) continue;
                    int dt = Combat.Dist(new Vector2Int(x, y), target.Pos);
                    bool wantClose = m.def.ai == AiType.Melee;
                    if (wantClose ? dt != 1 : (dt < 3 || dt > 5)) continue;
                    if (!w.map.HasLOS(m.x, m.y, x, y) || !w.map.HasLOS(x, y, target.x, target.y)) continue;
                    opts.Add(new Vector2Int(x, y));
                }
            return opts.Count > 0 ? w.rng.Pick(opts) : (Vector2Int?)null;
        }

        static bool CanEnter(World w, Monster m, int dx, int dy)
        {
            int nx = m.x + dx, ny = m.y + dy;
            return w.map.CanStep(m.x, m.y, dx, dy) && w.ActorAt(nx, ny) == null && w.map.Get(nx, ny) != Tile.Lava;
        }

        public static bool StepToward(World w, Monster m, Actor target)
        {
            var dist = w.playerDist;
            if (target == w.player && dist[m.x, m.y] != int.MaxValue)
            {
                int cur = dist[m.x, m.y];
                Vector2Int? best = null, sidestep = null;
                int bestD = cur;
                foreach (var d in Dirs)
                {
                    if (!CanEnter(w, m, d.x, d.y)) continue;
                    int nd = dist[m.x + d.x, m.y + d.y];
                    if (nd < bestD) { bestD = nd; best = d; }
                    else if (nd == cur && sidestep == null) sidestep = d;
                }
                var step = best ?? sidestep;
                if (step == null) return false;
                w.MoveActor(m, m.x + step.Value.x, m.y + step.Value.y);
                return true;
            }
            return Greedy(w, m, target.Pos, true);
        }

        static bool Greedy(World w, Monster m, Vector2Int goal, bool toward)
        {
            Vector2Int? best = null;
            float bestScore = toward ? Combat.Dist(m.Pos, goal) + 0.01f * (m.Pos - goal).sqrMagnitude
                                     : -(Combat.Dist(m.Pos, goal) + 0.01f * (m.Pos - goal).sqrMagnitude);
            foreach (var d in Dirs)
            {
                if (!CanEnter(w, m, d.x, d.y)) continue;
                var np = m.Pos + d;
                float score = Combat.Dist(np, goal) + 0.01f * (np - goal).sqrMagnitude;
                if (!toward) score = -score;
                if (score < bestScore) { bestScore = score; best = d; }
            }
            if (best == null) return false;
            w.MoveActor(m, m.x + best.Value.x, m.y + best.Value.y);
            return true;
        }

        static bool StepAway(World w, Monster m, Actor target)
        {
            if (target == w.player && w.playerDist[m.x, m.y] != int.MaxValue)
            {
                Vector2Int? best = null;
                int bestD = w.playerDist[m.x, m.y];
                foreach (var d in Dirs)
                {
                    if (!CanEnter(w, m, d.x, d.y)) continue;
                    int nd = w.playerDist[m.x + d.x, m.y + d.y];
                    if (nd != int.MaxValue && nd > bestD) { bestD = nd; best = d; }
                }
                if (best == null) return false;
                w.MoveActor(m, m.x + best.Value.x, m.y + best.Value.y);
                return true;
            }
            return Greedy(w, m, target.Pos, false);
        }

        static void RandomStep(World w, Monster m)
        {
            var d = Dirs[w.rng.Range(0, Dirs.Length)];
            if (CanEnter(w, m, d.x, d.y)) w.MoveActor(m, m.x + d.x, m.y + d.y);
        }
    }
}
