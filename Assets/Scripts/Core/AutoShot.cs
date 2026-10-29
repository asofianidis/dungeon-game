using System;
using System.Collections;
using System.IO;
using System.Linq;
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
            yield return Frames(30);
            Shot("1_title");
            yield return Frames(5);
            gm.state = GameState.ClassSelect;
            yield return Frames(10);
            Shot("2_classes");
            yield return Frames(5);

            gm.StartRun(ClassDatabase.Get("pyromancer"), 0, 4242, false);
            var w = gm.world;
            var p = w.player;
            var rng = new Rng(7);
            for (int i = 0; i < 6; i++) p.AddToBackpack(ItemGenerator.Generate(rng, 8, 200, 1f));
            p.AddToBackpack(ItemGenerator.GenerateUnique(rng, ItemDatabase.Uniques[0], 8));
            p.GainXp(400);
            yield return Frames(20);
            Shot("3_start");
            yield return Frames(5);

            // Wander toward unexplored space and fight whatever shows up.
            for (int step = 0; step < 160 && !p.dead; step++)
            {
                var e = w.NearestEnemy(6);
                if (e != null) { if (!w.PlayerUseSkill(0, e.Pos)) w.PlayerAttackTarget(e); }
                else
                {
                    var dist = w.map.DistanceField(new[] { w.map.stairs }, true);
                    var best = Vector2Int.zero; int bd = dist[p.x, p.y];
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                            if (w.map.CanStep(p.x, p.y, dx, dy) && dist[p.x + dx, p.y + dy] < bd) { bd = dist[p.x + dx, p.y + dy]; best = new Vector2Int(dx, dy); }
                    if (best == Vector2Int.zero || !w.PlayerMove(best.x, best.y)) w.PlayerWait();
                }
                if (p.LifePct < 0.5f) { p.life = p.MaxLife; }
                yield return Frames(2);
                if (step == 40) Shot("4_combat");
            }
            yield return Frames(10);
            Shot("5_explore");
            yield return Frames(5);
            gm.ui.DebugOpen(1);
            yield return Frames(5);
            Shot("6_inventory");
            yield return Frames(5);
            gm.ui.DebugOpen(2);
            yield return Frames(5);
            Shot("7_skills");
            yield return Frames(5);
            gm.ui.DebugOpen(3);
            yield return Frames(5);
            Shot("8_hero");
            yield return Frames(10);
            Application.Quit();
        }
    }
}
