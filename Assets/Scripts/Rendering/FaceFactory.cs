using System.Collections.Generic;
using UnityEngine;

namespace Depthbound
{
    /// <summary>
    /// Paints anime-style face textures for the Blender heads. The heads project their front onto
    /// UV space as u = 0.5 + x / 2.04r, v = 0.5 + z / 2.12r, so features are placed in that frame.
    /// </summary>
    public static class FaceFactory
    {
        const int Size = 256;
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        class Style
        {
            public Color skin, iris, irisDark, lash, brow, mouth;
            public bool slit, blush, fang, round, smile;
            public float browTilt, eyeScale = 1f;
        }

        static Style For(string variant)
        {
            switch (variant)
            {
                case "Princess":
                    return new Style
                    {
                        skin = new Color(0.96f, 0.82f, 0.86f), iris = new Color(1f, 0.35f, 0.7f), irisDark = new Color(0.45f, 0.05f, 0.3f),
                        lash = new Color(0.12f, 0.03f, 0.12f), brow = new Color(0.25f, 0.08f, 0.3f), mouth = new Color(0.75f, 0.25f, 0.38f),
                        blush = true, smile = true, browTilt = -0.15f, eyeScale = 1.08f,
                    };
                case "Imp":
                    return new Style
                    {
                        skin = new Color(0.7f, 0.18f, 0.14f), iris = new Color(1f, 0.85f, 0.2f), irisDark = new Color(0.6f, 0.35f, 0f),
                        lash = new Color(0.1f, 0.03f, 0.03f), brow = new Color(0.2f, 0.05f, 0.05f), mouth = new Color(0.2f, 0.03f, 0.05f),
                        round = true, smile = true, fang = true, browTilt = 0.25f, eyeScale = 1.1f,
                    };
                default: // Demon
                    return new Style
                    {
                        skin = new Color(0.62f, 0.16f, 0.16f), iris = new Color(1f, 0.78f, 0.15f), irisDark = new Color(0.75f, 0.25f, 0f),
                        lash = new Color(0.06f, 0.02f, 0.03f), brow = new Color(0.08f, 0.03f, 0.05f), mouth = new Color(0.25f, 0.04f, 0.05f),
                        slit = true, fang = true, browTilt = 0.35f, eyeScale = 0.95f,
                    };
            }
        }

        public static Texture2D Get(string variant)
        {
            if (cache.TryGetValue(variant, out var t)) return t;
            var s = For(variant);
            var px = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    px[y * Size + x] = Shade(s, (x + 0.5f) / Size, (y + 0.5f) / Size);
            t = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "Face" + variant };
            t.SetPixels(px);
            t.Apply(true);
            cache[variant] = t;
            return t;
        }

        static float Fill(float d, float aa = 0.004f) => Mathf.Clamp01(0.5f - d / aa);

        /// <summary>Signed distance-ish to an axis-aligned ellipse (negative inside).</summary>
        static float Ellipse(float u, float v, float cu, float cv, float ru, float rv)
        {
            float du = (u - cu) / ru, dv = (v - cv) / rv;
            return (Mathf.Sqrt(du * du + dv * dv) - 1f) * Mathf.Min(ru, rv);
        }

        /// <summary>Distance to an arc band y = cv + k (u - cu)^2 between u0 and u1.</summary>
        static float Arc(float u, float v, float cu, float cv, float k, float u0, float u1, float thickness)
        {
            if (u < u0 || u > u1) return 1f;
            float y = cv + k * (u - cu) * (u - cu);
            return Mathf.Abs(v - y) - thickness;
        }

        static Color Shade(Style s, float u, float v)
        {
            Color c = s.skin;
            if (s.blush)
                for (int side = -1; side <= 1; side += 2)
                    c = Color.Lerp(c, new Color(1f, 0.55f, 0.6f), 0.45f * Fill(Ellipse(u, v, 0.5f + side * 0.2f, 0.37f, 0.06f, 0.025f), 0.03f));

            for (int side = -1; side <= 1; side += 2)
            {
                float cu = 0.5f + side * 0.175f, cv = 0.47f;
                float ru = 0.072f * s.eyeScale, rv = (s.round ? 0.08f : 0.092f) * s.eyeScale;
                // Local coordinates mirrored so the outer corner is +x for both eyes.
                float lu = (u - cu) * side;

                // Sclera
                float sclera = Ellipse(u, v, cu, cv, ru, rv);
                if (sclera < 0.004f)
                {
                    var white = new Color(0.98f, 0.97f, 0.97f);
                    c = Color.Lerp(c, white, Fill(sclera));
                    // Iris: large, vertical, darker at the top.
                    float iu = cu - side * 0.006f, iv = cv - 0.008f;
                    float iris = Ellipse(u, v, iu, iv, ru * 0.72f, rv * 0.86f);
                    if (iris < 0.004f)
                    {
                        float g = Mathf.InverseLerp(iv + rv * 0.8f, iv - rv * 0.8f, v);
                        var ic = Color.Lerp(s.irisDark, s.iris, g);
                        float ring = Mathf.Clamp01((iris + 0.012f) / 0.012f);
                        ic = Color.Lerp(ic, s.irisDark * 0.6f, ring * 0.7f);
                        c = Color.Lerp(c, ic, Fill(iris) * Fill(sclera));
                        float pupil = s.slit ? Ellipse(u, v, iu, iv, ru * 0.14f, rv * 0.6f) : Ellipse(u, v, iu, iv, ru * 0.3f, rv * 0.34f);
                        c = Color.Lerp(c, new Color(0.05f, 0.02f, 0.04f), Fill(pupil) * Fill(sclera));
                        // Highlights
                        c = Color.Lerp(c, Color.white, Fill(Ellipse(u, v, iu - side * ru * 0.28f, iv + rv * 0.35f, ru * 0.2f, rv * 0.16f)));
                        c = Color.Lerp(c, Color.white, 0.85f * Fill(Ellipse(u, v, iu + side * ru * 0.25f, iv - rv * 0.38f, ru * 0.1f, rv * 0.07f)));
                    }
                }
                // Heavy upper lash line with an outer flick, thinner lower lash.
                float top = Arc(lu, v, 0f, cv + rv * 0.98f, -9f, -ru * 1.05f, ru * 1.15f, 0.011f);
                float flick = Arc(lu, v, ru * 0.9f, cv + rv * 0.55f, 18f, ru * 0.85f, ru * 1.45f, 0.008f);
                float lower = Arc(lu, v, 0f, cv - rv * 0.95f, 6f, -ru * 0.55f, ru * 0.85f, 0.003f);
                c = Color.Lerp(c, s.lash, Mathf.Max(Fill(top), Mathf.Max(Fill(flick), 0.8f * Fill(lower))));
                // Brow, tilted toward the nose for fierce characters.
                float browV = cv + rv + 0.055f + lu * s.browTilt * -0.5f;
                float brow = Arc(lu, v, 0f, browV, -3f, -ru * 0.8f, ru * 1.05f, 0.006f);
                c = Color.Lerp(c, s.brow, Fill(brow));
            }

            // Mouth
            float mv = 0.255f;
            float mouth = s.smile ? Arc(u, v, 0.5f, mv - 0.004f, 5f, 0.465f, 0.535f, 0.0035f) : Arc(u, v, 0.5f, mv, -2f, 0.47f, 0.53f, 0.003f);
            c = Color.Lerp(c, s.mouth, Fill(mouth));
            if (s.fang)
            {
                // A small fang under one side of the mouth.
                float fu = (u - 0.515f) / 0.008f, fv = (mv - 0.002f - v) / 0.02f;
                if (fv > 0 && fv < 1 && Mathf.Abs(fu) < 1 - fv) c = Color.Lerp(c, Color.white, 0.95f);
            }
            return c;
        }
    }
}
