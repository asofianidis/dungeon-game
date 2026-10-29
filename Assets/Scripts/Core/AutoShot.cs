using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Depthbound
{
    /// <summary>
    /// Dev tool: launching the player with "-autoshot &lt;folder&gt;" plays a short scripted session
    /// and saves screenshots of the main screens, then quits. It uses a throwaway, read-only save.
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
            MetaProgress.ReadOnly = true;
            var gm = GameManager.Instance;
            gm.meta = new MetaProgress();
            gm.autopilot = true;
            yield return Frames(30);
            Shot("1_title");
            yield return Frames(5);

            // Arrive at the castle: the princess's introduction opens automatically.
            gm.StartRun(ClassDatabase.Get("pyromancer"), 0, 4242, false);
            var w = gm.world;
            var p = w.player;
            p.pos = new Vector2(15, 21);
            p.facing = Vector2.up;
            yield return Frames(30);
            Shot("2_hub_dialogue");
            gm.ui.DebugOpen(0);
            yield return Frames(20);
            Shot("3_hub");
            p.pos = new Vector2(15, 6);
            p.facing = Vector2.down;
            yield return Frames(30);
            Shot("4_hub_gate");

            // Through the gate into depth 1, then fight a small pack.
            w.TakePortal();
            var rng = new Rng(7);
            for (int i = 0; i < 6; i++) p.AddToBackpack(ItemGenerator.Generate(rng, 8, 200, 1f));
            p.LearnSkill(SkillDatabase.Get("fire_bolt"));
            yield return Frames(20);
            foreach (var id in new[] { "skeleton", "crusader", "crossbowman" })
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
                if (p.LifePct < 0.5f) p.life = p.MaxLife;
                var foe = w.NearestEnemy(8f);
                if (foe != null && !w.PlayerUseSkill(0, foe.pos)) w.PlayerAttack(foe.pos);
                if (!shot && fight > 0.8f) { shot = true; Shot("5_combat"); }
                yield return null;
            }

            // Walk toward the stairs for a bit.
            var stairsField = w.map.DistanceField(new[] { w.map.stairs }, true);
            float elapsed = 0;
            while (elapsed < 6f && !p.dead)
            {
                elapsed += Time.deltaTime;
                if (p.LifePct < 0.5f) p.life = p.MaxLife;
                var e = w.NearestEnemy(7f);
                if (e != null) { p.moveInput = Vector2.zero; if (!w.PlayerUseSkill(0, e.pos)) w.PlayerAttack(e.pos); }
                else p.moveInput = w.SteerAlong(p, stairsField) ?? Vector2.zero;
                yield return null;
            }
            p.moveInput = Vector2.zero;
            Shot("6_explore");
            yield return Frames(5);

            // A guardian falls: its artifact drops and a portal home opens.
            w.depth = 5;
            w.GenerateFloor();
            yield return Frames(5);
            var boss = w.actors.OfType<Monster>().FirstOrDefault(m => m.isBoss);
            if (boss != null)
            {
                p.pos = boss.pos + new Vector2(0, -2.2f);
                if (w.map.Walkable(p.x, p.y) == false) p.pos = boss.pos;
                yield return Frames(10);
                Shot("7_boss");
                w.Kill(boss, p);
                yield return Frames(40);
                Shot("8_artifact");
            }
            gm.ui.DebugOpen(1);
            yield return Frames(5);
            Shot("9_inventory");
            gm.ui.DebugOpen(3);
            yield return Frames(5);
            Shot("10_hero");
            yield return Frames(10);
            Application.Quit();
        }
    }
}
