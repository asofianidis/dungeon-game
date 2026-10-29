using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Depthbound
{
    public static class SkillDatabase
    {
        static readonly Dictionary<string, SkillDef> skills = new Dictionary<string, SkillDef>();
        static readonly Dictionary<School, List<SkillDef>> bySchool = new Dictionary<School, List<SkillDef>>();
        static bool built;

        public static SkillDef Get(string id)
        {
            Build();
            return id != null && skills.TryGetValue(id, out var s) ? s : null;
        }

        public static IReadOnlyList<SkillDef> ForSchool(School s)
        {
            Build();
            return bySchool.TryGetValue(s, out var l) ? l : new List<SkillDef>();
        }

        public static IEnumerable<SkillDef> All { get { Build(); return skills.Values; } }

        public static List<SkillDef> PlayerSkills() => All.Where(s => !s.hidden).ToList();

        public static Color SchoolColor(School s)
        {
            switch (s)
            {
                case School.Blade: return new Color(0.82f, 0.84f, 0.9f);
                case School.Warcry: return new Color(0.9f, 0.58f, 0.25f);
                case School.Assassination: return new Color(0.62f, 0.5f, 0.78f);
                case School.Archery: return new Color(0.6f, 0.8f, 0.35f);
                case School.Fire: return new Color(1f, 0.45f, 0.15f);
                case School.Frost: return new Color(0.55f, 0.85f, 1f);
                case School.Storm: return new Color(0.98f, 0.92f, 0.3f);
                case School.Venom: return new Color(0.45f, 0.9f, 0.3f);
                case School.Holy: return new Color(1f, 0.95f, 0.62f);
                case School.Necromancy: return new Color(0.55f, 0.95f, 0.8f);
                case School.Blood: return new Color(0.9f, 0.15f, 0.22f);
                default: return new Color(0.7f, 0.38f, 1f);
            }
        }

        public static Color ElementColor(Element e)
        {
            switch (e)
            {
                case Element.Fire: return new Color(1f, 0.5f, 0.15f);
                case Element.Cold: return new Color(0.55f, 0.85f, 1f);
                case Element.Lightning: return new Color(1f, 0.95f, 0.35f);
                case Element.Poison: return new Color(0.45f, 0.95f, 0.3f);
                case Element.Holy: return new Color(1f, 0.97f, 0.7f);
                case Element.Shadow: return new Color(0.7f, 0.4f, 1f);
                default: return new Color(0.92f, 0.92f, 0.92f);
            }
        }

        static SkillDef S(string id, string name, School school, int tier, string flavor)
        {
            var s = new SkillDef { id = id, name = name, school = school, tier = tier, flavor = flavor, color = SchoolColor(school) };
            skills[id] = s;
            if (!bySchool.TryGetValue(school, out var list)) bySchool[school] = list = new List<SkillDef>();
            list.Add(s);
            return s;
        }

        static void Build()
        {
            if (built) return;
            built = true;

            // ================= BLADE =================
            S("blade_cleave", "Cleave", School.Blade, 1, "A wide swing that hits every adjacent enemy.")
                .Act(Targeting.Melee, 1, 1).Cost(4, 0.5f, 0).Weap(105, 10);
            S("blade_mastery", "Blade Mastery", School.Blade, 2, "Years of drills sharpen every strike.")
                .Passive().Mod(StatType.AttackDamage, 6, 6).Mod(StatType.Accuracy, 5, 5);
            S("blade_lunge", "Lunge", School.Blade, 3, "Leap to an enemy and run them through.")
                .Act(Targeting.Dash, 4).Cost(6, 0.5f, 4).Weap(150, 15);
            S("blade_riposte", "Riposte", School.Blade, 4, "Turn an enemy's attack into an opening.")
                .Passive().Mod(StatType.BlockChance, 2, 1.5f).Proc(ProcTrigger.OnStruck, 12, 3, "blade_counter");
            S("blade_whirlwind", "Whirlwind", School.Blade, 5, "Spin into a storm of steel.")
                .Act(Targeting.Self, 0, 2).Cost(12, 1, 3).Weap(80, 9).Hits(2);
            S("blade_decapitate", "Decapitate", School.Blade, 6, "A brutal finishing blow.")
                .Act(Targeting.Melee).Cost(14, 1, 6).Weap(250, 25).Execute(100).Crit(15);

            // ================= WARCRY =================
            S("war_battlecry", "Battle Cry", School.Warcry, 1, "A roar that steels your body for war.")
                .Act(Targeting.Self).Cost(8, 0.5f, 15).Buff(10).Mod(StatType.AttackDamage, 20, 6).Mod(StatType.Armor, 10, 8);
            S("war_ironskin", "Iron Skin", School.Warcry, 2, "Your flesh hardens like forged metal.")
                .Passive().Mod(StatType.MaxLifePct, 5, 5).Mod(StatType.Armor, 8, 8);
            S("war_shout", "Intimidating Shout", School.Warcry, 3, "Enemies nearby lose their nerve.")
                .Act(Targeting.Self, 0, 3).Cost(6, 0.5f, 8).Status(StatusType.Weaken, 100, 4);
            S("war_groundslam", "Ground Slam", School.Warcry, 4, "Smash the earth, stunning those around you.")
                .Act(Targeting.Self, 0, 1).Cost(10, 1, 5).Weap(130, 12).Status(StatusType.Stun, 55, 2);
            S("war_juggernaut", "Unstoppable", School.Warcry, 5, "Nothing can stop your advance.")
                .Passive().Mod(StatType.DamageReduction, 2, 1.5f).Mod(StatType.Thorns, 4, 4).Mod(StatType.LifeRegen, 0.5f, 0.4f).Key(Keystone.Juggernaut);
            S("war_avatar", "Avatar of War", School.Warcry, 6, "Become an unstoppable engine of destruction.")
                .Act(Targeting.Self).Cost(20, 1, 30).Buff(8).Mod(StatType.AttackDamage, 40, 6).Mod(StatType.DamageReduction, 15, 1.5f).Mod(StatType.LifeSteal, 4, 0.5f);

            // ================= ASSASSINATION =================
            S("as_backstab", "Backstab", School.Assassination, 1, "Strike a weak point with lethal precision.")
                .Act(Targeting.Melee).Cost(3, 0.4f, 0).Weap(140, 14).Crit(25);
            S("as_envenom", "Envenom", School.Assassination, 2, "Coat your blades in venom.")
                .Passive().Mod(StatType.PoisonDamage, 8, 6).Proc(ProcTrigger.OnHit, 20, 4, "proc_venom");
            S("as_shadowstep", "Shadow Step", School.Assassination, 3, "Vanish and reappear, primed to strike.")
                .Act(Targeting.Blink, 5).Cost(6, 0.4f, 5).Buff(3).Mod(StatType.CritChance, 30, 5);
            S("as_fan", "Fan of Knives", School.Assassination, 4, "Hurl knives in every direction.")
                .Act(Targeting.Self, 0, 2).Cost(9, 0.8f, 2).Weap(80, 10).Status(StatusType.Bleed, 40, 4);
            S("as_lethality", "Lethality", School.Assassination, 5, "Know exactly where to cut.")
                .Passive().Mod(StatType.CritChance, 3, 2).Mod(StatType.CritDamage, 15, 10);
            S("as_deathmark", "Death Mark", School.Assassination, 6, "Mark a target for death.")
                .Act(Targeting.Target, 7).Cost(10, 1, 8).Spell(30, 9, Element.Shadow).Status(StatusType.Vulnerable, 100, 6);

            // ================= ARCHERY =================
            S("arc_powershot", "Power Shot", School.Archery, 1, "A heavy shot that pierces and knocks back.")
                .Act(Targeting.Projectile, 7).Cost(4, 0.4f, 0).Weap(130, 13).Proj(1, true).Knock();
            S("arc_multishot", "Multishot", School.Archery, 2, "Loose several arrows at once.")
                .Act(Targeting.Projectile, 7).Cost(6, 0.5f, 1).Weap(75, 8).Proj(3);
            S("arc_eagleeye", "Eagle Eye", School.Archery, 3, "Nothing escapes your sight.")
                .Passive().Mod(StatType.Accuracy, 10, 10).Mod(StatType.CritChance, 2, 1.5f).Mod(StatType.LightRadius, 1, 0.2f);
            S("arc_rain", "Rain of Arrows", School.Archery, 4, "Blot out the sky.")
                .Act(Targeting.Ground, 7, 2).Cost(12, 1, 4).Weap(95, 10);
            S("arc_explosive", "Explosive Arrow", School.Archery, 5, "An arrow tipped with alchemical fire.")
                .Act(Targeting.Projectile, 7, 1).Cost(9, 0.8f, 2).Weap(130, 13, Element.Fire).Status(StatusType.Burn, 35, 3);
            S("arc_deadeye", "Deadeye", School.Archery, 6, "Every volley finds more targets.")
                .Passive().Mod(StatType.ExtraProjectiles, 1, 0.2f).Mod(StatType.AttackDamage, 5, 5);

            // ================= FIRE =================
            S("fire_bolt", "Firebolt", School.Fire, 1, "Hurl a ball of flame.")
                .Act(Targeting.Projectile, 7).Cost(4, 0.4f, 0).Spell(9, 3, Element.Fire).Status(StatusType.Burn, 40, 3);
            S("fire_pyromancy", "Pyromancy", School.Fire, 2, "Flame answers your call more eagerly.")
                .Passive().Mod(StatType.FireDamage, 8, 7).Mod(StatType.FireRes, 4, 3);
            S("fire_ball", "Fireball", School.Fire, 3, "An explosive sphere of fire.")
                .Act(Targeting.Projectile, 7, 1).Cost(10, 0.8f, 1).Spell(15, 5, Element.Fire).Status(StatusType.Burn, 30, 3);
            S("fire_nova", "Flame Nova", School.Fire, 4, "Erupt in a ring of fire.")
                .Act(Targeting.Self, 0, 2).Cost(12, 1, 4).Spell(18, 5, Element.Fire).Status(StatusType.Burn, 50, 3);
            S("fire_combustion", "Combustion", School.Fire, 5, "Slain enemies detonate.")
                .Passive().Mod(StatType.FireDamage, 4, 3).Proc(ProcTrigger.OnKill, 30, 5, "proc_explosion");
            S("fire_meteor", "Meteor", School.Fire, 6, "Call down a burning star.")
                .Act(Targeting.Ground, 8, 2).Cost(25, 2, 8).Spell(48, 12, Element.Fire).Status(StatusType.Burn, 60, 4);

            // ================= FROST =================
            S("frost_shard", "Ice Shard", School.Frost, 1, "A piercing shard of ice.")
                .Act(Targeting.Projectile, 7).Cost(3, 0.4f, 0).Spell(7, 2.5f, Element.Cold).Proj(1, true).Status(StatusType.Chill, 50, 3);
            S("frost_armor", "Frost Armor", School.Frost, 2, "Encase yourself in rime.")
                .Act(Targeting.Self).Cost(10, 0.5f, 20).Buff(15).Mod(StatType.ArmorPct, 25, 6).Mod(StatType.ColdRes, 15, 3).Mod(StatType.Thorns, 4, 3);
            S("frost_nova", "Frost Nova", School.Frost, 3, "A burst of cold that freezes enemies solid.")
                .Act(Targeting.Self, 0, 2).Cost(10, 0.8f, 6).Spell(10, 3, Element.Cold).Status(StatusType.Freeze, 45, 2);
            S("frost_cryomancy", "Cryomancy", School.Frost, 4, "Bitter cold seeps from your hands.")
                .Passive().Mod(StatType.ColdDamage, 8, 7).Mod(StatType.StatusChance, 3, 2);
            S("frost_blizzard", "Blizzard", School.Frost, 5, "A howling storm of ice.")
                .Act(Targeting.Ground, 7, 2).Cost(18, 1.5f, 5).Spell(22, 6, Element.Cold).Status(StatusType.Chill, 100, 3);
            S("frost_shatter", "Shatter", School.Frost, 6, "Frozen things break.")
                .Passive().Mod(StatType.ColdDamage, 5, 5).Key(Keystone.Shatter);

            // ================= STORM =================
            S("storm_spark", "Spark", School.Storm, 1, "Lightning that leaps between foes.")
                .Act(Targeting.Chain, 6).Cost(4, 0.4f, 0).Spell(6, 2.5f, Element.Lightning).Chain(2).Status(StatusType.Shock, 30, 3);
            S("storm_static", "Static Charge", School.Storm, 2, "Your body hums with power.")
                .Passive().Mod(StatType.LightningDamage, 8, 7).Mod(StatType.CritChance, 2, 1);
            S("storm_chain", "Chain Lightning", School.Storm, 3, "A bolt that arcs through many enemies.")
                .Act(Targeting.Chain, 7).Cost(9, 0.8f, 1).Spell(13, 4, Element.Lightning).Chain(4).Status(StatusType.Shock, 40, 3);
            S("storm_thunderclap", "Thunderclap", School.Storm, 4, "A deafening crack that stuns.")
                .Act(Targeting.Self, 0, 1).Cost(10, 1, 5).Spell(16, 4.5f, Element.Lightning).Status(StatusType.Stun, 50, 2);
            S("storm_overcharge", "Overcharge", School.Storm, 5, "Critical strikes unleash lightning.")
                .Passive().Mod(StatType.CritDamage, 10, 6).Proc(ProcTrigger.OnCrit, 35, 5, "proc_lightning");
            S("storm_thunderstorm", "Thunderstorm", School.Storm, 6, "The heavens split open.")
                .Act(Targeting.Ground, 8, 3).Cost(26, 2, 9).Spell(32, 8, Element.Lightning).Status(StatusType.Shock, 60, 4);

            // ================= VENOM =================
            S("ven_dart", "Toxic Dart", School.Venom, 1, "A dart dripping with poison.")
                .Act(Targeting.Projectile, 7).Cost(3, 0.3f, 0).Spell(5, 2, Element.Poison).Status(StatusType.Poison, 100, 5);
            S("ven_virulence", "Virulence", School.Venom, 2, "Your toxins grow more potent.")
                .Passive().Mod(StatType.PoisonDamage, 8, 7).Mod(StatType.DotDamage, 6, 5);
            S("ven_cloud", "Poison Cloud", School.Venom, 3, "Release a choking miasma.")
                .Act(Targeting.Ground, 6, 1).Cost(8, 0.6f, 3).Spell(6, 2, Element.Poison).Hits(2).Status(StatusType.Poison, 100, 5);
            S("ven_bramble", "Bramble Skin", School.Venom, 4, "Thorns sprout from your skin.")
                .Passive().Mod(StatType.Thorns, 6, 6).Mod(StatType.PoisonRes, 5, 4).Mod(StatType.LifeRegen, 0.3f, 0.3f);
            S("ven_plague", "Plague Bearer", School.Venom, 5, "Disease spreads from the dead.")
                .Passive().Mod(StatType.PoisonDamage, 10, 6).Key(Keystone.PlagueBearer);
            S("ven_rot", "Wither", School.Venom, 6, "Everything nearby rots from within.")
                .Act(Targeting.Self, 0, 3).Cost(18, 1.5f, 8).Spell(14, 4, Element.Poison).Hits(3).Status(StatusType.Poison, 100, 6);

            // ================= HOLY =================
            S("holy_smite", "Smite", School.Holy, 1, "A blow empowered by divine light.")
                .Act(Targeting.Melee).Cost(4, 0.4f, 0).Weap(125, 12, Element.Holy).Status(StatusType.Stun, 10, 1);
            S("holy_light", "Holy Light", School.Holy, 2, "Mend your wounds.")
                .Act(Targeting.Self).Cost(10, 0.8f, 6).Heal(22, 3);
            S("holy_devotion", "Devotion", School.Holy, 3, "Faith shields you from harm.")
                .Passive().Mod(StatType.AllRes, 3, 2.5f).Mod(StatType.LifeRegen, 0.4f, 0.3f);
            S("holy_consecrate", "Consecration", School.Holy, 4, "Sanctify the ground around you.")
                .Act(Targeting.Self, 0, 2).Cost(12, 1, 5).Spell(15, 4.5f, Element.Holy).Heal(5, 0.5f);
            S("holy_shield", "Divine Shield", School.Holy, 5, "A barrier of pure light.")
                .Act(Targeting.Self).Cost(15, 1, 12).Shield(25, 5);
            S("holy_judgement", "Judgement", School.Holy, 6, "Pass judgement on the wicked.")
                .Act(Targeting.Ground, 6, 2).Cost(22, 1.5f, 8).Spell(40, 10, Element.Holy).Status(StatusType.Stun, 40, 2);

            // ================= NECROMANCY =================
            S("nec_skeleton", "Raise Skeleton", School.Necromancy, 1, "Bind a skeletal warrior to your will.")
                .Summon("skeleton_minion", 2).Cost(10, 0.5f, 3);
            S("nec_bonespear", "Bone Spear", School.Necromancy, 2, "A spear of sharpened bone.")
                .Act(Targeting.Projectile, 7).Cost(5, 0.5f, 0).Spell(10, 3.5f, Element.Physical).Proj(1, true);
            S("nec_darkpact", "Dark Pact", School.Necromancy, 3, "Your servants grow mighty.")
                .Passive().Mod(StatType.SummonDamage, 12, 10).Mod(StatType.SummonLife, 12, 10);
            S("nec_decrepify", "Decrepify", School.Necromancy, 4, "Curse enemies to take more damage.")
                .Act(Targeting.Ground, 7, 2).Cost(8, 0.5f, 6).Status(StatusType.Vulnerable, 100, 6);
            S("nec_soulharvest", "Soul Harvest", School.Necromancy, 5, "Draw strength from the dying.")
                .Passive().Mod(StatType.LifeOnKill, 3, 3).Mod(StatType.ManaOnKill, 3, 2).Key(Keystone.Necrolord);
            S("nec_golem", "Bone Golem", School.Necromancy, 6, "Assemble a hulking guardian.")
                .Summon("bone_golem", 1).Cost(30, 2, 20);

            // ================= BLOOD =================
            S("blood_rend", "Rend", School.Blood, 1, "Tear open a bleeding wound.")
                .Act(Targeting.Melee).Cost(3, 0.3f, 0).Weap(100, 10).Status(StatusType.Bleed, 100, 4);
            S("blood_thirst", "Bloodthirst", School.Blood, 2, "Drink deep of your enemies.")
                .Passive().Mod(StatType.LifeSteal, 1.5f, 1f).Mod(StatType.AttackDamage, 3, 3);
            S("blood_frenzy", "Frenzy", School.Blood, 3, "Enter a frenzied rage.")
                .Act(Targeting.Self).LifeCost(10).Cost(0, 0, 12).Buff(8).Mod(StatType.DoubleStrike, 15, 4).Mod(StatType.AttackDamage, 10, 4);
            S("blood_pact", "Blood Pact", School.Blood, 4, "Pay for power with your own life.")
                .Passive().Mod(StatType.SpellDamage, 6, 5).Mod(StatType.AttackDamage, 6, 5).Mod(StatType.MaxLifePct, 4, 2).Key(Keystone.BloodMagic);
            S("blood_nova", "Blood Nova", School.Blood, 5, "Your blood erupts outward as a weapon.")
                .Act(Targeting.Self, 0, 2).LifeCost(8).Cost(0, 0, 4).Spell(22, 6.5f, Element.Physical).Status(StatusType.Bleed, 60, 4);
            S("blood_berserker", "Berserker", School.Blood, 6, "Pain only fuels you.")
                .Passive().Mod(StatType.AttackDamage, 4, 3).Mod(StatType.DamageReduction, 1, 1).Key(Keystone.Berserker);

            // ================= VOID =================
            S("void_bolt", "Void Bolt", School.Void, 1, "A bolt of unmaking.")
                .Act(Targeting.Projectile, 7).Cost(4, 0.4f, 0).Spell(8, 3, Element.Shadow).Status(StatusType.Vulnerable, 20, 3);
            S("void_blink", "Blink", School.Void, 2, "Fold space and step through it.")
                .Act(Targeting.Blink, 6).Cost(5, 0.3f, 6);
            S("void_intellect", "Arcane Intellect", School.Void, 3, "Your mind expands beyond mortal limits.")
                .Passive().Mod(StatType.MaxManaPct, 6, 6).Mod(StatType.ManaRegen, 0.3f, 0.25f).Mod(StatType.SpellDamage, 3, 3);
            S("void_rift", "Void Rift", School.Void, 4, "Tear a hole in reality.")
                .Act(Targeting.Ground, 7, 1).Cost(12, 1, 4).Spell(20, 6, Element.Shadow).Status(StatusType.Fear, 50, 3);
            S("void_echo", "Echoing Mind", School.Void, 5, "Your spells reverberate.")
                .Passive().Mod(StatType.CooldownReduction, 3, 2).Key(Keystone.Echo);
            S("void_singularity", "Singularity", School.Void, 6, "Collapse space into a single point.")
                .Act(Targeting.Ground, 8, 3).Cost(28, 2, 10).Spell(34, 9, Element.Shadow).Pull();

            // ================= HIDDEN PROC SKILLS =================
            S("blade_counter", "Counterstrike", School.Blade, 0, null).Hidden().Act(Targeting.Target, 99).Weap(120, 12);
            S("proc_venom", "Venom Strike", School.Venom, 0, null).Hidden().Act(Targeting.Target, 99).Spell(4, 2, Element.Poison).Status(StatusType.Poison, 100, 5);
            S("proc_explosion", "Corpse Blast", School.Fire, 0, null).Hidden().Act(Targeting.Ground, 99, 1).Spell(10, 3.5f, Element.Fire).Status(StatusType.Burn, 30, 3);
            S("proc_lightning", "Arc Discharge", School.Storm, 0, null).Hidden().Act(Targeting.Chain, 99).Spell(8, 3, Element.Lightning).Chain(2).Status(StatusType.Shock, 30, 3);
            S("proc_frostburst", "Frost Burst", School.Frost, 0, null).Hidden().Act(Targeting.Ground, 99, 1).Spell(9, 3, Element.Cold).Status(StatusType.Freeze, 30, 1);
            S("proc_holybolt", "Holy Bolt", School.Holy, 0, null).Hidden().Act(Targeting.Target, 99).Spell(10, 3, Element.Holy).Heal(3, 0.3f);
            S("proc_shadowburst", "Shadow Burst", School.Void, 0, null).Hidden().Act(Targeting.Ground, 99, 1).Spell(12, 4, Element.Shadow);
            S("proc_bloodspike", "Blood Spike", School.Blood, 0, null).Hidden().Act(Targeting.Target, 99).Spell(10, 3, Element.Physical).Status(StatusType.Bleed, 100, 4);

            // ================= MONSTER SKILLS =================
            S("m_arrow", "Arrow", School.Archery, 0, null).MonsterOnly().Act(Targeting.Projectile, 6).Weap(100, 0);
            S("m_firebolt", "Firebolt", School.Fire, 0, null).MonsterOnly().Act(Targeting.Projectile, 6).Cost(0, 0, 2).Spell(6, 0, Element.Fire).Status(StatusType.Burn, 30, 3);
            S("m_frostbolt", "Frostbolt", School.Frost, 0, null).MonsterOnly().Act(Targeting.Projectile, 6).Cost(0, 0, 2).Spell(6, 0, Element.Cold).Status(StatusType.Chill, 50, 2);
            S("m_spark", "Spark", School.Storm, 0, null).MonsterOnly().Act(Targeting.Projectile, 6).Cost(0, 0, 2).Spell(7, 0, Element.Lightning).Status(StatusType.Shock, 30, 2);
            S("m_spit", "Acid Spit", School.Venom, 0, null).MonsterOnly().Act(Targeting.Projectile, 5).Cost(0, 0, 2).Spell(4, 0, Element.Poison).Status(StatusType.Poison, 80, 4);
            S("m_shadowbolt", "Shadow Bolt", School.Void, 0, null).MonsterOnly().Act(Targeting.Projectile, 6).Cost(0, 0, 2).Spell(7, 0, Element.Shadow).Status(StatusType.Weaken, 30, 3);
            S("m_slam", "Slam", School.Warcry, 0, null).MonsterOnly().Act(Targeting.Self, 0, 1).Cost(0, 0, 5).Weap(140, 0).Status(StatusType.Stun, 35, 1);
            S("m_firenova", "Fire Nova", School.Fire, 0, null).MonsterOnly().Act(Targeting.Self, 0, 2).Cost(0, 0, 6).Spell(9, 0, Element.Fire).Status(StatusType.Burn, 50, 3);
            S("m_frostnova", "Frost Nova", School.Frost, 0, null).MonsterOnly().Act(Targeting.Self, 0, 2).Cost(0, 0, 7).Spell(7, 0, Element.Cold).Status(StatusType.Freeze, 30, 1);
            S("m_meteor", "Meteor", School.Fire, 0, null).MonsterOnly().Act(Targeting.Ground, 7, 1).Cost(0, 0, 6).Spell(14, 0, Element.Fire).Status(StatusType.Burn, 60, 3);
            S("m_lightningstorm", "Lightning Storm", School.Storm, 0, null).MonsterOnly().Act(Targeting.Ground, 7, 2).Cost(0, 0, 7).Spell(12, 0, Element.Lightning).Status(StatusType.Shock, 50, 3);
            S("m_charge", "Charge", School.Blade, 0, null).MonsterOnly().Act(Targeting.Dash, 4).Cost(0, 0, 5).Weap(150, 0).Status(StatusType.Stun, 30, 1);
            S("m_blink", "Blink", School.Void, 0, null).MonsterOnly().Act(Targeting.Blink, 5).Cost(0, 0, 6);
            S("m_heal", "Dark Mending", School.Holy, 0, null).MonsterOnly().Act(Targeting.Self).Cost(0, 0, 8).Heal(25, 0);
            S("m_summon_skel", "Raise Dead", School.Necromancy, 0, null).MonsterOnly().Summon("skeleton", 2).Cost(0, 0, 8);
            S("m_summon_spider", "Brood", School.Venom, 0, null).MonsterOnly().Summon("spiderling", 3).Cost(0, 0, 7);
            S("m_summon_imp", "Summon Imp", School.Fire, 0, null).MonsterOnly().Summon("imp", 2).Cost(0, 0, 8);
            S("m_curse", "Hex", School.Void, 0, null).MonsterOnly().Act(Targeting.Target, 6).Cost(0, 0, 8).Status(StatusType.Vulnerable, 100, 4);
            S("m_web", "Web", School.Venom, 0, null).MonsterOnly().Act(Targeting.Target, 5).Cost(0, 0, 7).Status(StatusType.Chill, 100, 3);
            S("m_breath", "Fire Breath", School.Fire, 0, null).MonsterOnly().Act(Targeting.Projectile, 5, 1).Cost(0, 0, 4).Spell(12, 0, Element.Fire).Proj(1, true).Status(StatusType.Burn, 60, 3);
        }
    }
}
