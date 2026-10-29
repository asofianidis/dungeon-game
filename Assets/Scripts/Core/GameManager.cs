using System;
using System.Collections.Generic;
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

        float repeatTimer, stunTimer, travelTimer;
        Vector2Int lastDir;
        List<Vector2Int> travel;
        Vector2Int travelGoal;
        int travelEnemies;
        float travelLife;

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
            Application.targetFrameRate = 60;
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
            travel = null;
            state = GameState.Playing;
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
            travel = null;
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
            if (ui.BlocksGameInput) return;
            HandleInput();
            if (world.player.dead) EndRun();
        }

        // ------------------------------------------------------------------ input

        static bool Any(params KeyCode[] keys)
        {
            foreach (var k in keys) if (Input.GetKey(k)) return true;
            return false;
        }

        static bool AnyDown(params KeyCode[] keys)
        {
            foreach (var k in keys) if (Input.GetKeyDown(k)) return true;
            return false;
        }

        static Vector2Int ReadDir()
        {
            int dx = 0, dy = 0;
            if (Any(KeyCode.W, KeyCode.UpArrow, KeyCode.Keypad8)) dy++;
            if (Any(KeyCode.S, KeyCode.DownArrow, KeyCode.Keypad2)) dy--;
            if (Any(KeyCode.A, KeyCode.LeftArrow, KeyCode.Keypad4)) dx--;
            if (Any(KeyCode.D, KeyCode.RightArrow, KeyCode.Keypad6)) dx++;
            if (Any(KeyCode.Q, KeyCode.Keypad7)) { dx--; dy++; }
            if (Any(KeyCode.E, KeyCode.Keypad9)) { dx++; dy++; }
            if (Any(KeyCode.Z, KeyCode.Keypad1)) { dx--; dy--; }
            if (Any(KeyCode.C, KeyCode.Keypad3)) { dx++; dy--; }
            return new Vector2Int(Mathf.Clamp(dx, -1, 1), Mathf.Clamp(dy, -1, 1));
        }

        static readonly KeyCode[] MoveKeys =
        {
            KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.Q, KeyCode.E, KeyCode.Z, KeyCode.C,
            KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
            KeyCode.Keypad1, KeyCode.Keypad2, KeyCode.Keypad3, KeyCode.Keypad4, KeyCode.Keypad6, KeyCode.Keypad7, KeyCode.Keypad8, KeyCode.Keypad9,
        };

        Vector2Int? MouseAim()
        {
            var t = view.MouseTile;
            return world.map.InBounds(t.x, t.y) ? t : (Vector2Int?)null;
        }

        void HandleInput()
        {
            var p = world.player;

            if (p.Incapacitated)
            {
                stunTimer += Time.deltaTime;
                if (stunTimer > 0.35f)
                {
                    stunTimer = 0;
                    world.Log(p.Has(StatusType.Freeze) ? "You are frozen solid!" : "You are stunned!", new Color(0.8f, 0.8f, 1f));
                    world.PlayerWait();
                }
                return;
            }

            if (travel != null)
            {
                if (Input.anyKeyDown) { travel = null; }
                else { StepTravel(); return; }
            }

            // Movement with key repeat.
            var dir = ReadDir();
            if (dir != Vector2Int.zero)
            {
                bool fresh = AnyDown(MoveKeys) || dir != lastDir;
                repeatTimer -= Time.deltaTime;
                if (fresh || repeatTimer <= 0)
                {
                    world.PlayerMove(dir.x, dir.y);
                    repeatTimer = fresh ? 0.22f : 0.085f;
                }
                lastDir = dir;
                return;
            }
            lastDir = Vector2Int.zero;

            for (int i = 0; i < Player.HotbarSize; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) { world.PlayerUseSkill(i, MouseAim()); return; }

            if (AnyDown(KeyCode.Space, KeyCode.Keypad5, KeyCode.Period) && !Input.GetKey(KeyCode.LeftShift)) { world.PlayerWait(); return; }
            if (AnyDown(KeyCode.Return, KeyCode.KeypadEnter) || (Input.GetKeyDown(KeyCode.Period) && Input.GetKey(KeyCode.LeftShift))) { world.Descend(); return; }
            if (Input.GetKeyDown(KeyCode.R)) { world.QuaffHealth(); return; }
            if (Input.GetKeyDown(KeyCode.F)) { world.QuaffMana(); return; }

            if (!ui.MouseOverUI())
            {
                if (Input.GetMouseButtonDown(0)) OnLeftClick();
                else if (Input.GetMouseButtonDown(1) && !string.IsNullOrEmpty(p.hotbar[0])) world.PlayerUseSkill(0, MouseAim());
            }
        }

        void OnLeftClick()
        {
            var aim = MouseAim();
            if (aim == null) return;
            var t = aim.Value;
            var map = world.map;
            var a = world.ActorAt(t);
            if (a != null && a.IsHostileTo(world.player) && map.visible[t.x, t.y])
            {
                if (world.PlayerAttackTarget(a)) return;
            }
            if (t == world.player.Pos)
            {
                if (map.Get(t.x, t.y) == Tile.StairsDown) world.Descend();
                else world.PlayerWait();
                return;
            }
            if (!map.explored[t.x, t.y]) return;
            int dx = t.x - world.player.x, dy = t.y - world.player.y;
            if (Mathf.Abs(dx) <= 1 && Mathf.Abs(dy) <= 1 && (DungeonMap.IsInteractable(map.Get(t.x, t.y)) || a != null))
            {
                world.PlayerMove(dx, dy);
                return;
            }
            BeginTravel(t);
        }

        void BeginTravel(Vector2Int goal)
        {
            travelGoal = goal;
            travel = new List<Vector2Int>();
            travelEnemies = CountVisibleEnemies();
            travelLife = world.player.life;
            travelTimer = 0;
            StepTravel();
        }

        int CountVisibleEnemies()
        {
            int n = 0;
            foreach (var _ in world.VisibleEnemies()) n++;
            return n;
        }

        void StepTravel()
        {
            travelTimer -= Time.deltaTime;
            if (travelTimer > 0) return;
            travelTimer = 0.07f;
            var p = world.player;
            var map = world.map;
            if (p.Pos == travelGoal || CountVisibleEnemies() > travelEnemies || p.life < travelLife - 0.5f) { travel = null; return; }

            var dist = map.DistanceField(new[] { travelGoal }, true);
            int cur = dist[p.x, p.y];
            bool goalBlocks = !map.Walkable(travelGoal.x, travelGoal.y);
            if (cur == int.MaxValue && !goalBlocks) { travel = null; return; }
            Vector2Int? best = null;
            int bestD = cur;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = p.x + dx, ny = p.y + dy;
                    if (!map.InBounds(nx, ny) || !map.explored[nx, ny]) continue;
                    if (new Vector2Int(nx, ny) == travelGoal && goalBlocks) { best = new Vector2Int(dx, dy); bestD = -1; continue; }
                    if (!map.CanStep(p.x, p.y, dx, dy)) continue;
                    var occ = world.ActorAt(nx, ny);
                    if (occ != null && occ.IsHostileTo(p)) continue;
                    if (dist[nx, ny] < bestD) { bestD = dist[nx, ny]; best = new Vector2Int(dx, dy); }
                }
            if (best == null) { travel = null; return; }
            if (!world.PlayerMove(best.Value.x, best.Value.y)) { travel = null; return; }
            travelLife = world.player.life;
            if (goalBlocks && Combat.Dist(world.player.Pos, travelGoal) == 0) travel = null;
        }
    }
}
