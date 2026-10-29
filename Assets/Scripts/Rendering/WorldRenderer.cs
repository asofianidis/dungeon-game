using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Depthbound
{
    public class FloatingText
    {
        public Vector3 pos;
        public string text;
        public Color color;
        public float t, size;
    }

    /// <summary>
    /// 3D presentation of the simulation. The game logic lives on a 2D plane (x, y); here world (x, y)
    /// maps to Unity (x, 0, y). The dungeon is one textured mesh with fog of war in its vertex colors,
    /// and characters/props are the Blender-built models from Resources/Models.
    /// </summary>
    public class WorldRenderer : MonoBehaviour
    {
        const float WallHeight = 1.0f;
        const float CameraPitch = 56f;

        class ActorView
        {
            public GameObject root, model;
            public ModelRig rig;
            public CharacterAnimator anim;
            public int lastSerial;
            public Renderer[] renderers;
            public SpriteRenderer ring;
            public float lastLife, flash, attack, windup, move, yaw, lastLock, death = -1;
            public Vector2 lastPos;
        }

        class PropView
        {
            public Tile tile;
            public GameObject go;
            public Renderer[] renderers;
            public ModelRig rig;
            public CharacterAnimator anim;
            public Light light;
        }

        class FxObj
        {
            public SpriteRenderer sr;
            public Vector3 a, b;
            public float t, dur, alpha, grow;
            public bool moves, billboard;
        }

        public readonly List<FloatingText> texts = new List<FloatingText>();
        public float zoom = 9f;
        public Vector2Int MouseTile { get; private set; }
        public Vector2 MouseWorld { get; private set; }

        World world;
        int builtVersion = -1, shadedVersion = -1;
        Camera cam;
        Light sun, torch;
        Transform floorRoot, actorRoot, fxRoot, itemRoot;

        Mesh mapMesh;
        Color32[] mapColors;
        int[,] vStart, vCount;
        Tile[,] tileState;
        Texture2D atlas;
        readonly Dictionary<(int kind, int variant), Rect> atlasUV = new Dictionary<(int, int), Rect>();
        Material mapMat, lavaMat;

        readonly Dictionary<Vector2Int, PropView> props = new Dictionary<Vector2Int, PropView>();
        readonly Dictionary<Actor, ActorView> views = new Dictionary<Actor, ActorView>();
        readonly Dictionary<Projectile, (SpriteRenderer sr, Light light)> projViews = new Dictionary<Projectile, (SpriteRenderer, Light)>();
        readonly Dictionary<GroundItem, GameObject> artifactViews = new Dictionary<GroundItem, GameObject>();
        readonly List<SpriteRenderer> itemPool = new List<SpriteRenderer>();
        readonly List<FxObj> fx = new List<FxObj>();
        SpriteRenderer hover;
        Sprite ringSprite, dotSprite, discSprite, frameSprite;
        MaterialPropertyBlock mpb;

        // ------------------------------------------------------------------ setup

        public void Bind(World w)
        {
            world = w;
            builtVersion = -1;
            Clear();
        }

        public void Clear()
        {
            foreach (var v in views.Values) if (v.root) Destroy(v.root);
            views.Clear();
            foreach (var p in projViews.Values) { if (p.sr) Destroy(p.sr.gameObject); }
            projViews.Clear();
            foreach (var a in artifactViews.Values) if (a) Destroy(a);
            artifactViews.Clear();
            foreach (var f in fx) if (f.sr) Destroy(f.sr.gameObject);
            fx.Clear();
            texts.Clear();
            Fx.Pending.Clear();
            props.Clear();
            if (floorRoot) Destroy(floorRoot.gameObject);
            floorRoot = null;
            foreach (var s in itemPool) if (s) s.gameObject.SetActive(false);
            if (hover) hover.enabled = false;
        }

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            cam.orthographic = false;
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 120f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.01f, 0.01f, 0.015f);

            var sunGo = new GameObject("Moonlight");
            sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(62f, -35f, 0);
            sun.intensity = 0.35f;
            sun.color = new Color(0.65f, 0.7f, 1f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;

            var torchGo = new GameObject("Torch");
            torch = torchGo.AddComponent<Light>();
            torch.type = LightType.Point;
            torch.color = new Color(1f, 0.78f, 0.55f);
            torch.intensity = 1.7f;
            torch.shadows = LightShadows.None;

            // Shadow distance and per-object light limits live on the URP asset (Assets/Settings).
            RenderSettings.ambientMode = AmbientMode.Flat;
            SetAmbient(new Color(0.12f, 0.1f, 0.14f));

            actorRoot = new GameObject("Actors").transform;
            itemRoot = new GameObject("Items").transform;
            fxRoot = new GameObject("Fx").transform;
            ringSprite = MakeRing();
            dotSprite = MakeDisc(8, 0.55f, 1f);
            discSprite = MakeDisc(32, 0.35f, 0.9f);
            frameSprite = MakeFrame();
            hover = NewSprite("Hover", null, frameSprite, 25);
            hover.transform.rotation = Quaternion.Euler(90, 0, 0);
            hover.enabled = false;
        }

        static Sprite MakeRing()
        {
            var t = SpriteFactory.NewTex(32, 32);
            for (int x = 0; x < 32; x++)
                for (int y = 0; y < 32; y++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f)) / 16f;
                    if (d <= 1f && d > 0.72f) t.SetPixel(x, y, new Color(1, 1, 1, 1));
                    else if (d <= 0.72f) t.SetPixel(x, y, new Color(1, 1, 1, 0.18f));
                }
            return SpriteFactory.ToSprite(t);
        }

        static Sprite MakeDisc(int size, float core, float edgeAlpha)
        {
            var t = SpriteFactory.NewTex(size, size);
            float c = (size - 1) / 2f;
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c)) / (size / 2f);
                    if (d > 1f) continue;
                    float a = d < core ? 1f : Mathf.Lerp(edgeAlpha, 0f, (d - core) / (1f - core));
                    t.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            t.filterMode = FilterMode.Bilinear;
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
            sr.shadowCastingMode = ShadowCastingMode.Off;
            return sr;
        }

        static Vector3 W3(Vector2 p, float h = 0) => new Vector3(p.x, h, p.y);

        // ------------------------------------------------------------------ frame

        void Update()
        {
            PortraitStudio.Tick();
            if (world == null || world.map == null) return;
            if (builtVersion != world.floorVersion) Rebuild();
            if (TilesChanged()) RebuildMapMesh();
            if (shadedVersion != world.fovVersion) Shade();
            UpdateCamera();
            UpdateLights();
            UpdateProps();
            UpdateActors();
            UpdateProjectiles();
            UpdateItems();
            ProcessFxEvents();
            UpdateFx();
            UpdateHover();
        }

        void Rebuild()
        {
            Clear();
            builtVersion = world.floorVersion;
            floorRoot = new GameObject("Floor").transform;
            BuildAtlas();
            RebuildMapMesh();
            var p = world.player;
            cam.transform.position = CameraPosFor(W3(p.pos));
            var biome = world.map.biome;
            SetAmbient(world.InHub
                ? new Color(0.24f, 0.13f, 0.18f)
                : Color.Lerp(new Color(0.16f, 0.14f, 0.19f), biome.wallA, 0.3f));
            sun.color = world.InHub ? new Color(1f, 0.6f, 0.7f) : Color.Lerp(new Color(0.6f, 0.65f, 1f), biome.wallA, 0.3f);
            sun.intensity = world.InHub ? 0.4f : 0.5f;
        }

        /// <summary>Our shaders read a flat ambient color directly rather than the baked ambient probe.</summary>
        static void SetAmbient(Color c)
        {
            RenderSettings.ambientLight = c;
            Shader.SetGlobalColor("_DepthboundAmbient", c);
        }

        // ------------------------------------------------------------------ dungeon mesh

        enum Kind { Floor, Wall, WallTop, Water, Lava, Ice, Carpet, COUNT }

        void BuildAtlas()
        {
            int bi = Biomes.IndexForDepth(world.depth);
            const int cell = 18, cols = 6, size = 128;
            if (atlas == null) atlas = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            atlasUV.Clear();
            int i = 0;
            for (int k = 0; k < (int)Kind.COUNT; k++)
                for (int v = 0; v < 4; v++, i++)
                {
                    Tile t = k == (int)Kind.Wall || k == (int)Kind.WallTop ? Tile.Wall : k == (int)Kind.Water ? Tile.Water :
                             k == (int)Kind.Lava ? Tile.Lava : k == (int)Kind.Ice ? Tile.Ice : k == (int)Kind.Carpet ? Tile.Carpet : Tile.Floor;
                    var src = SpriteFactory.TileSprite(bi, t, v).texture;
                    int cx = (i % cols) * cell, cy = (i / cols) * cell;
                    for (int x = -1; x <= 16; x++)
                        for (int y = -1; y <= 16; y++)
                        {
                            var c = src.GetPixel(Mathf.Clamp(x, 0, 15), Mathf.Clamp(y, 0, 15));
                            if (k == (int)Kind.WallTop) c = new Color(c.r * 0.55f, c.g * 0.55f, c.b * 0.6f, 1);
                            atlas.SetPixel(cx + 1 + x, cy + 1 + y, c);
                        }
                    const float e = 0.1f;
                    atlasUV[(k, v)] = new Rect((cx + 1 + e) / size, (cy + 1 + e) / size, (16 - 2 * e) / size, (16 - 2 * e) / size);
                }
            atlas.Apply();
            var shader = Shader.Find("Depthbound/Map") ?? Shader.Find("Universal Render Pipeline/Simple Lit");
            if (mapMat == null) mapMat = new Material(shader);
            if (lavaMat == null) lavaMat = new Material(shader);
            mapMat.mainTexture = atlas;
            lavaMat.mainTexture = atlas;
            lavaMat.SetFloat("_EmissionStrength", 0.9f);
        }

        bool TilesChanged()
        {
            var map = world.map;
            if (tileState == null || tileState.GetLength(0) != map.w || tileState.GetLength(1) != map.h) return true;
            for (int x = 0; x < map.w; x++)
                for (int y = 0; y < map.h; y++)
                    if (MeshKind(map.tiles[x, y]) != MeshKind(tileState[x, y])) return true;
            return false;
        }

        static int MeshKind(Tile t)
        {
            switch (t)
            {
                case Tile.Wall: return 1;
                case Tile.Water: return 2;
                case Tile.Lava: return 3;
                case Tile.Ice: return 4;
                case Tile.Carpet: return 5;
                case Tile.StairsDown: return 6;
                default: return 0;
            }
        }

        void RebuildMapMesh()
        {
            var map = world.map;
            tileState = (Tile[,])map.tiles.Clone();
            vStart = new int[map.w, map.h];
            vCount = new int[map.w, map.h];
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            var lavaTris = new List<int>();

            void Quad(Vector3 bl, Vector3 tl, Vector3 tr, Vector3 br, Rect r, Vector3 n, List<int> into)
            {
                int s = verts.Count;
                verts.Add(bl); verts.Add(tl); verts.Add(tr); verts.Add(br);
                uvs.Add(new Vector2(r.xMin, r.yMin)); uvs.Add(new Vector2(r.xMin, r.yMax));
                uvs.Add(new Vector2(r.xMax, r.yMax)); uvs.Add(new Vector2(r.xMax, r.yMin));
                for (int i = 0; i < 4; i++) normals.Add(n);
                into.Add(s); into.Add(s + 1); into.Add(s + 2);
                into.Add(s); into.Add(s + 2); into.Add(s + 3);
            }

            void Face(Vector3 center, Vector3 n, float h, Rect r)
            {
                var right = Vector3.Cross(Vector3.up, -n);
                var c = center + n * 0.5f + Vector3.up * (h / 2f);
                var hr = right * 0.5f; var hu = Vector3.up * (h / 2f);
                Quad(c - hr - hu, c - hr + hu, c + hr + hu, c + hr - hu, r, n, tris);
            }

            for (int x = 0; x < map.w; x++)
                for (int y = 0; y < map.h; y++)
                {
                    vStart[x, y] = verts.Count;
                    var t = map.tiles[x, y];
                    int v = map.variant[x, y] % 4;
                    var c = new Vector3(x, 0, y);
                    if (t == Tile.Wall)
                    {
                        bool exposed = false;
                        foreach (var d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
                        {
                            var nt = map.Get(x + d.x, y + d.y);
                            if (nt == Tile.Wall) continue;
                            exposed = true;
                            Face(c, new Vector3(d.x, 0, d.y), WallHeight, atlasUV[((int)Kind.Wall, v)]);
                        }
                        if (exposed)
                        {
                            float h = WallHeight;
                            Quad(c + new Vector3(-0.5f, h, -0.5f), c + new Vector3(-0.5f, h, 0.5f), c + new Vector3(0.5f, h, 0.5f), c + new Vector3(0.5f, h, -0.5f),
                                atlasUV[((int)Kind.WallTop, v)], Vector3.up, tris);
                        }
                    }
                    else if (t != Tile.StairsDown)
                    {
                        Kind k = t == Tile.Water ? Kind.Water : t == Tile.Lava ? Kind.Lava : t == Tile.Ice ? Kind.Ice : t == Tile.Carpet ? Kind.Carpet : Kind.Floor;
                        float h = k == Kind.Water || k == Kind.Lava ? -0.04f : 0f;
                        Quad(c + new Vector3(-0.5f, h, -0.5f), c + new Vector3(-0.5f, h, 0.5f), c + new Vector3(0.5f, h, 0.5f), c + new Vector3(0.5f, h, -0.5f),
                            atlasUV[((int)k, v)], Vector3.up, k == Kind.Lava ? lavaTris : tris);
                    }
                    vCount[x, y] = verts.Count - vStart[x, y];
                }

            if (mapMesh == null) mapMesh = new Mesh { name = "Dungeon", indexFormat = IndexFormat.UInt32 };
            mapMesh.Clear();
            mapMesh.SetVertices(verts);
            mapMesh.SetUVs(0, uvs);
            mapMesh.SetNormals(normals);
            mapMesh.subMeshCount = 2;
            mapMesh.SetTriangles(tris, 0);
            mapMesh.SetTriangles(lavaTris, 1);
            mapMesh.RecalculateBounds();
            mapColors = new Color32[verts.Count];

            var mapGo = floorRoot.Find("Dungeon")?.gameObject;
            if (mapGo == null)
            {
                mapGo = new GameObject("Dungeon");
                mapGo.transform.SetParent(floorRoot, false);
                mapGo.AddComponent<MeshFilter>();
                var mr = mapGo.AddComponent<MeshRenderer>();
                mr.sharedMaterials = new[] { mapMat, lavaMat };
                mr.shadowCastingMode = ShadowCastingMode.On;
                mr.receiveShadows = true;
            }
            mapGo.GetComponent<MeshFilter>().sharedMesh = mapMesh;
            shadedVersion = -1;
        }

        void Shade()
        {
            shadedVersion = world.fovVersion;
            var map = world.map;
            for (int x = 0; x < map.w; x++)
                for (int y = 0; y < map.h; y++)
                {
                    byte b = map.visible[x, y] ? (byte)255 : map.explored[x, y] ? (byte)80 : (byte)0;
                    var c = new Color32(b, b, b, 255);
                    int s = vStart[x, y], n = vCount[x, y];
                    for (int i = 0; i < n; i++) mapColors[s + i] = c;
                }
            mapMesh.colors32 = mapColors;
        }

        // ------------------------------------------------------------------ props

        static string PropModel(Tile t)
        {
            switch (t)
            {
                case Tile.Chest: return "chest";
                case Tile.ChestOpen: return "chest_open";
                case Tile.Shrine: case Tile.ShrineUsed: return "shrine";
                case Tile.Fountain: case Tile.FountainUsed: return "fountain";
                case Tile.Merchant: return "merchant";
                case Tile.TrapSprung: return "spikes";
                case Tile.Rubble: return "rocks";
                case Tile.StairsDown: return "stairs";
                case Tile.Pillar: return "pillar";
                case Tile.Brazier: return "brazier";
                case Tile.Throne: return "throne";
                case Tile.Princess: return "princess";
                case Tile.Portal: return "portal";
                default: return null;
            }
        }

        void UpdateProps()
        {
            var map = world.map;
            for (int x = 0; x < map.w; x++)
                for (int y = 0; y < map.h; y++)
                {
                    var t = map.tiles[x, y];
                    var key = new Vector2Int(x, y);
                    props.TryGetValue(key, out var pv);
                    string model = PropModel(t);
                    if (pv != null && (pv.tile != t || model == null))
                    {
                        if (PropModel(pv.tile) != model || model == null) { Destroy(pv.go); props.Remove(key); pv = null; }
                        else pv.tile = t;
                    }
                    if (model == null) continue;
                    if (!map.explored[x, y]) { if (pv != null) pv.go.SetActive(false); continue; }
                    if (pv == null) pv = props[key] = SpawnProp(t, model, x, y);
                    pv.go.SetActive(true);
                    bool used = t == Tile.ShrineUsed || t == Tile.FountainUsed;
                    float l = map.visible[x, y] ? 1f : 0.4f;
                    mpb.Clear();
                    mpb.SetColor("_Tint", used ? new Color(l * 0.45f, l * 0.45f, l * 0.5f) : new Color(l, l, l));
                    foreach (var r in pv.renderers) r.SetPropertyBlock(mpb);
                    if (pv.rig != null) pv.rig.Pose(Time.time, 0, 0, 0);
                    if (pv.light != null)
                    {
                        pv.light.enabled = map.visible[x, y] || world.InHub;
                        pv.light.intensity = (t == Tile.Portal ? 1.6f : 1.3f) * (0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 3f, x * 7.1f + y));
                    }
                    if (t == Tile.Shrine || t == Tile.Portal)
                    {
                        var crystal = pv.go.transform.FindDeep("Crystal");
                        if (crystal != null && t == Tile.Shrine) crystal.localRotation = Quaternion.Euler(0, Time.time * 60f, 0);
                    }
                }
        }

        PropView SpawnProp(Tile t, string model, int x, int y)
        {
            bool character = t == Tile.Princess || t == Tile.Merchant;
            var go = ModelLibrary.Spawn(model, floorRoot, character: character);
            var holder = new GameObject(model + "_prop");
            holder.transform.SetParent(floorRoot, false);
            holder.transform.position = new Vector3(x, 0, y);
            go.transform.SetParent(holder.transform, false);
            // Props face the camera (south) unless they sit at the south end of a room, like the hub gate.
            bool faceNorth = t == Tile.Portal && world.InHub;
            holder.transform.rotation = Quaternion.Euler(0, faceNorth ? 0 : 180, 0);
            var pv = new PropView { tile = t, go = holder, renderers = holder.GetComponentsInChildren<Renderer>() };
            if (t == Tile.Princess || t == Tile.Merchant)
            {
                pv.anim = CharacterAnimator.Attach(go);
                if (pv.anim == null) pv.rig = ModelRig.Attach(go, false, false);
            }
            if (t == Tile.Brazier || t == Tile.Portal)
            {
                var lg = new GameObject("light");
                lg.transform.SetParent(holder.transform, false);
                lg.transform.localPosition = new Vector3(0, t == Tile.Portal ? 1.0f : 1.3f, t == Tile.Portal ? -0.4f : 0);
                pv.light = lg.AddComponent<Light>();
                pv.light.type = LightType.Point;
                pv.light.range = t == Tile.Portal ? 5f : 6f;
                pv.light.color = t == Tile.Portal ? new Color(0.75f, 0.35f, 1f) : new Color(1f, 0.55f, 0.2f);
                pv.light.shadows = LightShadows.None;
            }
            return pv;
        }

        // ------------------------------------------------------------------ actors

        ActorView CreateView(Actor a)
        {
            var v = new ActorView { root = new GameObject(a.name), lastLife = a.life, lastPos = a.pos, lastLock = a.actionLock };
            v.root.transform.SetParent(actorRoot, false);
            bool hoverFly = false, squash = false;
            if (a is Player pl)
            {
                v.model = ModelLibrary.Spawn("demon", v.root.transform, primary: pl.cls.color, character: true);
            }
            else
            {
                var m = (Monster)a;
                hoverFly = m.def.shape == CreatureShape.Flyer;
                squash = m.def.shape == CreatureShape.Blob;
                v.model = ModelLibrary.Spawn(ModelLibrary.ModelFor(m.def.shape), v.root.transform, primary: m.def.c1, secondary: m.def.c2, character: true);
                v.model.transform.localScale = Vector3.one * (0.85f * a.scale);
                if (m.rank > 0 || m.faction == Faction.Player)
                {
                    v.ring = NewSprite("ring", v.root.transform, ringSprite, 3);
                    v.ring.transform.localRotation = Quaternion.Euler(90, 0, 0);
                    v.ring.transform.localPosition = new Vector3(0, 0.03f, 0);
                    v.ring.transform.localScale = Vector3.one * (a.radius * 2.6f);
                    v.ring.color = m.faction == Faction.Player ? new Color(0.3f, 1f, 0.5f, 0.8f)
                        : m.rank == 1 ? new Color(0.35f, 0.55f, 1f, 0.9f) : m.rank == 2 ? new Color(1f, 0.8f, 0.2f, 0.9f) : new Color(1f, 0.25f, 0.15f, 0.95f);
                }
            }
            v.rig = ModelRig.Attach(v.model, hoverFly, squash);
            v.anim = CharacterAnimator.Attach(v.model);
            v.lastSerial = a.actionSerial;
            v.renderers = v.model.GetComponentsInChildren<Renderer>();
            v.yaw = Mathf.Atan2(a.facing.x, a.facing.y) * Mathf.Rad2Deg;
            return v;
        }

        void UpdateActors()
        {
            var map = world.map;
            float dt = Time.deltaTime;
            foreach (var a in world.actors)
            {
                if (a.dead) continue;
                if (!views.TryGetValue(a, out var v)) views[a] = v = CreateView(a);
                bool vis = a == world.player || (map.InBounds(a.x, a.y) && map.visible[a.x, a.y]);
                v.root.SetActive(vis);
                v.root.transform.position = W3(a.pos);

                float speed = dt > 0 ? (a.pos - v.lastPos).magnitude / dt : 0;
                v.lastPos = a.pos;
                v.move = Mathf.MoveTowards(v.move, speed > 0.4f ? 1f : 0f, dt * 6f);
                if (a.facing.sqrMagnitude > 0.01f)
                {
                    float target = Mathf.Atan2(a.facing.x, a.facing.y) * Mathf.Rad2Deg;
                    v.yaw = Mathf.MoveTowardsAngle(v.yaw, target, dt * 720f);
                }
                v.root.transform.rotation = Quaternion.Euler(0, v.yaw, 0);
                var mon = a as Monster;
                if (a.actionSerial != v.lastSerial)
                {
                    v.lastSerial = a.actionSerial;
                    v.attack = 1f;
                    // A monster's swing lands when its wind-up ends: time the clip's strike (about 42% in) to match.
                    float duration = mon != null && mon.windup >= 0 ? Mathf.Max(0.2f, mon.windup / 0.42f) : (a.actionIsSpell ? 0.5f : 0.42f);
                    v.anim?.Play(a.actionIsSpell ? "Cast" : "Attack", duration);
                }
                v.attack = Mathf.Max(0, v.attack - dt * 4f);
                v.windup = Mathf.MoveTowards(v.windup, mon != null && mon.windup >= 0 ? 1f : 0f, dt * 8f);
                if (a.life < v.lastLife - 0.01f)
                {
                    v.flash = 1f;
                    if (v.anim != null && !v.anim.Busy) v.anim.Play("Hit", 0.3f);
                }
                v.lastLife = a.life;
                v.flash = Mathf.Max(0, v.flash - dt * 7f);
                if (v.anim != null) v.anim.Locomotion(v.move, speed / (a is Player ? 3.4f : 2.3f));
                if (!vis) continue;

                if (v.anim == null) v.rig.Pose(Time.time, v.move, v.attack > 0 ? 1f - v.attack : 0, v.windup);

                Color tint = Color.white;
                if (a.Has(StatusType.Freeze)) tint = new Color(0.55f, 0.8f, 1.3f);
                else if (a.Has(StatusType.Chill)) tint = new Color(0.75f, 0.9f, 1.15f);
                else if (a.Has(StatusType.Poison)) tint = new Color(0.7f, 1.1f, 0.6f);
                else if (a.Has(StatusType.Stun)) tint = new Color(1.2f, 1.2f, 0.7f);
                if (a is Player pp && pp.Dodging) tint = new Color(0.7f, 0.8f, 1.4f);
                Color flash = Color.white * (0.55f * v.flash);
                if (v.windup > 0) flash += new Color(1f, 0.1f, 0.05f) * (0.6f * v.windup * (0.6f + 0.4f * Mathf.Sin(Time.time * 40f)));
                if (a.Has(StatusType.Burn)) flash += new Color(0.5f, 0.15f, 0f) * (0.5f + 0.5f * Mathf.Sin(Time.time * 12f));
                mpb.Clear();
                mpb.SetColor("_Tint", tint);
                mpb.SetColor("_Flash", flash);
                foreach (var r in v.renderers) r.SetPropertyBlock(mpb);
            }
            var gone = views.Where(kv => kv.Key.dead || !world.actors.Contains(kv.Key)).ToList();
            foreach (var kv in gone)
            {
                var v = kv.Value;
                if (v.death < 0)
                {
                    // Play the death clip, then sink the body away.
                    v.death = v.anim != null ? v.anim.DeathLength + 0.6f : 0.5f;
                    v.anim?.Die();
                }
                v.death -= dt;
                if (v.root && v.death < 0.5f)
                {
                    v.root.transform.position += Vector3.down * dt * 0.8f;
                    v.root.transform.localScale = Vector3.one * Mathf.Clamp01(v.death * 2f + 0.2f);
                }
                if (v.ring) v.ring.enabled = false;
                if (v.death <= 0) { Destroy(v.root); views.Remove(kv.Key); }
            }
        }

        // ------------------------------------------------------------------ projectiles, items, fx

        void UpdateProjectiles()
        {
            var map = world.map;
            foreach (var p in world.projectiles)
            {
                if (!projViews.TryGetValue(p, out var pv))
                {
                    var sr = NewSprite("proj", fxRoot, dotSprite, 15);
                    Light l = null;
                    if (p.explodeRadius > 0 || (p.skill != null && !p.basic))
                    {
                        l = sr.gameObject.AddComponent<Light>();
                        l.type = LightType.Point;
                        l.range = 2.5f;
                        l.intensity = 1.2f;
                        l.shadows = LightShadows.None;
                    }
                    projViews[p] = pv = (sr, l);
                }
                int tx = Mathf.RoundToInt(p.pos.x), ty = Mathf.RoundToInt(p.pos.y);
                bool vis = map.InBounds(tx, ty) && map.visible[tx, ty];
                pv.sr.enabled = vis;
                pv.sr.transform.position = W3(p.pos, 0.75f);
                pv.sr.transform.rotation = cam.transform.rotation;
                pv.sr.transform.localScale = Vector3.one * p.size * 2.4f;
                pv.sr.color = Color.Lerp(p.color, Color.white, 0.25f);
                if (pv.light) { pv.light.enabled = vis; pv.light.color = p.color; }
            }
            var dead = projViews.Keys.Where(k => k.dead || !world.projectiles.Contains(k)).ToList();
            foreach (var k in dead) { Destroy(projViews[k].sr.gameObject); projViews.Remove(k); }
        }

        void UpdateItems()
        {
            var map = world.map;
            int used = 0;
            foreach (var g in world.ground)
            {
                if (!map.explored[g.x, g.y]) continue;
                if (g.artifact != null)
                {
                    if (!artifactViews.TryGetValue(g, out var av))
                    {
                        av = new GameObject("artifact");
                        av.transform.SetParent(itemRoot, false);
                        var model = ModelLibrary.Spawn("artifact", av.transform);
                        model.transform.localScale = Vector3.one * 1.8f;
                        var l = av.AddComponent<Light>();
                        l.type = LightType.Point; l.range = 5f; l.intensity = 3f; l.color = new Color(1f, 0.5f, 0.9f);
                        // A pillar of light so the relic reads from across the room.
                        var beam = NewSprite("beam", av.transform, discSprite, 14);
                        beam.transform.localPosition = new Vector3(0, 1.6f, 0);
                        beam.transform.localScale = new Vector3(0.7f, 4f, 1f);
                        beam.color = new Color(1f, 0.55f, 0.9f, 0.45f);
                        artifactViews[g] = av;
                    }
                    av.transform.position = new Vector3(g.x, 0.1f + 0.1f * Mathf.Sin(Time.time * 2f), g.y);
                    var relic = av.transform.GetChild(0);
                    relic.localRotation = Quaternion.Euler(0, Time.time * 70f, 0);
                    var beamT = av.transform.Find("beam");
                    if (beamT != null) beamT.rotation = Quaternion.Euler(0, cam.transform.eulerAngles.y, 0);
                    continue;
                }
                if (used >= itemPool.Count)
                {
                    var sr = NewSprite("item", itemRoot, null, 1);
                    itemPool.Add(sr);
                }
                var r = itemPool[used++];
                r.gameObject.SetActive(true);
                if (g.gold > 0) r.sprite = SpriteFactory.Icon("gold", new Color(1f, 0.95f, 0.6f));
                else if (g.potion > 0) r.sprite = SpriteFactory.Icon("potion", Color.white, g.potion == 1 ? new Color(0.9f, 0.15f, 0.2f) : new Color(0.2f, 0.4f, 1f));
                else r.sprite = SpriteFactory.ItemIcon(g.item);
                bool shiny = g.item != null && g.item.rarity >= Rarity.Epic;
                float bob = 0.06f * Mathf.Sin(Time.time * 3f + g.x * 1.3f);
                r.transform.position = new Vector3(g.x, 0.35f + bob, g.y);
                r.transform.rotation = cam.transform.rotation;
                r.transform.localScale = Vector3.one * (shiny ? 0.8f : 0.6f);
                float l2 = map.visible[g.x, g.y] ? 1f : 0.4f;
                r.color = new Color(l2, l2, l2);
            }
            for (int i = used; i < itemPool.Count; i++) itemPool[i].gameObject.SetActive(false);
            foreach (var k in artifactViews.Keys.Where(k => !world.ground.Contains(k)).ToList()) { Destroy(artifactViews[k]); artifactViews.Remove(k); }
        }

        bool Visible(Vector2 p)
        {
            int x = Mathf.RoundToInt(p.x), y = Mathf.RoundToInt(p.y);
            return world.map.InBounds(x, y) && world.map.visible[x, y];
        }

        void ProcessFxEvents()
        {
            foreach (var e in Fx.Pending)
            {
                switch (e.kind)
                {
                    case FxKind.Projectile:
                        if (!Visible(e.a) && !Visible(e.b)) break;
                        SpawnFx(W3(e.a, 0.75f), W3(e.b, 0.75f), dotSprite, e.color, 0.35f, 0.05f + Vector2.Distance(e.a, e.b) * 0.02f, 1f, true, true, 0);
                        break;
                    case FxKind.Burst:
                        if (!Visible(e.a)) break;
                        SpawnFx(W3(e.a, 0.06f), W3(e.a, 0.06f), discSprite, e.color, Mathf.Max(0.8f, e.radius * 2f), 0.4f, 0.6f, false, false, 0.25f);
                        break;
                    case FxKind.Line:
                    {
                        float len = Vector2.Distance(e.a, e.b);
                        for (float d = 0; d <= len; d += 0.35f)
                        {
                            var p = Vector2.Lerp(e.a, e.b, len > 0 ? d / len : 0);
                            if (Visible(p)) SpawnFx(W3(p, 0.8f), W3(p, 0.8f), dotSprite, e.color, 0.3f, 0.3f, 1f, false, true, 0);
                        }
                        break;
                    }
                    case FxKind.Flash:
                        if (Visible(e.a)) SpawnFx(W3(e.a, 0.7f), W3(e.a, 0.7f), discSprite, e.color, 0.9f, 0.15f, 0.7f, false, true, 0.3f);
                        break;
                    case FxKind.Text:
                        if (Visible(e.a))
                            texts.Add(new FloatingText { pos = new Vector3(e.a.x + Random.Range(-0.25f, 0.25f), 1.5f, e.a.y), text = e.text, color = e.color, size = e.size });
                        break;
                }
            }
            Fx.Pending.Clear();
        }

        void SpawnFx(Vector3 a, Vector3 b, Sprite s, Color c, float scale, float dur, float alpha, bool moves, bool billboard, float grow)
        {
            var sr = NewSprite("fx", fxRoot, s, 20);
            sr.transform.position = a;
            sr.transform.localScale = Vector3.one * scale;
            sr.transform.rotation = billboard ? cam.transform.rotation : Quaternion.Euler(90, 0, 0);
            c.a = alpha;
            sr.color = c;
            fx.Add(new FxObj { sr = sr, a = a, b = b, dur = dur, alpha = alpha, moves = moves, billboard = billboard, grow = grow });
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
                    if (f.grow > 0) f.sr.transform.localScale *= 1f + f.grow * Time.deltaTime * 4f;
                }
                if (f.billboard) f.sr.transform.rotation = cam.transform.rotation;
                if (f.t >= f.dur + (f.moves ? 0.05f : 0)) { Destroy(f.sr.gameObject); fx.RemoveAt(i); }
            }
            for (int i = texts.Count - 1; i >= 0; i--)
            {
                texts[i].t += Time.deltaTime;
                texts[i].pos += new Vector3(0, Time.deltaTime * 1.1f, 0);
                if (texts[i].t > 0.95f) texts.RemoveAt(i);
            }
        }

        // ------------------------------------------------------------------ camera, lights, mouse

        Vector3 CameraPosFor(Vector3 target)
        {
            float dist = zoom * 1.9f;
            return target + new Vector3(0, 0.6f, 0) + Quaternion.Euler(CameraPitch, 0, 0) * new Vector3(0, 0, -dist);
        }

        void UpdateCamera()
        {
            var ui = GameManager.Instance != null ? GameManager.Instance.ui : null;
            if (ui == null || !ui.MouseOverUI())
            {
                float scroll = Input.mouseScrollDelta.y;
                if (Mathf.Abs(scroll) > 0.01f) zoom = Mathf.Clamp(zoom - scroll * 0.8f, 4.5f, 16f);
            }
            var target = W3(world.player.pos);
            cam.transform.position = Vector3.Lerp(cam.transform.position, CameraPosFor(target), 1f - Mathf.Exp(-Time.deltaTime * 10f));
            cam.transform.rotation = Quaternion.Euler(CameraPitch, 0, 0);
        }

        void UpdateLights()
        {
            var p = world.player;
            // Held well above the head so the hero is lit like the floor instead of saturating.
            torch.transform.position = W3(p.pos, 3.4f);
            torch.range = Mathf.Max(6f, p.Get(StatType.LightRadius) * 1.5f + 1.5f);
            torch.intensity = 5.2f + 0.25f * Mathf.Sin(Time.time * 9f) * Mathf.PerlinNoise(Time.time * 2f, 0);
        }

        void UpdateHover()
        {
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            var ground = new Plane(Vector3.up, Vector3.zero);
            if (ground.Raycast(ray, out float d))
            {
                var hit = ray.GetPoint(d);
                MouseWorld = new Vector2(hit.x, hit.z);
                MouseTile = new Vector2Int(Mathf.RoundToInt(hit.x), Mathf.RoundToInt(hit.z));
            }
            var map = world.map;
            bool show = map.InBounds(MouseTile.x, MouseTile.y) && map.explored[MouseTile.x, MouseTile.y] && !(GameManager.Instance.ui?.MouseOverUI() ?? false);
            hover.enabled = show;
            if (!show) return;
            var a = world.ActorNear(MouseWorld, 0.4f);
            bool enemy = a != null && a.IsHostileTo(world.player) && Visible(a.pos);
            hover.sprite = enemy ? ringSprite : frameSprite;
            hover.transform.position = enemy ? W3(a.pos, 0.04f) : new Vector3(MouseTile.x, 0.03f, MouseTile.y);
            hover.transform.localScale = Vector3.one * (enemy ? a.radius * 3f : 1f);
            hover.color = enemy ? new Color(1f, 0.3f, 0.3f, 0.9f)
                : DungeonMap.IsInteractable(map.Get(MouseTile.x, MouseTile.y)) ? new Color(1f, 0.9f, 0.4f, 0.8f) : new Color(1f, 1f, 1f, 0.25f);
        }

        public Vector2 WorldToGui(Vector3 worldPos, float guiScale)
        {
            var sp = cam.WorldToScreenPoint(worldPos);
            if (sp.z < 0) return new Vector2(-1000, -1000);
            return new Vector2(sp.x / guiScale, (Screen.height - sp.y) / guiScale);
        }
    }

    public static class TransformExtensions
    {
        public static Transform FindDeep(this Transform t, string name)
        {
            foreach (var c in t.GetComponentsInChildren<Transform>(true)) if (c.name == name) return c;
            return null;
        }
    }
}
