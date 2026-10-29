using System.Collections.Generic;
using UnityEngine;

namespace Depthbound
{
    /// <summary>
    /// Renders a live, animated portrait of a character into a RenderTexture for dialogue boxes.
    /// The "studio" sits far outside the dungeon with its own camera and key light.
    /// </summary>
    public static class PortraitStudio
    {
        class Studio
        {
            public GameObject root;
            public Camera cam;
            public ModelRig rig;
            public RenderTexture texture;
            public float lastUsed;
        }

        static readonly Dictionary<string, Studio> studios = new Dictionary<string, Studio>();
        static readonly Vector3 Origin = new Vector3(5000f, 0f, 5000f);

        /// <summary>Portrait texture for a model, rendered this frame. Returns null if unavailable.</summary>
        public static Texture Get(string model, float headHeight = 1.12f)
        {
            if (!studios.TryGetValue(model, out var s) || s.root == null)
            {
                s = Create(model, studios.Count, headHeight);
                studios[model] = s;
            }
            s.cam.enabled = true;
            s.lastUsed = Time.unscaledTime;
            s.rig.Pose(Time.unscaledTime, 0, 0, 0);
            return s.texture;
        }

        /// <summary>Stop rendering portraits that have not been requested recently.</summary>
        public static void Tick()
        {
            foreach (var s in studios.Values)
                if (s.cam != null && s.cam.enabled && Time.unscaledTime - s.lastUsed > 0.25f) s.cam.enabled = false;
        }

        static Studio Create(string model, int index, float headHeight)
        {
            var s = new Studio();
            s.root = new GameObject("Portrait_" + model);
            Object.DontDestroyOnLoad(s.root);
            s.root.transform.position = Origin + new Vector3(index * 50f, 0, 0);
            var holder = new GameObject("holder").transform;
            holder.SetParent(s.root.transform, false);
            var go = ModelLibrary.Spawn(model, holder, character: true);
            s.rig = ModelRig.Attach(go, false, false);

            s.texture = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32) { name = "Portrait_" + model, antiAliasing = 4 };
            var camGo = new GameObject("PortraitCamera");
            camGo.transform.SetParent(s.root.transform, false);
            camGo.transform.localPosition = new Vector3(0.05f, headHeight + 0.02f, 1.25f);
            camGo.transform.LookAt(s.root.transform.position + new Vector3(0, headHeight - 0.08f, 0));
            s.cam = camGo.AddComponent<Camera>();
            s.cam.fieldOfView = 24f;
            s.cam.nearClipPlane = 0.05f;
            s.cam.farClipPlane = 6f;
            s.cam.clearFlags = CameraClearFlags.SolidColor;
            s.cam.backgroundColor = new Color(0.16f, 0.06f, 0.14f, 1f);
            s.cam.targetTexture = s.texture;
            s.cam.enabled = false;

            // Warm key light from the front-left and a cool rim from behind.
            AddLight(s.root.transform, new Vector3(-0.8f, headHeight + 0.6f, 1.2f), new Color(1f, 0.85f, 0.8f), 1.25f);
            AddLight(s.root.transform, new Vector3(0.6f, headHeight + 0.4f, -0.9f), new Color(0.6f, 0.45f, 1f), 1.2f);
            return s;
        }

        static void AddLight(Transform parent, Vector3 pos, Color color, float intensity)
        {
            var lg = new GameObject("PortraitLight");
            lg.transform.SetParent(parent, false);
            lg.transform.localPosition = pos;
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 4f;
            l.color = color;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
        }
    }
}
