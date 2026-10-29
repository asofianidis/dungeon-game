# Depthbound

A procedurally generated, real-time 3D action roguelike built in Unity 6. You are an Archdemon, the champion of a succubus princess, diving into an endless Abyss to recover her family's stolen artifacts from the human army.

Dungeons are generated in code. Characters and props are anime-style models built by a Blender script (`Tools/Blender`), rendered with cel shading in URP.

## Story and the castle hub

- Every run begins in **Castle Vaelthorn**, the seat of **Princess Lysandra**, Succubus Princess of the Ninth Circle. Her throne hall has the princess, a quartermaster (shop), a healing fountain, a shrine, an armory chest, and the **Abyssal Gate**.
- On your first visit she gives you the quest. The human army's **Radiant Crusade** scattered the **six Royal Artifacts of House Vaelthorn** into the Abyss and bound its guardians to keep them. Without them, the castle's wards fail and the human army marches in.
- Each biome's guardian (every 5th depth) holds one artifact: the Crown of Ashen Night, Thornveil Mirror, Tidebound Chalice, Heart of the Frozen Throne, Hellforge Scepter, and Sovereign's Sigil. Each one you recover is **permanent** and grants +8% Attack/Spell Damage and +5% Life on every future run.
- A slain guardian opens a **portal home**. Report to the princess, restock, then take the gate back to the next depth. Recover all six to break the human army.
- **The Radiant Crusade** (crusaders, crossbowmen, inquisitors, war priests) fights alongside the Abyss's monsters at every depth.
- You play a **High Demon**: an Archdemon who starts at level 3. Classes are demonic disciplines (Hellblade, Shadowfiend, Infernalist, Hellhunter, Rimefiend, Dread Templar, Soulbinder, Blood Fiend, Storm Archon, Plague Lord).

## 3D presentation

- Each floor is a single generated mesh: textured walls and floors, emissive lava, and fog of war in the vertex colors.
- An angled perspective camera with moonlight shadows, a torch light that follows you, and flickering braziers and portals.
- **Universal Render Pipeline** with custom HLSL shaders. Characters get anime cel shading, a rim light and ink outlines. Faces are painted procedurally (glossy irises, lashes, brows, blush) onto UV-mapped heads.
- **Sculpted, not assembled.** Every character and creature is sculpted as a signed distance field and meshed in Blender, so each body is one seamless surface: pelvis, rib cage, bust or pecs, deltoids, tapered limbs with muscle bulges, knees, feet, and hands with jointed fingers. Heads have a V-shaped anime jaw, and hair is a sculpted mass with a jagged fringe and flowing locks.
- **Clothing shaped from the body.** Bodices, plate armor, greaves, stockings and boots are shells of the body's own field, so they fit the figure. Skirts flare with folds and ruffled hems; capes, sleeves, pauldrons, helms and trims are layered on top.
- **Baked shading.** Ambient occlusion and tint gradients are baked per vertex and multiplied in by the shader, so creases, armor overlaps and hair shadow the surface under them.
- **Painted surface detail.** Cube-projected UVs carry procedural detail textures in Unity: cloth weave, brushed metal, chain mail, leather and wood grain, wing veins, stone.
- **Keyframed animation.** Every character has a skinned armature (bone-heat weights for bodies, weights copied from the body for clothing) and Blender-authored clips (Idle, Walk, Attack, Cast, Hit, Death) with anticipation, follow-through and easing. Unity crossfades the loops, layers one-shots over them, times monster swings to their wind-ups, and plays death poses before bodies sink away.
- **Live dialogue portraits.** An off-screen portrait studio renders the speaking character, animated, into the dialogue box.
- Models: the Archdemon, Princess Lysandra, templar knight, undead ghoul, hellhound, giant bat, cave spider, rock golem, ooze, imp quartermaster, plus every prop (throne, gate, pillars, braziers, chests, shrine, fountain, relic, ...).

## Real-time combat

- Free WASD movement with wall sliding and corner assist, plus a **dodge roll** (Space) with brief invulnerability.
- Hold Left Mouse to attack toward the cursor. Melee hits the enemy you're facing; bows and wands fire projectiles.
- Monster and player projectiles travel through the world and can be dodged.
- Melee monsters **flash red during a short windup** before swinging. Step or roll away and the swing misses.
- Attack/Cast Speed and Movement Speed are stats on gear, skills and shrines. Cooldowns, buffs and status effects run in seconds.
- The game pauses while any panel (inventory, skills, hero, help) is open.

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
| Monsters | 30 biome monsters + 4 human army units + 6 artifact guardians, 15 elite affixes |

Rare and better gear can:

- add ranks to a single skill or a whole school,
- **grant skills you haven't learned**, or
- **cast skills on hit, crit, kill, cast, or when struck**.

Combined with attribute allocation and 72 skills that stack across schools, this gives an effectively unbounded number of build combinations.

## Controls

| Action | Keys |
| --- | --- |
| Move | WASD / arrows |
| Attack | Hold Left Mouse (aims at the cursor) |
| Dodge roll | Space |
| Skills | Hold 1-8 (aimed at the cursor); Right Mouse casts slot 1 |
| Interact / talk / portals / descend | E (or click chests, shrines, merchants) |
| Potions | R (health), F (mana) |
| Panels | I inventory, K skills, P hero, H help, Esc menu |
| Zoom | Mouse wheel |

## Project layout

```
Assets/
  Scenes/Main.unity          Single bootstrap scene
  Resources/Models/          FBX models exported by Tools/Blender/build_models.py
  Resources/Shaders/         URP HLSL: Depthbound/Lit (cel shading + outlines) and Depthbound/Map (dungeon mesh with fog of war)
  Settings/                  URP pipeline and renderer assets
  Scripts/
    Core/      GameManager (state + input), World (real-time simulation, hub, floors, loot), Story (princess + artifacts), Rng, MetaProgress
    Dungeon/   DungeonMap (tiles, FOV, LOS, distance fields), DungeonGenerator (BSP/caves/ruins/hybrid + castle hub)
    Entities/  Actor, Player, Monster, class + monster databases
    Stats/     StatType definitions and formatting
    Skills/    SkillDef, SkillDatabase (all 72 + monster/proc skills), SkillExecutor
    Items/     Item, ItemDatabase (bases, affixes, legendaries), ItemGenerator
    Combat/    Combat (hit resolution, statuses, procs), Projectile, MonsterAI (real-time steering, windups)
    Rendering/ WorldRenderer (3D), ModelLibrary (FBX loading, cel materials), CharacterAnimator (clip playback), ModelRig (procedural fallback),
               FaceFactory (anime faces), DetailFactory (surface detail textures),
               PortraitStudio (dialogue portraits), SpriteFactory (textures/icons), Fx
    UI/        GameUI (IMGUI: all screens and panels)
  Editor/
    ProjectSetup.cs   Menu: Depthbound > Setup Main Scene / Build Windows
    UrpSetup.cs       Menu: Depthbound > Configure URP
    ModelImportSettings.cs  Import rules for the Blender FBX files (axis baking, skeletons)
    SmokeTest.cs      Headless test: data integrity, 300 generated floors, movement/navigation, simulated runs per class
    BalanceBot.cs     Real-time autoplay bot that reports how deep each class gets in an hour of play
Tools/Blender/
  build_models.py    Models, rigs and animates every character and prop, exporting FBX (pass names to rebuild a subset)
  dbmodel/sdf.py       Signed-distance-field sculpting: primitives, smooth unions, surface-nets meshing, gradient normals
  dbmodel/anatomy.py   Sculpted bodies, heads, hands, hair, horns, tails and wings
  dbmodel/garments.py  Clothing and armor as shells of the body: bodices, plates, skirts, capes, sleeves, domes
  dbmodel/shade.py     Baked vertex AO and tints, bone-heat weights, weight transfer to clothing
  dbmodel/geometry.py  Hard-surface modeling, UV, rigging and export helpers
  dbmodel/anim.py      Keyframed clip authoring for biped, quadruped, spider and flyer rigs
  preview_models.py  Renders a lineup PNG of exported models (yaw=40 for a three-quarter view, pose=Walk:5 for a clip frame,
                     color=VERTEX to see the baked AO, px=600 / zoom=3 focus=1.05 for close-ups)
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

# Rebuild the 3D models with Blender 5.x (writes Assets/Resources/Models/*.fbx; about 2 minutes)
blender -b --factory-startup --python Tools/Blender/build_models.py -- Assets/Resources/Models

# Scripted screenshots from a built player (uses a throwaway save)
Builds/Windows/Depthbound.exe -screen-width 1600 -screen-height 900 -screen-fullscreen 0 -autoshot shots
```

## Ideas for what's next

- Save and resume mid-run
- Sound effects and music
- Hand-painted texture maps, facial expressions, and more clip variety (run, dodge roll, emotes)
- More castle NPCs and branching dialogue
- Set items, crafting/rerolling at the merchant, more biomes and bosses
