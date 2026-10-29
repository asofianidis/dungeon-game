namespace Depthbound
{
    public enum Element { Physical, Fire, Cold, Lightning, Poison, Holy, Shadow }

    public enum StatusType { None, Burn, Poison, Bleed, Chill, Freeze, Stun, Shock, Weaken, Vulnerable, Fear }

    public enum School { Blade, Warcry, Assassination, Archery, Fire, Frost, Storm, Venom, Holy, Necromancy, Blood, Void }

    public enum ItemKind { Weapon, Offhand, Helm, Chest, Gloves, Boots, Belt, Amulet, Ring }

    public enum EquipSlot { Weapon, Offhand, Helm, Chest, Gloves, Boots, Belt, Amulet, Ring1, Ring2 }

    public enum Rarity { Common, Magic, Rare, Epic, Legendary }

    public enum Faction { Player, Monster }

    public enum Targeting { Self, Melee, Projectile, Ground, Chain, Dash, Blink, Summon, Target }

    public enum ProcTrigger { None, OnHit, OnCrit, OnKill, OnStruck, OnCast }

    public enum AiType { Melee, Ranged, Caster, Exploder, Summoner, Minion }

    public enum Keystone
    {
        None,
        BloodMagic,      // skills cost life instead of mana
        Berserker,       // more damage at low life
        Shatter,         // +damage vs chilled / frozen
        PlagueBearer,    // poisoned enemies explode on death
        Pyre,            // burning enemies explode on death
        Echo,            // spells may cast twice
        Juggernaut,      // cannot be stunned or frozen
        GlassCannon,     // +damage, -life
        Vampirism,       // life steal works on spells
        Conduit,         // physical damage converted to lightning
        Inferno,         // physical damage converted to fire
        Permafrost,      // physical damage converted to cold
        Necrolord,       // +2 max minions, minions heal you on hit
        Bulwark,         // block chance also applies to spells, +block
    }

    public static class EnumUtil
    {
        public static bool IsRing(EquipSlot s) => s == EquipSlot.Ring1 || s == EquipSlot.Ring2;

        public static ItemKind KindForSlot(EquipSlot s)
        {
            switch (s)
            {
                case EquipSlot.Weapon: return ItemKind.Weapon;
                case EquipSlot.Offhand: return ItemKind.Offhand;
                case EquipSlot.Helm: return ItemKind.Helm;
                case EquipSlot.Chest: return ItemKind.Chest;
                case EquipSlot.Gloves: return ItemKind.Gloves;
                case EquipSlot.Boots: return ItemKind.Boots;
                case EquipSlot.Belt: return ItemKind.Belt;
                case EquipSlot.Amulet: return ItemKind.Amulet;
                default: return ItemKind.Ring;
            }
        }

        public static string KeystoneDesc(Keystone k)
        {
            switch (k)
            {
                case Keystone.BloodMagic: return "Blood Magic: skills cost Life instead of Mana";
                case Keystone.Berserker: return "Berserker: up to 60% more damage as your Life drops";
                case Keystone.Shatter: return "Shatter: 40% more damage against Chilled or Frozen enemies";
                case Keystone.PlagueBearer: return "Plague Bearer: Poisoned enemies explode on death";
                case Keystone.Pyre: return "Pyre: Burning enemies explode on death";
                case Keystone.Echo: return "Echo: spells have a 25% chance to cast twice";
                case Keystone.Juggernaut: return "Juggernaut: cannot be Stunned or Frozen";
                case Keystone.GlassCannon: return "Glass Cannon: 50% more damage, 35% less Life";
                case Keystone.Vampirism: return "Vampirism: Life Steal also applies to spells";
                case Keystone.Conduit: return "Conduit: Physical damage is converted to Lightning";
                case Keystone.Inferno: return "Inferno: Physical damage is converted to Fire";
                case Keystone.Permafrost: return "Permafrost: Physical damage is converted to Cold";
                case Keystone.Necrolord: return "Necrolord: +2 maximum minions, minion hits heal you";
                case Keystone.Bulwark: return "Bulwark: +10% Block, Block also stops spells";
                default: return "";
            }
        }
    }
}
