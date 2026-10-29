using System.Collections.Generic;
using UnityEngine;

namespace Depthbound
{
    /// <summary>Generates every sprite in the game procedurally so the project has no binary art dependencies.</summary>
    public static class SpriteFactory
    {
        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        static Sprite white;

        public static Texture2D NewTex(int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var clear = new Color32[w * h];
            t.SetPixels32(clear);
            return t;
        }

        public static Sprite ToSprite(Texture2D t)
        {
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), t.width);
        }

        public static Sprite White
        {
            get
            {
                if (white != null) return white;
                var t = NewTex(4, 4);
                for (int x = 0; x < 4; x++) for (int y = 0; y < 4; y++) t.SetPixel(x, y, Color.white);
                white = ToSprite(t);
                return white;
            }
        }

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        static Color Shade(Color c, float f) => new Color(c.r * f, c.g * f, c.b * f, c.a);

        // ------------------------------------------------------------------ tiles

        public static Sprite TileSprite(int biomeIndex, Tile tile, int variant)
        {
            string key = $"t{biomeIndex}_{(int)tile}_{variant}";
            if (cache.TryGetValue(key, out var s)) return s;
            var b = Biomes.All[biomeIndex];
            var t = NewTex(16, 16);
            int seed = variant * 31 + biomeIndex * 7 + 1;

            if (tile == Tile.Wall) DrawWall(t, b, seed);
            else if (tile == Tile.Water) DrawLiquid(t, new Color(0.15f, 0.3f, 0.5f), new Color(0.35f, 0.55f, 0.75f), seed);
            else if (tile == Tile.Lava) DrawLiquid(t, new Color(0.75f, 0.2f, 0.05f), new Color(1f, 0.75f, 0.2f), seed);
            else if (tile == Tile.Ice) DrawLiquid(t, new Color(0.6f, 0.75f, 0.85f), new Color(0.9f, 0.95f, 1f), seed);
            else if (tile == Tile.Carpet) DrawCarpet(t, seed);
            else DrawFloor(t, b, seed);

            switch (tile)
            {
                case Tile.StairsDown: DrawStairs(t); break;
                case Tile.Chest: Stamp(t, ChestArt, ChestColors(false)); break;
                case Tile.ChestOpen: Stamp(t, ChestOpenArt, ChestColors(true)); break;
                case Tile.Shrine: Stamp(t, ShrineArt, ShrineColors(true)); break;
                case Tile.ShrineUsed: Stamp(t, ShrineArt, ShrineColors(false)); break;
                case Tile.Fountain: Stamp(t, FountainArt, FountainColors(true)); break;
                case Tile.FountainUsed: Stamp(t, FountainArt, FountainColors(false)); break;
                case Tile.Merchant: Stamp(t, MerchantArt, MerchantColors()); break;
                case Tile.TrapSprung: DrawSpikes(t); break;
                case Tile.Rubble: DrawRubble(t, b, seed); break;
            }
            s = ToSprite(t);
            cache[key] = s;
            return s;
        }

        static void DrawFloor(Texture2D t, Biome b, int seed)
        {
            for (int x = 0; x < 16; x++)
                for (int y = 0; y < 16; y++)
                {
                    float n = Hash(x / 2, y / 2, seed) * 0.6f + Hash(x, y, seed + 9) * 0.4f;
                    var c = Color.Lerp(b.floorA, b.floorB, n);
                    if (Hash(x, y, seed + 3) > 0.93f) c = Shade(c, 1.25f);
                    if (x == 0 || y == 0) c = Shade(c, 0.85f);
                    t.SetPixel(x, y, c);
                }
        }

        static void DrawCarpet(Texture2D t, int seed)
        {
            for (int x = 0; x < 16; x++)
                for (int y = 0; y < 16; y++)
                {
                    var c = Color.Lerp(new Color(0.45f, 0.04f, 0.1f), new Color(0.36f, 0.03f, 0.08f), Hash(x, y, seed) * 0.6f);
                    if (x == 1 || x == 14) c = new Color(0.85f, 0.65f, 0.2f);
                    if ((x == 7 || x == 8) && y % 8 < 2) c = new Color(0.75f, 0.55f, 0.18f);
                    t.SetPixel(x, y, c);
                }
        }

        static void DrawWall(Texture2D t, Biome b, int seed)
        {
            for (int x = 0; x < 16; x++)
                for (int y = 0; y < 16; y++)
                {
                    int row = y / 4;
                    int off = (row % 2) * 4;
                    bool mortar = y % 4 == 0 || (x + off) % 8 == 0;
                    var c = mortar ? b.wallB : Color.Lerp(b.wallA, b.wallB, Hash((x + off) / 8, row, seed) * 0.5f);
                    if (!mortar && Hash(x, y, seed + 5) > 0.9f) c = Shade(c, 0.85f);
                    if (y >= 14) c = Shade(b.wallA, 1.2f);
                    t.SetPixel(x, y, c);
                }
        }

        static void DrawLiquid(Texture2D t, Color a, Color b, int seed)
        {
            for (int x = 0; x < 16; x++)
                for (int y = 0; y < 16; y++)
                {
                    float wave = Mathf.Sin((x + seed) * 0.8f + y * 0.4f) * 0.5f + 0.5f;
                    var c = Color.Lerp(a, b, wave * 0.35f + Hash(x, y, seed) * 0.15f);
                    t.SetPixel(x, y, c);
                }
        }

        static void DrawStairs(Texture2D t)
        {
            for (int i = 0; i < 5; i++)
            {
                float f = 0.15f + i * 0.12f;
                for (int x = 2 + i; x < 14 - i; x++)
                    for (int y = 12 - i * 2; y < 14 - i * 2; y++)
                        t.SetPixel(x, y, new Color(f, f, f * 1.1f));
            }
            for (int x = 1; x < 15; x++) { t.SetPixel(x, 14, new Color(0.6f, 0.55f, 0.5f)); t.SetPixel(x, 1, new Color(0.05f, 0.05f, 0.07f)); }
        }

        static void DrawSpikes(Texture2D t)
        {
            var c = new Color(0.65f, 0.65f, 0.7f);
            for (int i = 0; i < 4; i++)
            {
                int bx = 3 + (i % 2) * 7, by = 3 + (i / 2) * 7;
                t.SetPixel(bx, by, c); t.SetPixel(bx + 1, by, c); t.SetPixel(bx, by + 1, Shade(c, 1.3f));
            }
        }

        static void DrawRubble(Texture2D t, Biome b, int seed)
        {
            for (int i = 0; i < 5; i++)
            {
                int cx = 3 + (int)(Hash(i, 1, seed) * 10), cy = 3 + (int)(Hash(i, 2, seed) * 10);
                var c = Shade(b.wallA, 0.8f + Hash(i, 3, seed) * 0.4f);
                t.SetPixel(cx, cy, c); t.SetPixel(cx + 1, cy, c); t.SetPixel(cx, cy + 1, Shade(c, 1.2f));
            }
        }

        // ------------------------------------------------------------------ ascii art stamping

        /// <summary>Draws ascii art (top row first) onto a texture. '.' is transparent.</summary>
        public static void Stamp(Texture2D t, string[] art, Dictionary<char, Color> colors, int ox = 0, int oy = 0)
        {
            int h = art.Length;
            for (int row = 0; row < h; row++)
            {
                string line = art[row];
                for (int col = 0; col < line.Length; col++)
                {
                    char ch = line[col];
                    if (ch == '.' || !colors.TryGetValue(ch, out var c)) continue;
                    int x = ox + col, y = oy + (h - 1 - row);
                    if (x >= 0 && y >= 0 && x < t.width && y < t.height) t.SetPixel(x, y, c);
                }
            }
        }

        static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }

        static readonly string[] ChestArt =
        {
            "................", "................", "................", "................",
            "...oooooooooo...", "..o##########o..", "..o#++####++#o..", "..o##########o..",
            "..oooo+yy+oooo..", "..o####yy####o..", "..o##########o..", "..o#++####++#o..",
            "..o##########o..", "..oooooooooooo..", "................", "................",
        };
        static readonly string[] ChestOpenArt =
        {
            "................", "................", "...oooooooooo...", "..o##########o..",
            "..o##########o..", "..oooooooooooo..", "..o..........o..", "..o..........o..",
            "..oooo+yy+oooo..", "..o####yy####o..", "..o##########o..", "..o#++####++#o..",
            "..o##########o..", "..oooooooooooo..", "................", "................",
        };
        static Dictionary<char, Color> ChestColors(bool open) => new Dictionary<char, Color>
        {
            { 'o', Hex("1a1008") }, { '#', open ? Hex("5a3a1a") : Hex("7a4a20") }, { '+', Hex("c09030") }, { 'y', Hex("ffd040") },
        };

        static readonly string[] ShrineArt =
        {
            "................", "......oooo......", ".....o*gg*o.....", ".....o*gg*o.....",
            "......oooo......", "......o##o......", "......o##o......", "......o##o......",
            "......o##o......", "......o##o......", ".....o####o.....", "....o######o....",
            "...oooooooooo...", "................", "................", "................",
        };
        static Dictionary<char, Color> ShrineColors(bool active) => new Dictionary<char, Color>
        {
            { 'o', Hex("101018") }, { '#', Hex("8a8a9a") }, { 'g', active ? Hex("60e0ff") : Hex("304050") }, { '*', active ? Hex("c0ffff") : Hex("405060") },
        };

        static readonly string[] FountainArt =
        {
            "................", "................", ".......oo.......", "......o~~o......",
            ".......oo.......", ".......##.......", ".......##.......", "..oooooooooooo..",
            ".o############o.", ".o#~~~~~~~~~~#o.", ".o#~~~~~~~~~~#o.", ".o############o.",
            "..oooooooooooo..", "................", "................", "................",
        };
        static Dictionary<char, Color> FountainColors(bool full) => new Dictionary<char, Color>
        {
            { 'o', Hex("101018") }, { '#', Hex("9a9aa8") }, { '~', full ? Hex("40a0ff") : Hex("3a3a40") },
        };

        static readonly string[] MerchantArt =
        {
            "................", "......oooo......", ".....ohhhho.....", "....ohhhhhho....",
            "....ohseseho....", "....ohsssshho...", "...ohhhhhhhhho..", "...ohhhyyhhhho..",
            "..ohhhhyyhhhhho.", "..ohhhhhhhhhhho.", "..ohhhhhhhhhhho.", "..oooooooooooo..",
            "..b..........b..", "................", "................", "................",
        };
        static Dictionary<char, Color> MerchantColors() => new Dictionary<char, Color>
        {
            { 'o', Hex("100810") }, { 'h', Hex("5a2a7a") }, { 's', Hex("e0b090") }, { 'e', Hex("ffd040") }, { 'y', Hex("ffd040") }, { 'b', Hex("5a3a1a") },
        };

        // ------------------------------------------------------------------ hero

        static readonly string[] HeroArt =
        {
            "................", "......oooo......", ".....ohhhho.....", ".....ohssso.....",
            ".....oseseo.....", ".....osssso.....", "....occcccco....", "...occdccdcco..w",
            "..oscccdcccso.w.", "..oscccccccsow..", "...ooddddddoo...", "....occcccco....",
            "....occoocco....", "....occ..cco....", "....obb..bbo....", "....ooo..ooo....",
        };

        public static Sprite Hero(ClassDef cls)
        {
            string key = "hero_" + cls.id;
            if (cache.TryGetValue(key, out var s)) return s;
            var t = NewTex(16, 16);
            var colors = new Dictionary<char, Color>
            {
                { 'o', new Color(0.07f, 0.05f, 0.05f) }, { 'h', Shade(cls.color, 0.55f) }, { 's', new Color(0.95f, 0.78f, 0.62f) },
                { 'e', new Color(0.1f, 0.1f, 0.15f) }, { 'c', cls.color }, { 'd', Shade(cls.color, 0.6f) },
                { 'b', new Color(0.35f, 0.22f, 0.12f) }, { 'w', new Color(0.85f, 0.85f, 0.92f) },
            };
            Stamp(t, HeroArt, colors);
            s = ToSprite(t);
            cache[key] = s;
            return s;
        }

        // ------------------------------------------------------------------ creatures

        static readonly Dictionary<CreatureShape, string[]> templates = new Dictionary<CreatureShape, string[]>
        {
            { CreatureShape.Humanoid, new[] { "00000000", "00000000", "00000099", "00000999", "00000999", "00000099", "00009999", "00099999", "00909999", "00909999", "00809999", "00008888", "00000990", "00000990", "00000990", "00009990" } },
            { CreatureShape.Beast, new[] { "00000000", "00000000", "00000000", "00006000", "00006999", "00000999", "00000799", "00999999", "09999999", "09999999", "09999999", "00999999", "00900090", "00900090", "00900090", "00000000" } },
            { CreatureShape.Blob, new[] { "00000000", "00000000", "00000000", "00000000", "00000777", "00079999", "00799999", "07999999", "07999999", "79999999", "99999999", "99999999", "99999999", "79999999", "07777777", "00000000" } },
            { CreatureShape.Flyer, new[] { "00000000", "00000000", "00000000", "00000099", "70000999", "97000999", "99700999", "99979999", "79999999", "07999999", "00799999", "00070999", "00000099", "00000060", "00000000", "00000000" } },
            { CreatureShape.Insect, new[] { "00000000", "00000000", "00000000", "00000909", "00000099", "00000999", "09000999", "00909999", "09099999", "00999999", "09099999", "00909999", "09000999", "00000099", "00000000", "00000000" } },
            { CreatureShape.Golem, new[] { "00000000", "00000000", "00000999", "00000999", "00000999", "09999999", "99999999", "99099999", "99099999", "99099999", "99999999", "00999999", "00999900", "00999900", "00999900", "09999900" } },
        };

        public static Sprite Creature(MonsterDef def)
        {
            string key = "mon_" + def.id;
            if (cache.TryGetValue(key, out var s)) return s;
            int seed = def.id.GetHashCode();
            var rng = new Rng(seed);
            var tmpl = templates[def.shape];
            var filled = new bool[16, 16];
            for (int row = 0; row < 16; row++)
                for (int col = 0; col < 8; col++)
                {
                    int dens = tmpl[row][col] - '0';
                    bool on = dens >= 9 ? rng.Chance(0.92f) : rng.Chance(dens / 9f * 0.75f);
                    if (dens == 0) on = false;
                    int y = 15 - row;
                    filled[col, y] = on;
                    filled[15 - col, y] = on;
                }
            var t = NewTex(16, 16);
            var outline = Shade(def.c1, 0.22f); outline.a = 1;
            for (int x = 0; x < 16; x++)
                for (int y = 0; y < 16; y++)
                {
                    if (filled[x, y])
                    {
                        int mx = x < 8 ? x : 15 - x;
                        bool accent = Hash(mx, y / 2, seed) > 0.7f;
                        var c = accent ? def.c2 : def.c1;
                        c = Shade(c, 0.8f + 0.35f * (y / 15f));
                        t.SetPixel(x, y, c);
                    }
                    else if (Neighbor(filled, x, y)) t.SetPixel(x, y, outline);
                }
            // Eyes: first row from the top with a solid center.
            for (int y = 14; y >= 6; y--)
            {
                if (filled[5, y] && filled[6, y] && filled[9, y] && filled[10, y])
                {
                    var eye = def.boss ? new Color(1f, 0.2f, 0.1f) : new Color(1f, 0.9f, 0.3f);
                    t.SetPixel(6, y, eye); t.SetPixel(9, y, eye);
                    break;
                }
            }
            s = ToSprite(t);
            cache[key] = s;
            return s;
        }

        static bool Neighbor(bool[,] f, int x, int y)
        {
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (Mathf.Abs(dx) + Mathf.Abs(dy) != 1) continue;
                    int nx = x + dx, ny = y + dy;
                    if (nx >= 0 && ny >= 0 && nx < 16 && ny < 16 && f[nx, ny]) return true;
                }
            return false;
        }

        // ------------------------------------------------------------------ item icons

        static readonly Dictionary<string, string[]> icons = new Dictionary<string, string[]>
        {
            { "sword", new[] { "..........oo", ".........o#o", "........o#o.", ".......o#o..", "......o#o...", ".....o#o....", "..o.o#o.....", "...o#o......", "..o+oo......", ".ohh........", "oh..........", "o..........." } },
            { "axe", new[] { "....ooo.o...", "...o###oho..", "..o####oho..", "..o####oho..", "...o###oho..", "....ooo.ho..", "........ho..", "........ho..", "........ho..", "........ho..", "........ho..", "........oo.." } },
            { "mace", new[] { "...o.o.o....", "..o#####o...", ".o#+#####o..", ".o#######o..", "..o#####o...", "...o.h.o....", ".....h......", ".....h......", ".....h......", ".....h......", "....ohh.....", "....oo......" } },
            { "dagger", new[] { "............", "........oo..", ".......o#o..", "......o#o...", ".....o#o....", "....o#o.....", "..oo+o......", "...oh.......", "..oh........", "............", "............", "............" } },
            { "spear", new[] { ".........oo.", "........o#+o", "........o##o", ".......hoo..", "......h.....", ".....h......", "....h.......", "...h........", "..h.........", ".h..........", "h...........", "............" } },
            { "staff", new[] { "....o+o.....", "...o+#+o....", "....o+o.....", ".....h......", ".....h......", ".....h......", ".....h......", ".....h......", ".....h......", ".....h......", ".....h......", ".....o......" } },
            { "wand", new[] { "..........+.", ".........+#+", "..........+.", "........h...", ".......h....", "......h.....", ".....h......", "....h.......", "...h........", "............", "............", "............" } },
            { "bow", new[] { "...oo.......", "...+#o......", "...+.#o.....", "...+..#o....", "...+...#o...", "...+....#...", "...+....#...", "...+...#o...", "...+..#o....", "...+.#o.....", "...+#o......", "...oo......." } },
            { "shield", new[] { ".oooooooooo.", ".o########o.", ".o##+##+##o.", ".o########o.", ".o###++###o.", ".o##++++##o.", ".o###++###o.", "..o######o..", "..o######o..", "...o####o...", "....o##o....", ".....oo....." } },
            { "orb", new[] { "............", "....oooo....", "...o#+##o...", "..o#++###o..", "..o#+####o..", "..o######o..", "...o####o...", "....oooo....", "....hhhh....", "...hhhhhh...", "............", "............" } },
            { "quiver", new[] { "...+..+.....", "...h..h..+..", "...h..h..h..", "..oooooooo..", "..o######o..", "..o#++++#o..", "..o######o..", "..o######o..", "..o#++++#o..", "..o######o..", "..o######o..", "..oooooooo.." } },
            { "tome", new[] { "............", ".oooooooooo.", ".o########o.", ".o#+####+#o.", ".o##++++##o.", ".o##+##+##o.", ".o##++++##o.", ".o#+####+#o.", ".o########o.", ".oooooooooo.", ".hhhhhhhhhh.", "............" } },
            { "helm", new[] { "............", "...oooooo...", "..o######o..", ".o##+#####o.", ".o########o.", ".o##oooo##o.", ".o#o....o#o.", ".o#o....o#o.", ".oo......oo.", "............", "............", "............" } },
            { "crown", new[] { "............", "............", ".+..+..+..+.", ".#o.#o.#o.#.", ".##.##.##.##", ".##########.", ".#+##+##+##.", ".##########.", ".oooooooooo.", "............", "............", "............" } },
            { "chest", new[] { "............", "..oo....oo..", ".o##o..o##o.", ".o###oo###o.", ".o########o.", "..o##++##o..", "..o######o..", "..o######o..", "..o######o..", "..o######o..", "..oooooooo..", "............" } },
            { "robe", new[] { "............", "..oo....oo..", ".o##o..o##o.", ".o###oo###o.", ".o########o.", "..o##++##o..", "..o######o..", "..o######o..", ".o########o.", "o##########o", "oooooooooooo", "............" } },
            { "gloves", new[] { "............", "...o.o.o....", "..o#o#o#o...", "..o#o#o#o...", "..o#####o.o.", "..o######o#o", "..o#######o.", "..o######o..", "...o####o...", "...o++++o...", "...oooooo...", "............" } },
            { "boots", new[] { "............", "...oooo.....", "...o##o.....", "...o##o.....", "...o##o.....", "...o##o.....", "...o##ooo...", "...o#####o..", "...o######o.", "...oooooooo.", "............", "............" } },
            { "belt", new[] { "............", "............", "............", "............", "oooooooooooo", "o####++####o", "o####+.+###o", "o####++####o", "oooooooooooo", "............", "............", "............" } },
            { "amulet", new[] { "..o......o..", "...o....o...", "....o..o....", ".....oo.....", "....o##o....", "...o#++#o...", "...o#+##o...", "....o##o....", ".....oo.....", "............", "............", "............" } },
            { "ring", new[] { "............", "............", ".....++.....", "....o##o....", "...o#oo#o...", "..o#o..o#o..", "..o#o..o#o..", "...o#oo#o...", "....o##o....", ".....oo.....", "............", "............" } },
            { "potion", new[] { "............", ".....oo.....", ".....hh.....", "....o..o....", "...o####o...", "..o#+####o..", "..o######o..", "..o######o..", "...o####o...", "....oooo....", "............", "............" } },
            { "gold", new[] { "............", "............", "............", "....oooo....", "...o####o...", "..o##++##o..", "..o#+##+#o..", "..o##++##o..", "...o####o...", "....oooo....", "............", "............" } },
        };

        static Color MaterialFor(string icon)
        {
            switch (icon)
            {
                case "chest": case "robe": case "gloves": case "boots": case "belt": case "quiver": return Hex("8a6040");
                case "tome": return Hex("6a3040");
                case "orb": return Hex("80b0ff");
                case "amulet": case "ring": case "crown": case "gold": return Hex("e0b040");
                case "staff": case "wand": case "bow": return Hex("a07040");
                default: return Hex("b0b4c0");
            }
        }

        public static Sprite Icon(string icon, Color accent, Color? material = null)
        {
            string key = $"icon_{icon}_{ColorUtility.ToHtmlStringRGB(accent)}_{(material.HasValue ? ColorUtility.ToHtmlStringRGB(material.Value) : "")}";
            if (cache.TryGetValue(key, out var s)) return s;
            if (!icons.TryGetValue(icon, out var art)) art = icons["ring"];
            var t = NewTex(12, 12);
            var mat = material ?? MaterialFor(icon);
            Stamp(t, art, new Dictionary<char, Color>
            {
                { 'o', new Color(0.08f, 0.07f, 0.06f) }, { '#', mat }, { '+', accent }, { 'h', Hex("6a4020") },
            });
            s = ToSprite(t);
            cache[key] = s;
            return s;
        }

        public static Sprite ItemIcon(Item it) => Icon(it.baseDef.icon, it.Color);

        // ------------------------------------------------------------------ skill icons

        public static Sprite SkillIcon(SkillDef s)
        {
            string key = "skill_" + s.id;
            if (cache.TryGetValue(key, out var spr)) return spr;
            var t = NewTex(16, 16);
            var bg = Shade(s.color, 0.3f); bg.a = 1;
            var fg = s.color;
            var hi = Color.Lerp(s.color, Color.white, 0.5f);
            for (int x = 0; x < 16; x++)
                for (int y = 0; y < 16; y++)
                {
                    bool border = x == 0 || y == 0 || x == 15 || y == 15;
                    t.SetPixel(x, y, border ? Shade(s.color, 0.6f) : Color.Lerp(bg, Shade(bg, 1.4f), y / 15f));
                }
            void P(int x, int y, Color c) { if (x > 0 && y > 0 && x < 15 && y < 15) t.SetPixel(x, y, c); }
            void Circle(int cx, int cy, float r, bool fill, Color c)
            {
                for (int x = 1; x < 15; x++)
                    for (int y = 1; y < 15; y++)
                    {
                        float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                        if (fill ? d <= r : Mathf.Abs(d - r) < 0.6f) P(x, y, c);
                    }
            }

            if (s.passive)
            {
                for (int i = 0; i < 6; i++) { P(7 - i, 7 + (5 - i), fg); P(8 + i, 7 + (5 - i), fg); P(7 - i, 8 - (5 - i), fg); P(8 + i, 8 - (5 - i), fg); }
                Circle(7, 7, 1.5f, true, hi);
                if (s.keystone != Keystone.None) Circle(7, 7, 6f, false, new Color(1f, 0.6f, 0.2f));
            }
            else switch (s.targeting)
            {
                case Targeting.Melee:
                    for (int i = 2; i < 14; i++) { P(i, i, hi); P(i + 1, i, fg); P(i, i + 1, fg); }
                    if (s.radius > 0) for (int i = 2; i < 14; i++) P(i, 15 - i, fg);
                    break;
                case Targeting.Projectile:
                    Circle(10, 10, 2.5f, true, hi);
                    for (int i = 2; i < 8; i++) { P(i, i, fg); P(i + 1, i, Shade(fg, 0.7f)); }
                    if (s.projectiles > 1) { Circle(5, 11, 1.5f, true, fg); Circle(11, 5, 1.5f, true, fg); }
                    break;
                case Targeting.Ground:
                    Circle(7, 7, 5f, true, Shade(fg, 0.6f));
                    Circle(7, 7, 3f, true, fg);
                    Circle(7, 7, 1.2f, true, hi);
                    break;
                case Targeting.Self:
                    if (s.radius > 0) { Circle(7, 7, 5.5f, false, fg); Circle(7, 7, 3f, false, hi); Circle(7, 7, 1f, true, hi); }
                    else if (s.healPct > 0 || s.shieldPct > 0) { for (int i = 3; i < 13; i++) { P(i, 7, hi); P(i, 8, hi); P(7, i, hi); P(8, i, hi); } }
                    else { for (int i = 0; i < 6; i++) for (int j = -i; j <= i; j++) P(7 + j, 12 - i, fg); for (int y = 2; y < 7; y++) { P(6, y, hi); P(7, y, hi); P(8, y, hi); } }
                    break;
                case Targeting.Chain:
                    { int x = 2, y = 13; bool r = true; while (y > 2) { P(x, y, hi); P(x + 1, y, fg); x += r ? 2 : -2; if (x > 12 || x < 3) r = !r; y--; } }
                    break;
                case Targeting.Dash:
                    for (int x = 2; x < 11; x++) { P(x, 7, fg); P(x, 8, fg); }
                    for (int i = 0; i < 5; i++) for (int j = -i; j <= i; j++) P(13 - i, 7 + j, hi);
                    break;
                case Targeting.Blink:
                    Circle(7, 7, 5f, false, fg); Circle(5, 9, 1.5f, true, hi); Circle(10, 5, 1.5f, true, hi);
                    break;
                case Targeting.Summon:
                    Circle(7, 9, 4f, true, hi);
                    P(5, 9, bg); P(6, 9, bg); P(9, 9, bg); P(10, 9, bg);
                    for (int x = 5; x <= 10; x++) P(x, 4, fg);
                    for (int x = 5; x <= 10; x += 2) P(x, 3, fg);
                    break;
                default:
                    Circle(7, 7, 5f, false, fg);
                    for (int i = 2; i < 14; i++) { P(i, 7, fg); P(7, i, fg); }
                    Circle(7, 7, 1.2f, true, hi);
                    break;
            }
            spr = ToSprite(t);
            cache[key] = spr;
            return spr;
        }
    }
}
