using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Depthbound
{
    public class GroundItem
    {
        public int x, y;
        public Item item;
        public int gold;
        public int potion; // 1 = health, 2 = mana
        public bool warned;
    }

    public class RunStats
    {
        public int kills, bossKills, goldEarned, legendaries, deepest = 1;
        public float time;
        public string killedBy = "";
    }

    /// <summary>All state for one run: the current floor, actors, projectiles, loot and the real-time simulation.</summary>
    public class World
    {
        public static World Current;

        public DungeonMap map;
        public Player player;
        public readonly List<Actor> actors = new List<Actor>();
        public readonly List<Projectile> projectiles = new List<Projectile>();
        public readonly List<GroundItem> ground = new List<GroundItem>();
        public readonly List<Item> shop = new List<Item>();
        public readonly RunStats stats = new RunStats();
        public readonly List<(string text, Color color)> log = new List<(string, Color)>();

        public int depth = 1, corruption, runSeed, floorVersion, fovVersion;
        public float time;
        public bool daily;
        public Rng rng;
        public int[,] playerDist;
        public MetaProgress meta;

        Vector2Int lastPlayerTile = new Vector2Int(-1, -1);
        float lastLogTime = -10, lavaTimer;
        readonly List<Actor> active = new List<Actor>();

        public static readonly string[] ShrineNames = { "Shrine of Power", "Shrine of Fortune", "Shrine of Warding", "Shrine of Fury", "Shrine of Wisdom", "Shrine of Precision" };

        public World(ClassDef cls, int seed, int corruption, MetaProgress meta, bool daily)
        {
            Current = this;
            runSeed = seed;
            this.corruption = corruption;
            this.meta = meta;
            this.daily = daily;
            rng = new Rng(seed);

            player = new Player(cls);
            player.metaMods.Add(new StatMod(StatType.MaxLifePct, 5 * meta.Rank(Upgrade.Vigor)));
            player.metaMods.Add(new StatMod(StatType.XpBonus, 5 * meta.Rank(Upgrade.Wisdom)));
            player.metaMods.Add(new StatMod(StatType.MagicFind, 5 * meta.Rank(Upgrade.Fortune)));
            player.metaMods.Add(new StatMod(StatType.GoldFind, 10 * meta.Rank(Upgrade.Greed)));
            player.metaMods.Add(new StatMod(StatType.AllRes, 2 * meta.Rank(Upgrade.Resilience)));
            player.attrPoints = 2 * meta.Rank(Upgrade.Talent);
            player.skillPoints = meta.Rank(Upgrade.Scholar);
            player.learned[cls.startSkill] = 1;
            player.hotbar[0] = cls.startSkill;

            int armory = meta.Rank(Upgrade.Armory);
            var wb = ItemDatabase.Base(cls.startWeapon);
            var weapon = armory > 0 ? ItemGenerator.Generate(rng, 2, armory >= 2 ? Rarity.Rare : Rarity.Magic, wb) : ItemGenerator.CreateBase(wb, 1);
            player.equipment[(int)EquipSlot.Weapon] = weapon;
            if (cls.startOffhand != null) player.equipment[(int)EquipSlot.Offhand] = ItemGenerator.CreateBase(ItemDatabase.Base(cls.startOffhand), 1);
            player.equipment[(int)EquipSlot.Chest] = ItemGenerator.CreateBase(ItemDatabase.Base(cls.attributes[2] > 8 ? "robe" : "leather_armor"), 1);
            player.healthPotions = 3 + meta.Rank(Upgrade.Alchemy);
            player.manaPotions = 2;
            player.Recalc();
            player.life = player.MaxLife;
            player.mana = player.MaxMana;

            GenerateFloor();
            Log($"{cls.name} enters the depths. Seed {seed}{(corruption > 0 ? $"  Corruption {corruption}" : "")}", new Color(1f, 0.85f, 0.5f));
        }

        public bool IsBossFloor => depth % 5 == 0;
        public int MonsterLevel => depth + corruption * 2;

        public void Log(string text, Color c)
        {
            // Holding a key that keeps failing should not flood the log.
            if (log.Count > 0 && log[log.Count - 1].text == text && time - lastLogTime < 1.5f) return;
            lastLogTime = time;
            log.Add((text, c));
            if (log.Count > 60) log.RemoveAt(0);
        }

        public void Log(string text) => Log(text, new Color(0.85f, 0.85f, 0.85f));

        // ------------------------------------------------------------------ floors

        public void GenerateFloor()
        {
            var floorRng = new Rng(runSeed * 31 + depth * 7919 + 17);
            map = DungeonGenerator.Generate(floorRng, depth);
            var allies = actors.Where(a => a != player && a.faction == Faction.Player && !a.dead).ToList();
            actors.Clear();
            projectiles.Clear();
            ground.Clear();
            shop.Clear();
            Place(player, map.start);
            foreach (var a in allies)
            {
                var spot = FindFreeNear(map.start, 4);
                if (spot != null) Place(a, spot.Value);
            }
            SpawnMonsters(floorRng);
            if (IsBossFloor) SpawnBoss(floorRng);
            if (HasMerchant()) StockShop(floorRng);
            floorVersion++;
            lastPlayerTile = new Vector2Int(-1, -1);
            OnPlayerTileChanged();
            Log($"Depth {depth} - {map.biome.name}" + (IsBossFloor ? "  (a powerful presence stirs...)" : ""), new Color(0.7f, 0.85f, 1f));
        }

        bool HasMerchant()
        {
            for (int x = 0; x < map.w; x++) for (int y = 0; y < map.h; y++) if (map.tiles[x, y] == Tile.Merchant) return true;
            return false;
        }

        void SpawnMonsters(Rng r)
        {
            var defs = map.biome.monsters.Select(MonsterDatabase.Get).Where(d => d != null).ToList();
            var dist = map.DistanceField(new[] { map.start }, false);
            var cells = new List<Vector2Int>();
            for (int x = 0; x < map.w; x++)
                for (int y = 0; y < map.h; y++)
                    if (map.tiles[x, y] == Tile.Floor && dist[x, y] != int.MaxValue && dist[x, y] >= 8) cells.Add(new Vector2Int(x, y));
            if (cells.Count == 0) return;

            int packs = Mathf.Min(5 + depth / 2 + corruption, 18);
            float rareChance = Mathf.Min(0.02f + (depth - 1) * 0.01f + corruption * 0.04f, 0.3f);
            float champChance = Mathf.Min(0.05f + (depth - 1) * 0.01f + corruption * 0.04f, 0.35f);
            int lvl = MonsterLevel;

            for (int i = 0; i < packs; i++)
            {
                var center = r.Pick(cells);
                var def = r.Weighted(defs, d => d.weight);
                float roll = r.Float();
                if (roll < rareChance)
                {
                    var leader = CreateMonster(def, lvl + 1, Faction.Monster, 2);
                    int affixCount = 2 + (depth >= 15 ? 1 : 0) + (corruption >= 3 ? 1 : 0);
                    for (int a = 0; a < affixCount; a++) leader.AddAffix((MonsterAffix)r.Range(0, (int)MonsterAffix.Mighty + 1));
                    PlaceNear(FinishMonster(leader), center);
                    int minions = r.Range(2, 5);
                    for (int m = 0; m < minions; m++) PlaceNear(FinishMonster(CreateMonster(r.Weighted(defs, d => d.weight), lvl, Faction.Monster, 0)), center);
                }
                else if (roll < rareChance + champChance)
                {
                    var affix = (MonsterAffix)r.Range(0, (int)MonsterAffix.Mighty + 1);
                    int n = r.Range(2, 4);
                    for (int m = 0; m < n; m++)
                    {
                        var c = CreateMonster(def, lvl, Faction.Monster, 1);
                        c.AddAffix(affix);
                        PlaceNear(FinishMonster(c), center);
                    }
                }
                else
                {
                    int n = r.Range(1, 2 + Mathf.Min(3, 1 + depth / 3));
                    for (int m = 0; m < n; m++) PlaceNear(FinishMonster(CreateMonster(r.Weighted(defs, d => d.weight), lvl, Faction.Monster, 0)), center);
                }
            }
        }

        void SpawnBoss(Rng r)
        {
            var def = MonsterDatabase.Get(map.biome.boss);
            var boss = CreateMonster(def, MonsterLevel + 2, Faction.Monster, 3);
            int cycle = Biomes.Cycle(depth);
            for (int i = 0; i < cycle + (corruption >= 2 ? 1 : 0); i++) boss.AddAffix((MonsterAffix)r.Range(0, (int)MonsterAffix.Mighty + 1));
            FinishMonster(boss);
            var spot = FindFreeNear(map.stairs, 3) ?? FindFreeNear(map.stairs, 10);
            if (spot == null) return;
            Place(boss, spot.Value);
            map.stairsSealed = true;
        }

        public Monster CreateMonster(MonsterDef def, int lvl, Faction f, int rank)
        {
            var m = new Monster(def, lvl, f) { rank = rank };
            float xpMult = 1;
            if (f == Faction.Monster)
            {
                int cycle = Biomes.Cycle(depth);
                m.lifeMult *= (1 + 0.25f * corruption) * (1 + 0.5f * cycle);
                m.dmgMult *= (1 + 0.15f * corruption) * (1 + 0.25f * cycle);
                xpMult = 1 + 0.2f * corruption;
            }
            switch (rank)
            {
                case 1: m.lifeMult *= 2.2f; m.dmgMult *= 1.3f; xpMult *= 2.5f; break;
                case 2: m.lifeMult *= 3.5f; m.dmgMult *= 1.5f; xpMult *= 5f; m.scale *= 1.15f; break;
                case 3: xpMult *= 1.5f; break;
            }
            m.radius = Mathf.Clamp(0.32f * m.scale, 0.22f, 0.48f);
            m.xpValue = def.xp * (1 + 0.12f * (lvl - 1)) * xpMult;
            m.skillRank = 1 + lvl / 8;
            return m;
        }

        Monster FinishMonster(Monster m)
        {
            m.Recalc();
            m.life = m.MaxLife;
            if (m.affixes.Contains(MonsterAffix.Shielded)) m.shield = m.MaxLife * 0.35f;
            m.thinkTimer = rng.Range(0f, 0.3f);
            return m;
        }

        void PlaceNear(Monster m, Vector2Int c)
        {
            var spot = FindFreeNear(c, 3);
            if (spot != null) Place(m, spot.Value);
        }

        void StockShop(Rng r)
        {
            int ilvl = depth + 2;
            for (int i = 0; i < 8; i++)
            {
                var rar = r.Weighted(new[] { (Rarity.Magic, 50f), (Rarity.Rare, 35f), (Rarity.Epic, 12f), (Rarity.Legendary, 3f) }, t => t.Item2).Item1;
                shop.Add(ItemGenerator.Generate(r, ilvl, rar, ItemGenerator.RollBase(r, ItemGenerator.RollKind(r), ilvl)));
            }
        }

        public int ShopPrice(Item it) => it.Value * 2;
        public int PotionPrice => 20 + depth * 3;

        // ------------------------------------------------------------------ space queries & movement

        public void Place(Actor a, Vector2Int p)
        {
            a.pos = p;
            if (!actors.Contains(a)) actors.Add(a);
        }

        public bool TileFree(int x, int y)
        {
            if (!map.Walkable(x, y)) return false;
            var c = new Vector2(x, y);
            foreach (var a in actors) if (!a.dead && (a.pos - c).sqrMagnitude < 0.5f) return false;
            return true;
        }

        /// <summary>The living actor whose body is closest to a point, within a radius.</summary>
        public Actor ActorNear(Vector2 p, float radius)
        {
            Actor best = null; float bd = float.MaxValue;
            foreach (var a in actors)
            {
                if (a.dead) continue;
                float d = Vector2.Distance(a.pos, p) - a.radius;
                if (d <= radius && d < bd) { bd = d; best = a; }
            }
            return best;
        }

        public Actor HostileNear(Actor from, Vector2 p, float radius)
        {
            Actor best = null; float bd = float.MaxValue;
            foreach (var a in actors)
            {
                if (a.dead || !from.IsHostileTo(a)) continue;
                float d = Vector2.Distance(a.pos, p) - a.radius;
                if (d <= radius && d < bd) { bd = d; best = a; }
            }
            return best;
        }

        /// <summary>True if a body of the given radius at p would overlap a solid tile.</summary>
        public bool Blocked(Vector2 p, float r, bool avoidLava)
        {
            float rr = r * 0.95f;
            for (int i = 0; i < 4; i++)
            {
                float cx = p.x + ((i & 1) == 0 ? -rr : rr);
                float cy = p.y + ((i & 2) == 0 ? -rr : rr);
                int tx = Mathf.RoundToInt(cx), ty = Mathf.RoundToInt(cy);
                var t = map.Get(tx, ty);
                if (!DungeonMap.IsWalkable(t)) return true;
                if (avoidLava && t == Tile.Lava) return true;
            }
            return false;
        }

        /// <summary>Moves an actor with wall sliding. Monsters refuse to step into lava.</summary>
        public void TryMove(Actor a, Vector2 delta)
        {
            bool avoidLava = a != player;
            int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / 0.25f));
            var d = delta / steps;
            for (int i = 0; i < steps; i++)
            {
                var nx = a.pos + new Vector2(d.x, 0);
                if (!Blocked(nx, a.radius, avoidLava)) a.pos = nx;
                else if (Mathf.Abs(d.x) >= Mathf.Abs(d.y)) Nudge(a, true, d.x, avoidLava);
                var ny = a.pos + new Vector2(0, d.y);
                if (!Blocked(ny, a.radius, avoidLava)) a.pos = ny;
                else if (Mathf.Abs(d.y) >= Mathf.Abs(d.x)) Nudge(a, false, d.y, avoidLava);
            }
        }

        /// <summary>Corner assist: when blocked moving into an open tile, slide toward that tile's center line.</summary>
        void Nudge(Actor a, bool alongX, float amount, bool avoidLava)
        {
            int tx = alongX ? Mathf.RoundToInt(a.pos.x + Mathf.Sign(amount) * 0.75f) : Mathf.RoundToInt(a.pos.x);
            int ty = alongX ? Mathf.RoundToInt(a.pos.y) : Mathf.RoundToInt(a.pos.y + Mathf.Sign(amount) * 0.75f);
            var t = map.Get(tx, ty);
            if (!DungeonMap.IsWalkable(t) || (avoidLava && t == Tile.Lava)) return;
            float off = alongX ? ty - a.pos.y : tx - a.pos.x;
            if (Mathf.Abs(off) < 0.01f) return;
            float s = Mathf.Sign(off) * Mathf.Min(Mathf.Abs(off), Mathf.Abs(amount));
            var np = a.pos + (alongX ? new Vector2(0, s) : new Vector2(s, 0));
            if (!Blocked(np, a.radius, avoidLava)) a.pos = np;
        }

        bool ClearPath(Vector2 from, Vector2 to, float r, bool avoidLava)
        {
            float len = Vector2.Distance(from, to);
            for (float t = 0.2f; t < len; t += 0.2f)
                if (Blocked(Vector2.Lerp(from, to, t / len), r, avoidLava)) return false;
            return !Blocked(to, r, avoidLava);
        }

        /// <summary>
        /// Direction for an actor following a distance field downhill. Looks a few tiles ahead and aims at the
        /// furthest one reachable in a straight line, so bodies round corners instead of wedging on them.
        /// </summary>
        public Vector2? SteerAlong(Actor a, int[,] field)
        {
            if (field == null || !map.InBounds(a.x, a.y) || field[a.x, a.y] == int.MaxValue) return null;
            bool avoidLava = a != player;
            var path = new List<Vector2Int>();
            var cur = a.Pos;
            for (int k = 0; k < 5; k++)
            {
                int bd = field[cur.x, cur.y];
                Vector2Int? next = null;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = cur.x + dx, ny = cur.y + dy;
                        if (!map.InBounds(nx, ny) || !map.CanStep(cur.x, cur.y, dx, dy) || (avoidLava && map.Get(nx, ny) == Tile.Lava)) continue;
                        if (field[nx, ny] < bd) { bd = field[nx, ny]; next = new Vector2Int(nx, ny); }
                    }
                if (next == null) break;
                path.Add(next.Value);
                cur = next.Value;
            }
            if (path.Count == 0) return null;
            for (int k = path.Count - 1; k >= 0; k--)
                if (ClearPath(a.pos, path[k], a.radius, avoidLava)) return ((Vector2)path[k] - a.pos).normalized;
            // Body doesn't fit yet (e.g. entering a 1-wide corridor off-center): aim at the next tile's center.
            // Wall sliding plus the shrinking sideways component lines us up without overshooting.
            return ((Vector2)path[0] - a.pos).normalized;
        }

        public Vector2Int? FindFreeNear(Vector2Int c, int radius)
        {
            if (TileFree(c.x, c.y) && map.Get(c.x, c.y) != Tile.Lava) return c;
            for (int r = 1; r <= radius; r++)
            {
                var opts = new List<Vector2Int>();
                for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                        int x = c.x + dx, y = c.y + dy;
                        if (TileFree(x, y) && map.Get(x, y) != Tile.Lava && map.Get(x, y) != Tile.StairsDown) opts.Add(new Vector2Int(x, y));
                    }
                if (opts.Count > 0) return rng.Pick(opts);
            }
            return null;
        }

        public List<Actor> ActorsInRadius(Vector2 center, float radius)
        {
            var result = new List<Actor>();
            int cx = Mathf.RoundToInt(center.x), cy = Mathf.RoundToInt(center.y);
            foreach (var a in actors)
            {
                if (a.dead) continue;
                if (Vector2.Distance(a.pos, center) > radius + a.radius * 0.5f) continue;
                if ((a.x != cx || a.y != cy) && !map.HasLOS(cx, cy, a.x, a.y)) continue;
                result.Add(a);
            }
            return result;
        }

        public void ComputeDistances() => playerDist = map.DistanceField(new[] { player.Pos }, true);

        public void UpdateFov()
        {
            map.ComputeFov(player.x, player.y, Mathf.Clamp(Mathf.RoundToInt(player.Get(StatType.LightRadius)), 3, 14));
            fovVersion++;
        }

        public IEnumerable<Monster> VisibleEnemies() =>
            actors.OfType<Monster>().Where(m => !m.dead && m.faction == Faction.Monster && map.visible[m.x, m.y]);

        public Monster NearestEnemy(float range)
        {
            Monster best = null; float bd = float.MaxValue;
            foreach (var m in VisibleEnemies())
            {
                float d = Combat.Dist(player, m);
                if (d > range || d >= bd || !map.HasLOS(player.x, player.y, m.x, m.y)) continue;
                best = m; bd = d;
            }
            return best;
        }

        // ------------------------------------------------------------------ projectiles

        public void SpawnBasicProjectile(Actor owner, Vector2 dir)
        {
            projectiles.Add(new Projectile
            {
                owner = owner, faction = owner.faction, pos = owner.pos + dir * (owner.radius * 0.8f), dir = dir, basic = true,
                speed = owner.faction == Faction.Player ? 14f : 8.5f, remaining = owner.attackRange + 1f, size = 0.22f,
                color = SkillDatabase.ElementColor(Combat.Convert(owner, owner.attackElement)),
            });
        }

        public void SpawnSkillProjectile(Actor owner, SkillDef s, int rank, Vector2 dir, Color color, int explodeRadius, bool isProc)
        {
            projectiles.Add(new Projectile
            {
                owner = owner, faction = owner.faction, pos = owner.pos + dir * (owner.radius * 0.8f), dir = dir, skill = s, rank = rank,
                isProc = isProc, pierce = s.pierce, explodeRadius = explodeRadius, color = color,
                speed = owner.faction == Faction.Player ? 13f : 8f, remaining = s.range + 1f, size = explodeRadius > 0 ? 0.38f : 0.28f,
            });
        }

        void UpdateProjectiles(float dt)
        {
            if (projectiles.Count == 0) return;
            var snapshot = actors.ToArray();
            for (int i = projectiles.Count - 1; i >= 0; i--)
            {
                var p = projectiles[i];
                float move = p.speed * dt;
                while (move > 0 && !p.dead)
                {
                    float step = Mathf.Min(0.2f, move);
                    move -= step;
                    p.pos += p.dir * step;
                    p.remaining -= step;
                    if (!map.Transparent(Mathf.RoundToInt(p.pos.x), Mathf.RoundToInt(p.pos.y)))
                    {
                        p.pos -= p.dir * step;
                        Impact(p);
                        break;
                    }
                    foreach (var a in snapshot)
                    {
                        if (a.dead || a.faction == p.faction || p.hit.Contains(a)) continue;
                        if (Vector2.Distance(a.pos, p.pos) > a.radius + p.size * 0.5f) continue;
                        p.hit.Add(a);
                        if (p.explodeRadius > 0) { Impact(p); break; }
                        HitWithProjectile(p, a);
                        if (!p.pierce) { p.dead = true; break; }
                    }
                    if (!p.dead && p.remaining <= 0)
                    {
                        if (p.explodeRadius > 0) Impact(p); else p.dead = true;
                    }
                }
                if (p.dead) projectiles.Remove(p);
            }
        }

        void Impact(Projectile p)
        {
            p.dead = true;
            if (p.explodeRadius > 0 && p.skill != null) SkillExecutor.Explosion(p.owner, p.skill, p.rank, p.pos, p.explodeRadius, p.isProc);
            else Fx.Flash(p.pos, p.color);
        }

        void HitWithProjectile(Projectile p, Actor a)
        {
            if (p.basic) Combat.Deal(Combat.BasicHit(p.owner, a, false));
            else SkillExecutor.HitWith(p.owner, p.skill, p.rank, a, p.isProc);
        }

        // ------------------------------------------------------------------ player actions

        /// <summary>Basic attack toward an aim point. Returns false if nothing happened.</summary>
        public bool PlayerAttack(Vector2 aim)
        {
            var p = player;
            if (p.dead || p.Incapacitated || p.actionLock > 0 || p.Dodging) return false;
            var dir = aim - p.pos;
            if (dir.sqrMagnitude < 0.01f) dir = p.facing;
            dir.Normalize();
            p.facing = dir;
            if (p.attackRange > 1)
            {
                int swings = 1 + (rng.Chance(p.Get(StatType.DoubleStrike) / 100f) ? 1 : 0);
                for (int i = 0; i < swings; i++) SpawnBasicProjectile(p, Combat.Rotate(dir, i * 6f));
            }
            else
            {
                var t = Combat.MeleeTarget(p, dir);
                if (t == null) return false;
                Combat.BasicAttack(p, t);
            }
            p.actionLock = p.ActionTime(0.6f);
            return true;
        }

        /// <summary>Resolve a sensible target for a hotbar skill and cast it.</summary>
        public bool PlayerUseSkill(int slot, Vector2 aim)
        {
            var p = player;
            if (p.dead || p.Incapacitated || p.Dodging || p.actionLock > 0) return false;
            var id = p.hotbar[slot];
            if (string.IsNullOrEmpty(id)) return false;
            var def = SkillDatabase.Get(id);
            int rank = p.SkillRank(id);
            if (def == null || rank <= 0) { Log("You can't use that skill right now."); return false; }

            Vector2 target = aim;
            Vector2 dir = aim - p.pos;
            float dist = dir.magnitude;
            switch (def.targeting)
            {
                case Targeting.Self:
                case Targeting.Summon:
                    target = p.pos;
                    break;
                case Targeting.Blink:
                case Targeting.Ground:
                    if (dist > def.range) target = p.pos + dir / dist * def.range;
                    break;
                case Targeting.Projectile:
                case Targeting.Melee:
                    break;
                default:
                {
                    var at = HostileNear(p, aim, 1.1f);
                    if (at == null || Combat.Dist(p, at) > def.range + 0.5f || !map.visible[at.x, at.y])
                    {
                        at = NearestEnemy(def.range);
                        if (at == null) { Log("No target in range."); return false; }
                    }
                    target = at.pos;
                    break;
                }
            }
            if (!SkillExecutor.TryCast(p, def, rank, target)) return false;
            bool quick = (def.targeting == Targeting.Self && def.radius == 0) || def.targeting == Targeting.Summon || def.targeting == Targeting.Blink;
            p.actionLock = p.ActionTime(quick ? 0.3f : 0.55f);
            return true;
        }

        public void PlayerDodge(Vector2 dir)
        {
            var p = player;
            if (p.dead || p.Incapacitated || p.Dodging || p.dodgeCooldown > 0) return;
            if (dir.sqrMagnitude < 0.01f) dir = p.facing;
            p.dodgeDir = dir.normalized;
            p.dodgeTime = Player.DodgeDuration;
            p.dodgeCooldown = Player.DodgeCooldownTime;
        }

        public bool QuaffHealth()
        {
            var p = player;
            if (p.potionLock > 0) return false;
            if (p.healthPotions <= 0) { Log("No health potions!"); return false; }
            if (p.life >= p.MaxLife) { Log("You are already at full life."); return false; }
            p.healthPotions--;
            p.potionLock = 1f;
            float amt = p.MaxLife * 0.45f + 10;
            p.Heal(amt);
            p.statuses.RemoveAll(s => s.type == StatusType.Poison || s.type == StatusType.Bleed || s.type == StatusType.Burn);
            Fx.Text(p.pos, "+" + Mathf.RoundToInt(amt), new Color(0.4f, 1f, 0.4f));
            return true;
        }

        public bool QuaffMana()
        {
            var p = player;
            if (p.potionLock > 0) return false;
            if (p.manaPotions <= 0) { Log("No mana potions!"); return false; }
            p.manaPotions--;
            p.potionLock = 1f;
            p.mana = Mathf.Min(p.MaxMana, p.mana + p.MaxMana * 0.6f + 10);
            Fx.Text(p.pos, "+mana", new Color(0.4f, 0.6f, 1f));
            return true;
        }

        Vector2Int? NearestInteractable()
        {
            Vector2Int? best = null; float bd = 1.45f;
            for (int dx = -2; dx <= 2; dx++)
                for (int dy = -2; dy <= 2; dy++)
                {
                    int x = player.x + dx, y = player.y + dy;
                    if (!DungeonMap.IsInteractable(map.Get(x, y))) continue;
                    float d = Vector2.Distance(player.pos, new Vector2(x, y));
                    if (d < bd) { bd = d; best = new Vector2Int(x, y); }
                }
            return best;
        }

        /// <summary>What pressing E would do right now, for the HUD.</summary>
        public string InteractHint()
        {
            if (map.Get(player.x, player.y) == Tile.StairsDown) return map.stairsSealed ? "The stairs are sealed" : "[E] Descend";
            var t = NearestInteractable();
            if (t != null)
            {
                switch (map.Get(t.Value.x, t.Value.y))
                {
                    case Tile.Chest: return "[E] Open chest";
                    case Tile.Shrine: return "[E] Touch shrine";
                    case Tile.Fountain: return "[E] Drink from fountain";
                    case Tile.Merchant: return "[E] Trade";
                }
            }
            if (map.Get(player.x, player.y) == Tile.StairsDown) return map.stairsSealed ? "The stairs are sealed" : "[E] Descend";
            return null;
        }

        public bool Interact()
        {
            if (player.dead) return false;
            // Standing on the stairs always means descend, even if a merchant or chest is adjacent.
            if (map.Get(player.x, player.y) == Tile.StairsDown) return Descend();
            var t = NearestInteractable();
            if (t != null) return InteractTile(t.Value.x, t.Value.y, map.Get(t.Value.x, t.Value.y));
            return false;
        }

        /// <summary>Interact with a specific tile if the player is close enough (mouse click).</summary>
        public bool InteractAt(Vector2Int tile)
        {
            if (!DungeonMap.IsInteractable(map.Get(tile.x, tile.y)) || Vector2.Distance(player.pos, tile) > 1.6f) return false;
            return InteractTile(tile.x, tile.y, map.Get(tile.x, tile.y));
        }

        public bool Descend()
        {
            if (map.Get(player.x, player.y) != Tile.StairsDown) { Log("There are no stairs here."); return false; }
            if (map.stairsSealed && !actors.Any(a => a.isBoss && !a.dead && a.faction == Faction.Monster)) map.stairsSealed = false;
            if (map.stairsSealed) { Log("The stairs are sealed. Defeat the guardian of this floor!", new Color(1f, 0.5f, 0.3f)); return false; }
            depth++;
            stats.deepest = Mathf.Max(stats.deepest, depth);
            player.statuses.Clear();
            player.Heal(player.MaxLife * 0.15f);
            GenerateFloor();
            return true;
        }

        bool InteractTile(int x, int y, Tile t)
        {
            var p = new Vector2Int(x, y);
            switch (t)
            {
                case Tile.Chest:
                    map.Set(x, y, Tile.ChestOpen);
                    Log("You open the chest.", new Color(1f, 0.9f, 0.5f));
                    int n = rng.Range(1, 4);
                    for (int i = 0; i < n; i++) DropItem(p, ItemGenerator.Generate(rng, MonsterLevel + 1, player.Get(StatType.MagicFind) + corruption * 10, 0.6f));
                    DropGold(p, Mathf.RoundToInt((10 + depth * 5) * rng.Range(0.7f, 1.5f)));
                    if (rng.Chance(0.3f)) AddGround(p, new GroundItem { potion = rng.Chance(0.6f) ? 1 : 2 });
                    return true;
                case Tile.Shrine:
                {
                    map.Set(x, y, Tile.ShrineUsed);
                    int kind = rng.Range(0, ShrineNames.Length);
                    var b = new Buff { id = "shrine", name = ShrineNames[kind], remaining = 60 };
                    switch (kind)
                    {
                        case 0: b.mods.Add(new StatMod(StatType.AttackDamage, 40)); b.mods.Add(new StatMod(StatType.SpellDamage, 40)); break;
                        case 1: b.mods.Add(new StatMod(StatType.MagicFind, 100)); b.mods.Add(new StatMod(StatType.GoldFind, 100)); break;
                        case 2: b.mods.Add(new StatMod(StatType.DamageReduction, 25)); b.mods.Add(new StatMod(StatType.AllRes, 25)); break;
                        case 3: b.mods.Add(new StatMod(StatType.ActionSpeed, 30)); b.mods.Add(new StatMod(StatType.MoveSpeed, 20)); break;
                        case 4: b.mods.Add(new StatMod(StatType.XpBonus, 50)); b.mods.Add(new StatMod(StatType.ManaRegen, 3)); break;
                        default: b.mods.Add(new StatMod(StatType.CritChance, 25)); b.mods.Add(new StatMod(StatType.Accuracy, 100)); break;
                    }
                    player.AddBuff(b);
                    Log($"You touch the {b.name}. You feel empowered.", new Color(0.6f, 0.9f, 1f));
                    Fx.Burst(player.pos, 1.5f, new Color(0.6f, 0.9f, 1f));
                    return true;
                }
                case Tile.Fountain:
                    map.Set(x, y, Tile.FountainUsed);
                    player.life = player.MaxLife;
                    player.mana = player.MaxMana;
                    player.statuses.Clear();
                    Log("The fountain's waters restore you completely.", new Color(0.5f, 0.8f, 1f));
                    Fx.Burst(player.pos, 1.5f, new Color(0.4f, 0.7f, 1f));
                    return true;
                case Tile.Merchant:
                    if (GameManager.Instance != null) GameManager.Instance.OpenShop();
                    return true;
            }
            return false;
        }

        void OnPlayerTileChanged()
        {
            var tile = player.Pos;
            if (tile == lastPlayerTile) return;
            lastPlayerTile = tile;
            ComputeDistances();
            UpdateFov();
            var t = map.Get(player.x, player.y);
            if (t == Tile.Trap)
            {
                map.Set(player.x, player.y, Tile.TrapSprung);
                Log("A spike trap springs!", new Color(1f, 0.4f, 0.4f));
                Combat.Environmental(player, player.MaxLife * 0.12f + depth * 2, Element.Physical, "a spike trap");
                if (!player.dead) player.AddStatus(StatusType.Bleed, 3, 2 + depth, Element.Physical, null);
            }
            else if (t == Tile.StairsDown && map.stairsSealed) Log("The stairs are sealed by a dark power.", new Color(0.8f, 0.8f, 1f));
        }

        void PickUpNearby()
        {
            for (int i = ground.Count - 1; i >= 0; i--)
            {
                var g = ground[i];
                if ((new Vector2(g.x, g.y) - player.pos).sqrMagnitude > 0.5f) continue;
                if (g.gold > 0)
                {
                    player.gold += g.gold; stats.goldEarned += g.gold;
                    Fx.Text(player.pos, $"+{g.gold}g", new Color(1f, 0.85f, 0.3f), 0.8f);
                    ground.RemoveAt(i);
                }
                else if (g.potion > 0)
                {
                    if (g.potion == 1) player.healthPotions++; else player.manaPotions++;
                    Log(g.potion == 1 ? "Picked up a Health Potion." : "Picked up a Mana Potion.", new Color(0.7f, 1f, 0.7f));
                    ground.RemoveAt(i);
                }
                else if (g.item != null)
                {
                    if (player.AddToBackpack(g.item))
                    {
                        Log("Picked up " + g.item.name, g.item.Color);
                        if (g.item.rarity == Rarity.Legendary)
                        {
                            stats.legendaries++;
                            if (!meta.codex.Contains(g.item.uniqueId)) { meta.codex.Add(g.item.uniqueId); meta.Save(); Log("New legendary recorded in your Codex!", Item.RarityColor(Rarity.Legendary)); }
                        }
                        ground.RemoveAt(i);
                    }
                    else if (!g.warned) { g.warned = true; Log("Your backpack is full!", new Color(1f, 0.5f, 0.5f)); }
                }
            }
        }

        // ------------------------------------------------------------------ inventory helpers

        public void DropItem(Vector2Int near, Item item) => AddGround(near, new GroundItem { item = item });
        public void DropGold(Vector2Int near, int amount)
        {
            amount = Mathf.RoundToInt(amount * (1 + player.Get(StatType.GoldFind) / 100f));
            if (amount > 0) AddGround(near, new GroundItem { gold = amount });
        }

        void AddGround(Vector2Int near, GroundItem g)
        {
            for (int r = 0; r <= 3; r++)
            {
                var opts = new List<Vector2Int>();
                for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                    {
                        int x = near.x + dx, y = near.y + dy;
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r || !map.Walkable(x, y) || map.Get(x, y) == Tile.Lava) continue;
                        if (ground.Any(o => o.x == x && o.y == y) || (x == player.x && y == player.y)) continue;
                        opts.Add(new Vector2Int(x, y));
                    }
                if (opts.Count == 0) continue;
                var p = rng.Pick(opts);
                g.x = p.x; g.y = p.y;
                ground.Add(g);
                return;
            }
            g.x = near.x; g.y = near.y;
            ground.Add(g);
        }

        public void Salvage(Item it)
        {
            if (!player.backpack.Remove(it)) return;
            int v = Mathf.Max(1, it.Value / 3);
            player.gold += v;
            Log($"Salvaged {it.name} for {v} gold.", new Color(1f, 0.85f, 0.4f));
        }

        public void DropFromBackpack(Item it)
        {
            if (!player.backpack.Remove(it)) return;
            var g = new GroundItem { item = it, warned = true };
            var spot = FindFreeNear(player.Pos, 2) ?? player.Pos;
            g.x = spot.x; g.y = spot.y;
            ground.Add(g);
        }

        public bool Buy(Item it)
        {
            int price = ShopPrice(it);
            if (player.gold < price) { Log("Not enough gold."); return false; }
            if (player.backpack.Count >= Player.BackpackSize) { Log("Your backpack is full."); return false; }
            player.gold -= price;
            shop.Remove(it);
            player.AddToBackpack(it);
            Log("Bought " + it.name, it.Color);
            if (it.rarity == Rarity.Legendary && !meta.codex.Contains(it.uniqueId)) { meta.codex.Add(it.uniqueId); meta.Save(); }
            return true;
        }

        public bool BuyPotion(int kind)
        {
            if (player.gold < PotionPrice) { Log("Not enough gold."); return false; }
            player.gold -= PotionPrice;
            if (kind == 1) player.healthPotions++; else player.manaPotions++;
            return true;
        }

        // ------------------------------------------------------------------ simulation

        /// <summary>Advances the whole simulation by dt seconds.</summary>
        public void Tick(float dt)
        {
            if (player.dead) return;
            time += dt;
            stats.time += dt;

            var p = player;
            TickActor(p, dt);
            if (p.dead) return;
            p.dodgeCooldown -= dt;
            p.potionLock -= dt;
            if (p.Dodging)
            {
                p.dodgeTime -= dt;
                TryMove(p, p.dodgeDir * Player.DodgeSpeed * dt);
            }
            else if (!p.Incapacitated && p.moveInput.sqrMagnitude > 0.01f)
            {
                var mv = p.moveInput.normalized;
                TryMove(p, mv * p.MoveSpeed * dt);
                if (p.actionLock <= 0) p.facing = mv;
            }
            OnPlayerTileChanged();
            PickUpNearby();

            // Only actors near the player (or awake) need full simulation.
            active.Clear();
            foreach (var a in actors)
            {
                if (a.dead || a == p) continue;
                bool near = (a.pos - p.pos).sqrMagnitude < 18f * 18f;
                if (near || (a is Monster m && (m.awake || m.faction == Faction.Player))) active.Add(a);
            }
            foreach (var a in active)
            {
                if (a.dead) continue;
                TickActor(a, dt);
                if (!a.dead && a is Monster m) MonsterAI.Tick(this, m, dt);
                if (p.dead) return;
            }
            UpdateProjectiles(dt);
            Separate();
            actors.RemoveAll(a => a.dead);
        }

        void Separate()
        {
            active.Add(player);
            for (int i = 0; i < active.Count; i++)
            {
                var a = active[i];
                if (a.dead) continue;
                for (int j = i + 1; j < active.Count; j++)
                {
                    var b = active[j];
                    if (b.dead) continue;
                    var d = b.pos - a.pos;
                    float min = a.radius + b.radius;
                    float sq = d.sqrMagnitude;
                    if (sq >= min * min) continue;
                    float len = Mathf.Sqrt(sq);
                    var n = len > 0.0001f ? d / len : new Vector2(rng.Range(-1f, 1f), rng.Range(-1f, 1f)).normalized;
                    float push = (min - len);
                    float wa = a == player ? 0.2f : 0.5f, wb = b == player ? 0.2f : 0.5f;
                    if (a.isBoss) wa = 0.1f;
                    if (b.isBoss) wb = 0.1f;
                    TryMove(a, -n * push * wa);
                    TryMove(b, n * push * wb);
                }
            }
        }

        void TickActor(Actor a, float dt)
        {
            if (a.dead) return;
            a.life = Mathf.Min(a.MaxLife, a.life + a.Get(StatType.LifeRegen) * dt);
            a.mana = Mathf.Min(a.MaxMana, a.mana + a.Get(StatType.ManaRegen) * dt);
            if (a.shield > 0) a.shield *= 1f - 0.03f * dt;
            a.actionLock -= dt;
            a.TickCooldowns(dt);
            Combat.TickStatuses(a, dt);
            if (a.dead) return;
            bool changed = false;
            for (int i = a.buffs.Count - 1; i >= 0; i--)
            {
                a.buffs[i].remaining -= dt;
                if (a.buffs[i].remaining <= 0) { a.buffs.RemoveAt(i); changed = true; }
            }
            if (changed) a.Recalc();
            if (a == player && map.Get(a.x, a.y) == Tile.Lava && a.Get(StatType.FireRes) < 75 && !player.Dodging)
            {
                lavaTimer += dt;
                if (lavaTimer >= 0.5f) { lavaTimer = 0; Combat.Environmental(a, a.MaxLife * 0.025f + 1, Element.Fire, "lava"); }
            }
            if (a is Monster m && m.lifetime > 0)
            {
                m.lifetime -= dt;
                if (m.lifetime <= 0) Kill(m, null);
            }
        }

        public void Kill(Actor victim, Actor killer)
        {
            if (victim.dead) return;
            victim.dead = true;
            victim.life = 0;

            if (victim == player)
            {
                if (string.IsNullOrEmpty(stats.killedBy)) stats.killedBy = killer != null ? (killer is Monster km ? km.DisplayName() : killer.name) : "the dungeon";
                Log("You have been slain by " + stats.killedBy + ".", new Color(1f, 0.3f, 0.3f));
                return;
            }

            var m = victim as Monster;
            if (m == null) return;
            Fx.Burst(victim.pos, 0.5f, new Color(0.6f, 0.1f, 0.1f));

            if (m.faction == Faction.Player)
            {
                Log($"Your {m.name} falls.", new Color(0.7f, 0.7f, 0.7f));
                return;
            }

            var credit = killer != null ? (killer.owner ?? killer) : null;
            bool byPlayer = credit == player;

            if (byPlayer)
            {
                float boom = victim.MaxLife * 0.2f;
                if (player.keystones.Contains(Keystone.Pyre) && victim.Has(StatusType.Burn)) Combat.Explode(player, Faction.Player, victim.pos, 1, boom, Element.Fire);
                if (player.keystones.Contains(Keystone.PlagueBearer) && victim.Has(StatusType.Poison)) Combat.Explode(player, Faction.Player, victim.pos, 1, boom, Element.Poison);
            }
            if (m.affixes.Contains(MonsterAffix.Volatile)) Combat.Explode(m, Faction.Monster, victim.pos, 1, m.weaponMax * 2f, Element.Fire);

            if (m.isSummon) return;
            stats.kills++;
            player.kills++;
            if (m.isBoss)
            {
                stats.bossKills++;
                map.stairsSealed = false;
                Log($"{m.name} has been vanquished! The stairs are unsealed.", new Color(1f, 0.7f, 0.2f));
            }
            if (!byPlayer) { DropLoot(m); return; }

            int levels = player.GainXp(m.xpValue);
            if (levels > 0)
            {
                Log($"Level up! You are now level {player.level}. (+{levels * 5} attribute points, +{levels} skill point)", new Color(1f, 1f, 0.5f));
                Fx.Text(player.pos, "LEVEL UP!", new Color(1f, 1f, 0.4f), 1.5f);
                Fx.Burst(player.pos, 1.5f, new Color(1f, 1f, 0.5f));
            }
            player.Heal(player.Get(StatType.LifeOnKill));
            player.mana = Mathf.Min(player.MaxMana, player.mana + player.Get(StatType.ManaOnKill));
            Combat.TriggerProcs(player, ProcTrigger.OnKill, null, victim.pos);
            DropLoot(m);
        }

        void DropLoot(Monster m)
        {
            float mf = player.Get(StatType.MagicFind) + corruption * 10;
            float qty = 1 + corruption * 0.1f;
            int ilvl = m.level;
            int drops = 0; float quality = 0;
            switch (m.rank)
            {
                case 0: drops = rng.Chance(0.12f * qty) ? 1 : 0; break;
                case 1: drops = 1 + (rng.Chance(0.4f * qty) ? 1 : 0); quality = 0.3f; break;
                case 2: drops = rng.Range(2, 4); quality = 0.7f; break;
                case 3: drops = rng.Range(4, 6); quality = 1.5f; break;
            }
            for (int i = 0; i < drops; i++)
            {
                var it = (m.rank == 3 && i == 0)
                    ? ItemGenerator.Generate(rng, ilvl, rng.Chance(0.25f) ? Rarity.Legendary : Rarity.Epic, ItemGenerator.RollBase(rng, ItemGenerator.RollKind(rng), ilvl))
                    : ItemGenerator.Generate(rng, ilvl, mf, quality);
                DropItem(m.Pos, it);
                if (it.rarity >= Rarity.Epic) Fx.Text(m.pos, it.rarity.ToString() + "!", it.Color, 1.1f);
            }
            if (rng.Chance(m.rank > 0 ? 0.9f : 0.35f)) DropGold(m.Pos, Mathf.RoundToInt((3 + ilvl * 1.5f) * rng.Range(0.6f, 1.4f) * (1 + m.rank)));
            if (rng.Chance(0.06f + m.rank * 0.1f)) AddGround(m.Pos, new GroundItem { potion = 1 });
            else if (rng.Chance(0.04f + m.rank * 0.06f)) AddGround(m.Pos, new GroundItem { potion = 2 });
        }

        public int EssenceEarned()
        {
            int d = stats.deepest;
            return Mathf.RoundToInt((d * d * 0.6f + d * 4 + stats.kills * 0.2f + stats.bossKills * 25) * (1 + 0.25f * corruption) * (daily ? 1.5f : 1f));
        }
    }
}
