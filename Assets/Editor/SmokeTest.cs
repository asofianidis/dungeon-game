using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Depthbound.EditorTools
{
    /// <summary>
    /// Headless simulation that plays many turns with every class to catch exceptions in generation,
    /// combat, skills, procs and loot. Run: Unity -batchmode -executeMethod Depthbound.EditorTools.SmokeTest.Run
    /// </summary>
    public static class SmokeTest
    {
        [MenuItem("Depthbound/Run Smoke Test")]
        public static void Run()
        {
            int failures = 0;
            try
            {
                failures += CheckData();
                failures += GenerateFloors();
                failures += MovementCheck();
                foreach (var cls in ClassDatabase.All)
                    failures += SimulateRun(cls, cls.id.GetHashCode() & 0x7FFFFFFF, 600);
                failures += SimulateRun(ClassDatabase.All[0], 1234, 400, corruption: 3, godMode: true, startDepth: 26);
            }
            catch (Exception e)
            {
                Debug.LogError("SMOKE: unexpected exception " + e);
                failures++;
            }
            Debug.Log(failures == 0 ? "SMOKE: ALL PASSED" : $"SMOKE: {failures} FAILURE(S)");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static int CheckData()
        {
            int fails = 0;
            foreach (var s in SkillDatabase.All)
            {
                if (s.procSkill != null && SkillDatabase.Get(s.procSkill) == null) { Debug.LogError($"SMOKE: {s.id} proc skill {s.procSkill} missing"); fails++; }
                if (s.summonId != null && MonsterDatabase.Get(s.summonId) == null) { Debug.LogError($"SMOKE: {s.id} summon {s.summonId} missing"); fails++; }
            }
            foreach (var m in MonsterDatabase.All)
                if (m.skills != null)
                    foreach (var id in m.skills)
                        if (SkillDatabase.Get(id) == null) { Debug.LogError($"SMOKE: monster {m.id} skill {id} missing"); fails++; }
            foreach (var b in Biomes.All)
            {
                foreach (var id in b.monsters) if (MonsterDatabase.Get(id) == null) { Debug.LogError($"SMOKE: biome monster {id} missing"); fails++; }
                if (MonsterDatabase.Get(b.boss) == null) { Debug.LogError($"SMOKE: boss {b.boss} missing"); fails++; }
            }
            foreach (var u in ItemDatabase.Uniques)
            {
                if (ItemDatabase.Base(u.baseId) == null) { Debug.LogError($"SMOKE: unique {u.id} base {u.baseId} missing"); fails++; }
                foreach (var p in u.powers)
                    if (p.skillId != null && SkillDatabase.Get(p.skillId) == null) { Debug.LogError($"SMOKE: unique {u.id} skill {p.skillId} missing"); fails++; }
            }
            foreach (var c in ClassDatabase.All)
            {
                if (SkillDatabase.Get(c.startSkill) == null) { Debug.LogError($"SMOKE: class {c.id} skill missing"); fails++; }
                if (ItemDatabase.Base(c.startWeapon) == null) { Debug.LogError($"SMOKE: class {c.id} weapon missing"); fails++; }
            }
            int playerSkills = SkillDatabase.PlayerSkills().Count;
            Debug.Log($"SMOKE: data ok - {playerSkills} player skills, {ItemDatabase.Bases.Count} bases, {ItemDatabase.Affixes.Count} affixes, {ItemDatabase.Uniques.Count} legendaries, {MonsterDatabase.All.Count()} monsters");
            return fails;
        }

        static int MovementCheck()
        {
            var w = new World(ClassDatabase.Get("pyromancer"), 4242, 0, new MetaProgress(), false);
            var p = w.player;
            var start = p.pos;
            var dist = w.map.DistanceField(new[] { w.map.stairs }, true);
            Debug.Log($"SMOKE: move start={p.pos} tile={p.Pos} distHere={dist[p.x, p.y]} stairs={w.map.stairs} speed={p.MoveSpeed} chill={p.Has(StatusType.Chill)}");
            for (int i = 0; i < 600 && Vector2.Distance(p.pos, w.map.stairs) > 1.2f; i++)
            {
                p.moveInput = w.SteerAlong(p, dist) ?? Vector2.zero;
                p.life = p.MaxLife;
                w.Tick(0.05f);
            }
            float moved = Vector2.Distance(start, p.pos);
            Debug.Log($"SMOKE: move end={p.pos} moved={moved:0.00} input={p.moveInput}");
            if (Vector2.Distance(p.pos, w.map.stairs) > 1.5f) { Debug.LogError("SMOKE: player failed to reach the stairs (wedged?)"); return 1; }
            return 0;
        }

        static int GenerateFloors()
        {
            int fails = 0;
            for (int depth = 1; depth <= 60; depth++)
            {
                for (int seed = 0; seed < 5; seed++)
                {
                    var map = DungeonGenerator.Generate(new Rng(seed * 977 + depth), depth);
                    var d = map.DistanceField(new[] { map.start }, false);
                    if (d[map.stairs.x, map.stairs.y] == int.MaxValue) { Debug.LogError($"SMOKE: depth {depth} seed {seed}: stairs unreachable"); fails++; }
                    var dl = map.DistanceField(new[] { map.start }, true);
                    if (dl[map.stairs.x, map.stairs.y] == int.MaxValue) { Debug.LogError($"SMOKE: depth {depth} seed {seed}: stairs blocked by lava"); fails++; }
                }
            }
            Debug.Log("SMOKE: generated 300 floors");
            return fails;
        }

        static int SimulateRun(ClassDef cls, int seed, int turns, int corruption = 0, bool godMode = false, int startDepth = 1)
        {
            var meta = new MetaProgress();
            meta.upgrades[(int)Upgrade.Armory] = 2;
            var w = new World(cls, seed, corruption, meta, false);
            var rng = new Rng(seed + 1);
            var p = w.player;
            if (startDepth > 1) { w.depth = startDepth; w.GenerateFloor(); }

            // Give the hero a pile of loot and skills so every system gets exercised.
            for (int i = 0; i < 25; i++)
            {
                var it = ItemGenerator.Generate(rng, 10 + i, 300, 1.5f);
                p.AddToBackpack(it);
            }
            foreach (var u in ItemDatabase.Uniques.Take(12)) p.AddToBackpack(ItemGenerator.GenerateUnique(rng, u, 20));
            p.GainXp(20000);
            var skills = SkillDatabase.PlayerSkills();
            for (int guard = 0; guard < 400 && p.skillPoints > 0; guard++) p.LearnSkill(rng.Pick(skills));
            for (int i = 0; i < 5; i++) { p.allocated[i] += p.attrPoints / 5; }
            p.attrPoints = 0;
            p.Recalc();

            int floors = 0, casts = 0;
            const float dt = 0.05f;
            int steps = turns * 4;
            for (int t = 0; t < steps; t++)
            {
                if (godMode || t % 200 == 0) { p.life = p.MaxLife; p.mana = p.MaxMana; }
                if (p.dead) break;
                if (t % 10 == 0) p.moveInput = new Vector2(rng.Range(-1f, 1f), rng.Range(-1f, 1f));
                int action = rng.Range(0, 100);
                var e = w.NearestEnemy(8);
                var aim = e != null ? e.pos : p.pos + new Vector2(rng.Range(-4f, 4f), rng.Range(-4f, 4f));
                if (action < 3 && p.backpack.Count > 0) p.Equip(rng.Pick(p.backpack));
                else if (action < 30) { if (w.PlayerUseSkill(rng.Range(0, Player.HotbarSize), aim)) casts++; }
                else if (action < 55) w.PlayerAttack(aim);
                else if (action < 57) w.QuaffHealth();
                else if (action < 59) w.PlayerDodge(p.moveInput);
                else if (action < 60) w.Interact();
                if (t % 600 == 599)
                {
                    p.pos = w.map.stairs;
                    w.Tick(dt);
                    if (w.map.stairsSealed) foreach (var m in w.actors.OfType<Monster>().Where(m => m.isBoss).ToList()) w.Kill(m, p);
                    if (w.Descend()) floors++;
                }
                w.Tick(dt);
            }            Fx.Pending.Clear();
            Debug.Log($"SMOKE: {cls.name} seed {seed}: {w.stats.time:0}s, depth {w.depth}, level {p.level}, kills {w.stats.kills}, casts {casts}, floors {floors}, dead={p.dead}, keystones={p.keystones.Count}, procs={p.procs.Count}");
            return 0;
        }
    }
}
