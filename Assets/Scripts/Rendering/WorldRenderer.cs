using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Depthbound
{
    public class FloatingText
    {
        public Vector3 pos;
        public string text;
        public Color color;
        public float t, size;
    }

    public class WorldRenderer : MonoBehaviour
    {
        class ActorView
        {
            public GameObject go;
            public SpriteRenderer sr, aura, barBg, barFill;
            public Vector3 pos;
            public float fade = -1;
        }

        class FxObj
        {
            public SpriteRenderer sr;
            public Vector3 a, b;
            public float t, dur, alpha;
            public bool moves;
        }

        public readonly List<FloatingText> texts = new List<FloatingText>();
        public float zoom = 8.5f;
        public Vector2Int MouseTile { get; private set; }

        World world;
        int builtVersion = -1, shadedTurn = -1;
        SpriteRenderer[,] tiles;
        Tile[,] tileState;
        Transform tileRoot, actorRoot, itemRoot, fxRoot;
        readonly Dictionary<Actor, ActorView> views = new Dictionary<Actor, ActorView>();
        readonly List<SpriteRenderer> itemPool = new List<SpriteRenderer>();
        readonly List<FxObj> fx = new List<FxObj>();
        Camera cam;
        SpriteRenderer hover;
        Sprite ringSprite, frameSprite;

        public void Bind(World w)
        {
            world = w;
            builtVersion = -1;
            Clear();
        }

        public void Clear()
        {
            foreach (var v in views.Values) if (v.go) Destroy(v.go);
            views.Clear();
            foreach (var f in fx) if (f.sr) Destroy(f.sr.gameObject);
            fx.Clear();
            texts.Clear();
            Fx.Pending.Clear();
            if (tileRoot) Destroy(tileRoot.gameObject);
            tileRoot = null;
            foreach (var s in itemPool) if (s) s.gameObject.SetActive(false);
            if (hover) hover.enabled = false;
        }

        void Awake()
        {
            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            cam.orthographic = true;
            cam.orthographicSize = zoom;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
            cam.transform.position = new Vector3(0, 0, -10);

            actorRoot = new GameObject("Actors").transform;
            itemRoot = new GameObject("Items").transform;
            fxRoot = new GameObject("Fx").transform;
            ringSprite = MakeRing();
            frameSprite = MakeFrame();
            hover = NewSprite("Hover", null, frameSprite, 25);
            hover.color = new Color(1f, 1f, 1f, 0.5f);
            hover.enabled = false;
        }

        static Sprite MakeRing()
        {
            var t = SpriteFactory.NewTex(16, 16);
            for (int x = 0; x < 16; x++)
                for (int y = 0; y < 16; y++)
                {
                    float dx = (x - 7.5f) / 7.5f, dy = (y - 7.5f) / 4.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d <= 1f) t.SetPixel(x, y, new Color(1, 1, 1, d > 0.7f ? 1f : 0.35f));
                }
            return SpriteFactory.ToSprite(t);
        }

        static Sprite MakeFrame()
        {
            var t = SpriteFactory.NewTex(16, 16);
            for (int i = 0; i < 16; i++)
            {
                t.SetPixel(i, 0, Color.white); t.SetPixel(i, 15, Color.white);
                t.SetPixel(0, i, Color.white); t.SetPixel(15, i, Color.white);
            }
            return SpriteFactory.ToSprite(t);
        }

        SpriteRenderer NewSprite(string name, Transform parent, Sprite s, int order)
        {
            var go = new GameObject(name);
            if (parent) go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s;
            sr.sortingOrder = order;
            return sr;
        }

        void Update()
        {
            if (world == null || world.map == null) return;
            if (builtVersion != world.floorVersion) Rebuild();
            if (shadedTurn != world.turn || Time.frameCount % 20 == 0) ShadeTiles();
            UpdateActors();
            UpdateItems();
            ProcessFxEvents();
            UpdateFx();
            UpdateCamera();
            UpdateHover();
        }

        void Rebuild()
        {
            Clear();
            builtVersion = world.floorVersion;
            var map = world.map;
            tileRoot = new GameObject("Tiles").transform;
            tiles = new SpriteRenderer[map.w, map.h];
            tileState = new Tile[map.w, map.h];
            int bi = Biomes.IndexForDepth(world.depth);
            for (int x = 0; x < map.w; x++)
                for (int y = 0; y < map.h; y++)
                {
                    var sr = NewSprite("t", tileRoot, null, 0);
                    sr.transform.position = new Vector3(x, y, 0);
                    tiles[x, y] = sr;
                    SetTileSprite(x, y, bi);
                }
            shadedTurn = -1;
            var p = world.player;
            cam.transform.position = new Vector3(p.x, p.y, -10);
        }

        void SetTileSprite(int x, int y, int bi)
        {
            var t = world.map.tiles[x, y];
            var shown = t == Tile.Trap ? Tile.Floor : t; // hidden traps look like floor
            tileState[x, y] = t;
            tiles[x, y].sprite = SpriteFactory.TileSprite(bi, shown, world.map.variant[x, y] % 4);
        }

        void ShadeTiles()
        {
            shadedTurn = world.turn;
            var map = world.map;
            int bi = Biomes.IndexForDepth(world.depth);
            var p = world.player;
            float radius = Mathf.Max(3, p.Get(StatType.LightRadius));
            for (int x = 0; x < map.w; x++)
                for (int y = 0; y < map.h; y++)
                {
                    var sr = tiles[x, y];
                    if (map.tiles[x, y] != tileState[x, y]) SetTileSprite(x, y, bi);
                    if (map.visible[x, y])
                    {
                        float d = Mathf.Sqrt((x - p.x) * (x - p.x) + (y - p.y) * (y - p.y));
                        float l = Mathf.Lerp(1f, 0.55f, d / (radius + 1));
                        if (map.tiles[x, y] == Tile.Lava) l = 1f;
                        sr.enabled = true;
                        sr.color = new Color(l, l, l);
                    }
                    else if (map.explored[x, y])
                    {
                        sr.enabled = true;
                        sr.color = new Color(0.28f, 0.28f, 0.36f);
                    }
                    else sr.enabled = false;
                }
        }

        Color AuraColor(Actor a)
        {
            if (a == world.player) return new Color(1f, 1f, 1f, 0.25f);
            if (a.faction == Faction.Player) return new Color(0.3f, 1f, 0.5f, 0.45f);
            var m = (Monster)a;
            switch (m.rank)
            {
                case 1: return new Color(0.35f, 0.55f, 1f, 0.7f);
                case 2: return new Color(1f, 0.8f, 0.2f, 0.75f);
                case 3: return new Color(1f, 0.2f, 0.15f, 0.8f);
                default: return new Color(0, 0, 0, 0.35f);
            }
        }

        void UpdateActors()
        {
            var map = world.map;
            foreach (var a in world.actors)
            {
                if (a.dead) continue;
                if (!views.TryGetValue(a, out var v))
                {
                    v = new ActorView();
                    v.go = new GameObject(a.name);
                    v.go.transform.SetParent(actorRoot, false);
                    v.aura = NewSprite("aura", v.go.transform, ringSprite, 2);
                    v.aura.transform.localPosition = new Vector3(0, -0.38f, 0);
                    v.aura.transform.localScale = new Vector3(0.9f, 0.9f, 1);
                    v.sr = NewSprite("body", v.go.transform, a is Player pl ? SpriteFactory.Hero(pl.cls) : SpriteFactory.Creature(((Monster)a).def), a == world.player ? 6 : 5);
                    v.barBg = NewSprite("barbg", v.go.transform, SpriteFactory.White, 10);
                    v.barFill = NewSprite("bar", v.go.transform, SpriteFactory.White, 11);
                    v.barBg.color = new Color(0, 0, 0, 0.8f);
                    v.pos = new Vector3(a.x, a.y, 0);
                    views[a] = v;
                }
                var target = new Vector3(a.x, a.y, 0);
                v.pos = Vector3.Lerp(v.pos, target, 1f - Mathf.Exp(-Time.deltaTime * 22f));
                if ((v.pos - target).sqrMagnitude > 25) v.pos = target;
                v.go.transform.position = v.pos;
                v.sr.transform.localScale = Vector3.one * a.scale;
                v.sr.transform.localPosition = new Vector3(0, (a.scale - 1f) * 0.4f, 0);
                bool vis = a == world.player || map.visible[a.x, a.y];
                v.go.SetActive(vis);
                if (!vis) continue;

                v.aura.color = AuraColor(a);
                Color tint = Color.white;
                if (a.Has(StatusType.Freeze)) tint = new Color(0.55f, 0.8f, 1f);
                else if (a.Has(StatusType.Burn)) tint = Color.Lerp(Color.white, new Color(1f, 0.55f, 0.3f), 0.5f + 0.5f * Mathf.Sin(Time.time * 12f));
                else if (a.Has(StatusType.Poison)) tint = new Color(0.7f, 1f, 0.6f);
                else if (a.Has(StatusType.Chill)) tint = new Color(0.75f, 0.9f, 1f);
                else if (a.Has(StatusType.Stun)) tint = new Color(1f, 1f, 0.6f);
                v.sr.color = tint;

                bool showBar = a != world.player && (a.life < a.MaxLife || (a is Monster mm && mm.rank > 0) || a.faction == Faction.Player);
                v.barBg.enabled = v.barFill.enabled = showBar;
                if (showBar)
                {
                    float top = 0.55f + (a.scale - 1f) * 0.8f;
                    float pct = Mathf.Clamp01(a.life / a.MaxLife);
                    v.barBg.transform.localPosition = new Vector3(0, top, 0);
                    v.barBg.transform.localScale = new Vector3(0.84f, 0.1f, 1);
                    v.barFill.transform.localScale = new Vector3(0.8f * pct, 0.06f, 1);
                    v.barFill.transform.localPosition = new Vector3(-0.4f + 0.4f * pct, top, 0);
                    v.barFill.color = a.faction == Faction.Player ? new Color(0.3f, 0.9f, 0.4f) : (a.shield > 0 ? new Color(0.8f, 0.8f, 1f) : new Color(0.9f, 0.15f, 0.15f));
                }
            }
            // Remove views of dead / departed actors with a short fade.
            var gone = views.Where(kv => kv.Key.dead || !world.actors.Contains(kv.Key)).ToList();
            foreach (var kv in gone)
            {
                var v = kv.Value;
                if (v.fade < 0) v.fade = 0.25f;
                v.fade -= Time.deltaTime;
                if (v.sr) v.sr.color = new Color(1f, 0.3f, 0.3f, Mathf.Clamp01(v.fade * 4));
                if (v.barBg) v.barBg.enabled = false;
                if (v.barFill) v.barFill.enabled = false;
                if (v.fade <= 0) { Destroy(v.go); views.Remove(kv.Key); }
            }
        }

        void UpdateItems()
        {
            var map = world.map;
            int used = 0;
            foreach (var g in world.ground)
            {
                if (!map.explored[g.x, g.y]) continue;
                if (used >= itemPool.Count)
                {
                    var sr = NewSprite("item", itemRoot, null, 1);
                    sr.transform.localScale = Vector3.one * 0.7f;
                    itemPool.Add(sr);
                }
                var r = itemPool[used++];
                r.gameObject.SetActive(true);
                r.transform.position = new Vector3(g.x, g.y, 0);
                if (g.gold > 0) r.sprite = SpriteFactory.Icon("gold", new Color(1f, 0.95f, 0.6f));
                else if (g.potion > 0) r.sprite = SpriteFactory.Icon("potion", Color.white, g.potion == 1 ? new Color(0.9f, 0.15f, 0.2f) : new Color(0.2f, 0.4f, 1f));
                else r.sprite = SpriteFactory.ItemIcon(g.item);
                float bob = g.item != null && g.item.rarity >= Rarity.Epic ? 0.06f * Mathf.Sin(Time.time * 4f + g.x) : 0;
                r.transform.position += new Vector3(0, bob, 0);
                float l = map.visible[g.x, g.y] ? 1f : 0.4f;
                r.color = new Color(l, l, l);
            }
            for (int i = used; i < itemPool.Count; i++) itemPool[i].gameObject.SetActive(false);
        }

        void ProcessFxEvents()
        {
            var map = world.map;
            foreach (var e in Fx.Pending)
            {
                switch (e.kind)
                {
                    case FxKind.Projectile:
                    {
                        if (!map.visible[e.a.x, e.a.y] && !map.visible[e.b.x, e.b.y]) break;
                        float dist = Vector2Int.Distance(e.a, e.b);
                        SpawnFx(new Vector3(e.a.x, e.a.y), new Vector3(e.b.x, e.b.y), e.color, 0.3f, 0.05f + dist * 0.025f, 1f, true);
                        break;
                    }
                    case FxKind.Burst:
                        for (int dx = -e.radius; dx <= e.radius; dx++)
                            for (int dy = -e.radius; dy <= e.radius; dy++)
                            {
                                int x = e.a.x + dx, y = e.a.y + dy;
                                if (!map.InBounds(x, y) || !map.visible[x, y] || !map.Transparent(x, y)) continue;
                                SpawnFx(new Vector3(x, y), new Vector3(x, y), e.color, 0.95f, 0.35f, 0.5f, false);
                            }
                        break;
                    case FxKind.Line:
                        foreach (var p in DungeonMap.Line(e.a, e.b))
                            if (map.InBounds(p.x, p.y) && map.visible[p.x, p.y]) SpawnFx(new Vector3(p.x, p.y), new Vector3(p.x, p.y), e.color, 0.35f, 0.3f, 0.9f, false);
                        break;
                    case FxKind.Flash:
                        if (map.InBounds(e.a.x, e.a.y) && map.visible[e.a.x, e.a.y]) SpawnFx(new Vector3(e.a.x, e.a.y), new Vector3(e.a.x, e.a.y), e.color, 0.9f, 0.15f, 0.55f, false);
                        break;
                    case FxKind.Text:
                        if (map.InBounds(e.a.x, e.a.y) && map.visible[e.a.x, e.a.y])
                            texts.Add(new FloatingText { pos = new Vector3(e.a.x + Random.Range(-0.25f, 0.25f), e.a.y + 0.3f), text = e.text, color = e.color, size = e.size });
                        break;
                }
            }
            Fx.Pending.Clear();
        }

        void SpawnFx(Vector3 a, Vector3 b, Color c, float scale, float dur, float alpha, bool moves)
        {
            var sr = NewSprite("fx", fxRoot, SpriteFactory.White, 20);
            sr.transform.position = a;
            sr.transform.localScale = Vector3.one * scale;
            c.a = alpha;
            sr.color = c;
            fx.Add(new FxObj { sr = sr, a = a, b = b, dur = dur, alpha = alpha, moves = moves });
        }

        void UpdateFx()
        {
            for (int i = fx.Count - 1; i >= 0; i--)
            {
                var f = fx[i];
                f.t += Time.deltaTime;
                float k = Mathf.Clamp01(f.t / f.dur);
                if (f.moves) f.sr.transform.position = Vector3.Lerp(f.a, f.b, k);
                else
                {
                    var c = f.sr.color; c.a = f.alpha * (1 - k); f.sr.color = c;
                }
                if (f.t >= f.dur + (f.moves ? 0.05f : 0)) { Destroy(f.sr.gameObject); fx.RemoveAt(i); }
            }
            for (int i = texts.Count - 1; i >= 0; i--)
            {
                texts[i].t += Time.deltaTime;
                texts[i].pos += new Vector3(0, Time.deltaTime * 0.9f, 0);
                if (texts[i].t > 0.95f) texts.RemoveAt(i);
            }
        }

        void UpdateCamera()
        {
            var ui = GameManager.Instance != null ? GameManager.Instance.ui : null;
            if (ui == null || !ui.MouseOverUI())
            {
                float scroll = Input.mouseScrollDelta.y;
                if (Mathf.Abs(scroll) > 0.01f) zoom = Mathf.Clamp(zoom - scroll * 0.8f, 4.5f, 16f);
            }
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, zoom, 1f - Mathf.Exp(-Time.deltaTime * 12f));
            Vector3 focus = views.TryGetValue(world.player, out var v) ? v.pos : new Vector3(world.player.x, world.player.y);
            var target = new Vector3(focus.x, focus.y, -10);
            cam.transform.position = Vector3.Lerp(cam.transform.position, target, 1f - Mathf.Exp(-Time.deltaTime * 10f));
        }

        void UpdateHover()
        {
            var wp = cam.ScreenToWorldPoint(Input.mousePosition);
            MouseTile = new Vector2Int(Mathf.RoundToInt(wp.x), Mathf.RoundToInt(wp.y));
            var map = world.map;
            bool show = map.InBounds(MouseTile.x, MouseTile.y) && map.explored[MouseTile.x, MouseTile.y] && !(GameManager.Instance.ui?.MouseOverUI() ?? false);
            hover.enabled = show;
            if (show)
            {
                hover.transform.position = new Vector3(MouseTile.x, MouseTile.y, 0);
                var a = world.ActorAt(MouseTile);
                hover.color = a != null && a.IsHostileTo(world.player) && map.visible[a.x, a.y] ? new Color(1f, 0.3f, 0.3f, 0.8f) : new Color(1f, 1f, 1f, 0.35f);
            }
        }

        public Vector2 WorldToGui(Vector3 world, float guiScale)
        {
            var sp = cam.WorldToScreenPoint(world);
            return new Vector2(sp.x / guiScale, (Screen.height - sp.y) / guiScale);
        }
    }
}
