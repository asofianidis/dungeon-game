using UnityEngine;

namespace Depthbound
{
    public class HitInfo
    {
        public Actor source, target;
        public float baseDamage;
        public Element element;
        public bool isAttack, isDot, isProc, melee;
        public bool canCrit = true;
        public StatusType status;
        public float statusChance;
        public int statusTurns = 3;
        public float bonusCrit, executeBonus;
    }

    public static class Combat
    {
        static World W => World.Current;
        static int procDepth;

        public static int Dist(Vector2Int a, Vector2Int b) => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
        public static int Dist(Actor a, Actor b) => Dist(a.Pos, b.Pos);

        public static float RollWeapon(Actor a) => W.rng.Range(a.weaponMin, a.weaponMax) + a.Get(StatType.FlatAttackDamage);

        public static Element Convert(Actor src, Element e)
        {
            if (e != Element.Physical || src == null) return e;
            if (src.keystones.Contains(Keystone.Conduit)) return Element.Lightning;
            if (src.keystones.Contains(Keystone.Inferno)) return Element.Fire;
            if (src.keystones.Contains(Keystone.Permafrost)) return Element.Cold;
            return e;
        }

        /// <summary>Adjacent and not cutting around a wall corner.</summary>
        public static bool CanMelee(Actor a, Actor b)
        {
            int dx = b.x - a.x, dy = b.y - a.y;
            if (Mathf.Abs(dx) > 1 || Mathf.Abs(dy) > 1 || (dx == 0 && dy == 0)) return false;
            if (dx != 0 && dy != 0)
                return W.map.Get(a.x + dx, a.y) != Tile.Wall && W.map.Get(a.x, a.y + dy) != Tile.Wall;
            return true;
        }

        public static void BasicAttack(Actor a, Actor t)
        {
            bool ranged = Dist(a, t) > 1;
            var col = SkillDatabase.ElementColor(Convert(a, a.attackElement));
            if (ranged) Fx.Projectile(a.Pos, t.Pos, col); else Fx.Flash(t.Pos, col);
            int swings = 1 + (W.rng.Chance(a.Get(StatType.DoubleStrike) / 100f) ? 1 : 0);
            for (int i = 0; i < swings && !t.dead && !a.dead; i++)
            {
                var h = new HitInfo { source = a, target = t, baseDamage = RollWeapon(a), element = a.attackElement, isAttack = true, melee = !ranged };
                if (a is Monster m && m.onHit != StatusType.None) { h.status = m.onHit; h.statusChance = m.onHitChance; }
                Deal(h);
            }
        }

        public static int Deal(HitInfo h)
        {
            var s = h.source; var t = h.target;
            if (t == null || t.dead) return 0;
            var e = Convert(s, h.element);

            if (h.isAttack && s != null && !h.isDot)
            {
                float acc = s.Get(StatType.Accuracy), eva = t.Get(StatType.Evasion);
                float hit = Mathf.Clamp(acc / (acc + eva * 0.6f), 0.2f, 0.97f);
                if (!W.rng.Chance(hit)) { Fx.Text(t.Pos, "miss", new Color(0.7f, 0.7f, 0.7f), 0.8f); return 0; }
            }
            if (!h.isDot && (h.isAttack || t.keystones.Contains(Keystone.Bulwark)))
            {
                float block = Mathf.Min(t.Get(StatType.BlockChance), 60f);
                if (block > 0 && W.rng.Chance(block / 100f)) { Fx.Text(t.Pos, "block", new Color(0.8f, 0.8f, 1f), 0.8f); return 0; }
            }

            float dmg = h.baseDamage;
            bool crit = false;
            if (s != null && !h.isDot)
            {
                float inc = (h.isAttack ? s.Get(StatType.AttackDamage) : s.Get(StatType.SpellDamage)) + s.Get(StatInfo.DamageStatFor(e));
                dmg *= Mathf.Max(0.1f, 1f + inc / 100f);
                if (s.keystones.Contains(Keystone.Berserker)) dmg *= 1f + 0.6f * (1f - s.LifePct);
                if (s.keystones.Contains(Keystone.GlassCannon)) dmg *= 1.5f;
                if (s.Has(StatusType.Weaken)) dmg *= 0.7f;
                if (s.keystones.Contains(Keystone.Shatter) && (t.Has(StatusType.Chill) || t.Has(StatusType.Freeze))) dmg *= 1.4f;
                if (h.executeBonus > 0 && t.LifePct < 0.3f) dmg *= 1f + h.executeBonus / 100f;
                if (h.canCrit)
                {
                    float cc = Mathf.Min(s.Get(StatType.CritChance) + h.bonusCrit, 95f);
                    if (W.rng.Chance(cc / 100f)) { crit = true; dmg *= 1f + s.Get(StatType.CritDamage) / 100f; }
                }
            }
            if (t.Has(StatusType.Shock)) dmg *= 1.2f;
            if (t.Has(StatusType.Vulnerable)) dmg *= 1.25f;
            float preMitigation = dmg;

            dmg = Mitigate(t, dmg, e, s != null ? s.level : W.depth, h.isDot);
            int final = Mathf.Max(1, Mathf.RoundToInt(dmg));
            ApplyDamage(t, final);

            Color tc = t == W.player ? new Color(1f, 0.3f, 0.3f) : SkillDatabase.ElementColor(e);
            Fx.Text(t.Pos, crit ? final + "!" : final.ToString(), tc, crit ? 1.35f : (h.isDot ? 0.75f : 1f));

            if (s != null && !h.isDot)
            {
                float ls = s.Get(StatType.LifeSteal);
                if (ls > 0 && (h.isAttack || s.keystones.Contains(Keystone.Vampirism))) s.Heal(final * Mathf.Min(ls, 40f) / 100f);
                if (s.owner != null && s.owner.keystones.Contains(Keystone.Necrolord)) s.owner.Heal(final * 0.1f);

                if (h.status != StatusType.None)
                {
                    float ch = h.statusChance + s.Get(StatType.StatusChance);
                    if (W.rng.Chance(ch / 100f)) ApplyStatus(s, t, h.status, h.statusTurns, preMitigation, e);
                }
                if (h.melee && h.isAttack && t.Get(StatType.Thorns) > 0 && !s.dead)
                {
                    int th = Mathf.RoundToInt(t.Get(StatType.Thorns));
                    ApplyDamage(s, th);
                    Fx.Text(s.Pos, th.ToString(), new Color(0.6f, 0.9f, 0.4f), 0.8f);
                    if (s.life <= 0) W.Kill(s, t);
                }
            }

            if (s != null && !h.isProc && !h.isDot && !s.dead)
            {
                TriggerProcs(s, ProcTrigger.OnHit, t, t.Pos);
                if (crit) TriggerProcs(s, ProcTrigger.OnCrit, t, t.Pos);
            }
            if (t.life <= 0) W.Kill(t, s);
            else if (s != null && !h.isProc && !h.isDot && !s.dead) TriggerProcs(t, ProcTrigger.OnStruck, s, s.Pos);
            return final;
        }

        public static float Mitigate(Actor t, float dmg, Element e, int attackerLevel, bool isDot)
        {
            if (e == Element.Physical)
            {
                if (!isDot)
                {
                    float armor = Mathf.Max(0, t.Get(StatType.Armor));
                    float red = armor / (armor + 12f * attackerLevel + 40f);
                    dmg *= 1f - Mathf.Min(red, 0.75f);
                }
            }
            else
            {
                float res = Mathf.Clamp(t.Get(StatInfo.ResFor(e)), -100f, 75f);
                dmg *= 1f - res / 100f;
            }
            return dmg * (1f - Mathf.Min(t.Get(StatType.DamageReduction), 50f) / 100f);
        }

        public static void ApplyDamage(Actor t, int amount)
        {
            if (t.shield > 0)
            {
                float absorbed = Mathf.Min(t.shield, amount);
                t.shield -= absorbed;
                amount -= Mathf.RoundToInt(absorbed);
            }
            t.life -= amount;
            if (t is Monster m && !m.awake) m.awake = true;
        }

        public static void ApplyStatus(Actor src, Actor t, StatusType type, int turns, float hitDamage, Element e)
        {
            float dotMult = 1f + (src != null ? src.Get(StatType.DotDamage) : 0) / 100f;
            float power = 0;
            Element de = e;
            switch (type)
            {
                case StatusType.Burn: power = hitDamage * 0.3f * dotMult; de = Element.Fire; break;
                case StatusType.Poison: power = hitDamage * 0.2f * dotMult; de = Element.Poison; break;
                case StatusType.Bleed: power = hitDamage * 0.25f * dotMult; de = Element.Physical; break;
            }
            if (power > 0) power = Mathf.Max(1, power);
            bool had = t.Has(type);
            t.AddStatus(type, turns, power, de, src);
            if (!had && t.Has(type)) Fx.Text(t.Pos, type.ToString(), StatusColor(type), 0.7f);
        }

        public static Color StatusColor(StatusType t)
        {
            switch (t)
            {
                case StatusType.Burn: return new Color(1f, 0.5f, 0.1f);
                case StatusType.Poison: return new Color(0.4f, 0.95f, 0.3f);
                case StatusType.Bleed: return new Color(0.9f, 0.1f, 0.15f);
                case StatusType.Chill: return new Color(0.6f, 0.85f, 1f);
                case StatusType.Freeze: return new Color(0.4f, 0.7f, 1f);
                case StatusType.Stun: return new Color(1f, 0.95f, 0.5f);
                case StatusType.Shock: return new Color(1f, 1f, 0.4f);
                case StatusType.Weaken: return new Color(0.7f, 0.6f, 0.5f);
                case StatusType.Vulnerable: return new Color(0.9f, 0.4f, 0.9f);
                case StatusType.Fear: return new Color(0.6f, 0.3f, 0.9f);
                default: return Color.white;
            }
        }

        public static void TickStatuses(Actor a)
        {
            for (int i = a.statuses.Count - 1; i >= 0 && i < a.statuses.Count; i--)
            {
                var s = a.statuses[i];
                if (s.power > 0 && (s.type == StatusType.Burn || s.type == StatusType.Poison || s.type == StatusType.Bleed))
                {
                    float dmg = s.power;
                    if (a.Has(StatusType.Shock)) dmg *= 1.2f;
                    if (a.Has(StatusType.Vulnerable)) dmg *= 1.25f;
                    dmg = Mitigate(a, dmg, s.element, s.source != null ? s.source.level : W.depth, true);
                    int final = Mathf.Max(1, Mathf.RoundToInt(dmg));
                    ApplyDamage(a, final);
                    Fx.Text(a.Pos, final.ToString(), StatusColor(s.type), 0.7f);
                    if (a.life <= 0) { W.Kill(a, s.source); return; }
                }
                s.turns--;
                if (s.turns <= 0) a.statuses.Remove(s);
            }
        }

        /// <summary>Raw environmental damage (traps, lava) with no attacker.</summary>
        public static void Environmental(Actor a, float amount, Element e, string cause)
        {
            float dmg = Mitigate(a, amount, e, W.depth, false);
            int final = Mathf.Max(1, Mathf.RoundToInt(dmg));
            ApplyDamage(a, final);
            Fx.Text(a.Pos, final.ToString(), SkillDatabase.ElementColor(e), 0.9f);
            if (a.life <= 0)
            {
                if (a == W.player) W.stats.killedBy = cause;
                W.Kill(a, null);
            }
        }

        /// <summary>Area damage around a point that hits everyone hostile to the source faction.</summary>
        public static void Explode(Actor source, Faction sourceFaction, Vector2Int center, int radius, float damage, Element e)
        {
            Fx.Burst(center, radius, SkillDatabase.ElementColor(e));
            foreach (var t in W.ActorsInRadius(center, radius))
            {
                if (t.faction == sourceFaction || t.dead) continue;
                Deal(new HitInfo { source = source, target = t, baseDamage = damage, element = e, isProc = true, canCrit = false });
            }
        }

        public static void TriggerProcs(Actor owner, ProcTrigger trigger, Actor other, Vector2Int pos)
        {
            if (owner == null || owner.procs.Count == 0 || procDepth > 2) return;
            procDepth++;
            try
            {
                var list = owner.procs.ToArray();
                foreach (var p in list)
                {
                    if (p.trigger != trigger || owner.dead) continue;
                    if (!W.rng.Chance(p.chance / 100f)) continue;
                    var def = SkillDatabase.Get(p.skillId);
                    if (def == null) continue;
                    var aim = other != null && !other.dead ? other.Pos : pos;
                    SkillExecutor.Cast(owner, def, Mathf.Max(1, p.rank), aim, true);
                }
            }
            finally { procDepth--; }
        }
    }
}
