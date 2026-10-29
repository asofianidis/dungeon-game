using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Depthbound
{
    /// <summary>Immediate-mode UI for every screen. Coordinates are in a virtual 900px-tall space.</summary>
    public class GameUI : MonoBehaviour
    {
        enum Panel { None, Inventory, Skills, Character, Help, Pause, Dialogue }

        const float VirtualHeight = 900f;

        Panel panel;
        bool shopOpen;
        float scale = 1f, VW, VH = VirtualHeight;
        readonly List<Rect> uiRects = new List<Rect>();
        readonly List<Rect> nextRects = new List<Rect>();

        GUIStyle box, label, small, title, header, button, tip, center, outline;
        Texture2D white, panelBg, minimap;
        int minimapFov = -1, minimapVersion = -1;

        string tipText;
        Item tipItem, tipCompare;
        School skillTab = School.Blade;
        ClassDef selectedClass;
        int corruptionChoice;
        string seedText = "";
        Vector2 codexScroll, logScroll;

        GameManager GM => GameManager.Instance;
        World W => GM.world;

        /// <summary>The simulation pauses while any menu is open.</summary>
        public bool PausesGame => shopOpen || panel != Panel.None;

        public void ResetForRun() { panel = Panel.None; shopOpen = false; }
        public void OpenShop() { shopOpen = true; panel = Panel.Inventory; }
        public void DebugOpen(int p) { panel = (Panel)p; }

        List<DialoguePage> dialogue;
        int dialogueIndex, dialogueFrame;

        public void OpenDialogue(List<DialoguePage> pages)
        {
            if (pages == null || pages.Count == 0) return;
            dialogue = pages;
            dialogueIndex = 0;
            dialogueFrame = Time.frameCount;
            shopOpen = false;
            panel = Panel.Dialogue;
        }

        void AdvanceDialogue()
        {
            dialogueIndex++;
            if (dialogue == null || dialogueIndex >= dialogue.Count) { dialogue = null; panel = Panel.None; }
        }

        public bool MouseOverUI()
        {
            var m = new Vector2(Input.mousePosition.x / scale, (Screen.height - Input.mousePosition.y) / scale);
            foreach (var r in uiRects) if (r.Contains(m)) return true;
            return false;
        }

        void Update()
        {
            if (GM == null || GM.state != GameState.Playing) return;
            if (panel == Panel.Dialogue)
            {
                // Ignore the key press that opened the dialogue.
                if (Time.frameCount != dialogueFrame && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
                    AdvanceDialogue();
                if (Input.GetKeyDown(KeyCode.Escape)) { dialogue = null; panel = Panel.None; }
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (shopOpen) { shopOpen = false; panel = Panel.None; }
                else if (panel != Panel.None) panel = Panel.None;
                else panel = Panel.Pause;
            }
            if (panel == Panel.Pause) return;
            if (Input.GetKeyDown(KeyCode.I) || Input.GetKeyDown(KeyCode.B)) Toggle(Panel.Inventory);
            if (Input.GetKeyDown(KeyCode.K)) Toggle(Panel.Skills);
            if (Input.GetKeyDown(KeyCode.P)) Toggle(Panel.Character);
            if (Input.GetKeyDown(KeyCode.H) || Input.GetKeyDown(KeyCode.F1)) Toggle(Panel.Help);
        }

        void Toggle(Panel p)
        {
            panel = panel == p ? Panel.None : p;
            if (p != Panel.Inventory) shopOpen = false;
        }

        // ------------------------------------------------------------------ styles & helpers

        void InitStyles()
        {
            if (box != null) return;
            white = Tex(new Color(1, 1, 1, 1));
            panelBg = Tex(new Color(0.07f, 0.065f, 0.085f, 0.94f));
            box = new GUIStyle(GUI.skin.box) { normal = { background = panelBg }, border = new RectOffset(2, 2, 2, 2) };
            label = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 15, wordWrap = true, normal = { textColor = new Color(0.9f, 0.9f, 0.9f) } };
            small = new GUIStyle(label) { fontSize = 13 };
            title = new GUIStyle(label) { fontSize = 58, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            header = new GUIStyle(label) { fontSize = 20, fontStyle = FontStyle.Bold, wordWrap = false };
            center = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
            button = new GUIStyle(GUI.skin.button) { richText = true, fontSize = 15 };
            tip = new GUIStyle(label) { fontSize = 14, padding = new RectOffset(10, 10, 8, 8), normal = { background = Tex(new Color(0.04f, 0.04f, 0.06f, 0.97f)), textColor = Color.white } };
            outline = new GUIStyle(label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
        }

        static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        void Rect(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = old;
        }

        void Frame(Rect r, Color c, float w = 2)
        {
            Rect(new Rect(r.x, r.y, r.width, w), c);
            Rect(new Rect(r.x, r.yMax - w, r.width, w), c);
            Rect(new Rect(r.x, r.y, w, r.height), c);
            Rect(new Rect(r.xMax - w, r.y, w, r.height), c);
        }

        Rect Box(Rect r, string heading = null)
        {
            nextRects.Add(r);
            GUI.Box(r, GUIContent.none, box);
            Frame(r, new Color(0.35f, 0.3f, 0.45f, 0.9f), 1);
            if (heading != null)
            {
                GUI.Label(new Rect(r.x + 12, r.y + 6, r.width - 24, 28), heading, header);
                return new Rect(r.x + 12, r.y + 38, r.width - 24, r.height - 46);
            }
            return new Rect(r.x + 10, r.y + 8, r.width - 20, r.height - 16);
        }

        void Bar(Rect r, float pct, Color fill, string text)
        {
            Rect(r, new Color(0, 0, 0, 0.85f));
            Rect(new Rect(r.x + 1, r.y + 1, (r.width - 2) * Mathf.Clamp01(pct), r.height - 2), fill);
            if (text != null) GUI.Label(r, text, new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, wordWrap = false });
        }

        void Shadowed(Rect r, string text, GUIStyle st, Color c)
        {
            var old = st.normal.textColor;
            st.normal.textColor = new Color(0, 0, 0, 0.9f);
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, st);
            st.normal.textColor = c;
            GUI.Label(r, text, st);
            st.normal.textColor = old;
        }

        static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
        static string Clock(float seconds) => $"{(int)(seconds / 60)}:{(int)(seconds % 60):00}";
        static string Secs(float s) => s < 10 ? s.ToString("0.0") : Mathf.CeilToInt(s).ToString();
        static string Col(string text, Color c) => $"<color=#{Hex(c)}>{text}</color>";

        bool Hover(Rect r) => r.Contains(Event.current.mousePosition);

        void Icon(Rect r, Sprite s)
        {
            if (s != null) GUI.DrawTexture(r, s.texture, ScaleMode.ScaleToFit);
        }

        // ------------------------------------------------------------------ main

        void OnGUI()
        {
            if (GM == null) return;
            InitStyles();
            scale = Screen.height / VirtualHeight;
            VW = Screen.width / scale;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));
            if (Event.current.type == EventType.Layout)
            {
                uiRects.Clear();
                uiRects.AddRange(nextRects);
                nextRects.Clear();
            }
            tipText = null; tipItem = null; tipCompare = null;

            switch (GM.state)
            {
                case GameState.Title: DrawTitle(); break;
                case GameState.ClassSelect: DrawClassSelect(); break;
                case GameState.Altar: DrawAltar(); break;
                case GameState.Codex: DrawCodex(); break;
                case GameState.Playing: DrawGame(); break;
                case GameState.Dead: DrawDeath(); break;
            }
            DrawTooltip();
        }

        // ------------------------------------------------------------------ title / meta screens

        void Backdrop()
        {
            Rect(new Rect(0, 0, VW, VH), new Color(0.03f, 0.025f, 0.04f, 1f));
            for (int i = 0; i < 40; i++)
            {
                float x = (Mathf.Sin(i * 12.9898f) * 43758.5453f % 1 + 1) % 1 * VW;
                float y = (Mathf.Sin(i * 78.233f) * 12345.678f % 1 + 1) % 1 * VH;
                float a = 0.05f + 0.05f * Mathf.Sin(Time.time + i);
                Rect(new Rect(x, y, 3, 3), new Color(0.8f, 0.5f, 1f, a));
            }
        }

        void DrawTitle()
        {
            Backdrop();
            var m = GM.meta;
            Shadowed(new Rect(0, 120, VW, 80), "DEPTHBOUND", title, new Color(0.95f, 0.75f, 0.35f));
            GUI.Label(new Rect(0, 200, VW, 30), "<i>The Archdemon's Descent - recover the Royal Artifacts of House Vaelthorn</i>", center);
            float w = 320, x = (VW - w) / 2, y = 280;
            if (GUI.Button(new Rect(x, y, w, 46), "New Run", button)) { selectedClass = null; GM.pendingDaily = false; GM.state = GameState.ClassSelect; }
            y += 56;
            string daily = m.lastDailyPlayed == GameManager.DailySeed().ToString() ? " (played)" : "";
            if (GUI.Button(new Rect(x, y, w, 46), $"Daily Descent  <size=12>#{GameManager.DailySeed()}{daily}</size>", button)) { selectedClass = null; GM.pendingDaily = true; GM.state = GameState.ClassSelect; }
            y += 56;
            if (GUI.Button(new Rect(x, y, w, 46), $"Soul Altar  <size=12>({m.essence} essence)</size>", button)) GM.state = GameState.Altar;
            y += 56;
            if (GUI.Button(new Rect(x, y, w, 46), $"Codex  <size=12>({m.codex.Count}/{ItemDatabase.Uniques.Count} legendaries)</size>", button)) GM.state = GameState.Codex;
            y += 56;
            if (GUI.Button(new Rect(x, y, w, 46), "Quit", button)) Application.Quit();
            y += 76;
            GUI.Label(new Rect(0, y, VW, 24), $"Best depth: {m.bestDepth}    Runs: {m.totalRuns}    Kills: {m.totalKills}    Bosses slain: {m.bossKills}    Corruption unlocked: {m.maxCorruption}", center);
        }

        void DrawClassSelect()
        {
            Backdrop();
            var m = GM.meta;
            Shadowed(new Rect(0, 20, VW, 50), GM.pendingDaily ? "DAILY DESCENT - Choose your discipline" : "Choose your demonic discipline", new GUIStyle(title) { fontSize = 34 }, new Color(0.95f, 0.8f, 0.5f));
            float cw = 230, ch = 150, gap = 12;
            float gx = (VW - (cw * 5 + gap * 4)) / 2, gy = 90;
            for (int i = 0; i < ClassDatabase.All.Count; i++)
            {
                var c = ClassDatabase.All[i];
                var r = new Rect(gx + (i % 5) * (cw + gap), gy + (i / 5) * (ch + gap), cw, ch);
                bool unlocked = m.unlockedClasses.Contains(c.id);
                bool sel = selectedClass == c;
                Rect(r, sel ? new Color(0.25f, 0.2f, 0.35f, 0.95f) : new Color(0.08f, 0.07f, 0.1f, 0.95f));
                Frame(r, sel ? new Color(1f, 0.8f, 0.4f) : c.color * 0.8f, sel ? 2 : 1);
                Icon(new Rect(r.x + 10, r.y + 10, 56, 56), SpriteFactory.Hero(c));
                GUI.Label(new Rect(r.x + 74, r.y + 10, cw - 80, 24), $"<b>{c.name}</b>", label);
                GUI.Label(new Rect(r.x + 74, r.y + 34, cw - 80, 40), Col(string.Join(" / ", c.affinities.Select(a => a.ToString())), SkillDatabase.SchoolColor(c.affinities[0])), small);
                GUI.Label(new Rect(r.x + 10, r.y + 72, cw - 20, 44), c.description, small);
                if (!unlocked)
                {
                    Rect(r, new Color(0, 0, 0, 0.55f));
                    if (GUI.Button(new Rect(r.x + 30, r.yMax - 36, cw - 60, 28), $"Unlock ({c.unlockCost} essence)", button)) m.TryUnlock(c);
                }
                else if (GUI.Button(new Rect(r.x, r.y, r.width, r.height - 30), GUIContent.none, GUIStyle.none)) selectedClass = c;
                if (unlocked) GUI.Label(new Rect(r.x + 10, r.yMax - 30, cw - 20, 24), $"<size=12>Starts with {SkillDatabase.Get(c.startSkill).name}</size>", small);
            }

            var info = Box(new Rect(gx, gy + 2 * (ch + gap) + 10, cw * 5 + gap * 4, 300), selectedClass != null ? selectedClass.name : "Select a class");
            if (selectedClass != null)
            {
                var c = selectedClass;
                var sb = new StringBuilder();
                sb.AppendLine(c.description);
                sb.AppendLine();
                for (int i = 0; i < 5; i++) sb.Append($"{ClassDatabase.AttributeNames[i]} {c.attributes[i]}    ");
                sb.AppendLine("\n");
                sb.AppendLine($"Class perk: +1 to all {c.affinities[0]} and {c.affinities[1]} skills.");
                var s = SkillDatabase.Get(c.startSkill);
                sb.AppendLine($"Starting skill: <b>{s.name}</b> - {s.flavor}");
                sb.AppendLine($"Starting gear: {ItemDatabase.Base(c.startWeapon).name}{(c.startOffhand != null ? ", " + ItemDatabase.Base(c.startOffhand).name : "")}");
                sb.AppendLine("\n<color=#aaaaaa>Every class can learn every skill school. Classes only set your starting point.</color>");
                GUI.Label(new Rect(info.x, info.y, info.width * 0.62f, info.height), sb.ToString(), label);

                float rx = info.x + info.width * 0.65f, ry = info.y;
                if (!GM.pendingDaily)
                {
                    GUI.Label(new Rect(rx, ry, 300, 24), $"Corruption: <b>{corruptionChoice}</b> / {m.maxCorruption}", label);
                    if (GUI.Button(new Rect(rx + 220, ry, 30, 24), "-", button)) corruptionChoice = Mathf.Max(0, corruptionChoice - 1);
                    if (GUI.Button(new Rect(rx + 254, ry, 30, 24), "+", button)) corruptionChoice = Mathf.Min(m.maxCorruption, corruptionChoice + 1);
                    GUI.Label(new Rect(rx, ry + 28, 320, 60), $"<size=12>Monsters +{corruptionChoice * 25}% life, +{corruptionChoice * 15}% damage, more elites. Loot and essence +{corruptionChoice * 25}%. Reach depth 11 to unlock the next level.</size>", small);
                    GUI.Label(new Rect(rx, ry + 92, 120, 24), "Seed (optional):", small);
                    seedText = GUI.TextField(new Rect(rx + 120, ry + 90, 180, 24), seedText, 10);
                }
                else GUI.Label(new Rect(rx, ry, 320, 80), "Everyone gets the same dungeon today. Earns 1.5x essence.", label);
                if (GUI.Button(new Rect(rx, info.yMax - 50, 300, 46), "<b>Descend</b>", button))
                {
                    int? seed = null;
                    if (!GM.pendingDaily && int.TryParse(seedText, out var sv)) seed = sv;
                    GM.StartRun(c, corruptionChoice, seed, GM.pendingDaily);
                }
            }
            if (GUI.Button(new Rect(20, VH - 60, 140, 40), "Back", button)) GM.state = GameState.Title;
        }

        void DrawAltar()
        {
            Backdrop();
            var m = GM.meta;
            var r = Box(new Rect((VW - 760) / 2, 60, 760, 720), $"Soul Altar  -  {m.essence} essence");
            GUI.Label(new Rect(r.x, r.y, r.width, 40), "<color=#aaaaaa>Essence is earned when a run ends. Spend it on permanent upgrades that make every future descent stronger.</color>", small);
            float y = r.y + 48;
            for (int i = 0; i < (int)Upgrade.COUNT; i++)
            {
                var u = (Upgrade)i;
                int rank = m.Rank(u), max = Upgrades.Max[i];
                var row = new Rect(r.x, y, r.width, 58);
                Rect(row, new Color(1, 1, 1, 0.04f));
                GUI.Label(new Rect(row.x + 8, row.y + 4, 260, 24), $"<b>{Upgrades.Names[i]}</b>  {rank}/{max}", label);
                GUI.Label(new Rect(row.x + 8, row.y + 28, 420, 24), Col(Upgrades.Desc[i], new Color(0.7f, 0.7f, 0.8f)), small);
                for (int k = 0; k < max; k++) Rect(new Rect(row.x + 270 + k * 16, row.y + 10, 12, 12), k < rank ? new Color(0.9f, 0.6f, 1f) : new Color(0.25f, 0.2f, 0.3f));
                if (rank < max)
                {
                    int cost = Upgrades.Cost(u, rank);
                    GUI.enabled = m.essence >= cost;
                    if (GUI.Button(new Rect(row.xMax - 150, row.y + 12, 140, 34), $"Buy ({cost})", button)) m.TryBuy(u);
                    GUI.enabled = true;
                }
                else GUI.Label(new Rect(row.xMax - 150, row.y + 18, 140, 24), "<color=#ffcc66>MAX</color>", center);
                y += 64;
            }
            if (GUI.Button(new Rect(20, VH - 60, 140, 40), "Back", button)) GM.state = GameState.Title;
        }

        void DrawCodex()
        {
            Backdrop();
            var m = GM.meta;
            var r = Box(new Rect((VW - 900) / 2, 40, 900, 780), $"Codex of Legends  -  {m.codex.Count}/{ItemDatabase.Uniques.Count} discovered");
            var uniques = ItemDatabase.Uniques;
            float rowH = 92;
            codexScroll = GUI.BeginScrollView(r, codexScroll, new Rect(0, 0, r.width - 20, uniques.Count * rowH));
            for (int i = 0; i < uniques.Count; i++)
            {
                var u = uniques[i];
                var row = new Rect(0, i * rowH, r.width - 24, rowH - 6);
                Rect(row, new Color(1, 1, 1, 0.04f));
                bool known = m.codex.Contains(u.id);
                var b = ItemDatabase.Base(u.baseId);
                Icon(new Rect(row.x + 8, row.y + 8, 48, 48), SpriteFactory.Icon(b.icon, known ? Item.RarityColor(Rarity.Legendary) : Color.gray));
                if (known)
                {
                    GUI.Label(new Rect(row.x + 66, row.y + 4, 400, 24), $"<b>{Col(u.name, Item.RarityColor(Rarity.Legendary))}</b>  <size=12><color=#999999>{b.name}</color></size>", label);
                    GUI.Label(new Rect(row.x + 66, row.y + 26, row.width - 76, 60), Col(string.Join("   |   ", u.powers.Select(p => p.Describe())), new Color(0.6f, 0.85f, 1f)), small);
                }
                else GUI.Label(new Rect(row.x + 66, row.y + 18, 400, 24), $"<color=#666666>??? ({b.name})</color>", label);
            }
            GUI.EndScrollView();
            if (GUI.Button(new Rect(20, VH - 60, 140, 40), "Back", button)) GM.state = GameState.Title;
        }

        void DrawDeath()
        {
            Backdrop();
            var w = W;
            var r = Box(new Rect((VW - 560) / 2, 120, 560, 560));
            Shadowed(new Rect(r.x, r.y + 10, r.width, 60), w != null && w.player.dead ? "YOU HAVE DIED" : "RUN ABANDONED", new GUIStyle(title) { fontSize = 40 }, new Color(0.9f, 0.25f, 0.25f));
            if (w != null)
            {
                var s = w.stats;
                var sb = new StringBuilder();
                sb.AppendLine($"{w.player.cls.name}, level {w.player.level}");
                if (w.player.dead) sb.AppendLine($"Slain by {s.killedBy} on depth {w.depth} ({w.map.biome.name})");
                sb.AppendLine();
                sb.AppendLine($"Deepest floor: <b>{s.deepest}</b>");
                sb.AppendLine($"Monsters slain: {s.kills}    Bosses: {s.bossKills}");
                sb.AppendLine($"Gold earned: {s.goldEarned}    Legendaries found: {s.legendaries}");
                sb.AppendLine($"Time survived: {Clock(s.time)}");
                sb.AppendLine();
                sb.AppendLine($"<size=20><color=#d8a0ff>+{GM.essenceAwarded} Soul Essence</color></size>  (total {GM.meta.essence})");
                if (GM.corruptionUnlocked) sb.AppendLine($"\n<color=#ff8844><b>Corruption {GM.meta.maxCorruption} unlocked!</b></color>");
                GUI.Label(new Rect(r.x + 20, r.y + 90, r.width - 40, 360), sb.ToString(), new GUIStyle(label) { alignment = TextAnchor.UpperCenter, fontSize = 17 });
            }
            if (GUI.Button(new Rect(r.x + 40, r.yMax - 60, 220, 44), "Descend Again", button)) { GM.pendingDaily = false; GM.ToTitle(); GM.state = GameState.ClassSelect; }
            if (GUI.Button(new Rect(r.xMax - 260, r.yMax - 60, 220, 44), "Title", button)) GM.ToTitle();
        }

        // ------------------------------------------------------------------ in-game

        void DrawGame()
        {
            var w = W;
            if (w == null) return;
            DrawWorldBars();
            DrawFloatingText();
            DrawHoverInfo();
            DrawTopLeft();
            DrawMinimap();
            DrawLog();
            DrawBottom();
            DrawMenuButtons();
            DrawPrompt();
            switch (panel)
            {
                case Panel.Inventory: DrawInventory(); if (shopOpen) DrawShop(); break;
                case Panel.Skills: DrawSkills(); break;
                case Panel.Character: DrawCharacter(); break;
                case Panel.Help: DrawHelp(); break;
                case Panel.Pause: DrawPause(); break;
                case Panel.Dialogue: DrawDialogue(); break;
            }
        }

        void DrawDialogue()
        {
            if (dialogue == null || dialogueIndex >= dialogue.Count) { panel = Panel.None; return; }
            var page = dialogue[dialogueIndex];
            Rect(new Rect(0, 0, VW, VH), new Color(0, 0, 0, 0.35f));
            float w = Mathf.Min(980, VW - 40);
            var r = Box(new Rect((VW - w) / 2, VH - 300, w, 250));
            // Live portrait of the speaker, framed on the left of the box.
            var portrait = page.speaker == Story.Princess ? PortraitStudio.Get("princess") : null;
            float textX = r.x + 6;
            if (portrait != null)
            {
                var pr = new Rect(r.x - 4, r.y - 2, 230, 230);
                Rect(new Rect(pr.x - 2, pr.y - 2, pr.width + 4, pr.height + 4), Story.PrincessColor * 0.8f);
                GUI.DrawTexture(pr, portrait, ScaleMode.ScaleAndCrop);
                textX = pr.xMax + 16;
            }
            Shadowed(new Rect(textX, r.y + 2, r.xMax - textX, 30), $"<b>{page.speaker}</b>", new GUIStyle(header) { fontSize = 22 }, Story.PrincessColor);
            GUI.Label(new Rect(textX, r.y + 40, r.xMax - textX - 6, 150), page.text, new GUIStyle(label) { fontSize = 18 });
            bool last = dialogueIndex == dialogue.Count - 1;
            GUI.Label(new Rect(textX, r.yMax - 34, 300, 26), $"<size=12><color=#999999>{dialogueIndex + 1}/{dialogue.Count}   Space / E to continue</color></size>", small);
            if (GUI.Button(new Rect(r.xMax - 170, r.yMax - 44, 160, 38), last ? "Farewell" : "Continue", button)) AdvanceDialogue();
        }

        void DrawWorldBars()
        {
            var map = W.map;
            foreach (var a in W.actors)
            {
                if (a.dead || a == W.player || !map.InBounds(a.x, a.y) || !map.visible[a.x, a.y]) continue;
                var m = a as Monster;
                bool show = a.life < a.MaxLife - 0.5f || (m != null && m.rank > 0) || a.faction == Faction.Player;
                if (!show) continue;
                var p = GM.view.WorldToGui(new Vector3(a.pos.x, 1.35f * a.scale + 0.2f, a.pos.y), scale);
                float w = 46 * Mathf.Clamp(9f / GM.view.zoom, 0.7f, 1.4f) * (a.isBoss ? 1.6f : 1f);
                var r = new Rect(p.x - w / 2, p.y - 4, w, 6);
                Rect(r, new Color(0, 0, 0, 0.8f));
                Color c = a.faction == Faction.Player ? new Color(0.3f, 0.9f, 0.4f) : m != null && m.rank == 3 ? new Color(1f, 0.3f, 0.1f) : m != null && m.rank == 2 ? new Color(1f, 0.8f, 0.2f) : m != null && m.rank == 1 ? new Color(0.4f, 0.6f, 1f) : new Color(0.9f, 0.15f, 0.15f);
                Rect(new Rect(r.x + 1, r.y + 1, (r.width - 2) * Mathf.Clamp01(a.life / a.MaxLife), r.height - 2), c);
            }
        }

        void DrawFloatingText()
        {
            foreach (var t in GM.view.texts)
            {
                var p = GM.view.WorldToGui(t.pos, scale);
                var st = new GUIStyle(outline) { fontSize = Mathf.RoundToInt(16 * t.size * Mathf.Clamp(9f / GM.view.zoom, 0.7f, 1.6f)) };
                var c = t.color; c.a = Mathf.Clamp01(1.3f - t.t);
                Shadowed(new Rect(p.x - 120, p.y - 14, 240, 28), t.text, st, c);
            }
        }

        void DrawHoverInfo()
        {
            if (MouseOverUI()) return;
            var map = W.map;
            var a = W.ActorNear(GM.view.MouseWorld, 0.4f);
            if (a == null || a == W.player || !map.visible[a.x, a.y]) return;
            var m = a as Monster;
            Color nameCol = m == null ? Color.white : m.rank == 3 ? new Color(1f, 0.4f, 0.3f) : m.rank == 2 ? new Color(1f, 0.85f, 0.3f) : m.rank == 1 ? new Color(0.5f, 0.7f, 1f) : Color.white;
            if (a.faction == Faction.Player) nameCol = new Color(0.5f, 1f, 0.6f);
            var r = Box(new Rect((VW - 360) / 2, 10, 360, 86));
            string n = m != null ? m.DisplayName() : a.name;
            GUI.Label(new Rect(r.x, r.y, r.width, 22), $"<b>{Col(n, nameCol)}</b>  <size=12>Lv {a.level}</size>", new GUIStyle(label) { alignment = TextAnchor.UpperCenter });
            Bar(new Rect(r.x, r.y + 26, r.width, 16), a.life / a.MaxLife, new Color(0.75f, 0.12f, 0.12f), $"{Mathf.CeilToInt(a.life)} / {Mathf.CeilToInt(a.MaxLife)}{(a.shield > 1 ? $" (+{Mathf.RoundToInt(a.shield)})" : "")}");
            var statuses = string.Join(" ", a.statuses.Select(s => s.type).Distinct().Select(s => Col(s.ToString(), Combat.StatusColor(s))));
            GUI.Label(new Rect(r.x, r.y + 46, r.width, 22), statuses, new GUIStyle(small) { alignment = TextAnchor.UpperCenter });
        }

        void DrawTopLeft()
        {
            var w = W; var p = w.player;
            var r = Box(new Rect(10, 10, 320, 112));
            GUI.Label(new Rect(r.x, r.y, r.width, 22), $"<b>Depth {w.depth}</b>  <color=#99aacc>{w.map.biome.name}</color>{(w.corruption > 0 ? $"  <color=#ff7744>C{w.corruption}</color>" : "")}", label);
            GUI.Label(new Rect(r.x, r.y + 22, r.width, 22), $"Level {p.level} {p.cls.name}", small);
            Bar(new Rect(r.x, r.y + 44, r.width, 12), p.xp / (float)p.XpToNext, new Color(0.7f, 0.55f, 1f), null);
            GUI.Label(new Rect(r.x, r.y + 60, r.width, 22), $"<color=#ffd040>{p.gold} gold</color>    Kills {w.stats.kills}    Time {Clock(w.stats.time)}", small);
            if (w.IsBossFloor && w.map.stairsSealed) GUI.Label(new Rect(r.x, r.y + 78, r.width, 22), "<color=#ff6644>A guardian seals the stairs</color>", small);
        }

        void DrawMinimap()
        {
            var w = W; var map = w.map;
            if (minimap == null || minimap.width != map.w || minimap.height != map.h)
            {
                minimap = new Texture2D(map.w, map.h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                minimapFov = -1;
            }
            if (minimapFov != w.fovVersion || minimapVersion != w.floorVersion || Time.frameCount % 6 == 0)
            {
                minimapFov = w.fovVersion; minimapVersion = w.floorVersion;
                var px = new Color32[map.w * map.h];
                for (int x = 0; x < map.w; x++)
                    for (int y = 0; y < map.h; y++)
                    {
                        Color c = new Color(0, 0, 0, 0);
                        if (map.explored[x, y])
                        {
                            var t = map.tiles[x, y];
                            bool vis = map.visible[x, y];
                            if (t == Tile.Wall) c = new Color(0.45f, 0.42f, 0.5f, 1);
                            else if (t == Tile.StairsDown) c = new Color(0.4f, 0.7f, 1f, 1);
                            else if (t == Tile.Lava) c = new Color(0.9f, 0.3f, 0.1f, 1);
                            else if (t == Tile.Water) c = new Color(0.2f, 0.35f, 0.6f, 1);
                            else if (DungeonMap.IsInteractable(t)) c = new Color(1f, 0.85f, 0.3f, 1);
                            else c = vis ? new Color(0.28f, 0.27f, 0.3f, 1) : new Color(0.16f, 0.15f, 0.18f, 1);
                        }
                        px[y * map.w + x] = c;
                    }
                foreach (var g in w.ground)
                    if (map.explored[g.x, g.y] && g.item != null && g.item.rarity >= Rarity.Rare) px[g.y * map.w + g.x] = g.item.Color;
                foreach (var a in w.actors)
                {
                    if (a.dead || !map.visible[a.x, a.y]) continue;
                    px[a.y * map.w + a.x] = a == w.player ? Color.white : a.faction == Faction.Player ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.2f, 0.2f);
                }
                minimap.SetPixels32(px);
                minimap.Apply();
            }
            float s = 3f;
            var r = new Rect(VW - map.w * s - 14, 10, map.w * s + 4, map.h * s + 4);
            nextRects.Add(r);
            Rect(r, new Color(0, 0, 0, 0.7f));
            GUI.DrawTexture(new Rect(r.x + 2, r.y + 2, map.w * s, map.h * s), minimap);
        }

        void DrawLog()
        {
            var log = W.log;
            float h = 20, x = 12, y = VH - 150;
            int n = Mathf.Min(7, log.Count);
            for (int i = 0; i < n; i++)
            {
                var (text, c) = log[log.Count - 1 - i];
                c.a = 1f - i * 0.11f;
                Shadowed(new Rect(x, y - i * h, 520, h), text, new GUIStyle(small) { wordWrap = false }, c);
            }
        }

        void DrawBottom()
        {
            var p = W.player;
            float slot = 54, gap = 6;
            float hw = Player.HotbarSize * (slot + gap) - gap;
            float x0 = (VW - hw) / 2, y0 = VH - slot - 14;
            var area = new Rect(x0 - 10, y0 - 66, hw + 140, slot + 76);
            nextRects.Add(area);

            float bw = (hw - 10) / 2;
            string lifeText = $"{Mathf.CeilToInt(p.life)} / {Mathf.CeilToInt(p.MaxLife)}" + (p.shield > 1 ? $"  (+{Mathf.RoundToInt(p.shield)})" : "");
            Bar(new Rect(x0, y0 - 30, bw, 22), p.life / p.MaxLife, new Color(0.75f, 0.1f, 0.12f), lifeText);
            if (p.shield > 1) Rect(new Rect(x0, y0 - 30, bw * Mathf.Clamp01(p.shield / p.MaxLife), 4), new Color(1f, 0.95f, 0.6f));
            Bar(new Rect(x0 + bw + 10, y0 - 30, bw, 22), p.MaxMana > 0 ? p.mana / p.MaxMana : 0, new Color(0.15f, 0.3f, 0.85f), $"{Mathf.FloorToInt(p.mana)} / {Mathf.CeilToInt(p.MaxMana)}");

            // Statuses & buffs
            var parts = new List<string>();
            foreach (var g in p.statuses.GroupBy(s => s.type))
                parts.Add(Col($"{g.Key}{(g.Count() > 1 ? " x" + g.Count() : "")} {Mathf.CeilToInt(g.Max(s => s.remaining))}s", Combat.StatusColor(g.Key)));
            foreach (var b in p.buffs) parts.Add(Col($"{b.name} {Mathf.CeilToInt(b.remaining)}s", new Color(0.6f, 0.9f, 1f)));
            if (p.dodgeCooldown > 0) parts.Add(Col($"Dodge {Secs(p.dodgeCooldown)}", new Color(0.7f, 0.7f, 0.8f)));
            if (parts.Count > 0) Shadowed(new Rect(x0, y0 - 58, hw + 200, 22), string.Join("   ", parts), new GUIStyle(small) { wordWrap = false }, Color.white);

            for (int i = 0; i < Player.HotbarSize; i++)
            {
                var r = new Rect(x0 + i * (slot + gap), y0, slot, slot);
                Rect(r, new Color(0, 0, 0, 0.75f));
                Frame(r, new Color(0.4f, 0.35f, 0.5f), 1);
                var id = p.hotbar[i];
                var def = SkillDatabase.Get(id);
                if (def != null)
                {
                    int rank = p.SkillRank(id);
                    Icon(new Rect(r.x + 3, r.y + 3, slot - 6, slot - 6), SpriteFactory.SkillIcon(def));
                    float cd = p.Cooldown(id);
                    float cost = SkillExecutor.Cost(p, def, Mathf.Max(1, rank), out bool life);
                    bool cantAfford = life ? p.life <= cost + 1 : p.mana < cost;
                    if (rank <= 0) Rect(r, new Color(0, 0, 0, 0.75f));
                    else if (cd > 0)
                    {
                        Rect(r, new Color(0, 0, 0, 0.6f));
                        Rect(new Rect(r.x, r.y, r.width, r.height * Mathf.Clamp01(cd / Mathf.Max(0.1f, SkillExecutor.CooldownFor(p, def)))), new Color(0, 0, 0, 0.35f));
                        GUI.Label(r, $"<b>{Secs(cd)}</b>", new GUIStyle(center) { fontSize = 18 });
                    }
                    else if (cantAfford) Rect(r, new Color(0.1f, 0.1f, 0.5f, 0.5f));
                    if (Hover(r)) tipText = $"<b>{def.name}</b>  (Rank {rank})\n<color=#aaaaaa>{def.school} - {def.TargetingLabel()}</color>\n\n{def.Describe(rank, p)}";
                    if (GUI.Button(r, GUIContent.none, GUIStyle.none)) W.PlayerUseSkill(i, p.pos + p.facing * 3f);
                }
                Shadowed(new Rect(r.x + 3, r.y + 1, 20, 18), (i + 1).ToString(), small, new Color(1f, 0.9f, 0.6f));
            }
            // Potions
            float px = x0 + hw + 14;
            var hp = new Rect(px, y0, slot, slot);
            var mp = new Rect(px + slot + gap, y0, slot, slot);
            Rect(hp, new Color(0, 0, 0, 0.75f)); Rect(mp, new Color(0, 0, 0, 0.75f));
            Icon(new Rect(hp.x + 6, hp.y + 6, slot - 12, slot - 12), SpriteFactory.Icon("potion", Color.white, new Color(0.9f, 0.15f, 0.2f)));
            Icon(new Rect(mp.x + 6, mp.y + 6, slot - 12, slot - 12), SpriteFactory.Icon("potion", Color.white, new Color(0.2f, 0.4f, 1f)));
            Shadowed(new Rect(hp.x + 3, hp.y + 1, 20, 18), "R", small, new Color(1f, 0.9f, 0.6f));
            Shadowed(new Rect(mp.x + 3, mp.y + 1, 20, 18), "F", small, new Color(1f, 0.9f, 0.6f));
            Shadowed(new Rect(hp.x, hp.yMax - 20, slot - 4, 18), p.healthPotions.ToString(), new GUIStyle(small) { alignment = TextAnchor.LowerRight }, Color.white);
            Shadowed(new Rect(mp.x, mp.yMax - 20, slot - 4, 18), p.manaPotions.ToString(), new GUIStyle(small) { alignment = TextAnchor.LowerRight }, Color.white);
            if (GUI.Button(hp, GUIContent.none, GUIStyle.none)) W.QuaffHealth();
            if (GUI.Button(mp, GUIContent.none, GUIStyle.none)) W.QuaffMana();
            if (Hover(hp)) tipText = "Health Potion (R)\nRestores 45% of maximum life and cures damage over time.";
            if (Hover(mp)) tipText = "Mana Potion (F)\nRestores 60% of maximum mana.";
        }

        void DrawPrompt()
        {
            if (PausesGame && panel != Panel.Pause && panel != Panel.Dialogue)
                Shadowed(new Rect(0, 100, VW, 24), "<b>PAUSED</b>  <size=12>(close the panel to resume)</size>", new GUIStyle(center) { fontSize = 16 }, new Color(1f, 0.85f, 0.5f));
            var hint = W.InteractHint();
            if (hint != null && !PausesGame)
                Shadowed(new Rect(0, VH - 175, VW, 24), hint, new GUIStyle(center) { fontSize = 16 }, new Color(1f, 0.9f, 0.6f));
        }

        void DrawMenuButtons()
        {
            var p = W.player;
            float bw = 120, bh = 32, x = VW - bw - 12, y = VH - (bh + 6) * 4 - 10;
            nextRects.Add(new Rect(x, y, bw, (bh + 6) * 4));
            if (GUI.Button(new Rect(x, y, bw, bh), $"Inventory (I){(p.backpack.Count >= Player.BackpackSize ? " <color=#ff6666>!</color>" : "")}", button)) Toggle(Panel.Inventory);
            if (GUI.Button(new Rect(x, y + bh + 6, bw, bh), $"Skills (K){(p.skillPoints > 0 ? $" <color=#ffdd44>+{p.skillPoints}</color>" : "")}", button)) Toggle(Panel.Skills);
            if (GUI.Button(new Rect(x, y + (bh + 6) * 2, bw, bh), $"Hero (P){(p.attrPoints > 0 ? $" <color=#ffdd44>+{p.attrPoints}</color>" : "")}", button)) Toggle(Panel.Character);
            if (GUI.Button(new Rect(x, y + (bh + 6) * 3, bw, bh), "Help (H)", button)) Toggle(Panel.Help);
        }

        // ------------------------------------------------------------------ inventory & shop

        static readonly EquipSlot[] slotOrder =
        {
            EquipSlot.Weapon, EquipSlot.Offhand, EquipSlot.Helm, EquipSlot.Chest, EquipSlot.Gloves,
            EquipSlot.Boots, EquipSlot.Belt, EquipSlot.Amulet, EquipSlot.Ring1, EquipSlot.Ring2,
        };

        void ItemSlot(Rect r, Item it, string emptyLabel)
        {
            Rect(r, new Color(0, 0, 0, 0.6f));
            Frame(r, it != null ? it.Color * 0.9f : new Color(0.3f, 0.3f, 0.35f), it != null && it.rarity >= Rarity.Rare ? 2 : 1);
            if (it != null) Icon(new Rect(r.x + 5, r.y + 5, r.width - 10, r.height - 10), SpriteFactory.ItemIcon(it));
            else if (emptyLabel != null) GUI.Label(r, $"<size=11><color=#666666>{emptyLabel}</color></size>", center);
        }

        void DrawInventory()
        {
            var p = W.player;
            var outer = new Rect(VW - 490, 130, 470, 600);
            var r = Box(outer, "Inventory");
            float s = 76, g = 9;
            for (int i = 0; i < slotOrder.Length; i++)
            {
                var slot = slotOrder[i];
                var sr = new Rect(r.x + (i % 5) * (s + g), r.y + (i / 5) * (s + 20), s, s);
                var it = p.Equipped(slot);
                ItemSlot(sr, it, slot.ToString());
                if (it != null && Hover(sr)) tipItem = it;
                if (it != null && GUI.Button(sr, GUIContent.none, GUIStyle.none)) p.Unequip(slot);
            }
            float by = r.y + 2 * (s + 20) + 6;
            GUI.Label(new Rect(r.x, by, r.width, 22), $"Backpack  {p.backpack.Count}/{Player.BackpackSize}", label);
            by += 24;
            float bs = 51, bg = 4;
            for (int i = 0; i < Player.BackpackSize; i++)
            {
                var br = new Rect(r.x + (i % 8) * (bs + bg), by + (i / 8) * (bs + bg), bs, bs);
                var it = i < p.backpack.Count ? p.backpack[i] : null;
                ItemSlot(br, it, null);
                if (it == null) continue;
                if (Hover(br)) { tipItem = it; tipCompare = p.Equipped(p.SlotFor(it)); }
                if (GUI.Button(br, GUIContent.none, GUIStyle.none))
                {
                    var e = Event.current;
                    if (e.shift) W.Salvage(it);
                    else if (e.control) W.DropFromBackpack(it);
                    else p.Equip(it);
                    break;
                }
            }
            float fy = by + 5 * (bs + bg) + 4;
            GUI.Label(new Rect(r.x, fy, r.width, 36), "<size=12><color=#999999>Click: equip / unequip    Shift+Click: salvage for gold    Ctrl+Click: drop</color></size>", small);
            if (GUI.Button(new Rect(r.x, fy + 30, 220, 28), "Salvage all Common/Magic", button))
                foreach (var it in p.backpack.Where(x => x.rarity <= Rarity.Magic).ToList()) W.Salvage(it);
        }

        void DrawShop()
        {
            var w = W; var p = w.player;
            var r = Box(new Rect(VW - 490 - 400, 130, 390, 600), "Wandering Merchant");
            float y = r.y;
            GUI.Label(new Rect(r.x, y, r.width, 22), $"<color=#ffd040>You have {p.gold} gold</color>", label);
            y += 28;
            if (GUI.Button(new Rect(r.x, y, 180, 30), $"Health Potion ({w.PotionPrice}g)", button)) w.BuyPotion(1);
            if (GUI.Button(new Rect(r.x + 186, y, 180, 30), $"Mana Potion ({w.PotionPrice}g)", button)) w.BuyPotion(2);
            y += 40;
            foreach (var it in w.shop.ToList())
            {
                var row = new Rect(r.x, y, r.width, 54);
                Rect(row, new Color(1, 1, 1, 0.04f));
                ItemSlot(new Rect(row.x + 2, row.y + 2, 50, 50), it, null);
                GUI.Label(new Rect(row.x + 60, row.y + 4, 210, 22), $"<b>{Col(it.name, it.Color)}</b>", new GUIStyle(small) { wordWrap = false, clipping = TextClipping.Clip });
                GUI.Label(new Rect(row.x + 60, row.y + 26, 210, 22), $"<color=#999999>{it.rarity} {it.baseDef.name}</color>", small);
                if (Hover(row)) { tipItem = it; tipCompare = p.Equipped(p.SlotFor(it)); }
                int price = w.ShopPrice(it);
                GUI.enabled = p.gold >= price;
                if (GUI.Button(new Rect(row.xMax - 96, row.y + 12, 92, 30), $"{price}g", button)) w.Buy(it);
                GUI.enabled = true;
                y += 58;
            }
            if (GUI.Button(new Rect(r.x, r.yMax - 32, 120, 30), "Leave", button)) { shopOpen = false; panel = Panel.None; }
        }

        // ------------------------------------------------------------------ skills

        void DrawSkills()
        {
            var p = W.player;
            var r = Box(new Rect((VW - 860) / 2, 60, 860, 720), "Skills");
            GUI.Label(new Rect(r.xMax - 260, r.y - 32, 260, 24), $"Skill points: <b><color=#ffdd44>{p.skillPoints}</color></b>", new GUIStyle(label) { alignment = TextAnchor.UpperRight });
            // School tabs
            float tw = (r.width - 5 * 6) / 6;
            for (int i = 0; i < 12; i++)
            {
                var sc = (School)i;
                var tr = new Rect(r.x + (i % 6) * (tw + 6), r.y + (i / 6) * 36, tw, 32);
                int pts = p.PointsInSchool(sc);
                Rect(tr, sc == skillTab ? SkillDatabase.SchoolColor(sc) * 0.55f : new Color(0.12f, 0.11f, 0.15f));
                Frame(tr, SkillDatabase.SchoolColor(sc) * (sc == skillTab ? 1f : 0.5f), 1);
                bool affinity = p.cls.affinities.Contains(sc);
                if (GUI.Button(tr, $"{(affinity ? "* " : "")}{sc}{(pts > 0 ? $" <size=11>({pts})</size>" : "")}", new GUIStyle(center) { fontSize = 14 })) skillTab = sc;
            }
            float y = r.y + 80;
            GUI.Label(new Rect(r.x, y, r.width, 22), $"<color=#999999>Each tier requires more points invested in the {skillTab} school. Skills marked with a hotbar number are equipped. (* = class affinity, +1 rank)</color>", small);
            y += 26;
            foreach (var s in SkillDatabase.ForSchool(skillTab).Where(s => !s.hidden).OrderBy(s => s.tier))
            {
                var row = new Rect(r.x, y, r.width, 70);
                Rect(row, new Color(1, 1, 1, Hover(row) ? 0.08f : 0.04f));
                Icon(new Rect(row.x + 6, row.y + 7, 56, 56), SpriteFactory.SkillIcon(s));
                int learned = p.Learned(s.id), eff = p.SkillRank(s.id);
                int bonus = eff > 0 ? eff - Mathf.Max(learned, p.GrantedRank(s.id)) : 0;
                string rankText = eff > 0 ? $"Rank {eff}" + (bonus > 0 ? $" <color=#66ddff>(+{bonus})</color>" : "") + (p.GrantedRank(s.id) > 0 ? " <color=#ffaa44>[item]</color>" : "") : "<color=#777777>Not learned</color>";
                GUI.Label(new Rect(row.x + 72, row.y + 6, 380, 22), $"<b>{s.name}</b>  <size=12><color=#aaaaaa>Tier {s.tier} - {s.TargetingLabel()}</color></size>", label);
                GUI.Label(new Rect(row.x + 72, row.y + 28, 380, 22), $"{rankText}   <size=12><color=#999999>{learned}/{SkillDef.MaxLearnedRank} points</color></size>", small);
                var reason = p.CannotLearnReason(s);
                if (reason != null && reason != "No skill points" && reason != "Max rank") GUI.Label(new Rect(row.x + 72, row.y + 48, 380, 20), $"<size=12><color=#cc7766>{reason}</color></size>", small);

                GUI.enabled = reason == null;
                if (GUI.Button(new Rect(row.x + 460, row.y + 18, 70, 34), "Learn", button)) p.LearnSkill(s);
                GUI.enabled = true;

                if (!s.passive && eff > 0)
                {
                    for (int k = 0; k < Player.HotbarSize; k++)
                    {
                        bool on = p.hotbar[k] == s.id;
                        var kr = new Rect(row.x + 546 + k * 32, row.y + 22, 28, 28);
                        if (GUI.Button(kr, on ? $"<b><color=#ffdd44>{k + 1}</color></b>" : $"<color=#777777>{k + 1}</color>", button))
                        {
                            if (on) p.hotbar[k] = null;
                            else
                            {
                                for (int j = 0; j < Player.HotbarSize; j++) if (p.hotbar[j] == s.id) p.hotbar[j] = null;
                                p.hotbar[k] = s.id;
                            }
                        }
                    }
                }
                if (Hover(row))
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"<b>{s.name}</b>  <color=#aaaaaa>{s.school} tier {s.tier}, requires level {s.RequiredLevel}</color>");
                    sb.AppendLine();
                    if (eff > 0) { sb.AppendLine($"<color=#ffdd88>Current (rank {eff}):</color>"); sb.AppendLine(s.Describe(eff, p)); sb.AppendLine(); }
                    sb.AppendLine($"<color=#88ddff>Next rank ({eff + 1}):</color>");
                    sb.Append(s.Describe(eff + 1, p));
                    tipText = sb.ToString();
                }
                y += 76;
            }
            var granted = p.granted.Keys.Select(SkillDatabase.Get).Where(d => d != null).ToList();
            if (granted.Count > 0)
            {
                GUI.Label(new Rect(r.x, r.yMax - 46, r.width, 22), "<color=#ffaa44>Granted by equipment:</color> " + string.Join(", ", granted.Select(d => $"{d.name} ({p.SkillRank(d.id)})")), small);
                GUI.Label(new Rect(r.x, r.yMax - 24, r.width, 22), "<size=12><color=#888888>Granted skills appear under their school and can be placed on the hotbar.</color></size>", small);
            }
        }

        // ------------------------------------------------------------------ character

        void DrawCharacter()
        {
            var p = W.player;
            var r = Box(new Rect(20, 140, 660, 660), $"{p.cls.name}  -  Level {p.level}");
            GUI.Label(new Rect(r.x, r.y, 300, 22), $"Attribute points: <b><color=#ffdd44>{p.attrPoints}</color></b>", label);
            float y = r.y + 28;
            var attrStats = new[] { StatType.Strength, StatType.Dexterity, StatType.Intelligence, StatType.Vitality, StatType.Spirit };
            for (int i = 0; i < 5; i++)
            {
                var row = new Rect(r.x, y, 320, 40);
                GUI.Label(new Rect(row.x, row.y, 150, 22), $"<b>{ClassDatabase.AttributeNames[i]}</b>  {Mathf.RoundToInt(p.Get(attrStats[i]))}", label);
                GUI.Label(new Rect(row.x, row.y + 18, 260, 20), $"<size=11><color=#888888>{ClassDatabase.AttributeHelp[i]}</color></size>", small);
                GUI.enabled = p.attrPoints > 0;
                if (GUI.Button(new Rect(row.x + 270, row.y + 4, 36, 28), "+", button))
                {
                    int n = Event.current.shift ? Mathf.Min(5, p.attrPoints) : 1;
                    p.allocated[i] += n; p.attrPoints -= n;
                    float lp = p.LifePct;
                    p.Recalc();
                    p.life = Mathf.Max(1, p.MaxLife * lp);
                }
                GUI.enabled = true;
                y += 44;
            }
            GUI.Label(new Rect(r.x, y, 320, 20), "<size=11><color=#888888>Shift+Click to add 5 points</color></size>", small);

            string Line(StatType s) => $"{StatInfo.Name(s)}: <b>{StatInfo.FormatValue(s, p.Get(s))}</b>";
            string Res(StatType s) { float v = p.Get(s); return $"{StatInfo.Name(s)}: <b>{Mathf.RoundToInt(v)}%</b>{(v > 75 ? " <color=#888888>(75 max)</color>" : "")}"; }

            var off = new StringBuilder();
            off.AppendLine("<b><color=#ffaa77>Offense</color></b>");
            off.AppendLine($"Weapon damage: <b>{Mathf.RoundToInt(p.weaponMin)}-{Mathf.RoundToInt(p.weaponMax)}</b> {Combat.Convert(p, p.attackElement)}{(p.attackRange > 1 ? $" (range {p.attackRange})" : "")}");
            foreach (var s in new[] { StatType.FlatAttackDamage, StatType.AttackDamage, StatType.SpellDamage, StatType.ActionSpeed, StatType.CritChance, StatType.CritDamage, StatType.Accuracy, StatType.DoubleStrike, StatType.StatusChance, StatType.DotDamage, StatType.LifeSteal, StatType.CooldownReduction, StatType.ManaCostReduction, StatType.ExtraProjectiles, StatType.AreaRadius, StatType.SummonDamage, StatType.SummonLife })
                if (Mathf.Abs(p.Get(s)) > 0.01f || s == StatType.AttackDamage || s == StatType.SpellDamage || s == StatType.CritChance) off.AppendLine(Line(s));
            foreach (var s in new[] { StatType.PhysicalDamage, StatType.FireDamage, StatType.ColdDamage, StatType.LightningDamage, StatType.PoisonDamage, StatType.HolyDamage, StatType.ShadowDamage })
                if (Mathf.Abs(p.Get(s)) > 0.01f) off.AppendLine(Line(s));

            var def = new StringBuilder();
            def.AppendLine("<b><color=#77bbff>Defense</color></b>");
            def.AppendLine($"Life: <b>{Mathf.CeilToInt(p.life)}/{Mathf.CeilToInt(p.MaxLife)}</b>   Mana: <b>{Mathf.FloorToInt(p.mana)}/{Mathf.CeilToInt(p.MaxMana)}</b>");
            def.AppendLine(Line(StatType.LifeRegen) + "   " + Line(StatType.ManaRegen));
            float armor = p.Get(StatType.Armor);
            def.AppendLine($"Armor: <b>{Mathf.RoundToInt(armor)}</b> <color=#888888>({Mathf.RoundToInt(100 * Mathf.Min(0.75f, armor / (armor + 12f * W.MonsterLevel + 40f)))}% vs depth)</color>");
            def.AppendLine(Line(StatType.Evasion));
            def.AppendLine(Line(StatType.BlockChance));
            foreach (var s in new[] { StatType.FireRes, StatType.ColdRes, StatType.LightningRes, StatType.PoisonRes, StatType.HolyRes, StatType.ShadowRes }) def.AppendLine(Res(s));
            if (p.Get(StatType.DamageReduction) > 0) def.AppendLine(Line(StatType.DamageReduction));
            if (p.Get(StatType.Thorns) > 0) def.AppendLine(Line(StatType.Thorns));
            def.AppendLine();
            def.AppendLine("<b><color=#ddcc66>Utility</color></b>");
            foreach (var s in new[] { StatType.MoveSpeed, StatType.MagicFind, StatType.GoldFind, StatType.LightRadius, StatType.XpBonus, StatType.LifeOnKill, StatType.ManaOnKill })
                if (Mathf.Abs(p.Get(s)) > 0.01f) def.AppendLine(Line(s));

            GUI.Label(new Rect(r.x + 330, r.y, 300, r.height - 240), def.ToString(), small);
            GUI.Label(new Rect(r.x, y + 26, 320, r.height - (y - r.y) - 26), off.ToString(), small);

            var special = new StringBuilder();
            foreach (var k in p.keystones) special.AppendLine(Col("KEYSTONE  " + EnumUtil.KeystoneDesc(k), new Color(1f, 0.6f, 0.3f)));
            foreach (var pr in p.procs) special.AppendLine(Col($"{pr.chance:0}% {SkillDef.TriggerText(pr.trigger)}: {SkillDatabase.Get(pr.skillId)?.name} (rank {pr.rank})", new Color(0.5f, 0.85f, 1f)));
            special.Insert(0, Story.QuestSummary(GM.meta) + "\n");
            GUI.Label(new Rect(r.x + 330, r.yMax - 230, 320, 230), special.ToString(), small);
        }

        // ------------------------------------------------------------------ help / pause

        void DrawHelp()
        {
            var r = Box(new Rect((VW - 760) / 2, 70, 760, 700), "How to Play");
            string text =
                "<b>Story</b>: You are an Archdemon sworn to Princess Lysandra of House Vaelthorn. The human army's Radiant Crusade scattered her family's six Royal Artifacts into the Abyss. Each biome's guardian (every 5th depth) holds one. Recover them all to break the human army. Every artifact permanently empowers you.\n" +
                "<b>The castle</b>: every run begins in Castle Vaelthorn. Speak with the princess, trade with the quartermaster, then step through the Abyssal Gate. Slain guardians open a portal home; the gate then returns you to the next depth.\n\n" +
                "<b>Move</b>: WASD / Arrows.    <b>Dodge roll</b>: Space (brief invulnerability).\n" +
                "<b>Attack</b>: hold Left Mouse toward the cursor. Melee hits the enemy you face; bows and wands fire projectiles.\n" +
                "<b>Skills</b>: hold 1-8 to cast at the cursor (or the nearest enemy). Right Mouse casts slot 1.\n" +
                "<b>Interact</b>: E (chests, shrines, fountains, merchants, stairs) or click them.    <b>Potions</b>: R health, F mana.\n" +
                "<b>Panels</b>: I inventory, K skills, P hero sheet, H help, Esc menu. The game pauses while a panel is open.\n" +
                "Enemies flash red before a melee swing - step or roll away to make it miss. Projectiles can be dodged.\n\n" +
                "<b>Building a hero</b>\n" +
                "- Every level grants 5 attribute points and 1 skill point. Any class can learn any of the 12 skill schools.\n" +
                "- Higher tiers in a school need points invested in that school first.\n" +
                "- Passives add stats, procs (\"chance on hit to cast...\") and Keystones that change the rules.\n\n" +
                "<b>Loot</b>\n" +
                "- <color=#6699ff>Magic</color>, <color=#ffe64d>Rare</color>, <color=#bf66ff>Epic</color> and <color=#ff8c1a>Legendary</color> items roll random affixes.\n" +
                "- Rare and better items can add skill ranks, <b>grant entirely new skills</b>, or cast skills on hit, crit, kill or when struck.\n" +
                "- Legendaries carry Keystones and unique powers. Collect them all in the Codex.\n\n" +
                "<b>Between runs</b>\n" +
                "- Death ends the run but grants Soul Essence. Spend it at the Soul Altar for permanent upgrades and new classes.\n" +
                "- Reach depth 11 to unlock the next Corruption level: harder monsters, better loot.\n" +
                "- Daily Descent: everyone gets the same seed each day.\n\n" +
                "<b>Map legend</b>: chests (loot), blue shrines (temporary buffs), fountains (full restore), merchants (shop), hidden spike traps, lava burns.";
            GUI.Label(r, text, small);
        }

        void DrawPause()
        {
            Rect(new Rect(0, 0, VW, VH), new Color(0, 0, 0, 0.5f));
            var r = Box(new Rect((VW - 320) / 2, 260, 320, 280), "Paused");
            float y = r.y + 6;
            if (GUI.Button(new Rect(r.x, y, r.width, 40), "Resume", button)) panel = Panel.None;
            y += 50;
            if (GUI.Button(new Rect(r.x, y, r.width, 40), "Help", button)) panel = Panel.Help;
            y += 50;
            if (GUI.Button(new Rect(r.x, y, r.width, 40), "Abandon Run (keep essence)", button)) { panel = Panel.None; GM.EndRun(); }
            y += 50;
            if (GUI.Button(new Rect(r.x, y, r.width, 40), "Quit Game", button)) Application.Quit();
        }

        // ------------------------------------------------------------------ tooltip

        void DrawTooltip()
        {
            var mouse = Event.current.mousePosition;
            if (tipItem != null)
            {
                string a = tipItem.Tooltip();
                float wdt = 330;
                float ha = tip.CalcHeight(new GUIContent(a), wdt);
                float x = mouse.x - wdt - 20, y = Mathf.Clamp(mouse.y - ha / 2, 4, VH - ha - 4);
                if (tipCompare != null && tipCompare != tipItem)
                {
                    string b = "<color=#888888>EQUIPPED</color>\n" + tipCompare.Tooltip();
                    float hb = tip.CalcHeight(new GUIContent(b), wdt);
                    float xb = x - wdt - 8;
                    if (xb < 4) { x = mouse.x + 20; xb = x + wdt + 8; }
                    GUI.Label(new Rect(xb, Mathf.Clamp(mouse.y - hb / 2, 4, VH - hb - 4), wdt, hb), b, tip);
                }
                if (x < 4) x = mouse.x + 20;
                GUI.Label(new Rect(x, y, wdt, ha), a, tip);
                return;
            }
            if (tipText != null)
            {
                float wdt = 380;
                float h = tip.CalcHeight(new GUIContent(tipText), wdt);
                float x = mouse.x + 18, y = mouse.y - h - 10;
                if (x + wdt > VW) x = mouse.x - wdt - 18;
                if (y < 4) y = mouse.y + 20;
                GUI.Label(new Rect(x, y, wdt, h), tipText, tip);
            }
        }
    }
}
