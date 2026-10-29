using System.Collections.Generic;
using UnityEngine;

namespace Depthbound
{
    public static class DungeonGenerator
    {
        public static DungeonMap Generate(Rng rng, int depth)
        {
            var biome = Biomes.ForDepth(depth);
            int grow = Mathf.Min(depth, 40);
            int w = 56 + grow / 2, h = 40 + grow / 3;

            DungeonMap map = null;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                map = new DungeonMap(w, h) { biome = biome };
                switch (biome.gen)
                {
                    case GenType.Bsp: Bsp(map, rng); break;
                    case GenType.Caves: Caves(map, rng); break;
                    case GenType.Ruins: Ruins(map, rng); break;
                    default: Hybrid(map, rng); break;
                }
                KeepLargestRegion(map);
                if (CountFloor(map) > w * h * 0.18f) break;
            }

            ChooseStartAndStairs(map, rng);
            AddLiquid(map, rng);
            AddFeatures(map, rng, depth);
            for (int x = 0; x < map.w; x++) for (int y = 0; y < map.h; y++) map.variant[x, y] = (byte)rng.Range(0, 256);
            return map;
        }

        // ------------------------------------------------------------------ layouts

        static void Carve(DungeonMap m, RectInt r)
        {
            for (int x = r.xMin; x < r.xMax; x++)
                for (int y = r.yMin; y < r.yMax; y++)
                    if (x > 0 && y > 0 && x < m.w - 1 && y < m.h - 1) m.tiles[x, y] = Tile.Floor;
        }

        static void Corridor(DungeonMap m, Rng rng, Vector2Int a, Vector2Int b)
        {
            bool horizontalFirst = rng.Chance(0.5f);
            var corner = horizontalFirst ? new Vector2Int(b.x, a.y) : new Vector2Int(a.x, b.y);
            CarveLine(m, a, corner);
            CarveLine(m, corner, b);
        }

        static void CarveLine(DungeonMap m, Vector2Int a, Vector2Int b)
        {
            int x = a.x, y = a.y;
            while (true)
            {
                if (x > 0 && y > 0 && x < m.w - 1 && y < m.h - 1 && m.tiles[x, y] == Tile.Wall) m.tiles[x, y] = Tile.Floor;
                if (x == b.x && y == b.y) break;
                if (x != b.x) x += x < b.x ? 1 : -1;
                else y += y < b.y ? 1 : -1;
            }
        }

        static void Bsp(DungeonMap m, Rng rng)
        {
            BspNode(m, rng, new RectInt(1, 1, m.w - 2, m.h - 2), 5);
        }

        static Vector2Int BspNode(DungeonMap m, Rng rng, RectInt area, int depthLeft)
        {
            bool canSplitH = area.height >= 14, canSplitW = area.width >= 16;
            if (depthLeft <= 0 || (!canSplitH && !canSplitW))
            {
                int rw = rng.Range(Mathf.Min(4, area.width - 2), Mathf.Max(5, area.width - 1));
                int rh = rng.Range(Mathf.Min(4, area.height - 2), Mathf.Max(5, area.height - 1));
                rw = Mathf.Clamp(rw, 3, area.width - 2); rh = Mathf.Clamp(rh, 3, area.height - 2);
                int rx = area.x + rng.Range(1, Mathf.Max(2, area.width - rw));
                int ry = area.y + rng.Range(1, Mathf.Max(2, area.height - rh));
                var room = new RectInt(rx, ry, rw, rh);
                Carve(m, room);
                m.rooms.Add(room);
                return new Vector2Int(rx + rw / 2, ry + rh / 2);
            }
            bool splitVertical = canSplitW && (!canSplitH || area.width > area.height || rng.Chance(0.5f));
            Vector2Int a, b;
            if (splitVertical)
            {
                int cut = rng.Range(area.width * 2 / 5, area.width * 3 / 5);
                a = BspNode(m, rng, new RectInt(area.x, area.y, cut, area.height), depthLeft - 1);
                b = BspNode(m, rng, new RectInt(area.x + cut, area.y, area.width - cut, area.height), depthLeft - 1);
            }
            else
            {
                int cut = rng.Range(area.height * 2 / 5, area.height * 3 / 5);
                a = BspNode(m, rng, new RectInt(area.x, area.y, area.width, cut), depthLeft - 1);
                b = BspNode(m, rng, new RectInt(area.x, area.y + cut, area.width, area.height - cut), depthLeft - 1);
            }
            Corridor(m, rng, a, b);
            return rng.Chance(0.5f) ? a : b;
        }

        static void Caves(DungeonMap m, Rng rng)
        {
            var g = new bool[m.w, m.h];
            for (int x = 0; x < m.w; x++)
                for (int y = 0; y < m.h; y++)
                    g[x, y] = x == 0 || y == 0 || x == m.w - 1 || y == m.h - 1 || rng.Chance(0.45f);
            for (int it = 0; it < 5; it++)
            {
                var n = new bool[m.w, m.h];
                for (int x = 0; x < m.w; x++)
                    for (int y = 0; y < m.h; y++)
                    {
                        if (x == 0 || y == 0 || x == m.w - 1 || y == m.h - 1) { n[x, y] = true; continue; }
                        int walls = 0, far = 0;
                        for (int dx = -2; dx <= 2; dx++)
                            for (int dy = -2; dy <= 2; dy++)
                            {
                                int nx = x + dx, ny = y + dy;
                                bool wall = nx < 0 || ny < 0 || nx >= m.w || ny >= m.h || g[nx, ny];
                                if (!wall) continue;
                                if (Mathf.Abs(dx) <= 1 && Mathf.Abs(dy) <= 1) walls++;
                                far++;
                            }
                        n[x, y] = walls >= 5 || (it < 3 && far <= 2);
                    }
                g = n;
            }
            for (int x = 0; x < m.w; x++)
                for (int y = 0; y < m.h; y++)
                    m.tiles[x, y] = g[x, y] ? Tile.Wall : Tile.Floor;
        }

        static void Ruins(DungeonMap m, Rng rng)
        {
            var centers = new List<Vector2Int>();
            for (int i = 0; i < 120 && m.rooms.Count < 22; i++)
            {
                int rw = rng.Range(4, 11), rh = rng.Range(4, 9);
                var r = new RectInt(rng.Range(1, m.w - rw - 1), rng.Range(1, m.h - rh - 1), rw, rh);
                bool overlap = false;
                foreach (var o in m.rooms)
                    if (new RectInt(o.x - 1, o.y - 1, o.width + 2, o.height + 2).Overlaps(r)) { overlap = true; break; }
                if (overlap) continue;
                Carve(m, r);
                var c = new Vector2Int(r.x + rw / 2, r.y + rh / 2);
                if (centers.Count > 0)
                {
                    var nearest = centers[0];
                    foreach (var o in centers) if ((o - c).sqrMagnitude < (nearest - c).sqrMagnitude) nearest = o;
                    Corridor(m, rng, c, nearest);
                    if (rng.Chance(0.25f)) Corridor(m, rng, c, rng.Pick(centers));
                }
                centers.Add(c);
                m.rooms.Add(r);
                // Crumbling pillars
                if (rw >= 7 && rh >= 6 && rng.Chance(0.6f))
                {
                    for (int px = r.x + 2; px < r.xMax - 2; px += 3)
                        for (int py = r.y + 2; py < r.yMax - 2; py += 3)
                            m.tiles[px, py] = rng.Chance(0.3f) ? Tile.Rubble : Tile.Wall;
                }
            }
        }

        static void Hybrid(DungeonMap m, Rng rng)
        {
            Caves(m, rng);
            var floorCells = new List<Vector2Int>();
            for (int x = 1; x < m.w - 1; x++) for (int y = 1; y < m.h - 1; y++) if (m.tiles[x, y] == Tile.Floor) floorCells.Add(new Vector2Int(x, y));
            int rooms = rng.Range(4, 8);
            for (int i = 0; i < rooms; i++)
            {
                int rw = rng.Range(5, 10), rh = rng.Range(4, 8);
                var r = new RectInt(rng.Range(2, m.w - rw - 2), rng.Range(2, m.h - rh - 2), rw, rh);
                Carve(m, r);
                m.rooms.Add(r);
                if (floorCells.Count > 0) Corridor(m, rng, new Vector2Int(r.x + rw / 2, r.y + rh / 2), rng.Pick(floorCells));
            }
        }

        // ------------------------------------------------------------------ post processing

        static int CountFloor(DungeonMap m)
        {
            int n = 0;
            for (int x = 0; x < m.w; x++) for (int y = 0; y < m.h; y++) if (m.Walkable(x, y)) n++;
            return n;
        }

        static void KeepLargestRegion(DungeonMap m)
        {
            var region = new int[m.w, m.h];
            int best = -1, bestSize = 0, id = 0;
            var q = new Queue<Vector2Int>();
            for (int x = 0; x < m.w; x++)
                for (int y = 0; y < m.h; y++)
                {
                    if (!m.Walkable(x, y) || region[x, y] != 0) continue;
                    id++;
                    int size = 0;
                    region[x, y] = id;
                    q.Enqueue(new Vector2Int(x, y));
                    while (q.Count > 0)
                    {
                        var c = q.Dequeue();
                        size++;
                        for (int dx = -1; dx <= 1; dx++)
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                int nx = c.x + dx, ny = c.y + dy;
                                if (!m.InBounds(nx, ny) || region[nx, ny] != 0 || !m.CanStep(c.x, c.y, dx, dy)) continue;
                                region[nx, ny] = id;
                                q.Enqueue(new Vector2Int(nx, ny));
                            }
                    }
                    if (size > bestSize) { bestSize = size; best = id; }
                }
            for (int x = 0; x < m.w; x++)
                for (int y = 0; y < m.h; y++)
                    if (m.Walkable(x, y) && region[x, y] != best) m.tiles[x, y] = Tile.Wall;
        }

        static List<Vector2Int> FloorCells(DungeonMap m)
        {
            var l = new List<Vector2Int>();
            for (int x = 1; x < m.w - 1; x++) for (int y = 1; y < m.h - 1; y++) if (m.tiles[x, y] == Tile.Floor) l.Add(new Vector2Int(x, y));
            return l;
        }

        static void ChooseStartAndStairs(DungeonMap m, Rng rng)
        {
            var cells = FloorCells(m);
            m.start = m.rooms.Count > 0 && rng.Chance(0.7f)
                ? Center(m, m.rooms[rng.Range(0, m.rooms.Count)])
                : rng.Pick(cells);
            if (m.tiles[m.start.x, m.start.y] != Tile.Floor) m.start = rng.Pick(cells);
            var dist = m.DistanceField(new[] { m.start }, false);
            Vector2Int far = m.start; int best = 0;
            foreach (var c in cells)
            {
                int d = dist[c.x, c.y];
                if (d == int.MaxValue) continue;
                // Pick among the farthest cells with a little randomness.
                int score = d * 10 + rng.Range(0, 30);
                if (score > best) { best = score; far = c; }
            }
            m.stairs = far;
            m.tiles[far.x, far.y] = Tile.StairsDown;
        }

        static Vector2Int Center(DungeonMap m, RectInt r) => new Vector2Int(r.x + r.width / 2, r.y + r.height / 2);

        static void AddLiquid(DungeonMap m, Rng rng)
        {
            if (m.biome.liquid == Tile.Floor || m.biome.liquidAmount <= 0) return;
            var cells = FloorCells(m);
            int target = Mathf.RoundToInt(cells.Count * m.biome.liquidAmount);
            int placed = 0, guard = 0;
            int reachable = m.CountReachable(m.start, true);
            while (placed < target && guard++ < 40)
            {
                var c = rng.Pick(cells);
                var changed = new List<Vector2Int>();
                int steps = rng.Range(6, 22);
                for (int i = 0; i < steps; i++)
                {
                    if (m.tiles[c.x, c.y] == Tile.Floor && (c - m.start).sqrMagnitude > 9 && (c - m.stairs).sqrMagnitude > 4)
                    {
                        m.tiles[c.x, c.y] = m.biome.liquid;
                        changed.Add(c);
                    }
                    c += new Vector2Int(rng.Range(-1, 2), rng.Range(-1, 2));
                    if (!m.InBounds(c.x, c.y)) break;
                }
                if (m.biome.liquid == Tile.Lava)
                {
                    // Lava must never cut the level apart for pathing monsters or the stairs.
                    int now = m.CountReachable(m.start, true);
                    var d = m.DistanceField(new[] { m.start }, true);
                    if (now < reachable - changed.Count || d[m.stairs.x, m.stairs.y] == int.MaxValue)
                    {
                        foreach (var p in changed) m.tiles[p.x, p.y] = Tile.Floor;
                        continue;
                    }
                    reachable = now;
                }
                placed += changed.Count;
            }
        }

        static void AddFeatures(DungeonMap m, Rng rng, int depth)
        {
            var cells = FloorCells(m);
            rng.Shuffle(cells);
            int reachable = m.CountReachable(m.start, false);
            int idx = 0;

            bool TryPlace(Tile t, bool preferAlcove)
            {
                for (int tries = 0; tries < 60 && idx < cells.Count; tries++)
                {
                    var c = cells[idx++];
                    if (m.tiles[c.x, c.y] != Tile.Floor) continue;
                    if ((c - m.start).sqrMagnitude < 9 || c == m.stairs) continue;
                    if (preferAlcove && WallNeighbors(m, c) < 3 && tries < 40) continue;
                    m.tiles[c.x, c.y] = t;
                    if (!DungeonMap.IsWalkable(t))
                    {
                        // A blocking feature must not split the level, nor cut the only lava-free route to the stairs.
                        int now = m.CountReachable(m.start, false);
                        var safe = m.DistanceField(new[] { m.start }, true);
                        if (now != reachable - 1 || safe[m.stairs.x, m.stairs.y] == int.MaxValue) { m.tiles[c.x, c.y] = Tile.Floor; continue; }
                        reachable = now;
                    }
                    return true;
                }
                return false;
            }

            int chests = rng.Range(2, 5);
            for (int i = 0; i < chests; i++) TryPlace(Tile.Chest, true);
            int shrines = rng.Range(0, 3);
            for (int i = 0; i < shrines; i++) TryPlace(Tile.Shrine, false);
            if (rng.Chance(0.45f)) TryPlace(Tile.Fountain, false);
            if (depth % 3 == 0 && depth % 5 != 0) TryPlace(Tile.Merchant, false);
            int traps = rng.Range(3, 6) + depth / 5;
            for (int i = 0; i < traps; i++) TryPlace(Tile.Trap, false);
            for (int i = 0; i < cells.Count / 60; i++) TryPlace(Tile.Rubble, false);
        }

        static int WallNeighbors(DungeonMap m, Vector2Int c)
        {
            int n = 0;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if ((dx != 0 || dy != 0) && m.Get(c.x + dx, c.y + dy) == Tile.Wall) n++;
            return n;
        }
    }
}
