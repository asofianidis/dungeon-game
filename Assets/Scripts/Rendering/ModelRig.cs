using System.Collections.Generic;
using UnityEngine;

namespace Depthbound
{
    /// <summary>
    /// Procedural animation for the skinned Blender models. Bones are rotated about axes of the
    /// character's own frame (right = X, up = Y, forward = Z), so it works regardless of how each bone
    /// is oriented after import. Handles bipeds, quadrupeds, spiders, flyers and blobs.
    /// </summary>
    public class ModelRig : MonoBehaviour
    {
        class Bone
        {
            public Transform t;
            public Quaternion bind;
            public Vector3 bindPos;
        }

        readonly Dictionary<string, Bone> bones = new Dictionary<string, Bone>();
        readonly List<Bone> all = new List<Bone>();
        Transform frame;
        Vector3 modelScale, modelPos;
        public bool hover, squash;
        float seed;

        public static ModelRig Attach(GameObject model, bool hover, bool squash)
        {
            var rig = model.AddComponent<ModelRig>();
            rig.hover = hover;
            rig.squash = squash;
            rig.seed = Random.value * 10f;
            rig.frame = model.transform.parent != null ? model.transform.parent : model.transform;
            rig.modelScale = model.transform.localScale;
            rig.modelPos = model.transform.localPosition;
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
                if (t == model.transform || rig.bones.ContainsKey(t.name) || t.GetComponent<Renderer>() != null) continue;
                var b = new Bone { t = t, bind = t.localRotation, bindPos = t.localPosition };
                rig.bones[t.name] = b;
                rig.all.Add(b);
            }
            return rig;
        }

        static readonly Vector3 Right = Vector3.right, Up = Vector3.up, Fwd = Vector3.forward;

        void Rot(string name, Vector3 frameAxis, float degrees)
        {
            if (Mathf.Abs(degrees) < 0.01f || !bones.TryGetValue(name, out var b) || b.t.parent == null) return;
            var axis = b.t.parent.InverseTransformDirection(frame.TransformDirection(frameAxis));
            b.t.localRotation = Quaternion.AngleAxis(degrees, axis) * b.t.localRotation;
        }

        void Lift(string name, float amount)
        {
            if (!bones.TryGetValue(name, out var b) || b.t.parent == null) return;
            b.t.localPosition = b.bindPos + b.t.parent.InverseTransformVector(frame.up * amount);
        }

        bool Has(string name) => bones.ContainsKey(name);

        /// <param name="move">0..1 walking amount</param>
        /// <param name="attack">0..1 progress through an attack swing (0 = not attacking)</param>
        /// <param name="windup">0..1 wind-up before a melee swing</param>
        public void Pose(float time, float move, float attack, float windup)
        {
            foreach (var b in all) { b.t.localRotation = b.bind; b.t.localPosition = b.bindPos; }
            float t = time + seed;
            float walk = t * 9.5f;
            float breathe = Mathf.Sin(t * 2.1f);

            // ---- Bipeds ----
            if (Has("UpperLegL"))
            {
                Lift("Hips", Mathf.Abs(Mathf.Sin(walk)) * 0.035f * move - 0.01f * move);
                Rot("Hips", Up, Mathf.Sin(walk) * 6f * move);
                Rot("Spine", Right, 5f * move + breathe * 1.2f);
                Rot("Chest", Up, -Mathf.Sin(walk) * 8f * move);
                Rot("Chest", Right, breathe * 1.5f - 6f * windup);
                Rot("Head", Up, Mathf.Sin(t * 0.8f) * 7f);
                Rot("Head", Right, Mathf.Sin(t * 1.3f) * 2f);
                foreach (var (side, s) in new[] { ("L", 0f), ("R", Mathf.PI) })
                {
                    float ph = walk + s;
                    Rot("UpperLeg" + side, Right, -Mathf.Sin(ph) * 30f * move);
                    Rot("LowerLeg" + side, Right, Mathf.Max(0f, Mathf.Cos(ph)) * 40f * move);
                    Rot("Foot" + side, Right, -Mathf.Max(0f, Mathf.Cos(ph)) * 12f * move);
                }
                // Arms swing opposite the legs; the right arm also attacks.
                float sideOut = 8f + breathe * 1.5f;
                Rot("UpperArmL", Fwd, -sideOut);
                Rot("UpperArmL", Right, Mathf.Sin(walk) * 24f * move);
                Rot("LowerArmL", Right, -18f - 10f * move);
                Rot("UpperArmR", Fwd, sideOut);
                if (attack > 0)
                {
                    float k = 1f - (1f - attack) * (1f - attack);           // ease out
                    Rot("UpperArmR", Right, Mathf.Lerp(-150f, 20f, k));
                    Rot("LowerArmR", Right, Mathf.Lerp(-50f, -10f, k));
                    Rot("Chest", Up, Mathf.Lerp(-18f, 22f, k));
                }
                else
                {
                    Rot("UpperArmR", Right, -Mathf.Sin(walk) * 24f * move - 150f * windup);
                    Rot("LowerArmR", Right, -18f - 10f * move - 40f * windup);
                }
            }

            // ---- Quadrupeds (front legs Leg/Shin, hind legs Leg2/Shin2) and spiders (Leg..Leg4) ----
            if (Has("LegL"))
            {
                bool spider = Has("Leg4L");
                for (int i = 1; i <= 4; i++)
                {
                    string idx = i == 1 ? "" : i.ToString();
                    if (!Has("Leg" + idx + "L")) continue;
                    foreach (var (side, sign) in new[] { ("L", 1f), ("R", -1f) })
                    {
                        // Diagonal gait: front-left moves with hind-right.
                        float ph = walk * (spider ? 1.4f : 1f) + (i % 2 == 0 ? Mathf.PI : 0f) + (side == "R" ? Mathf.PI : 0f);
                        if (spider)
                        {
                            Rot("Leg" + idx + side, Up, Mathf.Sin(ph) * 18f * move * sign);
                            Rot("Leg" + idx + side, Fwd, Mathf.Max(0f, Mathf.Cos(ph)) * 14f * move * sign + breathe * 2f * sign);
                        }
                        else
                        {
                            Rot("Leg" + idx + side, Right, -Mathf.Sin(ph) * 28f * move);
                            Rot("Shin" + idx + side, Right, (i == 1 ? -1f : 1f) * Mathf.Max(0f, Mathf.Cos(ph)) * 35f * move);
                        }
                    }
                }
                if (!spider)
                {
                    Lift("Hips", Mathf.Abs(Mathf.Sin(walk)) * 0.03f * move);
                    Rot("Head", Right, Mathf.Sin(walk * 2f) * 3f * move + breathe * 2f - 15f * windup + 25f * Mathf.Sin(attack * Mathf.PI));
                    Rot("Chest", Right, breathe * 1.5f);
                }
                else
                {
                    Rot("Abdomen", Right, breathe * 4f);
                    Rot("Head", Right, -12f * windup + 18f * Mathf.Sin(attack * Mathf.PI));
                }
            }

            // ---- Wings ----
            float flap = hover ? Mathf.Sin(t * 15f) * 38f : breathe * 5f + Mathf.Sin(t * 8f) * 8f * move;
            foreach (var (side, sign) in new[] { ("L", 1f), ("R", -1f) })
            {
                Rot("Wing" + side, Up, -sign * (10f - 25f * windup));
                Rot("Wing" + side, Fwd, sign * flap);
                Rot("WingTip" + side, Fwd, sign * flap * 0.5f);
            }

            // ---- Tails ----
            for (int i = 1; i <= 3; i++) Rot("Tail" + i, Up, Mathf.Sin(t * 2.6f - i * 0.7f) * 11f);

            // ---- Flyers and blobs move the whole model ----
            if (hover)
            {
                Rot("Body", Right, -6f * windup + 10f * Mathf.Sin(attack * Mathf.PI));
                transform.localPosition = modelPos + new Vector3(0, Mathf.Sin(t * 3f) * 0.07f, 0);
            }
            if (squash)
            {
                float s = 1f + Mathf.Sin(t * (move > 0 ? 9f : 3f)) * (move > 0 ? 0.12f : 0.05f) + 0.2f * windup;
                transform.localScale = new Vector3(modelScale.x / Mathf.Sqrt(s), modelScale.y * s, modelScale.z / Mathf.Sqrt(s));
            }
        }
    }
}
