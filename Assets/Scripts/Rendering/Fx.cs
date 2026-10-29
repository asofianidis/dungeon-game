using System.Collections.Generic;
using UnityEngine;

namespace Depthbound
{
    public enum FxKind { Projectile, Burst, Line, Flash, Text }

    public struct FxEvent
    {
        public FxKind kind;
        public Vector2 a, b;
        public Color color;
        public string text;
        public float radius;
        public float size;
    }

    /// <summary>Game logic pushes purely cosmetic events here; WorldRenderer drains them each frame.</summary>
    public static class Fx
    {
        public static readonly List<FxEvent> Pending = new List<FxEvent>();

        public static void Projectile(Vector2 a, Vector2 b, Color c) => Add(new FxEvent { kind = FxKind.Projectile, a = a, b = b, color = c });
        public static void Burst(Vector2 c, float radius, Color col) => Add(new FxEvent { kind = FxKind.Burst, a = c, radius = radius, color = col });
        public static void Line(Vector2 a, Vector2 b, Color c) => Add(new FxEvent { kind = FxKind.Line, a = a, b = b, color = c });
        public static void Flash(Vector2 p, Color c) => Add(new FxEvent { kind = FxKind.Flash, a = p, color = c });
        public static void Text(Vector2 p, string t, Color c, float size = 1f) => Add(new FxEvent { kind = FxKind.Text, a = p, text = t, color = c, size = size });

        static void Add(FxEvent e)
        {
            // Headless simulations never drain the queue; keep it bounded.
            if (Pending.Count > 2000) Pending.RemoveRange(0, 1000);
            Pending.Add(e);
        }
    }
}
