using System.Collections.Generic;
using UnityEngine;

namespace Depthbound
{
    /// <summary>
    /// Loads the Blender-built FBX models from Resources/Models, restores their part hierarchy
    /// (encoded as "Part@Parent" names), and swaps imported materials for shared role-based ones.
    /// </summary>
    public static class ModelLibrary
    {
        static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        static readonly Dictionary<string, bool> facesBackward = new Dictionary<string, bool>();
        static Shader lit;

        static readonly Dictionary<string, Color> palette = new Dictionary<string, Color>
        {
            { "Primary", new Color(0.55f, 0.1f, 0.1f) }, { "Secondary", new Color(0.3f, 0.3f, 0.3f) },
            { "Skin", new Color(0.5f, 0.1f, 0.12f) }, { "SkinPale", new Color(0.85f, 0.7f, 0.85f) },
            { "Horn", new Color(0.12f, 0.1f, 0.09f) }, { "Eye", new Color(1f, 0.75f, 0.2f) },
            { "Glow", new Color(0.75f, 0.3f, 1f) }, { "Metal", new Color(0.62f, 0.63f, 0.68f) },
            { "Gold", new Color(0.92f, 0.72f, 0.2f) }, { "Wood", new Color(0.38f, 0.23f, 0.11f) },
            { "Stone", new Color(0.42f, 0.4f, 0.45f) }, { "Cloth", new Color(0.42f, 0.06f, 0.22f) },
            { "Dark", new Color(0.05f, 0.04f, 0.06f) }, { "Water", new Color(0.2f, 0.45f, 0.85f) },
            { "Fire", new Color(1f, 0.5f, 0.1f) }, { "Hair", new Color(0.12f, 0.05f, 0.18f) },
            { "White", new Color(0.9f, 0.9f, 0.86f) }, { "Wing", new Color(0.28f, 0.05f, 0.1f) },
        };

        static readonly Dictionary<string, float> emissive = new Dictionary<string, float>
        {
            { "Eye", 1.6f }, { "Glow", 1.2f }, { "Fire", 1.8f }, { "Water", 0.25f },
        };

        public static Shader LitShader => lit != null ? lit : (lit = Shader.Find("Depthbound/Lit") ?? Shader.Find("Standard"));

        static string Role(string materialName)
        {
            string n = materialName.Replace(" (Instance)", "");
            int dot = n.IndexOf('.');
            return dot > 0 ? n.Substring(0, dot) : n;
        }

        public static Material Get(string role, Color? overrideColor = null)
        {
            var color = overrideColor ?? (palette.TryGetValue(role, out var c) ? c : Color.magenta);
            string key = role + "_" + ColorUtility.ToHtmlStringRGB(color);
            if (materials.TryGetValue(key, out var m)) return m;
            m = new Material(LitShader) { name = key, color = color };
            if (emissive.TryGetValue(role, out var e)) m.SetColor("_Emission", color * e);
            materials[key] = m;
            return m;
        }

        /// <summary>Instantiate a model. Primary/Secondary/Skin colors may be overridden per instance.</summary>
        public static GameObject Spawn(string model, Transform parent, Color? primary = null, Color? secondary = null, Color? skin = null)
        {
            if (!prefabs.TryGetValue(model, out var prefab))
            {
                prefab = Resources.Load<GameObject>("Models/" + model);
                prefabs[model] = prefab;
            }
            GameObject go;
            if (prefab == null)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(parent, false);
                go.GetComponent<Renderer>().sharedMaterial = Get("Primary", primary);
                return go;
            }
            go = Object.Instantiate(prefab, parent, false);
            go.name = model;
            Relink(go.transform);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string role = mats[i] != null ? Role(mats[i].name) : "Primary";
                    Color? o = role == "Primary" ? primary : role == "Secondary" ? secondary : (role == "Skin" ? skin : null);
                    mats[i] = Get(role, o);
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = true;
            }
            // Models are authored facing Blender's front; make sure they face +Z (forward) in Unity.
            if (FacesBackward(model, go.transform)) go.transform.localRotation = Quaternion.Euler(0, 180, 0);
            return go;
        }

        static bool FacesBackward(string model, Transform root)
        {
            if (facesBackward.TryGetValue(model, out var b)) return b;
            float z = 0; int n = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("Eye") && t.name != "Snout" && t.name != "Visor" && t.name != "Lock" && t.name != "Rune") continue;
                var r = t.GetComponent<Renderer>();
                z += root.InverseTransformPoint(r != null ? r.bounds.center : t.position).z;
                n++;
            }
            b = n > 0 && z / n < 0;
            facesBackward[model] = b;
            return b;
        }

        /// <summary>Re-parent flat "Part@Parent" nodes so that e.g. the head follows the body.</summary>
        static void Relink(Transform root)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            var byName = new Dictionary<string, Transform>();
            foreach (var t in all)
            {
                string baseName = BaseName(t.name);
                if (!byName.ContainsKey(baseName)) byName[baseName] = t;
            }
            foreach (var t in all)
            {
                int at = t.name.IndexOf('@');
                if (at < 0) continue;
                string parentName = t.name.Substring(at + 1);
                int dot = parentName.IndexOf('.');
                if (dot > 0) parentName = parentName.Substring(0, dot);
                string own = t.name.Substring(0, at);
                if (byName.TryGetValue(parentName, out var p) && p != t) t.SetParent(p, true);
                t.name = own;
            }
        }

        static string BaseName(string n)
        {
            int at = n.IndexOf('@');
            if (at >= 0) n = n.Substring(0, at);
            int dot = n.IndexOf('.');
            return dot > 0 ? n.Substring(0, dot) : n;
        }

        public static string ModelFor(CreatureShape s)
        {
            switch (s)
            {
                case CreatureShape.Beast: return "beast";
                case CreatureShape.Blob: return "blob";
                case CreatureShape.Flyer: return "flyer";
                case CreatureShape.Insect: return "insect";
                case CreatureShape.Golem: return "golem";
                case CreatureShape.Human: return "human";
                default: return "humanoid";
            }
        }
    }
}
