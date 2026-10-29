using UnityEngine;

namespace Depthbound
{
    /// <summary>
    /// Plays the keyframed clips authored in Blender (Idle, Walk, Attack, Cast, Hit, Death).
    /// Idle/Walk loop on layer 0, one-shots override them on layer 1, and Death takes over on layer 2.
    /// </summary>
    public class CharacterAnimator : MonoBehaviour
    {
        Animation anim;
        string loco, oneShot;
        float oneShotEnd;
        bool dead;

        public bool Busy => oneShot != null;
        public float DeathLength => anim["Death"] != null ? anim["Death"].length : 0.5f;

        public static CharacterAnimator Attach(GameObject model)
        {
            var a = model.GetComponentInChildren<Animation>(true);
            if (a == null || a.GetClipCount() == 0 || a["Idle"] == null) return null;
            var ca = model.AddComponent<CharacterAnimator>();
            ca.anim = a;
            a.cullingType = AnimationCullingType.AlwaysAnimate;
            foreach (AnimationState s in a)
            {
                s.layer = s.name == "Death" ? 2 : (s.name == "Idle" || s.name == "Walk") ? 0 : 1;
                s.wrapMode = s.name == "Death" ? WrapMode.ClampForever : (s.name == "Idle" || s.name == "Walk") ? WrapMode.Loop : WrapMode.Once;
            }
            // Desynchronize crowds so monsters don't breathe in unison.
            a["Idle"].time = Random.value * a["Idle"].length;
            a.Play("Idle");
            ca.loco = "Idle";
            return ca;
        }

        /// <param name="move">0..1 how much the character is walking</param>
        /// <param name="speed">playback speed for the walk cycle</param>
        public void Locomotion(float move, float speed)
        {
            if (dead) return;
            string target = move > 0.35f && anim["Walk"] != null ? "Walk" : "Idle";
            if (target != loco)
            {
                anim.CrossFade(target, 0.18f);
                loco = target;
            }
            if (anim["Walk"] != null) anim["Walk"].speed = Mathf.Clamp(speed, 0.5f, 2.2f);
        }

        /// <summary>Play a one-shot clip, optionally stretched to last `duration` seconds.</summary>
        public void Play(string clip, float duration = -1f)
        {
            if (dead || anim[clip] == null) return;
            var s = anim[clip];
            s.speed = duration > 0 ? s.length / duration : 1f;
            s.time = 0;
            anim.CrossFade(clip, 0.05f);
            oneShot = clip;
            oneShotEnd = Time.time + s.length / Mathf.Max(0.01f, s.speed);
        }

        public void Die()
        {
            if (dead) return;
            dead = true;
            if (anim["Death"] != null) anim.CrossFade("Death", 0.1f, PlayMode.StopAll);
        }

        void Update()
        {
            if (oneShot != null && Time.time >= oneShotEnd - 0.1f)
            {
                anim.Blend(oneShot, 0f, 0.12f);
                oneShot = null;
            }
        }
    }
}
