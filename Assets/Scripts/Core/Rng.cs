using System;
using System.Collections.Generic;

namespace Depthbound
{
    /// <summary>Deterministic xorshift RNG so every seed reproduces the same dungeon.</summary>
    public class Rng
    {
        ulong state;

        public Rng(int seed)
        {
            state = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;
            if (state == 0) state = 1;
            Next(); Next();
        }

        public ulong Next()
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            return state;
        }

        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return minInclusive + (int)(Next() % (ulong)(maxExclusive - minInclusive));
        }

        public float Float() => (Next() >> 40) / (float)(1UL << 24);
        public float Range(float min, float max) => min + (max - min) * Float();
        public bool Chance(float p) => Float() < p;
        public int Seed() => (int)(Next() & 0x7FFFFFFF);

        public T Pick<T>(IList<T> list) => list[Range(0, list.Count)];

        public T Weighted<T>(IList<T> list, Func<T, float> weight)
        {
            float total = 0;
            for (int i = 0; i < list.Count; i++) total += Math.Max(0, weight(list[i]));
            if (total <= 0) return list[Range(0, list.Count)];
            float r = Float() * total;
            for (int i = 0; i < list.Count; i++)
            {
                r -= Math.Max(0, weight(list[i]));
                if (r <= 0) return list[i];
            }
            return list[list.Count - 1];
        }

        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Range(0, i + 1);
                T t = list[i]; list[i] = list[j]; list[j] = t;
            }
        }
    }
}
