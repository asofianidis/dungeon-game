"""
Builds every 3D model for Depthbound from primitives and exports them as FBX for Unity.

Usage (from the repo root):
    blender -b --factory-startup --python Tools/Blender/build_models.py -- Assets/Resources/Models

Conventions
- 1 Blender unit = 1 tile. Characters stand on z=0 and face -Y (Blender's front view).
- Parts that animate are separate objects whose origin sits at the joint, named so the game
  can find them: Body, Head, ArmL/ArmR, LegL/LegR (LegL2, LegR2... for extra legs), WingL/WingR,
  Tail, Lid, Crystal, Fire.
- The FBX hierarchy is exported flat (every part is a direct child of Root), because Blender's
  baked axis conversion mangles nested transforms. A part's logical parent is encoded in its
  name as "Part@Parent"; the game re-parents parts at load time.
- Materials are named by role (Primary, Secondary, Skin, Eye, Glow, ...). The game replaces them at
  runtime, tinting Primary/Secondary per monster and making Eye/Glow/Fire emissive.
"""
import bpy
import bmesh
import math
import os
import sys
from mathutils import Matrix, Vector, Euler

OUT = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else "Models"
os.makedirs(OUT, exist_ok=True)

PALETTE = {
    "Primary": (0.55, 0.1, 0.1, 1), "Secondary": (0.3, 0.3, 0.3, 1), "Skin": (0.5, 0.1, 0.12, 1),
    "SkinPale": (0.85, 0.7, 0.85, 1), "Horn": (0.12, 0.1, 0.09, 1), "Eye": (1.0, 0.75, 0.2, 1),
    "Glow": (0.75, 0.3, 1.0, 1), "Metal": (0.62, 0.63, 0.68, 1), "Gold": (0.92, 0.72, 0.2, 1),
    "Wood": (0.38, 0.23, 0.11, 1), "Stone": (0.42, 0.4, 0.45, 1), "Cloth": (0.42, 0.06, 0.22, 1),
    "Dark": (0.05, 0.04, 0.06, 1), "Water": (0.2, 0.45, 0.85, 1), "Fire": (1.0, 0.5, 0.1, 1),
    "Hair": (0.12, 0.05, 0.18, 1), "White": (0.9, 0.9, 0.86, 1), "Wing": (0.28, 0.05, 0.1, 1),
}
MATS = {}
WORLD_PIVOT = {}
CURRENT_ROOT = [None]


def mat(name):
    if name in MATS:
        return MATS[name]
    m = bpy.data.materials.new(name)
    m.diffuse_color = PALETTE[name]
    try:
        m.use_nodes = True
    except Exception:
        pass
    if m.node_tree:
        bsdf = m.node_tree.nodes.get("Principled BSDF")
        if bsdf:
            bsdf.inputs["Base Color"].default_value = PALETTE[name]
    MATS[name] = m
    return m


def clear():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for me in list(bpy.data.meshes):
        bpy.data.meshes.remove(me)
    WORLD_PIVOT.clear()


def make_mesh(name, kind, size, segments):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    if kind == "cube":
        bmesh.ops.create_cube(bm, size=1.0)
    elif kind == "sphere":
        bmesh.ops.create_uvsphere(bm, u_segments=segments, v_segments=max(4, segments // 2), radius=0.5)
    elif kind == "cyl":
        bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segments, radius1=0.5, radius2=0.5, depth=1.0)
    elif kind == "cone":
        bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segments, radius1=0.5, radius2=0.0, depth=1.0)
    elif kind == "frustum":
        bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segments, radius1=0.5, radius2=0.3, depth=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    bm.to_mesh(me)
    bm.free()
    return me


def root(name="Root"):
    ob = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(ob)
    WORLD_PIVOT[ob.name] = Vector((0, 0, 0))
    CURRENT_ROOT[0] = ob
    return ob


def part(name, kind, size, loc, material, parent, pivot=None, rot=(0, 0, 0), segments=12):
    """Create a primitive part. loc is the world-space center; pivot is the world-space joint."""
    me = make_mesh(name, kind, size, segments)
    me.transform(Euler([math.radians(a) for a in rot]).to_matrix().to_4x4())
    piv = Vector(pivot) if pivot is not None else Vector(loc)
    me.transform(Matrix.Translation(Vector(loc) - piv))
    me.materials.append(mat(material))
    root_ob = CURRENT_ROOT[0]
    full = name if parent is root_ob else name + "@" + parent.name.split("@")[0].split(".")[0]
    ob = bpy.data.objects.new(full, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.parent = root_ob
    ob.location = piv
    WORLD_PIVOT[ob.name] = piv
    return ob


def mirror(name, kind, size, loc, material, parent, pivot=None, rot=(0, 0, 0), segments=12):
    """Create a left/right pair; the given coordinates describe the left (+X) side."""
    x, y, z = loc
    l = part(name + "L", kind, size, (x, y, z), material, parent,
             None if pivot is None else pivot, rot, segments)
    rp = None if pivot is None else (-pivot[0], pivot[1], pivot[2])
    r = part(name + "R", kind, size, (-x, y, z), material, parent, rp, (rot[0], -rot[1], -rot[2]), segments)
    return l, r


def export(name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in bpy.context.scene.objects:
        o.select_set(True)
    path = os.path.join(OUT, name + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"EMPTY", "MESH"},
        apply_scale_options="FBX_SCALE_ALL", bake_space_transform=True,
        axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE", add_leaf_bones=False,
        use_mesh_modifiers=True, path_mode="STRIP",
    )
    print("exported", path)
    clear()


# ---------------------------------------------------------------------------------------------
# Characters
# ---------------------------------------------------------------------------------------------

def demon():
    r = root()
    body = part("Body", "cube", (0.44, 0.28, 0.46), (0, 0, 0.8), "Primary", r, pivot=(0, 0, 0.56))
    part("Belt", "cube", (0.46, 0.3, 0.08), (0, 0, 0.58), "Dark", body)
    part("Chest", "cube", (0.3, 0.05, 0.2), (0, -0.15, 0.86), "Gold", body)
    head = part("Head", "sphere", (0.28, 0.28, 0.3), (0, -0.02, 1.17), "Skin", body, pivot=(0, 0, 1.03))
    mirror("Eye", "sphere", (0.06, 0.04, 0.04), (0.065, -0.14, 1.19), "Eye", head, segments=8)
    # Big curved horns: two segments each.
    hl, hr = mirror("Horn", "cone", (0.09, 0.09, 0.24), (0.14, 0.0, 1.32), "Horn", head, rot=(0, 35, 0), segments=8)
    mirror("HornTip", "cone", (0.06, 0.06, 0.2), (0.25, 0.08, 1.45), "Horn", head, rot=(40, 75, 0), segments=8)
    mirror("Pauldron", "sphere", (0.2, 0.2, 0.16), (0.27, 0, 0.98), "Primary", body)
    al, ar = mirror("Arm", "cyl", (0.12, 0.12, 0.44), (0.3, 0, 0.76), "Skin", body, pivot=(0.28, 0, 0.97))
    part("ClawL", "cone", (0.12, 0.12, 0.12), (0.3, -0.02, 0.5), "Horn", al, rot=(180, 0, 0), segments=6)
    part("ClawR", "cone", (0.12, 0.12, 0.12), (-0.3, -0.02, 0.5), "Horn", ar, rot=(180, 0, 0), segments=6)
    part("Weapon", "cube", (0.05, 0.03, 0.7), (-0.3, -0.25, 0.72), "Metal", ar, rot=(70, 0, 0))
    part("Guard", "cube", (0.2, 0.05, 0.04), (-0.3, -0.06, 0.56), "Gold", ar)
    ll, lr = mirror("Leg", "cyl", (0.15, 0.15, 0.5), (0.12, 0, 0.3), "Dark", r, pivot=(0.12, 0, 0.56))
    part("HoofL", "cube", (0.15, 0.2, 0.08), (0.12, -0.03, 0.04), "Horn", ll)
    part("HoofR", "cube", (0.15, 0.2, 0.08), (-0.12, -0.03, 0.04), "Horn", lr)
    wl, wr = mirror("Wing", "cube", (0.62, 0.03, 0.5), (0.45, 0.24, 1.1), "Wing", body, pivot=(0.12, 0.16, 0.98), rot=(0, -15, 0))
    part("WingBoneL", "cyl", (0.04, 0.04, 0.7), (0.45, 0.22, 1.3), "Horn", wl, rot=(0, 70, 0), segments=6)
    part("WingBoneR", "cyl", (0.04, 0.04, 0.7), (-0.45, 0.22, 1.3), "Horn", wr, rot=(0, -70, 0), segments=6)
    tail = part("Tail", "cyl", (0.06, 0.06, 0.55), (0, 0.34, 0.42), "Skin", r, pivot=(0, 0.13, 0.58), rot=(-55, 0, 0), segments=6)
    part("TailTip", "cone", (0.14, 0.04, 0.16), (0, 0.56, 0.24), "Horn", tail, rot=(-130, 0, 0), segments=4)
    export("demon")


def princess():
    r = root()
    body = part("Body", "frustum", (0.24, 0.2, 0.32), (0, 0, 0.86), "Cloth", r, pivot=(0, 0, 0.7), rot=(180, 0, 0))
    part("Gown", "cone", (0.62, 0.62, 0.72), (0, 0, 0.36), "Cloth", r)
    part("GownTrim", "cyl", (0.63, 0.63, 0.04), (0, 0, 0.03), "Gold", r, segments=16)
    part("Necklace", "cyl", (0.18, 0.16, 0.03), (0, -0.01, 1.0), "Gold", body, segments=12)
    head = part("Head", "sphere", (0.24, 0.24, 0.27), (0, -0.01, 1.16), "SkinPale", body, pivot=(0, 0, 1.03))
    mirror("Eye", "sphere", (0.05, 0.03, 0.03), (0.055, -0.12, 1.18), "Eye", head, segments=8)
    part("Hair", "sphere", (0.28, 0.27, 0.28), (0, 0.04, 1.2), "Hair", head)
    part("HairBack", "cube", (0.24, 0.08, 0.42), (0, 0.13, 0.98), "Hair", head)
    part("Crown", "cyl", (0.17, 0.17, 0.06), (0, 0, 1.31), "Gold", head, segments=10)
    for i in range(5):
        a = math.radians(i * 72)
        part("CrownSpike", "cone", (0.04, 0.04, 0.08), (math.sin(a) * 0.08, math.cos(a) * 0.08, 1.37), "Gold", head, segments=4)
    part("CrownGem", "sphere", (0.04, 0.03, 0.04), (0, -0.09, 1.32), "Glow", head, segments=6)
    mirror("Horn", "cone", (0.06, 0.06, 0.2), (0.11, 0.02, 1.33), "Horn", head, rot=(-20, 30, 0), segments=8)
    mirror("HornTip", "cone", (0.04, 0.04, 0.14), (0.17, 0.09, 1.43), "Horn", head, rot=(-60, 45, 0), segments=8)
    al, ar = mirror("Arm", "cyl", (0.07, 0.07, 0.36), (0.17, 0, 0.82), "SkinPale", body, pivot=(0.15, 0, 0.98), rot=(0, 15, 0))
    part("SleeveL", "frustum", (0.13, 0.13, 0.12), (0.19, 0, 0.93), "Cloth", al, segments=8)
    part("SleeveR", "frustum", (0.13, 0.13, 0.12), (-0.19, 0, 0.93), "Cloth", ar, segments=8)
    mirror("Wing", "cube", (0.4, 0.02, 0.32), (0.3, 0.18, 1.05), "Wing", body, pivot=(0.08, 0.12, 0.95), rot=(0, -20, 0))
    tail = part("Tail", "cyl", (0.04, 0.04, 0.5), (0, 0.32, 0.4), "SkinPale", r, pivot=(0, 0.2, 0.62), rot=(-60, 0, 0), segments=6)
    part("TailTip", "sphere", (0.12, 0.04, 0.1), (0, 0.52, 0.2), "Cloth", tail, segments=6)
    export("princess")


def human():
    r = root()
    body = part("Body", "cube", (0.38, 0.24, 0.42), (0, 0, 0.78), "Metal", r, pivot=(0, 0, 0.56))
    part("Tabard", "cube", (0.26, 0.04, 0.5), (0, -0.13, 0.72), "White", body)
    part("Cross", "cube", (0.05, 0.05, 0.26), (0, -0.16, 0.8), "Gold", body)
    part("CrossBar", "cube", (0.16, 0.05, 0.05), (0, -0.16, 0.84), "Gold", body)
    head = part("Head", "cyl", (0.24, 0.24, 0.26), (0, 0, 1.12), "Metal", body, pivot=(0, 0, 1.0), segments=10)
    part("Visor", "cube", (0.18, 0.04, 0.04), (0, -0.12, 1.13), "Dark", head)
    part("Plume", "cube", (0.04, 0.2, 0.12), (0, 0.02, 1.3), "Primary", head)
    mirror("Pauldron", "sphere", (0.17, 0.17, 0.13), (0.23, 0, 0.96), "Metal", body)
    al, ar = mirror("Arm", "cyl", (0.1, 0.1, 0.4), (0.25, 0, 0.76), "Metal", body, pivot=(0.24, 0, 0.95))
    part("Shield", "cube", (0.06, 0.34, 0.42), (0.32, -0.08, 0.68), "Primary", al)
    part("ShieldBoss", "sphere", (0.05, 0.1, 0.1), (0.36, -0.08, 0.7), "Gold", al, segments=6)
    part("Weapon", "cube", (0.04, 0.03, 0.6), (-0.26, -0.22, 0.72), "Metal", ar, rot=(70, 0, 0))
    part("Hilt", "cube", (0.16, 0.05, 0.04), (-0.26, -0.05, 0.56), "Gold", ar)
    mirror("Leg", "cyl", (0.14, 0.14, 0.5), (0.1, 0, 0.3), "Metal", r, pivot=(0.1, 0, 0.56))
    export("human")


def creature_humanoid():
    r = root()
    body = part("Body", "cube", (0.36, 0.26, 0.4), (0, 0.04, 0.72), "Primary", r, pivot=(0, 0, 0.5), rot=(12, 0, 0))
    part("Ribs", "cube", (0.26, 0.04, 0.24), (0, -0.1, 0.74), "Secondary", body)
    head = part("Head", "sphere", (0.26, 0.26, 0.26), (0, -0.08, 1.0), "Primary", body, pivot=(0, -0.02, 0.9))
    mirror("Eye", "sphere", (0.06, 0.04, 0.05), (0.06, -0.2, 1.01), "Eye", head, segments=6)
    part("Jaw", "cube", (0.16, 0.1, 0.06), (0, -0.16, 0.9), "Secondary", head)
    al, ar = mirror("Arm", "cyl", (0.09, 0.09, 0.5), (0.25, -0.02, 0.62), "Secondary", body, pivot=(0.22, 0, 0.86))
    part("ClawL", "cone", (0.1, 0.1, 0.12), (0.25, -0.04, 0.34), "Horn", al, rot=(180, 0, 0), segments=5)
    part("ClawR", "cone", (0.1, 0.1, 0.12), (-0.25, -0.04, 0.34), "Horn", ar, rot=(180, 0, 0), segments=5)
    mirror("Leg", "cyl", (0.12, 0.12, 0.48), (0.1, 0, 0.26), "Secondary", r, pivot=(0.1, 0, 0.5))
    export("humanoid")


def creature_beast():
    r = root()
    body = part("Body", "cube", (0.34, 0.66, 0.32), (0, 0.05, 0.48), "Primary", r, pivot=(0, 0.05, 0.48))
    part("Stripe", "cube", (0.2, 0.5, 0.05), (0, 0.05, 0.66), "Secondary", body)
    head = part("Head", "cube", (0.26, 0.26, 0.24), (0, -0.38, 0.6), "Primary", body, pivot=(0, -0.26, 0.56))
    part("Snout", "cube", (0.16, 0.16, 0.12), (0, -0.55, 0.55), "Secondary", head)
    mirror("Eye", "sphere", (0.05, 0.04, 0.05), (0.08, -0.51, 0.66), "Eye", head, segments=6)
    mirror("Ear", "cone", (0.08, 0.06, 0.14), (0.09, -0.34, 0.78), "Secondary", head, segments=4)
    mirror("Leg", "cyl", (0.1, 0.1, 0.34), (0.12, -0.2, 0.17), "Secondary", r, pivot=(0.12, -0.2, 0.36))
    mirror("Leg2", "cyl", (0.1, 0.1, 0.34), (0.12, 0.3, 0.17), "Secondary", r, pivot=(0.12, 0.3, 0.36))
    part("Tail", "cone", (0.08, 0.08, 0.36), (0, 0.5, 0.55), "Primary", body, pivot=(0, 0.38, 0.56), rot=(-120, 0, 0), segments=6)
    export("beast")


def creature_blob():
    r = root()
    body = part("Body", "sphere", (0.72, 0.72, 0.5), (0, 0, 0.25), "Primary", r, pivot=(0, 0, 0.0), segments=16)
    part("Core", "sphere", (0.3, 0.3, 0.22), (0.1, 0.05, 0.34), "Secondary", body, segments=10)
    mirror("Eye", "sphere", (0.09, 0.06, 0.09), (0.12, -0.3, 0.36), "Eye", body, segments=8)
    export("blob")


def creature_flyer():
    r = root()
    body = part("Body", "sphere", (0.32, 0.3, 0.36), (0, 0, 0.85), "Primary", r, pivot=(0, 0, 0.85))
    head = part("Head", "sphere", (0.22, 0.22, 0.2), (0, -0.12, 1.06), "Primary", body, pivot=(0, -0.05, 1.0))
    mirror("Eye", "sphere", (0.05, 0.04, 0.05), (0.05, -0.22, 1.08), "Eye", head, segments=6)
    mirror("Ear", "cone", (0.06, 0.04, 0.12), (0.07, -0.1, 1.2), "Secondary", head, segments=4)
    mirror("Wing", "cube", (0.6, 0.02, 0.36), (0.42, 0.02, 0.92), "Secondary", body, pivot=(0.12, 0.02, 0.9), rot=(0, -10, 0))
    part("Tail", "cone", (0.08, 0.08, 0.3), (0, 0.12, 0.62), "Primary", body, pivot=(0, 0.05, 0.72), rot=(160, 0, 0), segments=5)
    export("flyer")


def creature_insect():
    r = root()
    body = part("Body", "sphere", (0.34, 0.34, 0.24), (0, -0.05, 0.32), "Primary", r, pivot=(0, 0, 0.32))
    part("Abdomen", "sphere", (0.46, 0.52, 0.38), (0, 0.38, 0.4), "Secondary", body, segments=12)
    head = part("Head", "sphere", (0.2, 0.2, 0.18), (0, -0.28, 0.33), "Primary", body, pivot=(0, -0.2, 0.33))
    for i, (x, z) in enumerate([(0.04, 0.38), (0.08, 0.35), (0.06, 0.3)]):
        part("Eye", "sphere", (0.04, 0.03, 0.04), (x, -0.37, z), "Eye", head, segments=5)
        part("Eye", "sphere", (0.04, 0.03, 0.04), (-x, -0.37, z), "Eye", head, segments=5)
    for i, y in enumerate([-0.18, -0.06, 0.06, 0.18]):
        suffix = "" if i == 0 else str(i + 1)
        mirror("Leg" + suffix, "cyl", (0.05, 0.05, 0.5), (0.3, y * 1.2, 0.2), "Primary", r, pivot=(0.12, y, 0.32), rot=(0, 60, 0), segments=5)
    export("insect")


def creature_golem():
    r = root()
    body = part("Body", "cube", (0.62, 0.42, 0.56), (0, 0, 0.9), "Primary", r, pivot=(0, 0, 0.62))
    part("Crack", "cube", (0.06, 0.03, 0.3), (0.1, -0.22, 0.92), "Glow", body)
    part("Crack", "cube", (0.04, 0.03, 0.2), (-0.15, -0.22, 0.85), "Glow", body)
    head = part("Head", "cube", (0.26, 0.24, 0.2), (0, -0.06, 1.28), "Primary", body, pivot=(0, 0, 1.18))
    part("EyeSlit", "cube", (0.18, 0.03, 0.04), (0, -0.19, 1.29), "Eye", head)
    al, ar = mirror("Arm", "cube", (0.22, 0.24, 0.62), (0.44, 0, 0.78), "Secondary", body, pivot=(0.4, 0, 1.08))
    part("FistL", "cube", (0.26, 0.28, 0.22), (0.44, -0.02, 0.42), "Primary", al)
    part("FistR", "cube", (0.26, 0.28, 0.22), (-0.44, -0.02, 0.42), "Primary", ar)
    mirror("Leg", "cube", (0.22, 0.24, 0.6), (0.17, 0, 0.32), "Secondary", r, pivot=(0.17, 0, 0.62))
    export("golem")


# ---------------------------------------------------------------------------------------------
# Props
# ---------------------------------------------------------------------------------------------

def chest(open_):
    r = root()
    part("Base", "cube", (0.6, 0.4, 0.32), (0, 0, 0.16), "Wood", r)
    part("Band", "cube", (0.62, 0.42, 0.05), (0, 0, 0.25), "Gold", r)
    lid = part("Lid", "cube", (0.62, 0.42, 0.14), (0, 0, 0.39), "Wood", r, pivot=(0, 0.2, 0.32),
               rot=(0, 0, 0))
    part("Lock", "cube", (0.1, 0.04, 0.12), (0, -0.22, 0.32), "Gold", lid)
    if open_:
        lid.rotation_euler = (math.radians(-100), 0, 0)
        bpy.context.view_layer.update()
        part("Treasure", "cube", (0.5, 0.3, 0.05), (0, 0, 0.32), "Gold", r)
    export("chest_open" if open_ else "chest")


def shrine():
    r = root()
    part("Base", "cyl", (0.6, 0.6, 0.16), (0, 0, 0.08), "Stone", r, segments=8)
    part("Pillar", "cube", (0.26, 0.26, 0.7), (0, 0, 0.5), "Stone", r)
    part("Cap", "cube", (0.38, 0.38, 0.08), (0, 0, 0.88), "Stone", r)
    part("Crystal", "sphere", (0.2, 0.2, 0.34), (0, 0, 1.15), "Glow", r, segments=4)
    export("shrine")


def fountain():
    r = root()
    part("Basin", "cyl", (0.9, 0.9, 0.3), (0, 0, 0.15), "Stone", r, segments=8)
    part("Water", "cyl", (0.8, 0.8, 0.05), (0, 0, 0.28), "Water", r, segments=8)
    part("Spout", "cyl", (0.16, 0.16, 0.6), (0, 0, 0.5), "Stone", r, segments=8)
    part("Bowl", "cyl", (0.4, 0.4, 0.08), (0, 0, 0.8), "Stone", r, segments=8)
    part("Crystal", "sphere", (0.1, 0.1, 0.1), (0, 0, 0.9), "Water", r, segments=6)
    export("fountain")


def merchant():
    r = root()
    body = part("Body", "sphere", (0.34, 0.3, 0.4), (0, 0, 0.42), "Skin", r, pivot=(0, 0, 0.3))
    head = part("Head", "sphere", (0.26, 0.24, 0.24), (0, -0.04, 0.72), "Skin", body, pivot=(0, 0, 0.6))
    mirror("Eye", "sphere", (0.05, 0.03, 0.05), (0.06, -0.15, 0.74), "Eye", head, segments=6)
    mirror("Horn", "cone", (0.05, 0.05, 0.12), (0.08, 0, 0.88), "Horn", head, segments=6)
    part("Hood", "sphere", (0.3, 0.3, 0.22), (0, 0.03, 0.78), "Cloth", head)
    part("Pack", "cube", (0.4, 0.3, 0.46), (0, 0.24, 0.52), "Wood", body)
    part("PackStrap", "cube", (0.42, 0.32, 0.05), (0, 0.24, 0.6), "Gold", body)
    al, ar = mirror("Arm", "cyl", (0.07, 0.07, 0.26), (0.18, -0.04, 0.38), "Skin", body, pivot=(0.16, 0, 0.52))
    part("Lantern", "sphere", (0.12, 0.12, 0.14), (-0.22, -0.12, 0.22), "Fire", ar, segments=6)
    mirror("Leg", "cyl", (0.08, 0.08, 0.18), (0.08, 0, 0.09), "Dark", r, pivot=(0.08, 0, 0.2))
    export("merchant")


def portal():
    r = root()
    mirror("Pillar", "cube", (0.18, 0.24, 1.6), (0.55, 0, 0.8), "Stone", r)
    part("Lintel", "cube", (1.3, 0.28, 0.2), (0, 0, 1.68), "Stone", r)
    for i in range(14):
        a = math.radians(i * 360 / 14)
        part("Rune", "cube", (0.08, 0.05, 0.08), (math.cos(a) * 0.45, -0.13, 0.85 + math.sin(a) * 0.62), "Glow", r)
    part("Crystal", "cyl", (0.88, 1.2, 0.05), (0, 0, 0.85), "Glow", r, rot=(90, 0, 0), segments=20)
    part("Step", "cube", (1.4, 0.7, 0.08), (0, 0, 0.04), "Stone", r)
    export("portal")


def throne():
    r = root()
    part("Dais", "cube", (1.0, 0.9, 0.16), (0, 0.05, 0.08), "Stone", r)
    part("Seat", "cube", (0.62, 0.5, 0.2), (0, 0.05, 0.38), "Cloth", r)
    part("Back", "cube", (0.66, 0.12, 1.2), (0, 0.3, 0.9), "Cloth", r)
    part("BackTrim", "cube", (0.7, 0.1, 0.08), (0, 0.3, 1.5), "Gold", r)
    mirror("Spire", "cone", (0.12, 0.12, 0.36), (0.3, 0.3, 1.66), "Gold", r, segments=6)
    mirror("Rest", "cube", (0.1, 0.5, 0.26), (0.34, 0.05, 0.5), "Gold", r)
    export("throne")


def pillar():
    r = root()
    part("Base", "cube", (0.62, 0.62, 0.2), (0, 0, 0.1), "Stone", r)
    part("Shaft", "cyl", (0.44, 0.44, 1.9), (0, 0, 1.15), "Stone", r, segments=10)
    part("Top", "cube", (0.62, 0.62, 0.18), (0, 0, 2.15), "Stone", r)
    part("Band", "cyl", (0.47, 0.47, 0.06), (0, 0, 1.6), "Gold", r, segments=10)
    export("pillar")


def brazier():
    r = root()
    for i in range(3):
        a = math.radians(i * 120)
        part("Leg", "cyl", (0.05, 0.05, 0.7), (math.cos(a) * 0.16, math.sin(a) * 0.16, 0.35), "Dark", r,
             rot=(math.degrees(math.sin(a)) * 0.2, -math.degrees(math.cos(a)) * 0.2, 0), segments=5)
    part("Bowl", "frustum", (0.5, 0.5, 0.18), (0, 0, 0.76), "Metal", r, rot=(180, 0, 0), segments=10)
    part("Fire", "cone", (0.34, 0.34, 0.42), (0, 0, 1.04), "Fire", r, segments=7)
    export("brazier")


def stairs():
    r = root()
    # A pit with steps descending below the floor (the floor tile is left out under it).
    for i in range(5):
        part("Step", "cube", (0.9, 0.18, 0.12), (0, 0.38 - i * 0.18, -0.08 - i * 0.14), "Stone", r)
    part("WallL", "cube", (0.06, 1.0, 0.9), (0.47, 0, -0.45), "Dark", r)
    part("WallR", "cube", (0.06, 1.0, 0.9), (-0.47, 0, -0.45), "Dark", r)
    part("WallB", "cube", (1.0, 0.06, 0.9), (0, 0.47, -0.45), "Dark", r)
    part("Pit", "cube", (1.0, 1.0, 0.05), (0, 0, -0.9), "Dark", r)
    export("stairs")


def spikes():
    r = root()
    for x in (-0.25, 0, 0.25):
        for y in (-0.25, 0, 0.25):
            part("Spike", "cone", (0.08, 0.08, 0.22), (x, y, 0.11), "Metal", r, segments=5)
    export("spikes")


def rocks():
    r = root()
    for (x, y, s) in [(-0.2, -0.1, 0.22), (0.18, 0.12, 0.16), (0.05, -0.25, 0.12), (-0.05, 0.25, 0.14)]:
        part("Rock", "sphere", (s, s * 0.9, s * 0.7), (x, y, s * 0.3), "Stone", r, segments=6)
    export("rocks")


def artifact():
    r = root()
    part("Base", "cyl", (0.3, 0.3, 0.06), (0, 0, 0.03), "Gold", r, segments=8)
    body = part("Crystal", "sphere", (0.26, 0.26, 0.44), (0, 0, 0.55), "Glow", r, segments=4)
    part("Halo", "cyl", (0.5, 0.5, 0.03), (0, 0, 0.55), "Gold", body, segments=12)
    export("artifact")


if __name__ == "__main__":
    clear()
    for build in (demon, princess, human, creature_humanoid, creature_beast, creature_blob, creature_flyer,
                  creature_insect, creature_golem, lambda: chest(False), lambda: chest(True), shrine, fountain,
                  merchant, portal, throne, pillar, brazier, stairs, spikes, rocks, artifact):
        build()
    print("DONE")
