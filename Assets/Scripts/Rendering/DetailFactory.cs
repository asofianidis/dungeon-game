using System.Collections.Generic;
using UnityEngine;

namespace Depthbound
{
    /// <summary>
    /// Procedural, tileable detail textures (mean value 0.5) that give flat-colored materials a
    /// painted surface: cloth weave, brushed metal, chain mail, leather grain, wood grain, stone.
    /// </summary>
    public static class DetailFactory
    {
        const int Size = 128;
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        /// <summary>Detail pattern and strength for a material role, or null for none.</summary>
        public static (string kind, float strength)? ForRole(string role)
        {
            switch (role)
            {
                case "Cloth": case "Lace": case "White": case "Robe": case "Stocking": return ("weave", 0.32f);
                case "Metal": return ("metal", 0.4f);
                case "Gold": return ("metal", 0.28f);
                case "Chain": return ("chain", 0.55f);
                case "Leather": case "Dark": return ("leather", 0.35f);
                case "Wing": return ("membrane", 0.3f);
                case "Wood": return ("wood", 0.5f);
                case "Stone": return ("stone", 0.45f);
                case "Horn": return ("wood", 0.25f);
                case "Primary": case "Secondary": return ("grain", 0.22f);
                default: return null;
            }
        }

        public static Texture2D Get(string kind)
        {
            if (cache.TryGetValue(kind, out var t)) return t;
            var px = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float v = Sample(kind, x / (float)Size, y / (float)Size);
                    px[y * Size + x] = new Color(v, v, v, 1);
                }
            t = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "Detail_" + kind, anisoLevel = 2 };
            t.SetPixels(px);
            t.Apply(true);
            cache[kind] = t;
            return t;
        }

        // Tileable value noise on a torus.
        static float Hash(int x, int y, int period)
        {
            x = ((x % period) + period) % period;
            y = ((y % period) + period) % period;
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }

        static float Noise(float u, float v, int period)
        {
            float x = u * period, y = v * period;
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            float a = Hash(xi, yi, period), b = Hash(xi + 1, yi, period), c = Hash(xi, yi + 1, period), d = Hash(xi + 1, yi + 1, period);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Fbm(float u, float v, int basePeriod, int octaves = 4)
        {
            float sum = 0, amp = 0.5f, norm = 0;
            int p = basePeriod;
            for (int i = 0; i < octaves; i++) { sum += Noise(u, v, p) * amp; norm += amp; amp *= 0.5f; p *= 2; }
            return sum / norm;
        }

        static float Sample(string kind, float u, float v)
        {
            float tau = Mathf.PI * 2;
            switch (kind)
            {
                case "weave":
                {
                    float warp = Mathf.Abs(Mathf.Sin(u * tau * 24));
                    float weft = Mathf.Abs(Mathf.Sin(v * tau * 24));
                    bool over = (Mathf.FloorToInt(u * 24) + Mathf.FloorToInt(v * 24)) % 2 == 0;
                    float thread = over ? warp : weft;
                    return 0.5f + (thread - 0.6f) * 0.5f + (Fbm(u, v, 8) - 0.5f) * 0.25f;
                }
                case "metal":
                {
                    float brushed = Fbm(u * 1f, v * 0.06f + u * 0.02f, 32, 3);
                    float scratch = Mathf.Pow(Noise(u + v * 3f, v * 0.2f, 64), 8f);
                    return 0.5f + (brushed - 0.5f) * 0.45f + scratch * 0.35f + (Fbm(u, v, 4) - 0.5f) * 0.2f;
                }
                case "chain":
                {
                    float cu = u * 16, cv = v * 16 + (Mathf.FloorToInt(u * 16) % 2) * 0.5f;
                    float du = cu - Mathf.Floor(cu) - 0.5f, dv = cv - Mathf.Floor(cv) - 0.5f;
                    float r = Mathf.Sqrt(du * du + dv * dv);
                    float ringv = 1f - Mathf.Clamp01(Mathf.Abs(r - 0.32f) / 0.12f);
                    return 0.28f + ringv * 0.55f;
                }
                case "leather":
                    return 0.5f + (Fbm(u, v, 16) - 0.5f) * 0.5f - Mathf.Pow(Noise(u, v, 48), 6) * 0.25f;
                case "membrane":
                {
                    float vein = Mathf.Pow(1f - Mathf.Abs(Fbm(u, v, 4) - 0.5f) * 2f, 12);
                    return 0.55f - vein * 0.35f + (Fbm(u, v, 16) - 0.5f) * 0.2f;
                }
                case "wood":
                {
                    float rings = Mathf.Sin((v * 6 + Fbm(u, v, 4) * 2.5f) * tau * 2);
                    return 0.5f + rings * 0.18f + (Fbm(u * 0.2f, v * 4f, 16) - 0.5f) * 0.3f;
                }
                case "stone":
                {
                    float n = Fbm(u, v, 6);
                    float speck = Noise(u, v, 64) > 0.85f ? 0.15f : 0f;
                    return 0.5f + (n - 0.5f) * 0.6f + speck;
                }
                default: // grain
                    return 0.5f + (Fbm(u, v, 12) - 0.5f) * 0.45f;
            }
        }
    }
}
