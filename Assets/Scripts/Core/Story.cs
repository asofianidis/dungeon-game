using System.Collections.Generic;
using UnityEngine;

namespace Depthbound
{
    public class DialoguePage
    {
        public string speaker, text;
        public DialoguePage(string speaker, string text) { this.speaker = speaker; this.text = text; }
    }

    public class ArtifactDef
    {
        public string id, name, lore;
        public int biome;
    }

    /// <summary>The castle hub's narrative: Princess Lysandra's quest for the six Royal Artifacts of House Vaelthorn.</summary>
    public static class Story
    {
        public const string Princess = "Princess Lysandra";
        public const string House = "House Vaelthorn";
        public const string CastleName = "Castle Vaelthorn";

        public static readonly List<ArtifactDef> Artifacts = new List<ArtifactDef>
        {
            new ArtifactDef { id = "crown", biome = 0, name = "Crown of Ashen Night", lore = "Worn by every Vaelthorn queen since the First Fall." },
            new ArtifactDef { id = "mirror", biome = 1, name = "Thornveil Mirror", lore = "It shows each soul the shape of its own desire." },
            new ArtifactDef { id = "chalice", biome = 2, name = "Tidebound Chalice", lore = "Filled once, it never runs dry of the sea of souls." },
            new ArtifactDef { id = "heart", biome = 3, name = "Heart of the Frozen Throne", lore = "The cold, still-beating heart of the old Demon King." },
            new ArtifactDef { id = "scepter", biome = 4, name = "Hellforge Scepter", lore = "Forged in the first fire, it commands every flame below." },
            new ArtifactDef { id = "sigil", biome = 5, name = "Sovereign's Sigil", lore = "The seal that binds the castle's wards. Without it, the walls fall." },
        };

        /// <summary>Permanent per-artifact bonus, applied at the start of every run.</summary>
        public static IEnumerable<StatMod> ArtifactBonus()
        {
            yield return new StatMod(StatType.AttackDamage, 8);
            yield return new StatMod(StatType.SpellDamage, 8);
            yield return new StatMod(StatType.MaxLifePct, 5);
        }

        public const string BonusText = "+8% Attack and Spell Damage and +5% Maximum Life, permanently";

        public static ArtifactDef ForBiome(int biome) => Artifacts.Find(a => a.biome == biome);
        public static ArtifactDef Get(string id) => Artifacts.Find(a => a.id == id);

        static DialoguePage P(string text) => new DialoguePage(Princess, text);

        public static List<DialoguePage> PrincessDialogue(MetaProgress meta)
        {
            var pages = new List<DialoguePage>();
            int found = meta.artifacts.Count;

            if (meta.questStage == 0)
            {
                pages.Add(P("So. You answered my summons after all, Archdemon. Rise. Kneeling does not suit one of your rank."));
                pages.Add(P($"I am Lysandra of {House}, Princess of the Ninth Circle and heir to the Crimson Throne. Three nights ago, the Radiant Crusade breached our lower vaults."));
                pages.Add(P("Their paladins could not destroy the six Royal Artifacts of my family, so they scattered them into the Abyss beneath this castle and bound its most terrible guardians to keep them."));
                pages.Add(P("Without the artifacts, the wards on these walls weaken with every dawn. When they fail, the human army camped at our border will march through these halls and put everything we are to the torch."));
                pages.Add(P("You are the mightiest demon left in my service. Pass through the Abyssal Gate. Slay the guardians that hold my family's treasures, one in every fifth depth, and bring the artifacts home to me."));
                pages.Add(P($"Each one you recover will lend its power to you as well: {BonusText}. Now go, and remind those crusaders why mortals once feared the dark."));
                meta.questStage = 1;
                meta.artifactsReported = found;
                meta.Save();
                return pages;
            }

            if (found >= Artifacts.Count && meta.questStage < 2)
            {
                pages.Add(P("All six... The Crown, the Mirror, the Chalice, the Heart, the Scepter, and the Sigil. You have done the impossible."));
                pages.Add(P("Listen. Do you hear it? The wards are singing again. At the border, the Crusade's banners are burning. Their army breaks and flees before our legions."));
                pages.Add(P($"{House} endures because of you, Archdemon. From this night, you stand at my right hand, Champion of the Crimson Throne."));
                pages.Add(P("But the Abyss is endless, and the humans always return. Descend as deep as you dare. The throne will remember every depth you conquer."));
                meta.questStage = 2;
                meta.artifactsReported = found;
                meta.Save();
                return pages;
            }

            if (meta.artifactsReported < found)
            {
                for (int i = meta.artifactsReported; i < found; i++)
                {
                    var a = Get(meta.artifacts[i]);
                    if (a != null) pages.Add(P($"The {a.name}! {a.lore} I can feel its power settling into you already."));
                }
                meta.artifactsReported = found;
                meta.Save();
            }

            if (meta.questStage >= 2)
            {
                pages.Add(P("My Champion returns. The Crusade licks its wounds beyond the border, and the Abyss still calls. Go, and come back to me victorious."));
                return pages;
            }

            pages.Add(P($"You have returned {found} of the six artifacts, Archdemon. The wards hold, for now."));
            var next = Artifacts.Find(a => !meta.artifacts.Contains(a.id));
            if (next != null)
            {
                var biome = Biomes.All[next.biome];
                pages.Add(P($"The {next.name} is held by the guardian of the {biome.name}, at depth {5 * (next.biome + 1)}. Bring it back to me."));
            }
            return pages;
        }

        public static string QuestSummary(MetaProgress meta)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b>Quest: The Royal Artifacts</b>  ({meta.artifacts.Count}/{Artifacts.Count})");
            foreach (var a in Artifacts)
            {
                bool got = meta.artifacts.Contains(a.id);
                string where = $"{Biomes.All[a.biome].name}, depth {5 * (a.biome + 1)}";
                sb.AppendLine(got ? $"<color=#ffcc55>[X] {a.name}</color>" : $"<color=#888888>[ ] {a.name}  ({where})</color>");
            }
            return sb.ToString();
        }

        public static Color PrincessColor => new Color(1f, 0.55f, 0.8f);
    }
}
