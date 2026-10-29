# Depthbound

A procedurally generated, turn-based 2D roguelike dungeon crawler built in Unity 6. Dive as deep as you can, die, grow stronger, and dive again.

Everything (dungeons, monsters, sprites, icons, and UI) is generated from code, so the project has no binary art dependencies.

## Core loop

- **Endless descent.** Each floor is procedurally generated from the run seed. Six biomes (Forgotten Crypt, Fungal Caverns, Sunken Ruins, Frozen Depths, Molten Forge, Abyssal Halls) cycle forever, getting harder each cycle.
- **Bosses every 5 floors** seal the stairs until they're defeated.
- **Permadeath with meta-progression.** Dying earns Soul Essence for the Soul Altar (permanent upgrades) and unlocks new classes.
- **Corruption levels.** Reach depth 11 to unlock the next tier. Monsters get stronger, and loot and essence get better.
- **Daily Descent.** Everyone plays the same seed each day.
- **Codex.** Track every legendary you've found.

## Build depth

| System | Content |
| --- | --- |
| Classes | 10 starting archetypes (any class can learn any skill) |
| Skill schools | 12 schools x 6 tiers = **72 skills** (actives, passives, procs, keystones) |
| Keystones | 14 rule-changing effects (Blood Magic, Echo, Shatter, Necrolord, Conduit, ...) |
| Item bases | 60 bases across 9 slot types (1H/2H, melee/ranged weapons) |
| Affixes | 56 prefixes/suffixes that scale with item level |
| Rarities | Common, Magic, Rare, Epic, Legendary |
| Legendaries | 32 uniques with keystones, granted skills, and procs |
| Monsters | 30 biome monsters + 6 bosses, 15 elite affixes (Vampiric, Volatile, Teleporter, ...) |

Rare and better gear can:

- add ranks to a single skill or a whole school,
- **grant skills you haven't learned**, or
- **cast skills on hit, crit, kill, cast, or when struck**.

Combined with attribute allocation and 72 skills that stack across schools, this gives an effectively unbounded number of build combinations.

## Controls

| Action | Keys |
| --- | --- |
| Move / attack | WASD, arrows, numpad; diagonals Q E Z C |
| Travel | Left-click a tile |
| Skills | 1-8 (aimed at the mouse cursor); right-click casts slot 1 |
| Wait / pick up | Space |
| Descend | Enter or `>` on stairs |
| Potions | R (health), F (mana) |
| Panels | I inventory, K skills, P hero, H help, Esc menu |
| Zoom | Mouse wheel |

## Project layout

```
Assets/
  Scenes/Main.unity          Single bootstrap scene
  Scripts/
    Core/      GameManager (state + input), World (turn loop, floors, loot), Rng, MetaProgress
    Dungeon/   DungeonMap (tiles, FOV, LOS, distance fields), DungeonGenerator (BSP/caves/ruins/hybrid)
    Entities/  Actor, Player, Monster, class + monster databases
    Stats/     StatType definitions and formatting
    Skills/    SkillDef, SkillDatabase (all 72 + monster/proc skills), SkillExecutor
    Items/     Item, ItemDatabase (bases, affixes, legendaries), ItemGenerator
    Combat/    Combat (hit resolution, statuses, procs), MonsterAI
    Rendering/ SpriteFactory (procedural pixel art), WorldRenderer, Fx
    UI/        GameUI (IMGUI: all screens and panels)
  Editor/
    ProjectSetup.cs   Menu: Depthbound > Setup Main Scene / Build Windows
    SmokeTest.cs      Headless test: data integrity, 300 generated floors, simulated runs per class
    BalanceBot.cs     Autoplay bot that reports how deep each class gets
```

## Running

1. Open the folder in **Unity 6000.6.7f1** (or newer Unity 6).
2. Open `Assets/Scenes/Main.unity` and press Play. The game also bootstraps itself from any scene.

### Headless tools

```powershell
# Data integrity + generation + simulated runs (exit code 0 = pass)
Unity.exe -batchmode -nographics -projectPath . -executeMethod Depthbound.EditorTools.SmokeTest.Run -logFile smoke.log

# Difficulty curve report
Unity.exe -batchmode -nographics -projectPath . -executeMethod Depthbound.EditorTools.BalanceBot.Run -logFile balance.log

# Windows build -> Builds/Windows/Depthbound.exe
Unity.exe -batchmode -nographics -projectPath . -executeMethod Depthbound.EditorTools.ProjectSetup.BuildWindows -logFile build.log

# Scripted screenshots from a built player
Builds/Windows/Depthbound.exe -screen-width 1600 -screen-height 900 -screen-fullscreen 0 -autoshot shots
```

## Ideas for what's next

- Save and resume mid-run
- Sound effects and music
- Hand-made art (e.g. Blender-rendered sprite sheets) to replace the procedural placeholders
- Set items, crafting/rerolling at the merchant, more biomes and bosses
