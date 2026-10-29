using System.Collections.Generic;
using UnityEngine;

namespace Depthbound
{
    public enum CreatureShape { Humanoid, Beast, Blob, Flyer, Insect, Golem }

    public class MonsterDef
    {
        public string id, name;
        public int biome = -1;
        public float life, damage, armor, evasion, speed = 100, xp, weight = 1, regen, scale = 1;
        public AiType ai = AiType.Melee;
        public int range = 1;
        public Element element = Element.Physical;
        public StatusType onHit;
        public float onHitChance;
        public string[] skills;
        public Color c1, c2;
        public bool boss;
        public float[] res = new float[6];
        public CreatureShape shape;

        public MonsterDef Sk(params string[] s) { skills = s; return this; }
        public MonsterDef Res(float fire, float cold, float light, float poison, float holy, float shadow) { res = new[] { fire, cold, light, poison, holy, shadow }; return this; }
        public MonsterDef Spd(float s) { speed = s; return this; }
        public MonsterDef Ranged(int r) { range = r; return this; }
        public MonsterDef Hit(StatusType s, float chance) { onHit = s; onHitChance = chance; return this; }
        public MonsterDef El(Element e) { element = e; return this; }
        public MonsterDef Boss() { boss = true; return this; }
        public MonsterDef Regen(float r) { regen = r; return this; }
        public MonsterDef Scale(float s) { scale = s; return this; }
        public MonsterDef W(float w) { weight = w; return this; }
    }

    public static class MonsterDatabase
    {
        static readonly Dictionary<string, MonsterDef> defs = new Dictionary<string, MonsterDef>();

        public static MonsterDef Get(string id) => id != null && defs.TryGetValue(id, out var d) ? d : null;
        public static IEnumerable<MonsterDef> All => defs.Values;

        static MonsterDef M(string id, string name, int biome, float life, float dmg, float armor, float eva, AiType ai, CreatureShape shape, Color c1, Color c2, float xp)
        {
            var d = new MonsterDef { id = id, name = name, biome = biome, life = life, damage = dmg, armor = armor, evasion = eva, ai = ai, shape = shape, c1 = c1, c2 = c2, xp = xp };
            defs[id] = d;
            return d;
        }

        static Color C(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var c); return c; }

        static MonsterDatabase()
        {
            // ---- 0: Forgotten Crypt ----
            M("rat", "Plague Rat", 0, 10, 2.5f, 0, 15, AiType.Melee, CreatureShape.Beast, C("8a7560"), C("c9a0a0"), 4).Spd(140).Hit(StatusType.Poison, 20).Scale(0.7f).W(1.2f);
            M("skeleton", "Skeleton", 0, 22, 4, 6, 8, AiType.Melee, CreatureShape.Humanoid, C("e8e2cc"), C("9a9480"), 7).Res(0, 20, 0, 100, -25, 25);
            M("zombie", "Rotting Corpse", 0, 40, 5, 2, 0, AiType.Melee, CreatureShape.Humanoid, C("6f8a56"), C("4a3a30"), 9).Spd(70).Hit(StatusType.Poison, 30).Res(-25, 20, 0, 75, -25, 25);
            M("skel_archer", "Skeleton Archer", 0, 16, 3.5f, 3, 12, AiType.Ranged, CreatureShape.Humanoid, C("d8d0b0"), C("6a5a40"), 8).Ranged(6).Sk("m_arrow").Res(0, 20, 0, 100, -25, 25);
            M("ghost", "Wailing Spirit", 0, 18, 4, 0, 40, AiType.Caster, CreatureShape.Flyer, C("b0c8e8"), C("6080b0"), 10).Ranged(5).El(Element.Shadow).Sk("m_shadowbolt").Res(25, 50, 25, 100, -50, 50).W(0.7f);
            M("cultist", "Crypt Cultist", 0, 20, 4, 2, 10, AiType.Summoner, CreatureShape.Humanoid, C("702030"), C("d0a060"), 12).Ranged(5).Sk("m_summon_skel", "m_firebolt").W(0.5f);
            M("bonelord", "The Bone Lord", 0, 220, 7, 15, 10, AiType.Caster, CreatureShape.Humanoid, C("f0ead0"), C("40d0a0"), 150).Ranged(5).Boss().Sk("m_summon_skel", "m_slam", "m_shadowbolt").Res(0, 30, 0, 100, -25, 40);

            // ---- 1: Fungal Caverns ----
            M("spider", "Cave Spider", 1, 18, 4, 2, 20, AiType.Melee, CreatureShape.Insect, C("3a3040"), C("a03040"), 8).Spd(120).Hit(StatusType.Poison, 40).Res(0, 0, 0, 50, 0, 0);
            M("spiderling", "Spiderling", 1, 7, 2, 0, 20, AiType.Melee, CreatureShape.Insect, C("504060"), C("c05050"), 2).Spd(130).Hit(StatusType.Poison, 30).Scale(0.6f).W(0.4f);
            M("myconid", "Myconid", 1, 26, 3, 3, 5, AiType.Caster, CreatureShape.Blob, C("c08060"), C("f0d0a0"), 9).Ranged(5).Sk("m_spit").Res(-25, 0, 0, 75, 0, 0);
            M("bat", "Giant Bat", 1, 12, 3, 0, 35, AiType.Melee, CreatureShape.Flyer, C("40303a"), C("905060"), 6).Spd(150).Scale(0.8f);
            M("slime", "Ooze", 1, 45, 4, 0, 0, AiType.Melee, CreatureShape.Blob, C("60c050"), C("b0f070"), 9).Spd(80).Hit(StatusType.Poison, 50).Res(0, 0, 0, 75, 0, 0).Regen(0.02f);
            M("broodmother", "Broodmother", 1, 35, 4, 4, 10, AiType.Summoner, CreatureShape.Insect, C("402040"), C("d04080"), 14).Ranged(4).Sk("m_summon_spider", "m_web").Scale(1.2f).W(0.5f);
            M("sporequeen", "The Spore Queen", 1, 320, 7, 10, 10, AiType.Summoner, CreatureShape.Blob, C("d070a0"), C("f0e080"), 180).Ranged(5).Boss().Sk("m_spit", "m_summon_spider", "m_web").Res(0, 0, 0, 75, 0, 0);

            // ---- 2: Sunken Ruins ----
            M("drowned", "Drowned One", 2, 32, 5, 4, 8, AiType.Melee, CreatureShape.Humanoid, C("4a7080"), C("90b0a0"), 10).Hit(StatusType.Chill, 30).Res(0, 40, -25, 25, 0, 0);
            M("crab", "Ruin Crab", 2, 30, 5, 20, 5, AiType.Melee, CreatureShape.Insect, C("c05030"), C("f0a070"), 10).Res(0, 25, -25, 0, 0, 0);
            M("siren", "Siren", 2, 22, 5, 2, 20, AiType.Caster, CreatureShape.Humanoid, C("50a0b0"), C("e0f0f0"), 12).Ranged(6).Sk("m_frostbolt", "m_curse").Res(0, 50, -25, 0, 0, 0);
            M("deepone", "Deep One", 2, 40, 6, 8, 10, AiType.Melee, CreatureShape.Beast, C("30506a"), C("80e0c0"), 13).Hit(StatusType.Bleed, 30).Res(0, 40, -25, 25, 0, 0);
            M("ruin_archer", "Ruin Sentinel", 2, 24, 4.5f, 8, 10, AiType.Ranged, CreatureShape.Golem, C("909080"), C("50c0d0"), 11).Ranged(6).Sk("m_arrow");
            M("leviathan", "Leviathan Priest", 2, 380, 8, 15, 15, AiType.Caster, CreatureShape.Humanoid, C("205070"), C("40f0d0"), 220).Ranged(6).Boss().Sk("m_frostnova", "m_frostbolt", "m_curse").Res(0, 60, -25, 25, 0, 0);

            // ---- 3: Frozen Depths ----
            M("wolf", "Frost Wolf", 3, 28, 5, 3, 25, AiType.Melee, CreatureShape.Beast, C("d0e0f0"), C("6080a0"), 10).Spd(140).Hit(StatusType.Chill, 25).Res(-25, 60, 0, 0, 0, 0);
            M("ice_elem", "Ice Elemental", 3, 34, 5, 10, 5, AiType.Caster, CreatureShape.Golem, C("a0e0ff"), C("ffffff"), 13).Ranged(6).El(Element.Cold).Sk("m_frostbolt", "m_frostnova").Res(-40, 100, 0, 100, 0, 0);
            M("yeti", "Yeti", 3, 60, 8, 6, 5, AiType.Melee, CreatureShape.Beast, C("f0f0f0"), C("6090b0"), 16).Sk("m_slam").Scale(1.3f).Res(-25, 60, 0, 0, 0, 0);
            M("frost_wraith", "Frost Wraith", 3, 26, 6, 0, 40, AiType.Melee, CreatureShape.Flyer, C("80a0d0"), C("e0f0ff"), 12).Spd(120).El(Element.Cold).Hit(StatusType.Freeze, 10).Res(0, 75, 0, 100, -40, 40);
            M("ice_archer", "Frostbound Hunter", 3, 26, 5, 5, 15, AiType.Ranged, CreatureShape.Humanoid, C("405060"), C("a0d0f0"), 12).Ranged(6).Sk("m_arrow", "m_frostbolt").Res(0, 40, 0, 0, 0, 0);
            M("colossus", "Rime Colossus", 3, 460, 10, 30, 5, AiType.Melee, CreatureShape.Golem, C("b0e0ff"), C("405080"), 260).Boss().Sk("m_frostnova", "m_slam", "m_charge").Res(-25, 100, 0, 100, 0, 0);

            // ---- 4: Molten Forge ----
            M("imp", "Imp", 4, 16, 4, 2, 30, AiType.Caster, CreatureShape.Flyer, C("d04020"), C("f0c040"), 8).Ranged(5).Spd(120).El(Element.Fire).Sk("m_firebolt").Res(75, -25, 0, 0, 0, 25).Scale(0.75f);
            M("fire_golem", "Magma Golem", 4, 70, 8, 20, 0, AiType.Melee, CreatureShape.Golem, C("402020"), C("ff7020"), 18).Spd(80).El(Element.Fire).Hit(StatusType.Burn, 40).Res(100, -25, 0, 50, 0, 0).Scale(1.3f);
            M("salamander", "Salamander", 4, 34, 6, 6, 15, AiType.Ranged, CreatureShape.Beast, C("e06020"), C("f0e050"), 13).Ranged(4).El(Element.Fire).Sk("m_breath").Res(75, -25, 0, 0, 0, 0);
            M("forge_guard", "Forge Guardian", 4, 50, 7, 25, 5, AiType.Melee, CreatureShape.Golem, C("707070"), C("f08030"), 15).Sk("m_charge").Res(40, 0, -25, 50, 0, 0);
            M("magma_slime", "Magma Slime", 4, 20, 12, 0, 0, AiType.Exploder, CreatureShape.Blob, C("ff5010"), C("ffd040"), 9).Spd(110).El(Element.Fire).Res(100, -50, 0, 0, 0, 0);
            M("forgemaster", "Forgemaster Vulkar", 4, 540, 11, 30, 10, AiType.Caster, CreatureShape.Golem, C("303030"), C("ff6010"), 300).Ranged(6).Boss().Sk("m_meteor", "m_firenova", "m_summon_imp").Res(100, -25, 0, 50, 0, 25);

            // ---- 5: Abyssal Halls ----
            M("shade", "Shade", 5, 30, 7, 0, 45, AiType.Melee, CreatureShape.Flyer, C("201830"), C("a060ff"), 14).Spd(120).El(Element.Shadow).Hit(StatusType.Weaken, 25).Res(0, 25, 0, 100, -50, 100);
            M("horror", "Abyssal Horror", 5, 90, 9, 15, 0, AiType.Melee, CreatureShape.Beast, C("402050"), C("e04080"), 22).Spd(90).Sk("m_slam").Scale(1.4f).Hit(StatusType.Bleed, 40).Res(25, 25, 25, 25, -25, 75);
            M("void_caster", "Void Speaker", 5, 34, 7, 4, 20, AiType.Caster, CreatureShape.Humanoid, C("301040"), C("c080ff"), 16).Ranged(6).El(Element.Shadow).Sk("m_shadowbolt", "m_blink", "m_curse").Res(0, 0, 0, 0, -50, 75);
            M("demon", "Pit Demon", 5, 60, 9, 12, 15, AiType.Melee, CreatureShape.Humanoid, C("901010"), C("f0a020"), 19).El(Element.Fire).Sk("m_charge", "m_firenova").Res(60, -25, 0, 25, -50, 25).Scale(1.2f);
            M("wisp", "Storm Wisp", 5, 18, 14, 0, 40, AiType.Exploder, CreatureShape.Flyer, C("f0f060"), C("ffffff"), 11).Spd(140).El(Element.Lightning).Res(0, 0, 100, 100, 0, 0).Scale(0.7f);
            M("hollowking", "The Hollow King", 5, 640, 12, 25, 20, AiType.Caster, CreatureShape.Humanoid, C("100818"), C("d0a0ff"), 360).Ranged(6).Boss().Sk("m_lightningstorm", "m_shadowbolt", "m_blink", "m_summon_skel").Res(25, 25, 25, 50, -25, 75);

            // ---- Player minions ----
            M("skeleton_minion", "Skeletal Servant", -1, 30, 5, 8, 10, AiType.Minion, CreatureShape.Humanoid, C("e0f0e8"), C("40d0a0"), 0).Res(0, 20, 0, 100, 0, 25);
            M("bone_golem", "Bone Golem", -1, 120, 9, 25, 0, AiType.Minion, CreatureShape.Golem, C("f0ead0"), C("40f0b0"), 0).Scale(1.3f).Sk("m_slam").Res(25, 25, 25, 100, 0, 25);
        }
    }
}
