using System.Collections.Generic;
using UnityEngine;

namespace Depthbound
{
    /// <summary>Procedural animation for the part-based models: walk cycles, wing flaps, attacks and wind-ups.</summary>
    public class ModelRig : MonoBehaviour
    {
        class Joint
        {
            public Transform t;
            public Quaternion rot;
            public Vector3 pos, scale;
            public float side, phase;
        }

        Joint body, head, armL, armR, tail;
        readonly List<Joint> legs = new List<Joint>();
        readonly List<Joint> wings = new List<Joint>();
        public bool hover, squash;
        float seed;

        static Joint J(Transform t, float side = 0, float phase = 0) =>
            t == null ? null : new Joint { t = t, rot = t.localRotation, pos = t.localPosition, scale = t.localScale, side = side, phase = phase };

        public static ModelRig Attach(GameObject model, bool hover, bool squash)
        {
            var rig = model.AddComponent<ModelRig>();
            rig.hover = hover;
            rig.squash = squash;
            rig.seed = Random.value * 10f;
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (n == "Body") rig.body = J(t);
                else if (n == "Head") rig.head = J(t);
                else if (n == "ArmL") rig.armL = J(t, 1);
                else if (n == "ArmR") rig.armR = J(t, -1);
                else if (n == "Tail") rig.tail = J(t);
                else if (n.StartsWith("Wing") && (n.EndsWith("L") || n.EndsWith("R")) && !n.StartsWith("WingBone"))
                    rig.wings.Add(J(t, n.EndsWith("L") ? 1 : -1));
                else if (n.StartsWith("Leg") && (n.EndsWith("L") || n.EndsWith("R")))
                {
                    string mid = n.Substring(3, n.Length - 4);
                    int index = mid.Length > 0 && int.TryParse(mid, out var k) ? k - 1 : 0;
                    float side = n.EndsWith("L") ? 1 : -1;
                    // Alternate legs (and diagonal pairs for quadrupeds / insects).
                    float phase = (side > 0 ? 0 : Mathf.PI) + (index % 2) * Mathf.PI;
                    rig.legs.Add(J(t, side, phase));
                }
            }
            return rig;
        }

        /// <param name="move">0..1 walking amount</param>
        /// <param name="attack">0..1 attack swing (1 = start of swing)</param>
        /// <param name="windup">0..1 wind-up before a melee swing</param>
        public void Pose(float time, float move, float attack, float windup)
        {
            float t = time + seed;
            float walk = t * 11f;
            foreach (var l in legs)
                l.t.localRotation = l.rot * Quaternion.Euler(Mathf.Sin(walk + l.phase) * 32f * move, 0, 0);

            float swing = Mathf.Sin(walk) * 22f * move;
            float strike = attack > 0 ? Mathf.Sin(attack * Mathf.PI) : 0;
            if (armL != null) armL.t.localRotation = armL.rot * Quaternion.Euler(-swing, 0, 0);
            if (armR != null)
                armR.t.localRotation = armR.rot * Quaternion.Euler(swing - 95f * strike - 120f * windup, 0, 0);

            float flap = hover ? Mathf.Sin(t * 16f) * 35f : Mathf.Sin(t * 2.2f) * 8f + (move > 0 ? Mathf.Sin(t * 9f) * 6f : 0);
            foreach (var w in wings)
                w.t.localRotation = w.rot * Quaternion.Euler(0, 0, flap * w.side) * Quaternion.Euler(0, -12f * w.side * windup, 0);

            if (tail != null) tail.t.localRotation = tail.rot * Quaternion.Euler(0, Mathf.Sin(t * 3f) * 14f, 0);

            if (body != null)
            {
                float bob = Mathf.Abs(Mathf.Sin(walk)) * 0.05f * move;
                float hoverY = hover ? Mathf.Sin(t * 3f) * 0.08f : 0;
                body.t.localPosition = body.pos + new Vector3(0, bob + hoverY, 0);
                body.t.localRotation = body.rot * Quaternion.Euler(10f * strike - 14f * windup + 4f * move, 0, 0);
                if (squash)
                {
                    float s = 1f + Mathf.Sin(t * (move > 0 ? 10f : 3f)) * (move > 0 ? 0.12f : 0.05f);
                    body.t.localScale = new Vector3(body.scale.x / Mathf.Sqrt(s), body.scale.y * s, body.scale.z / Mathf.Sqrt(s));
                }
            }
            if (head != null) head.t.localRotation = head.rot * Quaternion.Euler(Mathf.Sin(t * 1.7f) * 3f, Mathf.Sin(t * 0.9f) * 6f, 0);
        }
    }
}
