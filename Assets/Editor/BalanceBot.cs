using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Depthbound.EditorTools
{
    /// <summary>
    /// A simple autoplay bot used to sanity-check difficulty. It explores, fights, loots, equips
    /// upgrades and spends points, then reports how deep each class gets.
    /// Run: Unity -batchmode -executeMethod Depthbound.EditorTools.BalanceBot.Run
    /// </summary>
    public static class BalanceBot
    {
        [MenuItem("Depthbound/Run Balance Bot")]
        public static void Run()
        {
            var sb = new StringBuilder("BALANCE results\n");
            var depths = new List<int>();
            foreach (var cls in ClassDatabase.All)
                for (int s = 0; s < 3; s++)
                {
                    var (depth, level, turns, cause) = Play(cls, 1000 + s * 7919 + cls.id.Length * 31, 0);
                    depths.Add(depth);
                    sb.AppendLine($"BALANCE {cls.name,-14} seed#{s}: depth {depth,3}  level {level,3}  turns {turns,5}  died to {cause}");
                }
            sb.AppendLine($"BALANCE average depth {depths.Average():0.0}, min {depths.Min()}, max {depths.Max()}");
            Debug.Log(sb.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static int Score(Item it) => it == null ? -1 : (int)it.rarity * 12 + it.ilvl + it.powers.Count(p => p.IsSkillPower) * 6;

        static (int, int, int, string) Play(ClassDef cls, int seed, int corruption)
        {
            var w = new World(cls, seed, corruption, new MetaProgress(), false);
            var p = w.player;
            var rng = new Rng(seed);
            int floorTurns = 0, lastDepth = 1;
            int primary = System.Array.IndexOf(cls.attributes, cls.attributes.Max());
            var trace = new Queue<string>();
            var badLoot = new HashSet<GroundItem>();
            void T(string s) { trace.Enqueue($"{s}@{p.x},{p.y}"); if (trace.Count > 8) trace.Dequeue(); }
            for (int t = 0; t < 20000 && !p.dead; t++)
            {
                Fx.Pending.Clear();
                if (w.depth != lastDepth) { lastDepth = w.depth; floorTurns = 0; }
                floorTurns++;
                Spend(p, cls, primary, rng);
                foreach (var it in p.backpack.ToList())
                    if (Score(it) > Score(p.Equipped(p.SlotFor(it)))) p.Equip(it);
                foreach (var it in p.backpack.Where(i => Score(i) <= Score(p.Equipped(p.SlotFor(i)))).ToList()) w.Salvage(it);

                if (p.LifePct < 0.4f && p.healthPotions > 0) { w.QuaffHealth(); continue; }
                if (p.mana < p.MaxMana * 0.15f && p.manaPotions > 0 && rng.Chance(0.3f)) { w.QuaffMana(); continue; }

                var enemy = w.NearestEnemy(12) ?? w.VisibleEnemies().OrderBy(m => Combat.Dist(p, m)).FirstOrDefault();
                if (enemy != null)
                {
                    if (TrySkill(w, p, enemy, rng)) continue;
                    if (w.PlayerAttackTarget(enemy)) continue;
                    if (StepToward(w, enemy.Pos)) continue;
                    w.PlayerWait();
                    continue;
                }
                var loot = w.ground.Where(g => w.map.visible[g.x, g.y] && !badLoot.Contains(g)).OrderBy(g => Combat.Dist(p.Pos, new Vector2Int(g.x, g.y))).FirstOrDefault();
                if (loot != null && p.backpack.Count < Player.BackpackSize - 1)
                {
                    if (StepToward(w, new Vector2Int(loot.x, loot.y))) { T("loot"); continue; }
                    badLoot.Add(loot);
                }
                if (p.LifePct < 0.7f && floorTurns < 1500) { T("rest"); w.PlayerWait(); continue; }

                if (p.Pos == w.map.stairs && !w.map.stairsSealed) { T("descend:" + w.Descend()); continue; }
                Vector2Int? goal = null;
                if (floorTurns < 600) goal = Frontier(w);
                if (goal == null)
                {
                    if (w.map.stairsSealed)
                    {
                        var boss = w.actors.OfType<Monster>().FirstOrDefault(m => m.isBoss && !m.dead);
                        goal = boss != null ? boss.Pos : w.map.stairs;
                    }
                    else goal = w.map.stairs;
                }
                if (!StepToward(w, goal.Value)) { T($"stuck->{goal.Value.x},{goal.Value.y}"); if (!w.PlayerMove(rng.Range(-1, 2), rng.Range(-1, 2))) w.PlayerWait(); } else T($"go->{goal.Value.x},{goal.Value.y}");
            }
            string diag = "timeout";
            if (!p.dead)
            {
                var vis = w.VisibleEnemies().ToList();
                var reach = w.map.DistanceField(new[] { p.Pos }, true);
                var lt = w.ground.Where(g => w.map.visible[g.x, g.y]).OrderBy(g => Combat.Dist(p.Pos, new Vector2Int(g.x, g.y))).FirstOrDefault();
                string lootInfo = lt == null ? "none" : $"{lt.x},{lt.y} tile={w.map.Get(lt.x, lt.y)} item={(lt.item != null ? lt.item.name : lt.gold > 0 ? "gold" : "potion")} reach={reach[lt.x, lt.y] != int.MaxValue} occ={w.ActorAt(lt.x, lt.y)?.name} bp={p.backpack.Count}";
                diag = $"timeout loot=[{lootInfo}] trace=[{string.Join(" ", trace)}] stairs={w.map.stairs} floorTurns={floorTurns} sealed={w.map.stairsSealed} stairsReach={reach[w.map.stairs.x, w.map.stairs.y] != int.MaxValue} visible=[" +
                       string.Join(", ", vis.Select(m => $"{m.name}@d{Combat.Dist(p, m)} reach={reach[m.x, m.y] != int.MaxValue} los={w.map.HasLOS(p.x, p.y, m.x, m.y)}")) + "]";
            }
            return (w.depth, p.level, w.turn, p.dead ? w.stats.killedBy : diag);
        }

        static void Spend(Player p, ClassDef cls, int primary, Rng rng)
        {
            while (p.attrPoints > 0)
            {
                int a = p.attrPoints % 5 < 3 ? primary : 3;
                p.allocated[a]++; p.attrPoints--;
                p.Recalc();
            }
            for (int guard = 0; guard < 20 && p.skillPoints > 0; guard++)
            {
                var options = cls.affinities.SelectMany(SkillDatabase.ForSchool).Where(s => !s.hidden && p.CannotLearnReason(s) == null).ToList();
                if (options.Count == 0) break;
                p.LearnSkill(rng.Weighted(options, s => p.Learned(s.id) > 0 ? 1f : 3f));
            }
        }

        static bool TrySkill(World w, Player p, Monster e, Rng rng)
        {
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
                if (def.targeting == Targeting.Self && def.radius > 0 && Combat.Dist(p, e) > def.radius) continue;
                var target = def.targeting == Targeting.Self || def.targeting == Targeting.Summon ? p.Pos : e.Pos;
                if (SkillExecutor.CanCast(p, def, p.SkillRank(id), target) != null) continue;
                if (w.PlayerUseSkill(i, e.Pos)) return true;
            }
            return false;
        }

        static bool StepToward(World w, Vector2Int goal)
        {
            var p = w.player;
            var dist = w.map.DistanceField(new[] { goal }, true);
            int cur = dist[p.x, p.y];
            Vector2Int? best = null;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = p.x + dx, ny = p.y + dy;
                    if (!w.map.InBounds(nx, ny)) continue;
                    bool goalTile = nx == goal.x && ny == goal.y && (!w.map.Walkable(nx, ny) || w.map.CanStep(p.x, p.y, dx, dy));
                    if (!goalTile && (!w.map.CanStep(p.x, p.y, dx, dy) || dist[nx, ny] >= cur)) continue;
                    var occ = w.ActorAt(nx, ny);
                    if (occ != null && occ.IsHostileTo(p) && !goalTile) continue;
                    if (best == null || goalTile || dist[nx, ny] < dist[p.x + best.Value.x, p.y + best.Value.y]) best = new Vector2Int(dx, dy);
                    if (goalTile) break;
                }
            return best != null && w.PlayerMove(best.Value.x, best.Value.y);
        }

        static Vector2Int? Frontier(World w)
        {
            var map = w.map;
            var dist = map.DistanceField(new[] { w.player.Pos }, true);
            Vector2Int? best = null; int bd = int.MaxValue;
            for (int x = 1; x < map.w - 1; x++)
                for (int y = 1; y < map.h - 1; y++)
                {
                    if (!map.explored[x, y] || !map.Walkable(x, y) || dist[x, y] >= bd) continue;
                    bool frontier = !map.explored[x + 1, y] || !map.explored[x - 1, y] || !map.explored[x, y + 1] || !map.explored[x, y - 1];
                    if (!frontier) continue;
                    bd = dist[x, y]; best = new Vector2Int(x, y);
                }
            return best;
        }
    }
}
