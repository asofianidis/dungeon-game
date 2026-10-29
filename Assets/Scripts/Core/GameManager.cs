using System;
using UnityEngine;

namespace Depthbound
{
    public enum GameState { Title, ClassSelect, Playing, Dead, Altar, Codex }

    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public GameState state = GameState.Title;
        public MetaProgress meta;
        public World world;
        public WorldRenderer view;
        public GameUI ui;

        public int essenceAwarded;
        public bool corruptionUnlocked, pendingDaily;
        /// <summary>When set, keyboard/mouse input is ignored (used by the screenshot tool).</summary>
        public bool autopilot;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null || FindAnyObjectByType<GameManager>() != null) return;
            var go = new GameObject("Depthbound");
            go.AddComponent<GameManager>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Application.targetFrameRate = 120;
            meta = MetaProgress.Load();
            view = gameObject.AddComponent<WorldRenderer>();
            ui = gameObject.AddComponent<GameUI>();
            AutoShot.TryAttach(gameObject);
        }

        public static int DailySeed()
        {
            var d = DateTime.UtcNow;
            return d.Year * 10000 + d.Month * 100 + d.Day;
        }

        public void StartRun(ClassDef cls, int corruption, int? seed, bool daily)
        {
            int s = seed ?? (daily ? DailySeed() : Environment.TickCount & 0x7FFFFFFF);
            world = new World(cls, s, daily ? 0 : corruption, meta, daily);
            if (daily) { meta.lastDailyPlayed = DailySeed().ToString(); meta.Save(); }
            view.Bind(world);
            ui.ResetForRun();
            state = GameState.Playing;
            if (meta.questStage == 0) ui.OpenDialogue(Story.PrincessDialogue(meta));
        }

        public void OpenShop() => ui.OpenShop();

        public void EndRun()
        {
            if (world == null) return;
            essenceAwarded = world.EssenceEarned();
            meta.essence += essenceAwarded;
            meta.totalRuns++;
            meta.totalKills += world.stats.kills;
            meta.bossKills += world.stats.bossKills;
            meta.bestDepth = Mathf.Max(meta.bestDepth, world.stats.deepest);
            corruptionUnlocked = false;
            if (!world.daily && world.corruption == meta.maxCorruption && world.stats.deepest > 10)
            {
                meta.maxCorruption++;
                corruptionUnlocked = true;
            }
            meta.Save();
            state = GameState.Dead;
        }

        public void ToTitle()
        {
            state = GameState.Title;
            world = null;
            view.Bind(null);
        }

        void Update()
        {
            if (state != GameState.Playing || world == null) return;
            if (world.player.dead) { EndRun(); return; }
            if (ui.PausesGame) { world.player.moveInput = Vector2.zero; return; }
            if (!autopilot) HandleInput();
            // Clamp dt so a hitch never teleports things through walls.
            world.Tick(Mathf.Min(Time.deltaTime, 0.05f));
            if (world.player.dead) EndRun();
        }

        // ------------------------------------------------------------------ input

        static bool Any(params KeyCode[] keys)
        {
            foreach (var k in keys) if (Input.GetKey(k)) return true;
            return false;
        }

        void HandleInput()
        {
            var p = world.player;
            var mv = Vector2.zero;
            if (Any(KeyCode.W, KeyCode.UpArrow)) mv.y += 1;
            if (Any(KeyCode.S, KeyCode.DownArrow)) mv.y -= 1;
            if (Any(KeyCode.A, KeyCode.LeftArrow)) mv.x -= 1;
            if (Any(KeyCode.D, KeyCode.RightArrow)) mv.x += 1;
            p.moveInput = mv;

            var aim = view.MouseWorld;
            if (Input.GetKeyDown(KeyCode.Space)) world.PlayerDodge(mv.sqrMagnitude > 0 ? mv : aim - p.pos);
            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) world.Interact();
            if (Input.GetKeyDown(KeyCode.R)) world.QuaffHealth();
            if (Input.GetKeyDown(KeyCode.F)) world.QuaffMana();

            for (int i = 0; i < Player.HotbarSize; i++)
                if (Input.GetKey(KeyCode.Alpha1 + i)) world.PlayerUseSkill(i, aim);

            if (ui.MouseOverUI()) return;
            if (Input.GetMouseButtonDown(0) && world.InteractAt(view.MouseTile)) return;
            if (Input.GetMouseButton(0)) world.PlayerAttack(aim);
            if (Input.GetMouseButton(1) && !string.IsNullOrEmpty(p.hotbar[0])) world.PlayerUseSkill(0, aim);
        }
    }
}
