using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Depthbound.EditorTools
{
    /// <summary>
    /// A simple real-time autoplay bot used to sanity-check difficulty. It explores, fights (kiting with
    /// ranged builds), dodges, loots, equips upgrades and spends points, then reports how deep each class gets.
    /// Run: Unity -batchmode -executeMethod Depthbound.EditorTools.BalanceBot.Run
    /// </summary>
    public static class BalanceBot
    {
        const float Dt = 0.05f;
        const float MaxGameSeconds = 3600f;

        [MenuItem("Depthbound/Run Balance Bot")]
        public static void Run()
        {
            var sb = new StringBuilder("BALANCE results\n");
            var depths = new List<int>();
            foreach (var cls in ClassDatabase.All)
                for (int s = 0; s < 2; s++)
                {
                    var (depth, level, time, cause) = Play(cls, 1000 + s * 7919 + cls.id.Length * 31);
                    depths.Add(depth);
                    sb.AppendLine($"BALANCE {cls.name,-14} seed#{s}: depth {depth,3}  level {level,3}  time {time / 60f,5:0.0}m  died to {cause}");
                }
            sb.AppendLine($"BALANCE average depth {depths.Average():0.0}, min {depths.Min()}, max {depths.Max()}");
            Debug.Log(sb.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static int Score(Item it) => it == null ? -1 : (int)it.rarity * 12 + it.ilvl + it.powers.Count(p => p.IsSkillPower) * 6;

        class Brain
        {
            public Vector2Int goal = new Vector2Int(-1, -1);
            public int[,] field;
            public float fieldAge;
            public Vector2 lastPos;
            public float stuckTime, unstickTime;
            public Vector2 unstickDir;
            public int lastDepth = 1;
            public float floorTime;
            public readonly HashSet<GroundItem> badLoot = new HashSet<GroundItem>();
            public GroundItem lootTarget;
            public float lootTime;
            public readonly Queue<string> trace = new Queue<string>();
            public Monster foe;
            public float foeLife, foeStall;
        }

        static (int, int, float, string) Play(ClassDef cls, int seed)
        {
            var w = new World(cls, seed, 0, new MetaProgress(), false);
            var p = w.player;
            var rng = new Rng(seed);
            var b = new Brain { lastPos = p.pos };
            int primary = System.Array.IndexOf(cls.attributes, cls.attributes.Max());
            int step = 0;
            while (w.stats.time < MaxGameSeconds && !p.dead)
            {
                Fx.Pending.Clear();
                if (w.depth != b.lastDepth) { b.lastDepth = w.depth; b.floorTime = 0; b.badLoot.Clear(); b.goal = new Vector2Int(-1, -1); }
                b.floorTime += Dt;
                if (step++ % 4 == 0) Decide(w, p, b, rng, primary, p.attackRange > 1 || (HasRangedSkill(p) && p.mana > 12));
                w.Tick(Dt);
            }
            string diag = "timeout";
            if (!p.dead)
            {
                var e = w.VisibleEnemies().OrderBy(m => Combat.Dist(p, m)).FirstOrDefault();
                diag = $"timeout floorTime={b.floorTime:0} sealed={w.map.stairsSealed} pos={p.pos} stairs={w.map.stairs} mana={p.mana:0} range={p.attackRange} enemy={(e == null ? "none" : $"{e.name} d={Combat.Dist(p, e):0.0} los={w.map.HasLOS(p.x, p.y, e.x, e.y)} life={e.life:0}/{e.MaxLife:0}")} loot={b.lootTarget != null} trace=[{string.Join(" | ", b.trace)}]";
            }
            return (w.depth, p.level, w.stats.time, p.dead ? w.stats.killedBy : diag);
        }

        static void Decide(World w, Player p, Brain b, Rng rng, int primary, bool caster)
        {
            if (w.InHub) { w.TakePortal(); return; }
            Spend(p, primary, rng);
            if (rng.Chance(0.05f))
            {
                foreach (var it in p.backpack.ToList())
                    if (Score(it) > Score(p.Equipped(p.SlotFor(it)))) p.Equip(it);
                foreach (var it in p.backpack.Where(i => Score(i) <= Score(p.Equipped(p.SlotFor(i)))).ToList()) w.Salvage(it);
            }
            if (p.LifePct < 0.4f) w.QuaffHealth();
            if (p.mana < p.MaxMana * 0.15f && rng.Chance(0.2f)) w.QuaffMana();

            // Stuck detection: if we have not moved while trying to, wiggle for a moment.
            if (p.moveInput.sqrMagnitude > 0 && (p.pos - b.lastPos).sqrMagnitude < 0.0004f) b.stuckTime += 4 * Dt; else b.stuckTime = 0;
            b.lastPos = p.pos;
            if (b.unstickTime > 0) { b.unstickTime -= 4 * Dt; p.moveInput = b.unstickDir; return; }
            if (b.stuckTime > 1.2f) { b.stuckTime = 0; b.unstickTime = 0.5f; b.unstickDir = Random2(rng); p.moveInput = b.unstickDir; return; }

            var enemy = w.NearestEnemy(9f);
            if (enemy != null)
            {
                float d = Combat.Dist(p, enemy);
                // If our hits stop landing (e.g. shots clipping a wall corner), close in instead of standing still.
                if (b.foe != enemy || enemy.life < b.foeLife - 0.01f) { b.foe = enemy; b.foeLife = enemy.life; b.foeStall = 0; }
                else b.foeStall += 4 * Dt;
                if (b.foeStall > 3f) caster = false;
                // Roll away from an imminent melee swing.
                if (enemy.windup >= 0 && enemy.windupTarget == p && rng.Chance(0.5f)) w.PlayerDodge(p.pos - enemy.pos);
                if (!TrySkill(w, p, enemy, rng)) w.PlayerAttack(enemy.pos);
                if (caster) p.moveInput = d < 3f ? (p.pos - enemy.pos).normalized : d > 5.5f ? PathTo(w, b, enemy.Pos) : Vector2.zero;
                else p.moveInput = Combat.CanMelee(p, enemy) ? Vector2.zero : PathTo(w, b, enemy.Pos);
                return;
            }

            if (b.lootTarget != null && (!w.ground.Contains(b.lootTarget) || b.lootTime > 6f)) { if (w.ground.Contains(b.lootTarget)) b.badLoot.Add(b.lootTarget); b.lootTarget = null; }
            if (b.lootTarget == null && p.backpack.Count < Player.BackpackSize - 1)
                b.lootTarget = w.ground.Where(g => w.map.visible[g.x, g.y] && !b.badLoot.Contains(g)).OrderBy(g => (new Vector2(g.x, g.y) - p.pos).sqrMagnitude).FirstOrDefault();
            if (b.lootTarget != null)
            {
                b.lootTime += 4 * Dt;
                var lt = new Vector2(b.lootTarget.x, b.lootTarget.y);
                p.moveInput = (lt - p.pos).sqrMagnitude < 2.5f ? (lt - p.pos).normalized : PathTo(w, b, new Vector2Int(b.lootTarget.x, b.lootTarget.y));
                return;
            }
            b.lootTime = 0;
            if (p.LifePct < 0.7f && b.floorTime < 150f) { p.moveInput = Vector2.zero; return; }

            if (p.Pos == w.map.stairs && !w.map.stairsSealed) { p.moveInput = Vector2.zero; w.Interact(); return; }
            Vector2Int goal;
            var frontier = b.floorTime < 90f ? Frontier(w) : null;
            if (frontier != null) goal = frontier.Value;
            else if (w.map.stairsSealed)
            {
                var boss = w.actors.OfType<Monster>().FirstOrDefault(m => m.isBoss && !m.dead);
                goal = boss != null ? boss.Pos : w.map.stairs;
            }
            else goal = w.map.stairs;
            if (goal == w.map.stairs && Vector2.Distance(p.pos, w.map.stairs) < 1.5f) p.moveInput = ((Vector2)w.map.stairs - p.pos).normalized;
            else p.moveInput = PathTo(w, b, goal);
            b.trace.Enqueue($"goal={goal} pos={p.pos} in={p.moveInput}");
            if (b.trace.Count > 6) b.trace.Dequeue();
        }

        static bool HasRangedSkill(Player p) => p.hotbar.Any(id =>
        {
            var d = SkillDatabase.Get(id);
            return d != null && d.DealsDamage && p.SkillRank(id) > 0 &&
                   (d.targeting == Targeting.Projectile || d.targeting == Targeting.Chain || d.targeting == Targeting.Ground || d.targeting == Targeting.Target);
        });

        static Vector2 Random2(Rng rng) => new Vector2(rng.Range(-1f, 1f), rng.Range(-1f, 1f)).normalized;

        static void Spend(Player p, int primary, Rng rng)
        {
            while (p.attrPoints > 0)
            {
                int a = p.attrPoints % 5 < 3 ? primary : 3;
                p.allocated[a]++; p.attrPoints--;
                p.Recalc();
            }
            for (int guard = 0; guard < 20 && p.skillPoints > 0; guard++)
            {
                var options = p.cls.affinities.SelectMany(SkillDatabase.ForSchool).Where(s => !s.hidden && p.CannotLearnReason(s) == null).ToList();
                if (options.Count == 0) break;
                p.LearnSkill(rng.Weighted(options, s => p.Learned(s.id) > 0 ? 1f : 3f));
            }
        }

        static bool TrySkill(World w, Player p, Monster e, Rng rng)
        {
            if (p.actionLock > 0) return false;
            var order = Enumerable.Range(0, Player.HotbarSize).ToList();
            rng.Shuffle(order);
            foreach (int i in order)
            {
                var id = p.hotbar[i];
                var def = SkillDatabase.Get(id);
                if (def == null) continue;
                if (def.IsBuff && p.buffs.Any(b => b.id == def.id)) continue;
                if (def.healPct > 0 && p.LifePct > 0.6f) continue;
                if (def.targeting == Targeting.Blink) continue;
                if (def.targeting == Targeting.Self && def.radius > 0 && Combat.Dist(p, e) > SkillExecutor.WorldRadius(def.radius) + 0.3f) continue;
                if (def.targeting == Targeting.Projectile && Combat.Dist(p, e) > def.range) continue;
                var target = def.targeting == Targeting.Self || def.targeting == Targeting.Summon ? p.pos : e.pos;
                if (SkillExecutor.CanCast(p, def, p.SkillRank(id), target) != null) continue;
                if (w.PlayerUseSkill(i, e.pos)) return true;
            }
            return false;
        }

        static Vector2 PathTo(World w, Brain b, Vector2Int goal)
        {
            var p = w.player;
            b.fieldAge += 4 * Dt;
            if (goal != b.goal || b.field == null || b.fieldAge > 1.5f)
            {
                b.goal = goal;
                b.field = w.map.DistanceField(new[] { goal }, true);
                b.fieldAge = 0;
            }
            if (Vector2.Distance(p.pos, goal) < 1.5f) return ((Vector2)goal - p.pos).normalized;
            return w.SteerAlong(p, b.field) ?? ((Vector2)goal - p.pos).normalized;
        }

        static Vector2Int? Frontier(World w)
        {
            var map = w.map;
            var dist = map.DistanceField(new[] { w.player.Pos }, true);
            Vector2Int? best = null; int bd = int.MaxValue;
            for (int x = 1; x < map.w - 1; x++)
                for (int y = 1; y < map.h - 1; y++)
                {
                    if (!map.explored[x, y] || !map.Walkable(x, y) || dist[x, y] >= bd || dist[x, y] < 2) continue;
                    bool frontier = !map.explored[x + 1, y] || !map.explored[x - 1, y] || !map.explored[x, y + 1] || !map.explored[x, y - 1];
                    if (!frontier) continue;
                    bd = dist[x, y]; best = new Vector2Int(x, y);
                }
            return best;
        }
    }
}
