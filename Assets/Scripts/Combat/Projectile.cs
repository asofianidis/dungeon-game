using System.Collections.Generic;
using UnityEngine;

namespace Depthbound
{
    /// <summary>A projectile travelling through the world in real time. Can be dodged.</summary>
    public class Projectile
    {
        public Actor owner;
        public Faction faction;
        public Vector2 pos, dir;
        public float speed = 12f, remaining, size = 0.28f;
        public Color color = Color.white;

        // Either a skill projectile or a basic weapon attack.
        public SkillDef skill;
        public int rank = 1;
        public bool isProc, basic;

        public bool pierce;
        public int explodeRadius;
        public bool dead;
        public readonly HashSet<Actor> hit = new HashSet<Actor>();
    }
}
