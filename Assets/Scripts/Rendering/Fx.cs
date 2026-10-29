using System.Collections.Generic;
using UnityEngine;

namespace Depthbound
{
    public enum FxKind { Projectile, Burst, Line, Flash, Text }

    public struct FxEvent
    {
        public FxKind kind;
        public Vector2Int a, b;
        public Color color;
        public string text;
        public int radius;
        public float size;
    }

    /// <summary>Game logic pushes purely cosmetic events here; FxRenderer drains them each frame.</summary>
    public static class Fx
    {
        public static readonly List<FxEvent> Pending = new List<FxEvent>();

        public static void Projectile(Vector2Int a, Vector2Int b, Color c) => Pending.Add(new FxEvent { kind = FxKind.Projectile, a = a, b = b, color = c });
        public static void Burst(Vector2Int c, int radius, Color col) => Pending.Add(new FxEvent { kind = FxKind.Burst, a = c, radius = radius, color = col });
        public static void Line(Vector2Int a, Vector2Int b, Color c) => Pending.Add(new FxEvent { kind = FxKind.Line, a = a, b = b, color = c });
        public static void Flash(Vector2Int p, Color c) => Pending.Add(new FxEvent { kind = FxKind.Flash, a = p, color = c });
        public static void Text(Vector2Int p, string t, Color c, float size = 1f) => Pending.Add(new FxEvent { kind = FxKind.Text, a = p, text = t, color = c, size = size });
    }
}
