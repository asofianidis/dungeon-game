using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace Depthbound
{
    /// <summary>
    /// Dev tool: launching the player with "-autoshot &lt;folder&gt;" plays a short scripted session
    /// and saves screenshots of the main screens, then quits. Used to verify rendering in CI.
    /// </summary>
    public class AutoShot : MonoBehaviour
    {
        string dir;

        public static void TryAttach(GameObject go)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-autoshot");
            if (i < 0 || i + 1 >= args.Length) return;
            go.AddComponent<AutoShot>().dir = args[i + 1];
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        void Shot(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));

        IEnumerator Start()
        {
            Directory.CreateDirectory(dir);
            var gm = GameManager.Instance;
            gm.autopilot = true;
            yield return Frames(30);
            Shot("1_title");
            yield return Frames(5);

            gm.StartRun(ClassDatabase.Get("pyromancer"), 0, 4242, false);
            var w = gm.world;
            var p = w.player;
            var rng = new Rng(7);
            for (int i = 0; i < 6; i++) p.AddToBackpack(ItemGenerator.Generate(rng, 8, 200, 1f));
            p.AddToBackpack(ItemGenerator.GenerateUnique(rng, ItemDatabase.Uniques[0], 8));
            p.GainXp(400);
            p.LearnSkill(SkillDatabase.Get("fire_bolt"));
            yield return Frames(20);
            Shot("2_start");

            // Spawn a small pack nearby and fight it for a couple of seconds.
            foreach (var id in new[] { "skeleton", "skeleton", "skel_archer" })
            {
                var spot = w.FindFreeNear(p.Pos + new Vector2Int(3, 0), 3);
                if (spot == null) continue;
                var m = w.CreateMonster(MonsterDatabase.Get(id), 2, Faction.Monster, 0);
                m.Recalc(); m.life = m.MaxLife; m.awake = true;
                w.Place(m, spot.Value);
            }
            float fight = 0; bool shot = false;
            while (fight < 3f)
            {
                fight += Time.deltaTime;
                var foe = w.NearestEnemy(8f);
                if (foe != null && !w.PlayerUseSkill(0, foe.pos)) w.PlayerAttack(foe.pos);
                if (!shot && fight > 0.9f && (w.projectiles.Count > 0 || fight > 1.5f)) { shot = true; Shot("3_combat"); }
                yield return null;
            }

            // Run toward the stairs, fighting anything that shows up, for ~12 seconds of real time.
            float elapsed = 0;
            var stairsField = w.map.DistanceField(new[] { w.map.stairs }, true);
            while (elapsed < 12f && !p.dead)
            {
                elapsed += Time.deltaTime;
                if (p.LifePct < 0.5f) p.life = p.MaxLife;
                var e = w.NearestEnemy(7f);
                if (e != null)
                {
                    p.moveInput = Vector2.zero;
                    if (!w.PlayerUseSkill(0, e.pos)) w.PlayerAttack(e.pos);
                }
                else
                {
                    p.moveInput = w.SteerAlong(p, stairsField) ?? Vector2.zero;
                }
                yield return null;
            }
            p.moveInput = Vector2.zero;
            yield return Frames(5);
            Shot("4_explore");
            yield return Frames(5);
            gm.ui.DebugOpen(1);
            yield return Frames(5);
            Shot("5_inventory");
            yield return Frames(5);
            gm.ui.DebugOpen(2);
            yield return Frames(5);
            Shot("6_skills");
            yield return Frames(10);
            Application.Quit();
        }
    }
}
