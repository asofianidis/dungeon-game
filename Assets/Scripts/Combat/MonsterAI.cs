using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Depthbound
{
    /// <summary>Real-time behaviour: monsters re-think a few times a second and steer continuously in between.</summary>
    public static class MonsterAI
    {
        static readonly Vector2Int[] Dirs =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
            new Vector2Int(1, 1), new Vector2Int(-1, 1), new Vector2Int(1, -1), new Vector2Int(-1, -1),
        };

        public const float MeleeWindup = 0.35f;

        public static void Tick(World w, Monster m, float dt)
        {
            if (m.Incapacitated) { m.windup = -1; m.windupTarget = null; return; }
            if (m.windup >= 0)
            {
                m.windup -= dt;
                if (m.windup < 0) FinishWindup(w, m);
                return;
            }
            m.thinkTimer -= dt;
            if (m.thinkTimer <= 0)
            {
                m.thinkTimer = 0.12f + w.rng.Range(0f, 0.1f);
                if (m.faction == Faction.Player) ThinkAlly(w, m); else Think(w, m);
            }
            if (m.windup < 0 && m.moveDir.sqrMagnitude > 0.001f)
            {
                w.TryMove(m, m.moveDir * m.MoveSpeed * dt);
                m.facing = m.moveDir;
            }
        }

        static void FinishWindup(World w, Monster m)
        {
            var t = m.windupTarget;
            m.windupTarget = null;
            m.windup = -1;
            m.actionLock = m.ActionTime(0.8f);
            if (t == null || t.dead) return;
            // The swing lands only if the target is still (roughly) in reach: dodging away works.
            if (Combat.Dist(m, t) <= Combat.Reach(m, t) + 0.3f && w.map.HasLOS(m.x, m.y, t.x, t.y)) Combat.BasicAttack(m, t);
        }

        static void StartWindup(Monster m, Actor t, float time)
        {
            m.windup = time;
            m.windupTarget = t;
            m.MarkAction(false);
            m.moveDir = Vector2.zero;
            m.facing = (t.pos - m.pos).normalized;
        }

        static void Think(World w, Monster m)
        {
            m.moveDir = Vector2.zero;
            var p = w.player;
            if (!m.awake)
            {
                if (w.map.visible[m.x, m.y] && Combat.Dist(m, p) <= 9f)
                {
                    m.awake = true;
                    foreach (var o in w.ActorsInRadius(m.pos, 5f))
                        if (o is Monster om && om.faction == Faction.Monster) om.awake = true;
                }
                return;
            }

            // Target the player, unless a player minion is noticeably closer.
            Actor target = p;
            float td = Combat.Dist(m, p);
            foreach (var a in w.actors)
            {
                if (a.dead || a.faction != Faction.Player || a == p) continue;
                float d = Combat.Dist(m, a);
                if (d < td - 1.5f && d < 6f) { target = a; td = d; }
            }
            bool los = w.map.HasLOS(m.x, m.y, target.x, target.y);

            if (m.Has(StatusType.Fear)) { m.moveDir = Away(w, m, target); return; }
            if (m.actionLock <= 0 && TryUseSkill(w, m, target, td, los)) { m.actionLock = m.ActionTime(1.0f); return; }

            switch (m.def.ai)
            {
                case AiType.Exploder:
                    if (Combat.CanMelee(m, target))
                    {
                        Combat.Explode(m, Faction.Monster, m.pos, 1, m.weaponMax * 1.5f, m.attackElement);
                        w.Kill(m, null);
                    }
                    else m.moveDir = Toward(w, m, target, los);
                    break;
                case AiType.Ranged:
                case AiType.Caster:
                case AiType.Summoner:
                    if (td <= m.attackRange + 0.5f && los)
                    {
                        if (td < 2.5f) m.moveDir = Away(w, m, target) * 0.85f;
                        if (m.actionLock <= 0)
                        {
                            Combat.BasicAttack(m, target);
                            m.MarkAction(m.def.ai != AiType.Ranged);
                            m.actionLock = m.ActionTime(1.4f);
                        }
                    }
                    else m.moveDir = Toward(w, m, target, los);
                    break;
                default:
                    if (Combat.CanMelee(m, target))
                    {
                        if (m.actionLock <= 0) StartWindup(m, target, MeleeWindup / Mathf.Max(0.5f, m.SpeedMult));
                    }
                    else m.moveDir = Toward(w, m, target, los);
                    break;
            }
        }

        static void ThinkAlly(World w, Monster m)
        {
            m.moveDir = Vector2.zero;
            var p = w.player;
            Monster target = null; float best = float.MaxValue;
            foreach (var a in w.actors)
            {
                if (a.dead || a.faction != Faction.Monster || !(a is Monster e)) continue;
                float d = Combat.Dist(m, e);
                if (d > 9f || d >= best || !w.map.HasLOS(m.x, m.y, e.x, e.y)) continue;
                best = d; target = e;
            }
            if (target != null)
            {
                target.awake = true;
                if (m.actionLock <= 0 && TryUseSkill(w, m, target, best, true)) { m.actionLock = m.ActionTime(1.0f); return; }
                if (Combat.CanMelee(m, target)) { if (m.actionLock <= 0) StartWindup(m, target, 0.15f); }
                else m.moveDir = Toward(w, m, target, true);
                return;
            }
            if (Combat.Dist(m, p) > 2.5f) m.moveDir = Toward(w, m, p, w.map.HasLOS(m.x, m.y, p.x, p.y));
        }

        static bool TryUseSkill(World w, Monster m, Actor target, float d, bool los)
        {
            if (m.skills.Count == 0 || !w.rng.Chance(0.35f)) return false;
            var order = m.skills.ToList();
            w.rng.Shuffle(order);
            foreach (var id in order)
            {
                var s = SkillDatabase.Get(id);
                if (s == null || m.Cooldown(id) > 0) continue;
                var aim = target.pos;
                switch (s.targeting)
                {
                    case Targeting.Summon:
                    {
                        var def = MonsterDatabase.Get(s.summonId);
                        int mine = w.actors.Count(a => a is Monster o && o.owner == m && o.def == def && !o.dead);
                        if (mine >= s.SummonCap(m.skillRank) || d > 9f) continue;
                        break;
                    }
                    case Targeting.Self:
                        if (s.healPct > 0) { if (m.LifePct > 0.5f) continue; }
                        else if (s.radius > 0 && d > SkillExecutor.WorldRadius(s.radius) + target.radius) continue;
                        aim = m.pos;
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
                        if (d < 2f || !los || d > s.range) continue;
                        break;
                    case Targeting.Melee:
                        if (!Combat.CanMelee(m, target)) continue;
                        break;
                    default:
                        if (!los || d > s.range) continue;
                        break;
                }
                if (SkillExecutor.TryCast(m, s, m.skillRank, aim)) { m.MarkAction(!s.weapon); return true; }
            }
            return false;
        }

        static Vector2? BlinkSpot(World w, Monster m, Actor target, int range)
        {
            var opts = new List<Vector2>();
            bool wantClose = m.def.ai == AiType.Melee;
            for (int dx = -range; dx <= range; dx++)
                for (int dy = -range; dy <= range; dy++)
                {
                    int x = m.x + dx, y = m.y + dy;
                    if (!w.TileFree(x, y) || w.map.Get(x, y) == Tile.Lava) continue;
                    float dt = Vector2.Distance(new Vector2(x, y), target.pos);
                    if (wantClose ? dt > 1.5f : (dt < 3 || dt > 5.5f)) continue;
                    if (!w.map.HasLOS(m.x, m.y, x, y) || !w.map.HasLOS(x, y, target.x, target.y)) continue;
                    opts.Add(new Vector2(x, y));
                }
            return opts.Count > 0 ? w.rng.Pick(opts) : (Vector2?)null;
        }

        /// <summary>Steer straight at a visible, nearby target; otherwise follow the distance field around walls.</summary>
        static Vector2 Toward(World w, Monster m, Actor target, bool los)
        {
            var delta = target.pos - m.pos;
            var direct = delta.normalized;
            if (los && delta.magnitude < 7f && !w.Blocked(m.pos + direct * 0.35f, m.radius, true)) return direct;
            if (target == w.player)
            {
                var step = w.SteerAlong(m, w.playerDist);
                if (step != null) return step.Value;
            }
            return direct;
        }

        static Vector2 Away(World w, Monster m, Actor target)
        {
            var away = (m.pos - target.pos).normalized;
            if (!w.Blocked(m.pos + away * 0.4f, m.radius, true)) return away;
            var step = target == w.player ? PathStep(w, m, false) : null;
            return step ?? Combat.Rotate(away, w.rng.Chance(0.5f) ? 90 : -90);
        }

        /// <summary>Direction to the neighbouring tile that is one step closer to (or farther from) the player.</summary>
        static Vector2? PathStep(World w, Monster m, bool closer)
        {
            var dist = w.playerDist;
            var map = w.map;
            if (dist == null || !map.InBounds(m.x, m.y)) return null;
            int cur = dist[m.x, m.y];
            if (cur == int.MaxValue) return null;
            Vector2Int? best = null;
            int bestD = cur;
            foreach (var d in Dirs)
            {
                int nx = m.x + d.x, ny = m.y + d.y;
                if (!map.InBounds(nx, ny) || !map.CanStep(m.x, m.y, d.x, d.y) || map.Get(nx, ny) == Tile.Lava) continue;
                int nd = dist[nx, ny];
                if (nd == int.MaxValue) continue;
                if (closer ? nd < bestD : nd > bestD) { bestD = nd; best = new Vector2Int(nx, ny); }
            }
            if (best == null) return null;
            return ((Vector2)best.Value - m.pos).normalized;
        }
    }
}
