using System.Collections.Generic;
using UnityEngine;

namespace Depthbound
{
    public enum Tile : byte
    {
        Wall, Floor, StairsDown, Water, Lava, Chest, ChestOpen, Shrine, ShrineUsed,
        Fountain, FountainUsed, Merchant, Trap, TrapSprung, Rubble, Ice,
    }

    public enum GenType { Bsp, Caves, Ruins, Hybrid }

    public class Biome
    {
        public string name;
        public GenType gen;
        public Color floorA, floorB, wallA, wallB;
        public Tile liquid = Tile.Floor;
        public float liquidAmount;
        public string[] monsters;
        public string boss;
    }

    public static class Biomes
    {
        static Color C(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var c); return c; }

        public static readonly List<Biome> All = new List<Biome>
        {
            new Biome { name = "Forgotten Crypt", gen = GenType.Bsp, floorA = C("3a3632"), floorB = C("2e2b28"), wallA = C("6a645a"), wallB = C("4a453e"),
                monsters = new[] { "rat", "skeleton", "zombie", "skel_archer", "ghost", "cultist" }, boss = "bonelord" },
            new Biome { name = "Fungal Caverns", gen = GenType.Caves, floorA = C("2e3a2c"), floorB = C("26302a"), wallA = C("5a4a5e"), wallB = C("3e3242"),
                liquid = Tile.Water, liquidAmount = 0.03f, monsters = new[] { "spider", "myconid", "bat", "slime", "broodmother" }, boss = "sporequeen" },
            new Biome { name = "Sunken Ruins", gen = GenType.Ruins, floorA = C("34403e"), floorB = C("2a3534"), wallA = C("5e7068"), wallB = C("43524c"),
                liquid = Tile.Water, liquidAmount = 0.08f, monsters = new[] { "drowned", "crab", "siren", "deepone", "ruin_archer" }, boss = "leviathan" },
            new Biome { name = "Frozen Depths", gen = GenType.Caves, floorA = C("3c4652"), floorB = C("323b46"), wallA = C("8aa4c0"), wallB = C("5c7290"),
                liquid = Tile.Ice, liquidAmount = 0.06f, monsters = new[] { "wolf", "ice_elem", "yeti", "frost_wraith", "ice_archer" }, boss = "colossus" },
            new Biome { name = "Molten Forge", gen = GenType.Bsp, floorA = C("3a2a24"), floorB = C("30221e"), wallA = C("5a3a30"), wallB = C("402824"),
                liquid = Tile.Lava, liquidAmount = 0.04f, monsters = new[] { "imp", "fire_golem", "salamander", "forge_guard", "magma_slime" }, boss = "forgemaster" },
            new Biome { name = "Abyssal Halls", gen = GenType.Hybrid, floorA = C("261e30"), floorB = C("1e1826"), wallA = C("4a3a62"), wallB = C("32284a"),
                liquid = Tile.Lava, liquidAmount = 0.02f, monsters = new[] { "shade", "horror", "void_caster", "demon", "wisp" }, boss = "hollowking" },
        };

        public static int IndexForDepth(int depth) => ((depth - 1) / 5) % All.Count;
        public static Biome ForDepth(int depth) => All[IndexForDepth(depth)];
        public static int Cycle(int depth) => (depth - 1) / (5 * All.Count);
    }

    public class DungeonMap
    {
        public readonly int w, h;
        public readonly Tile[,] tiles;
        public readonly bool[,] explored, visible;
        public readonly byte[,] variant;
        public Biome biome;
        public Vector2Int start, stairs;
        public bool stairsSealed;
        public readonly List<RectInt> rooms = new List<RectInt>();

        public DungeonMap(int w, int h)
        {
            this.w = w; this.h = h;
            tiles = new Tile[w, h];
            explored = new bool[w, h];
            visible = new bool[w, h];
            variant = new byte[w, h];
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < w && y < h;
        public Tile Get(int x, int y) => InBounds(x, y) ? tiles[x, y] : Tile.Wall;
        public void Set(int x, int y, Tile t) { if (InBounds(x, y)) tiles[x, y] = t; }

        public static bool IsWalkable(Tile t)
        {
            switch (t)
            {
                case Tile.Floor: case Tile.StairsDown: case Tile.Water: case Tile.Lava: case Tile.ChestOpen:
                case Tile.ShrineUsed: case Tile.FountainUsed: case Tile.Trap: case Tile.TrapSprung: case Tile.Rubble: case Tile.Ice:
                    return true;
                default: return false;
            }
        }

        public static bool IsInteractable(Tile t) => t == Tile.Chest || t == Tile.Shrine || t == Tile.Fountain || t == Tile.Merchant;

        public bool Walkable(int x, int y) => IsWalkable(Get(x, y));
        public bool PathWalkable(int x, int y) { var t = Get(x, y); return IsWalkable(t) && t != Tile.Lava; }
        public bool Transparent(int x, int y) => Get(x, y) != Tile.Wall;

        /// <summary>Diagonal moves may not cut wall corners.</summary>
        public bool CanStep(int x, int y, int dx, int dy)
        {
            if (!Walkable(x + dx, y + dy)) return false;
            if (dx != 0 && dy != 0)
            {
                if (Get(x + dx, y) == Tile.Wall || Get(x, y + dy) == Tile.Wall) return false;
            }
            return true;
        }

        public bool HasLOS(int x0, int y0, int x1, int y1)
        {
            int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            int x = x0, y = y0;
            while (true)
            {
                if (x == x1 && y == y1) return true;
                if (!(x == x0 && y == y0) && !Transparent(x, y)) return false;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x += sx; }
                if (e2 < dx) { err += dx; y += sy; }
            }
        }

        public static List<Vector2Int> Line(Vector2Int a, Vector2Int b)
        {
            var pts = new List<Vector2Int>();
            int x0 = a.x, y0 = a.y, x1 = b.x, y1 = b.y;
            int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            while (true)
            {
                pts.Add(new Vector2Int(x0, y0));
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
            return pts;
        }

        public void ComputeFov(int ox, int oy, int radius)
        {
            System.Array.Clear(visible, 0, visible.Length);
            visible[ox, oy] = true;
            explored[ox, oy] = true;
            int r2 = radius * radius + radius;
            for (int i = -radius; i <= radius; i++)
            {
                CastRay(ox, oy, ox + i, oy - radius, r2);
                CastRay(ox, oy, ox + i, oy + radius, r2);
                CastRay(ox, oy, ox - radius, oy + i, r2);
                CastRay(ox, oy, ox + radius, oy + i, r2);
            }
        }

        void CastRay(int x0, int y0, int x1, int y1, int r2)
        {
            int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            int x = x0, y = y0;
            while (true)
            {
                if (!InBounds(x, y)) return;
                int ddx = x - x0, ddy = y - y0;
                if (ddx * ddx + ddy * ddy > r2) return;
                visible[x, y] = true;
                explored[x, y] = true;
                if (!(x == x0 && y == y0) && !Transparent(x, y)) return;
                if (x == x1 && y == y1) return;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x += sx; }
                if (e2 < dx) { err += dx; y += sy; }
            }
        }

        /// <summary>BFS distance field over walkable tiles (8-way). Unreachable = int.MaxValue.</summary>
        public int[,] DistanceField(IEnumerable<Vector2Int> sources, bool avoidLava)
        {
            var dist = new int[w, h];
            for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) dist[x, y] = int.MaxValue;
            var q = new Queue<Vector2Int>();
            foreach (var s in sources)
            {
                if (!InBounds(s.x, s.y)) continue;
                dist[s.x, s.y] = 0;
                q.Enqueue(s);
            }
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                int d = dist[c.x, c.y] + 1;
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = c.x + dx, ny = c.y + dy;
                    if (!InBounds(nx, ny) || dist[nx, ny] <= d) continue;
                    if (!CanStep(c.x, c.y, dx, dy)) continue;
                    if (avoidLava && Get(nx, ny) == Tile.Lava) continue;
                    dist[nx, ny] = d;
                    q.Enqueue(new Vector2Int(nx, ny));
                }
            }
            return dist;
        }

        public int CountReachable(Vector2Int from, bool avoidLava)
        {
            var d = DistanceField(new[] { from }, avoidLava);
            int n = 0;
            for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) if (d[x, y] != int.MaxValue) n++;
            return n;
        }
    }
}
