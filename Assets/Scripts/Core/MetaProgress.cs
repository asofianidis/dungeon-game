using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Depthbound
{
    public enum Upgrade { Vigor, Wisdom, Alchemy, Fortune, Greed, Talent, Scholar, Armory, Resilience, COUNT }

    public static class Upgrades
    {
        public static readonly string[] Names = { "Vigor", "Wisdom", "Alchemy", "Fortune", "Greed", "Talent", "Scholar", "Armory", "Resilience" };
        public static readonly string[] Desc =
        {
            "+5% Maximum Life per rank", "+5% Experience per rank", "+1 starting Health Potion per rank",
            "+5% Magic Find per rank", "+10% Gold Find per rank", "+2 starting attribute points per rank",
            "+1 starting skill point per rank", "Start with a Magic (1) or Rare (2) weapon", "+2% All Resistances per rank",
        };
        public static readonly int[] Max = { 10, 10, 5, 10, 5, 5, 3, 2, 10 };
        static readonly int[] BaseCost = { 20, 20, 25, 30, 15, 35, 80, 100, 25 };

        public static int Cost(Upgrade u, int currentRank) => Mathf.RoundToInt(BaseCost[(int)u] * Mathf.Pow(currentRank + 1, 1.5f));
    }

    [Serializable]
    public class MetaProgress
    {
        public int essence;
        public List<string> unlockedClasses = new List<string> { "warrior", "rogue", "pyromancer" };
        public int[] upgrades = new int[(int)Upgrade.COUNT];
        public int maxCorruption;
        public int bestDepth, totalRuns, totalKills, bossKills;
        public List<string> codex = new List<string>();
        public string lastDailyPlayed = "";

        static string FilePath => Path.Combine(Application.persistentDataPath, "depthbound_meta.json");

        public int Rank(Upgrade u) => upgrades != null && (int)u < upgrades.Length ? upgrades[(int)u] : 0;

        public bool TryBuy(Upgrade u)
        {
            int r = Rank(u);
            if (r >= Upgrades.Max[(int)u]) return false;
            int cost = Upgrades.Cost(u, r);
            if (essence < cost) return false;
            essence -= cost;
            upgrades[(int)u]++;
            Save();
            return true;
        }

        public bool TryUnlock(ClassDef c)
        {
            if (unlockedClasses.Contains(c.id) || essence < c.unlockCost) return false;
            essence -= c.unlockCost;
            unlockedClasses.Add(c.id);
            Save();
            return true;
        }

        public static MetaProgress Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var m = JsonUtility.FromJson<MetaProgress>(File.ReadAllText(FilePath));
                    if (m != null)
                    {
                        if (m.upgrades == null || m.upgrades.Length != (int)Upgrade.COUNT)
                        {
                            var u = new int[(int)Upgrade.COUNT];
                            if (m.upgrades != null) Array.Copy(m.upgrades, u, Mathf.Min(u.Length, m.upgrades.Length));
                            m.upgrades = u;
                        }
                        return m;
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("Failed to load meta progress: " + e.Message); }
            return new MetaProgress();
        }

        public void Save()
        {
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(this, true)); }
            catch (Exception e) { Debug.LogWarning("Failed to save meta progress: " + e.Message); }
        }
    }
}
