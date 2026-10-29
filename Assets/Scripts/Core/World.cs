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
    }

    public class RunStats
    {
        public int kills, bossKills, goldEarned, legendaries, deepest = 1, turns;
        public string killedBy = "";
    }

    /// <summary>All state for one run: the current floor, actors, loot and the turn loop.</summary>
    public class World
    {
        public static World Current;

        public DungeonMap map;
        public Player player;
        public readonly List<Actor> actors = new List<Actor>();
        Actor[,] occ;
        public readonly List<GroundItem> ground = new List<GroundItem>();
        public readonly List<Item> shop = new List<Item>();
        public readonly RunStats stats = new RunStats();
        public readonly List<(string text, Color color)> log = new List<(string, Color)>();

        public int depth = 1, corruption, runSeed, turn, floorVersion;
        public bool daily;
        public Rng rng;
        public int[,] playerDist;
        public MetaProgress meta;

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
            log.Add((text, c));
            if (log.Count > 60) log.RemoveAt(0);
        }

        public void Log(string text) => Log(text, new Color(0.85f, 0.85f, 0.85f));

        // ------------------------------------------------------------------ floors

        public void GenerateFloor()
        {
            var floorRng = new Rng(runSeed * 31 + depth * 7919 + 17);
            map = DungeonGenerator.Generate(floorRng, depth);
            occ = new Actor[map.w, map.h];
            var allies = actors.Where(a => a != player && a.faction == Faction.Player && !a.dead).ToList();
            actors.Clear();
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
            ComputeDistances();
            UpdateFov();
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
                    FinishMonster(leader);
                    PlaceNear(leader, center);
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
            m.xpValue = def.xp * (1 + 0.12f * (lvl - 1)) * xpMult;
            m.skillRank = 1 + lvl / 8;
            return m;
        }

        Monster FinishMonster(Monster m)
        {
            m.Recalc();
            m.life = m.MaxLife;
            if (m.affixes.Contains(MonsterAffix.Shielded)) m.shield = m.MaxLife * 0.35f;
            m.energy = rng.Range(0f, 99f);
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

        // ------------------------------------------------------------------ occupancy

        public Actor ActorAt(Vector2Int p) => ActorAt(p.x, p.y);
        public Actor ActorAt(int x, int y) => map.InBounds(x, y) ? occ[x, y] : null;
        public bool IsFree(int x, int y) => map.Walkable(x, y) && ActorAt(x, y) == null;

        public void Place(Actor a, Vector2Int p)
        {
            a.x = p.x; a.y = p.y;
            occ[p.x, p.y] = a;
            if (!actors.Contains(a)) actors.Add(a);
        }

        public void MoveActor(Actor a, int nx, int ny)
        {
            if (occ[a.x, a.y] == a) occ[a.x, a.y] = null;
            a.x = nx; a.y = ny;
            occ[nx, ny] = a;
        }

        void Swap(Actor a, Actor b)
        {
            int ax = a.x, ay = a.y;
            a.x = b.x; a.y = b.y; b.x = ax; b.y = ay;
            occ[a.x, a.y] = a; occ[b.x, b.y] = b;
        }

        public Vector2Int? FindFreeNear(Vector2Int c, int radius)
        {
            if (IsFree(c.x, c.y) && map.Get(c.x, c.y) != Tile.Lava) return c;
            for (int r = 1; r <= radius; r++)
            {
                var opts = new List<Vector2Int>();
                for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                        int x = c.x + dx, y = c.y + dy;
                        if (IsFree(x, y) && map.Get(x, y) != Tile.Lava && map.Get(x, y) != Tile.StairsDown) opts.Add(new Vector2Int(x, y));
                    }
                if (opts.Count > 0) return rng.Pick(opts);
            }
            return null;
        }

        public IEnumerable<Actor> ActorsInRadius(Vector2Int center, int radius)
        {
            var result = new List<Actor>();
            foreach (var a in actors)
            {
                if (a.dead) continue;
                if (Combat.Dist(a.Pos, center) > radius) continue;
                if (a.Pos != center && !map.HasLOS(center.x, center.y, a.x, a.y)) continue;
                result.Add(a);
            }
            return result;
        }

        public void ComputeDistances() => playerDist = map.DistanceField(new[] { player.Pos }, true);

        public void UpdateFov() => map.ComputeFov(player.x, player.y, Mathf.Clamp(Mathf.RoundToInt(player.Get(StatType.LightRadius)), 3, 14));

        public IEnumerable<Monster> VisibleEnemies() =>
            actors.OfType<Monster>().Where(m => !m.dead && m.faction == Faction.Monster && map.visible[m.x, m.y]);

        // ------------------------------------------------------------------ player actions

        public bool PlayerMove(int dx, int dy)
        {
            if (player.dead) return false;
            int nx = player.x + dx, ny = player.y + dy;
            var target = ActorAt(nx, ny);
            if (target != null)
            {
                if (target.IsHostileTo(player))
                {
                    if (!Combat.CanMelee(player, target)) return false;
                    Combat.BasicAttack(player, target);
                    EndTurn();
                    return true;
                }
                if (!map.CanStep(player.x, player.y, dx, dy)) return false;
                Swap(player, target);
                OnPlayerEnter();
                EndTurn();
                return true;
            }
            var t = map.Get(nx, ny);
            if (DungeonMap.IsInteractable(t))
            {
                if (dx != 0 && dy != 0 && !map.CanStep(player.x, player.y, dx, 0) && !map.CanStep(player.x, player.y, 0, dy)) return false;
                return Interact(nx, ny, t);
            }
            if (!map.CanStep(player.x, player.y, dx, dy)) return false;
            MoveActor(player, nx, ny);
            OnPlayerEnter();
            EndTurn();
            return true;
        }

        public bool PlayerAttackTarget(Actor target)
        {
            if (target == null || target.dead || !target.IsHostileTo(player)) return false;
            int d = Combat.Dist(player, target);
            bool canHit = player.attackRange > 1
                ? d <= player.attackRange && map.HasLOS(player.x, player.y, target.x, target.y)
                : Combat.CanMelee(player, target);
            if (!canHit) return false;
            Combat.BasicAttack(player, target);
            EndTurn();
            return true;
        }

        public void PlayerWait()
        {
            if (ground.Any(g => g.x == player.x && g.y == player.y)) PickUpHere();
            EndTurn();
        }

        /// <summary>Resolve a sensible target for a hotbar skill and cast it.</summary>
        public bool PlayerUseSkill(int slot, Vector2Int? aim)
        {
            var id = player.hotbar[slot];
            if (string.IsNullOrEmpty(id)) return false;
            var def = SkillDatabase.Get(id);
            int rank = player.SkillRank(id);
            if (def == null || rank <= 0) { Log("You can't use that skill right now."); return false; }
            if (aim != null && !map.InBounds(aim.Value.x, aim.Value.y)) aim = null;

            Vector2Int target = player.Pos;
            switch (def.targeting)
            {
                case Targeting.Self:
                case Targeting.Summon:
                    break;
                case Targeting.Blink:
                    if (aim == null) { Log("Point the mouse where you want to teleport."); return false; }
                    target = aim.Value;
                    break;
                case Targeting.Ground:
                case Targeting.Projectile:
                    if (aim != null && aim.Value != player.Pos && map.visible[aim.Value.x, aim.Value.y] && Combat.Dist(player.Pos, aim.Value) <= def.range && map.HasLOS(player.x, player.y, aim.Value.x, aim.Value.y))
                        target = aim.Value;
                    else
                    {
                        var e = NearestEnemy(def.range);
                        if (e == null) { Log("No target in range."); return false; }
                        target = e.Pos;
                    }
                    break;
                default:
                {
                    int range = def.targeting == Targeting.Melee ? 1 : def.range;
                    var at = aim != null ? ActorAt(aim.Value) : null;
                    if (at != null && at.IsHostileTo(player) && Combat.Dist(player, at) <= range && map.visible[at.x, at.y]) target = at.Pos;
                    else
                    {
                        var e = def.targeting == Targeting.Melee
                            ? VisibleEnemies().FirstOrDefault(m => Combat.CanMelee(player, m))
                            : NearestEnemy(range);
                        if (e == null) { Log(def.targeting == Targeting.Melee ? "No adjacent enemy." : "No target in range."); return false; }
                        target = e.Pos;
                    }
                    break;
                }
            }
            if (!SkillExecutor.TryCast(player, def, rank, target)) return false;
            EndTurn();
            return true;
        }

        public Monster NearestEnemy(int range)
        {
            Monster best = null; int bd = int.MaxValue;
            foreach (var m in VisibleEnemies())
            {
                int d = Combat.Dist(player, m);
                if (d > range || d >= bd || !map.HasLOS(player.x, player.y, m.x, m.y)) continue;
                best = m; bd = d;
            }
            return best;
        }

        public bool QuaffHealth()
        {
            if (player.healthPotions <= 0) { Log("No health potions!"); return false; }
            if (player.life >= player.MaxLife) { Log("You are already at full life."); return false; }
            player.healthPotions--;
            float amt = player.MaxLife * 0.45f + 10;
            player.Heal(amt);
            player.statuses.RemoveAll(s => s.type == StatusType.Poison || s.type == StatusType.Bleed || s.type == StatusType.Burn);
            Fx.Text(player.Pos, "+" + Mathf.RoundToInt(amt), new Color(0.4f, 1f, 0.4f));
            EndTurn();
            return true;
        }

        public bool QuaffMana()
        {
            if (player.manaPotions <= 0) { Log("No mana potions!"); return false; }
            player.manaPotions--;
            player.mana = Mathf.Min(player.MaxMana, player.mana + player.MaxMana * 0.6f + 10);
            Fx.Text(player.Pos, "+mana", new Color(0.4f, 0.6f, 1f));
            EndTurn();
            return true;
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

        bool Interact(int x, int y, Tile t)
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
                    if (rng.Chance(0.3f)) ground.Add(new GroundItem { x = x, y = y, potion = rng.Chance(0.6f) ? 1 : 2 });
                    EndTurn();
                    return true;
                case Tile.Shrine:
                {
                    map.Set(x, y, Tile.ShrineUsed);
                    int kind = rng.Range(0, ShrineNames.Length);
                    var b = new Buff { id = "shrine", name = ShrineNames[kind], turns = 60 };
                    switch (kind)
                    {
                        case 0: b.mods.Add(new StatMod(StatType.AttackDamage, 40)); b.mods.Add(new StatMod(StatType.SpellDamage, 40)); break;
                        case 1: b.mods.Add(new StatMod(StatType.MagicFind, 100)); b.mods.Add(new StatMod(StatType.GoldFind, 100)); break;
                        case 2: b.mods.Add(new StatMod(StatType.DamageReduction, 25)); b.mods.Add(new StatMod(StatType.AllRes, 25)); break;
                        case 3: b.mods.Add(new StatMod(StatType.DoubleStrike, 40)); b.mods.Add(new StatMod(StatType.CooldownReduction, 25)); break;
                        case 4: b.mods.Add(new StatMod(StatType.XpBonus, 50)); b.mods.Add(new StatMod(StatType.ManaRegen, 3)); break;
                        default: b.mods.Add(new StatMod(StatType.CritChance, 25)); b.mods.Add(new StatMod(StatType.Accuracy, 100)); break;
                    }
                    player.AddBuff(b);
                    Log($"You touch the {b.name}. You feel empowered.", new Color(0.6f, 0.9f, 1f));
                    Fx.Burst(player.Pos, 1, new Color(0.6f, 0.9f, 1f));
                    EndTurn();
                    return true;
                }
                case Tile.Fountain:
                    map.Set(x, y, Tile.FountainUsed);
                    player.life = player.MaxLife;
                    player.mana = player.MaxMana;
                    player.statuses.Clear();
                    Log("The fountain's waters restore you completely.", new Color(0.5f, 0.8f, 1f));
                    Fx.Burst(player.Pos, 1, new Color(0.4f, 0.7f, 1f));
                    EndTurn();
                    return true;
                case Tile.Merchant:
                    if (GameManager.Instance != null) GameManager.Instance.OpenShop();
                    return false;
            }
            return false;
        }

        void OnPlayerEnter()
        {
            PickUpHere();
            var t = map.Get(player.x, player.y);
            if (t == Tile.Trap)
            {
                map.Set(player.x, player.y, Tile.TrapSprung);
                Log("A spike trap springs!", new Color(1f, 0.4f, 0.4f));
                Combat.Environmental(player, player.MaxLife * 0.12f + depth * 2, Element.Physical, "a spike trap");
                if (!player.dead) player.AddStatus(StatusType.Bleed, 3, 2 + depth, Element.Physical, null);
            }
            else if (t == Tile.StairsDown) Log(map.stairsSealed ? "The stairs are sealed by a dark power." : "Stairs down. Press Enter or > to descend.", new Color(0.8f, 0.8f, 1f));
        }

        void PickUpHere()
        {
            var here = ground.Where(g => g.x == player.x && g.y == player.y).ToList();
            foreach (var g in here)
            {
                if (g.gold > 0)
                {
                    player.gold += g.gold; stats.goldEarned += g.gold;
                    Fx.Text(player.Pos, $"+{g.gold}g", new Color(1f, 0.85f, 0.3f), 0.8f);
                    ground.Remove(g);
                }
                else if (g.potion > 0)
                {
                    if (g.potion == 1) player.healthPotions++; else player.manaPotions++;
                    Log(g.potion == 1 ? "Picked up a Health Potion." : "Picked up a Mana Potion.", new Color(0.7f, 1f, 0.7f));
                    ground.Remove(g);
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
                        ground.Remove(g);
                    }
                    else Log("Your backpack is full!", new Color(1f, 0.5f, 0.5f));
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
            AddGround(player.Pos, new GroundItem { item = it });
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

        // ------------------------------------------------------------------ turn loop

        public void EndTurn()
        {
            turn++;
            stats.turns++;
            TickActor(player);
            if (player.dead) return;
            ComputeDistances();

            var list = actors.ToArray();
            float chillBonus = player.Has(StatusType.Chill) ? 1.6f : 1f;
            foreach (var a in list)
            {
                if (a == player || a.dead) continue;
                a.energy += a.Speed * chillBonus;
                int guard = 0;
                while (a.energy >= 100 && !a.dead && guard++ < 3)
                {
                    a.energy -= 100;
                    if (!a.Incapacitated && a is Monster m) MonsterAI.Act(this, m);
                    if (player.dead) return;
                }
            }
            foreach (var a in list)
                if (a != player && !a.dead) TickActor(a);

            actors.RemoveAll(a => a.dead);
            UpdateFov();
        }

        void TickActor(Actor a)
        {
            if (a.dead) return;
            a.life = Mathf.Min(a.MaxLife, a.life + a.Get(StatType.LifeRegen));
            a.mana = Mathf.Min(a.MaxMana, a.mana + a.Get(StatType.ManaRegen));
            a.shield *= 0.97f;
            a.TickCooldowns();
            Combat.TickStatuses(a);
            if (a.dead) return;
            bool changed = false;
            for (int i = a.buffs.Count - 1; i >= 0; i--)
                if (--a.buffs[i].turns <= 0) { a.buffs.RemoveAt(i); changed = true; }
            if (changed) a.Recalc();
            if (map.Get(a.x, a.y) == Tile.Lava && a.Get(StatType.FireRes) < 75)
                Combat.Environmental(a, a.MaxLife * 0.08f + 3, Element.Fire, "lava");
            if (a is Monster m && m.lifetime > 0 && --m.lifetime == 0) Kill(m, null);
        }

        public void Kill(Actor victim, Actor killer)
        {
            if (victim.dead) return;
            victim.dead = true;
            victim.life = 0;
            if (map.InBounds(victim.x, victim.y) && occ[victim.x, victim.y] == victim) occ[victim.x, victim.y] = null;

            if (victim == player)
            {
                if (string.IsNullOrEmpty(stats.killedBy)) stats.killedBy = killer != null ? (killer is Monster km ? km.DisplayName() : killer.name) : "the dungeon";
                Log("You have been slain by " + stats.killedBy + ".", new Color(1f, 0.3f, 0.3f));
                return;
            }

            var m = victim as Monster;
            if (m == null) return;
            Fx.Burst(victim.Pos, 0, new Color(0.6f, 0.1f, 0.1f));

            if (m.faction == Faction.Player)
            {
                if (!m.isSummon || m.lifetime != 0) Log($"Your {m.name} falls.", new Color(0.7f, 0.7f, 0.7f));
                return;
            }

            var credit = killer != null ? (killer.owner ?? killer) : null;
            bool byPlayer = credit == player;

            if (byPlayer)
            {
                float boom = victim.MaxLife * 0.2f;
                if (player.keystones.Contains(Keystone.Pyre) && victim.Has(StatusType.Burn)) Combat.Explode(player, Faction.Player, victim.Pos, 1, boom, Element.Fire);
                if (player.keystones.Contains(Keystone.PlagueBearer) && victim.Has(StatusType.Poison)) Combat.Explode(player, Faction.Player, victim.Pos, 1, boom, Element.Poison);
            }
            if (m.affixes.Contains(MonsterAffix.Volatile)) Combat.Explode(m, Faction.Monster, victim.Pos, 1, m.weaponMax * 2f, Element.Fire);

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
                Fx.Text(player.Pos, "LEVEL UP!", new Color(1f, 1f, 0.4f), 1.5f);
                Fx.Burst(player.Pos, 1, new Color(1f, 1f, 0.5f));
            }
            player.Heal(player.Get(StatType.LifeOnKill));
            player.mana = Mathf.Min(player.MaxMana, player.mana + player.Get(StatType.ManaOnKill));
            Combat.TriggerProcs(player, ProcTrigger.OnKill, null, victim.Pos);
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
                if (it.rarity >= Rarity.Epic) Fx.Text(m.Pos, it.rarity.ToString() + "!", it.Color, 1.1f);
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
