"""
Builds every 3D model for Depthbound and exports FBX files (with animation clips) for Unity.

Usage (from the repo root):
    blender -b --factory-startup --python Tools/Blender/build_models.py -- Assets/Resources/Models [name ...]

Pass model names after the output folder to rebuild only those (e.g. "demon princess").

Characters are modeled, rigged and animated:
- Bodies are sculpted with the Skin modifier over a joint graph with muscle nodes (biceps, forearms,
  thighs, calves), then subdivided. Hands have fingers and thumbs. Heads have noses, pointed ears and
  a front-projected face UV painted in Unity (material role "Face<Variant>").
- Hair is layered S-curved clumps and ribbon strands; horns, tails and wing fingers are tapered tubes;
  gowns, capes and moldings are lathed (open lathes give folds); armor is layered and bevelled.
- Each character has an armature with weighted bodies and rigid accessories, plus keyframed clips
  (Idle, Walk, Attack, Cast, Hit, Death) authored in dbmodel/anim.py.
- Hard-surface and cloth parts get cube-projected UVs for Unity's detail textures.

Conventions: 1 unit = 1 tile, models stand on z=0 facing -Y, an empty named "Front" marks the front,
materials are role names, and prop parts may encode their parent as "Part@Parent".
"""
import math
import os
import random
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from dbmodel.geometry import (Rig, anime_head, apply_modifiers, bevel_box, bind, blob, build_armature, clear,  # noqa: E402
                              curve_pts, export, finalize, front_marker, join, lathe, link, membrane, prism, skin, tube, xform)
from dbmodel import anim  # noqa: E402

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = ARGS[0] if ARGS else "Models"
ONLY = set(ARGS[1:])
os.makedirs(OUT, exist_ok=True)


def out(name):
    return os.path.join(OUT, name + ".fbx")


def finish_character(name, rig, parts, kind, **style):
    arm = build_armature(rig)
    body = join(parts, "Mesh")
    mod = body.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    body.parent = arm
    front_marker(arm)
    anim.add_clips(arm, kind, **style)
    export(out(name), animated=True)


# =============================================================================================
# Humanoid anatomy
# =============================================================================================

def humanoid_joints(h=1.0, shoulder=0.16, hip=0.08, hunch=0.0):
    J = {
        "hips": (0, 0, 0.56 * h), "waist": (0, -0.004, 0.63 * h), "spine": (0, -0.005 - hunch * 0.4, 0.69 * h),
        "chest": (0, -0.01 - hunch, 0.8 * h), "neck": (0, -0.01 - hunch * 1.4, 0.92 * h), "headBase": (0, -0.01 - hunch * 1.6, 0.97 * h),
    }
    sh = J["chest"][2] + 0.07 * h
    cy = J["chest"][1]
    for s, side in ((1, "L"), (-1, "R")):
        J["shoulder" + side] = (s * shoulder, cy, sh)
        J["bicep" + side] = (s * (shoulder + 0.02), cy + 0.012, sh - 0.1 * h)
        J["elbow" + side] = (s * (shoulder + 0.035), cy + 0.025, sh - 0.2 * h)
        J["forearm" + side] = (s * (shoulder + 0.045), cy + 0.005, sh - 0.29 * h)
        J["wrist" + side] = (s * (shoulder + 0.05), cy - 0.02, sh - 0.38 * h)
        J["hand" + side] = (s * (shoulder + 0.055), cy - 0.035, sh - 0.45 * h)
        J["hip" + side] = (s * hip, 0, 0.53 * h)
        J["thigh" + side] = (s * (hip + 0.004), -0.012, 0.41 * h)
        J["knee" + side] = (s * (hip + 0.005), -0.02, 0.29 * h)
        J["calf" + side] = (s * (hip + 0.006), 0.012, 0.2 * h)
        J["ankle" + side] = (s * (hip + 0.008), 0.01, 0.06 * h)
        J["toe" + side] = (s * (hip + 0.01), -0.085, 0.025 * h)
    return {k: Vector(v) for k, v in J.items()}


def humanoid_body(J, role, female=False, bulk=1.0, gaunt=False, subsurf=2):
    """Torso, arms (to the wrist) and legs as one sculpted skin mesh with muscle nodes."""
    t = 0.55 if gaunt else 1.0
    lt = t ** 0.5
    nodes, idx = [], {}

    def node(key, r):
        idx[key] = len(nodes)
        nodes.append((J[key], r))

    f = female
    node("hips", ((0.12 if f else 0.1) * bulk * t, (0.085 if f else 0.075) * bulk * t))
    node("waist", ((0.068 if f else 0.088) * bulk * t, (0.058 if f else 0.065) * bulk * t))
    node("spine", ((0.078 if f else 0.095) * bulk * t, 0.064 * bulk * t))
    node("chest", ((0.09 if f else 0.12) * bulk * t, (0.074 if f else 0.08) * bulk * t))
    node("neck", 0.034 * bulk * (0.8 if gaunt else 1.0))
    for side in "LR":
        node("shoulder" + side, (0.042 if f else 0.056) * bulk * lt)
        node("bicep" + side, (0.034 if f else 0.047) * bulk * lt)
        node("elbow" + side, (0.027 if f else 0.036) * bulk * lt)
        node("forearm" + side, (0.028 if f else 0.039) * bulk * lt)
        node("wrist" + side, (0.02 if f else 0.026) * bulk * lt)
        node("hip" + side, (0.064 if f else 0.068) * bulk * lt)
        node("thigh" + side, (0.058 if f else 0.06) * bulk * lt)
        node("knee" + side, (0.038 if f else 0.045) * bulk * lt)
        node("calf" + side, (0.04 if f else 0.047) * bulk * lt)
        node("ankle" + side, (0.026 if f else 0.031) * bulk * lt)
        node("toe" + side, (0.025 if f else 0.03) * bulk)
    E = [("hips", "waist"), ("waist", "spine"), ("spine", "chest"), ("chest", "neck")]
    for s in "LR":
        E += [("chest", "shoulder" + s), ("shoulder" + s, "bicep" + s), ("bicep" + s, "elbow" + s), ("elbow" + s, "forearm" + s),
              ("forearm" + s, "wrist" + s), ("hips", "hip" + s), ("hip" + s, "thigh" + s), ("thigh" + s, "knee" + s),
              ("knee" + s, "calf" + s), ("calf" + s, "ankle" + s), ("ankle" + s, "toe" + s)]
    return skin("Body", nodes, [(idx[a], idx[b]) for a, b in E], role, subsurf=subsurf)


def hand(J, side, role, scale=1.0, claws=None):
    """A hand with four fingers and a thumb, hanging with the palm toward the thigh."""
    s = 1 if side == "L" else -1
    w, c = J["wrist" + side], J["hand" + side]
    down = (c - w).normalized()
    palm = w + down * 0.035 * scale
    nodes = [(w, 0.019 * scale), (palm, (0.026 * scale, 0.013 * scale))]
    edges = [(0, 1)]
    for k, dy in enumerate((-0.016, -0.005, 0.006, 0.016)):
        base = palm + Vector((s * 0.004, dy * scale, -0.022 * scale))
        mid = base + Vector((s * 0.003, dy * 0.15, -0.022 * scale * (1.0 - abs(dy) * 6)))
        tip = mid + Vector((-s * 0.002, -0.004, -0.018 * scale * (1.0 - abs(dy) * 6)))
        i = len(nodes)
        nodes += [(base, 0.0085 * scale), (mid, 0.0075 * scale), (tip, 0.0065 * scale)]
        edges += [(1, i), (i, i + 1), (i + 1, i + 2)]
    i = len(nodes)
    tb = palm + Vector((-s * 0.008, -0.022 * scale, 0.004))
    nodes += [(tb, 0.01 * scale), (tb + Vector((0, -0.014 * scale, -0.016 * scale)), 0.008 * scale)]
    edges += [(1, i), (i, i + 1)]
    parts = [skin("Hand", nodes, edges, role, subsurf=1)]
    if claws:
        for k, dy in enumerate((-0.016, -0.005, 0.006, 0.016)):
            tip = palm + Vector((s * 0.005, dy * scale, -0.07 * scale))
            parts.append(tube("Claw", [tip, tip + Vector((0, -0.01, -0.035 * scale))], [0.006 * scale, 0.0], claws, segs=5))
    return parts


def humanoid_rig(J, head_top):
    rig = Rig()
    rig.add("Hips", J["hips"], J["spine"])
    rig.add("Spine", J["spine"], J["chest"], "Hips")
    rig.add("Chest", J["chest"], J["neck"], "Spine")
    rig.add("Neck", J["neck"], J["headBase"], "Chest")
    rig.add("Head", J["headBase"], head_top, "Neck")
    for s in "LR":
        rig.add("UpperArm" + s, J["shoulder" + s], J["elbow" + s], "Chest")
        rig.add("LowerArm" + s, J["elbow" + s], J["wrist" + s], "UpperArm" + s)
        rig.add("Hand" + s, J["wrist" + s], J["hand" + s] + (J["hand" + s] - J["wrist" + s]), "LowerArm" + s)
        rig.add("UpperLeg" + s, J["hip" + s], J["knee" + s], "Hips")
        rig.add("LowerLeg" + s, J["knee" + s], J["ankle" + s], "UpperLeg" + s)
        rig.add("Foot" + s, J["ankle" + s], J["toe" + s], "LowerLeg" + s)
    return rig


BODY_BONES = ["Hips", "Spine", "Chest", "Neck"] + [b + s for s in "LR" for b in ("UpperArm", "LowerArm", "Hand", "UpperLeg", "LowerLeg", "Foot")]


def head_features(head_c, r, skin_role, ears=True, ear_len=1.0):
    """Nose and pointed demonic ears."""
    parts = [blob("Nose", r * 0.1, head_c + Vector((0, -r * 0.86, -r * 0.12)), skin_role, scale=(0.55, 0.7, 1.0), subdiv=2)]
    if ears:
        for s in (1, -1):
            base = head_c + Vector((s * r * 0.92, r * 0.05, -r * 0.05))
            tip = head_c + Vector((s * r * (1.25 + 0.35 * ear_len), r * 0.3, r * (0.25 + 0.3 * ear_len)))
            parts.append(tube("Ear", curve_pts(base, tip, (0, 0, -0.01), 4), [r * 0.2, r * 0.15, r * 0.08, 0.0], skin_role, segs=8, flat=0.35, ref=(0, 1, 0)))
    return parts


def anime_hair(center, r, role, seed=1, length=1.0, back=1.0, spikes=1.0, bangs=7, ahoge=True):
    """Layered anime hair: an open cap, two rings of S-curved clumps, swept bangs and a cowlick."""
    rnd = random.Random(seed)
    c = Vector(center)
    parts = []
    cap = blob("HairCap", r * 1.09, c, role, scale=(1.0, 1.03, 0.98), subdiv=3)
    for v in cap.data.vertices:   # open the cap at the face
        rel = v.co - c
        if rel.y < -0.3 * r and rel.z < 0.38 * r:
            v.co = c + rel * 0.74
    parts.append(cap)
    for ring, (count, height, reach) in enumerate(((13, 0.55, 1.0), (10, 0.15, 1.25))):
        for i in range(count):
            a = 2 * math.pi * (i + ring * 0.5) / count + rnd.uniform(-0.12, 0.12)
            dx, dy = math.sin(a), math.cos(a)
            if dy < -0.5:
                continue
            base = c + Vector((dx * r * 0.88, dy * r * 0.88, r * height))
            L = r * rnd.uniform(0.9, 1.25) * reach * (back if dy > 0.3 else length)
            tip = base + Vector((dx * L * 0.7 * spikes, dy * L * 0.75, -L * (0.55 + 0.3 * ring)))
            bend = Vector((dx * L * 0.25, dy * L * 0.2, L * 0.15))
            curl = Vector((-dx, -dy, 0)) * L * 0.08
            pts = curve_pts(base, tip, bend, 5, sway=curl)
            parts.append(tube("Hair", pts, [r * 0.33, r * 0.27, r * 0.18, r * 0.08, 0.0], role, segs=8, flat=0.5, twist=0.5))
    for k in range(bangs):
        x = (k - (bangs - 1) / 2) * r * 0.24
        base = c + Vector((x, -r * 0.62, r * 0.7))
        tip = c + Vector((x * 1.2 + rnd.uniform(-0.015, 0.015) + 0.02 * r, -r * 1.03, r * (0.15 + 0.06 * abs(k - bangs // 2) / bangs)))
        parts.append(tube("Bang", curve_pts(base, tip, (0, -r * 0.18, r * 0.05), 4), [r * 0.19, r * 0.15, r * 0.08, 0.0], role, segs=8, flat=0.45))
    if ahoge:
        base = c + Vector((0.0, -r * 0.1, r * 1.02))
        parts.append(tube("Ahoge", curve_pts(base, base + Vector((r * 0.25, -r * 0.35, r * 0.5)), (0, r * 0.3, r * 0.15), 5),
                          [r * 0.09, r * 0.08, r * 0.05, r * 0.03, 0.0], role, segs=6, flat=0.4))
    return parts


def curved_horn(base, side, length, role, curl=1.0, thickness=0.05, rings=True):
    pts, radii = [], []
    for i in range(11):
        t = i / 10
        ang = t * 2.2 * curl
        pts.append(Vector(base) + Vector((side * (0.15 * t + 0.06 * math.sin(ang)) * length,
                                          (0.55 * t - 0.25 * math.sin(ang * 0.8)) * length * 0.6,
                                          (0.55 * math.sin(ang * 0.7) + 0.2 * t) * length)))
        ridge = 1.0 + (0.12 if rings and i % 2 == 1 and i < 8 else 0.0)
        radii.append((thickness * (1 - t) + 0.002) * ridge)
    radii[-1] = 0.0
    return tube("Horn", pts, radii, role, segs=12)


def bat_wing(shoulder, side, span, role_membrane, role_bone, droop=0.0):
    s = side
    root = Vector(shoulder)
    wrist = root + Vector((s * span * 0.38, span * 0.18, span * 0.28))
    tips = [wrist + Vector((s * span * 0.62, span * 0.1, span * 0.32 - droop)),
            wrist + Vector((s * span * 0.66, span * 0.12, -span * 0.08 - droop)),
            wrist + Vector((s * span * 0.38, span * 0.1, -span * 0.42 - droop))]
    low = root + Vector((s * span * 0.08, span * 0.12, -span * 0.48))
    parts = [tube("WingArm", curve_pts(root, wrist, (0, 0, span * 0.05), 4), [0.024, 0.02, 0.018, 0.016], role_bone, segs=7)]
    parts.append(tube("WingClaw", [wrist, wrist + Vector((s * 0.01, -0.02, span * 0.09))], [0.012, 0.0], role_bone, segs=5))
    for tp in tips:
        parts.append(tube("WingFinger", curve_pts(wrist, tp, (0, 0, span * 0.05), 4), [0.015, 0.012, 0.008, 0.0], role_bone, segs=6))
    poly = [root, wrist, tips[0]]
    for a, b in ((tips[0], tips[1]), (tips[1], tips[2]), (tips[2], low)):
        mid = (a + b) / 2
        poly += [mid + (wrist - mid).normalized() * span * 0.11, b]
    poly = poly[:-1] + [low]
    parts.append(membrane("Membrane", [p + Vector((0, 0.005, 0)) for p in poly], role_membrane))
    return parts, wrist, tips[1]


def demon_tail(root, role, tip_role, length=0.7, heart=False):
    pts, radii = [], []
    for i in range(9):
        t = i / 8
        pts.append(Vector(root) + Vector((0.08 * math.sin(t * 3.0), length * (0.35 * t + 0.4 * t * t), -length * 0.55 * t + length * 0.45 * t * t * t)))
        radii.append(0.035 * (1 - t * 0.72))
    parts = [tube("Tail", pts, radii, role, segs=9)]
    tip = pts[-1]
    d = (pts[-1] - pts[-2]).normalized()
    w = 0.085 if heart else 0.07
    parts.append(tube("TailTip", [tip - d * 0.01, tip + d * 0.04, tip + d * 0.08, tip + d * 0.14], [0.0, w, w * 0.75, 0.0], tip_role, segs=10, flat=0.25, ref=(1, 0, 0)))
    return parts, pts


def add_wings(rig, parts, J, span, membrane_role, bone_role, droop=0.0, attach="Chest"):
    for s, side in ((1, "L"), (-1, "R")):
        root = J["chest"] + Vector((s * 0.06, 0.07, 0.06))
        wparts, wrist, tip = bat_wing(root, s, span, membrane_role, bone_role, droop=droop)
        rig.add("Wing" + side, root, wrist, attach)
        rig.add("WingTip" + side, wrist, tip, "Wing" + side)
        for wp in wparts:
            parts.append(bind(wp, rig, ["Wing" + side, "WingTip" + side]))


def add_tail(rig, parts, root, skin_role, tip_role, length, heart=False):
    tparts, tpts = demon_tail(root, skin_role, tip_role, length=length, heart=heart)
    rig.add("Tail1", tpts[0], tpts[3], "Hips")
    rig.add("Tail2", tpts[3], tpts[6], "Tail1")
    rig.add("Tail3", tpts[6], tpts[8], "Tail2")
    for tp in tparts:
        parts.append(bind(tp, rig, ["Tail1", "Tail2", "Tail3"]))


def plate_shell(name, J, role, grow=0.0, subsurf=2):
    return skin(name, [(J["hips"] + Vector((0, 0, 0.02)), (0.118 + grow, 0.088 + grow)), (J["waist"], (0.1 + grow, 0.076 + grow)),
                       (J["spine"], (0.106 + grow, 0.08 + grow)), (J["chest"], (0.134 + grow, 0.094 + grow)),
                       (J["neck"] + Vector((0, 0, -0.04)), (0.07 + grow, 0.062 + grow))],
                [(0, 1), (1, 2), (2, 3), (3, 4)], role, subsurf=subsurf)


def ring(name, z, r, role, h=0.012, center=(0, 0, 0), segs=24, scale_y=1.0):
    ob = lathe(name, [(r * 0.97, z + h), (r, z + h * 0.5), (r, z - h * 0.5), (r * 0.97, z - h)], role, segs=segs, center=(0, 0, 0))
    if scale_y != 1.0:
        xform(ob, (0, 0, 0), (0, 0, 0), (1, scale_y, 1))
    return xform(ob, center)


def sword(rig, parts, J, side="R", length=0.62, role="Metal"):
    h = J["hand" + side]
    blade = prism("Blade", [(-0.02, 0.0), (0.02, 0.0), (0.016, length * 0.86), (0.0, length), (-0.016, length * 0.86)], 0.012, role, bevel=0.004,
                  loc=h + Vector((0, -0.04, 0.02)), rot=(-75, 0, 0))
    parts.append(bind(blade, rig, "Hand" + side))
    fuller = prism("Fuller", [(-0.005, 0.05), (0.005, 0.05), (0.004, length * 0.7), (-0.004, length * 0.7)], 0.014, "Dark", bevel=0.0,
                   loc=h + Vector((0, -0.04, 0.02)), rot=(-75, 0, 0))
    parts.append(bind(fuller, rig, "Hand" + side))
    guard = tube("Guard", curve_pts(h + Vector((-0.08, -0.03, 0.02)), h + Vector((0.08, -0.03, 0.02)), (0, -0.01, 0.025), 5), [0.012, 0.014, 0.016, 0.014, 0.012], "Gold", segs=8)
    parts.append(bind(guard, rig, "Hand" + side))
    parts.append(bind(tube("Grip", [h + Vector((0, 0.05, -0.03)), h + Vector((0, -0.02, 0.01))], [0.015, 0.015], "Leather", segs=8), rig, "Hand" + side))
    parts.append(bind(blob("Pommel", 0.018, h + Vector((0, 0.065, -0.04)), "Glow", subdiv=1, faceted=True), rig, "Hand" + side))


# =============================================================================================
# Characters
# =============================================================================================

def archdemon():
    J = humanoid_joints(h=1.0, shoulder=0.17, hip=0.085)
    head_r = 0.15
    head_c = J["headBase"] + Vector((0, -0.01, head_r * 0.95))
    rig = humanoid_rig(J, head_c + Vector((0, 0, head_r)))
    parts = [bind(humanoid_body(J, "Skin", bulk=1.08), rig, BODY_BONES)]
    for side in "LR":
        for p in hand(J, side, "Skin", scale=1.1, claws="Horn"):
            parts.append(bind(p, rig, "Hand" + side))
    parts.append(bind(anime_head("Head", head_c, head_r, "FaceDemon", chin=0.28), rig, "Head"))
    for p in head_features(head_c, head_r, "Skin", ear_len=1.0):
        parts.append(bind(p, rig, "Head"))
    for p in anime_hair(head_c, head_r, "Hair", seed=3, length=0.95, back=1.2, spikes=1.25, bangs=7):
        parts.append(bind(p, rig, "Head"))
    for s in (1, -1):
        parts.append(bind(curved_horn(head_c + Vector((s * head_r * 0.55, 0.0, head_r * 0.7)), s, 0.46, "Horn", curl=1.0, thickness=0.047), rig, "Head"))

    # Layered plate armor with gold trim.
    parts.append(bind(plate_shell("Breastplate", J, "Primary"), rig, ["Hips", "Spine", "Chest"]))
    parts.append(bind(tube("Ridge", curve_pts(J["chest"] + Vector((0, -0.1, 0.07)), J["spine"] + Vector((0, -0.105, -0.02)), (0, -0.02, 0), 4),
                           [0.012, 0.014, 0.012, 0.008], "Gold", segs=6), rig, ["Chest", "Spine"]))
    parts.append(bind(lathe("Gorget", [(0.06, J["neck"].z - 0.02), (0.085, J["neck"].z - 0.05), (0.1, J["neck"].z - 0.07)], "Primary", segs=20,
                            center=(0, J["neck"].y, 0)), rig, "Chest"))
    parts.append(bind(ring("GorgetTrim", J["neck"].z - 0.07, 0.1, "Gold", center=(0, J["neck"].y, 0)), rig, "Chest"))
    for k in range(3):
        z = J["waist"].z + 0.03 - k * 0.035
        parts.append(bind(ring("AbPlate", z, 0.097 - k * 0.003, "Primary", h=0.014, scale_y=0.8), rig, "Spine" if k == 0 else "Hips"))
    parts.append(bind(ring("Belt", J["hips"].z + 0.02, 0.124, "Leather", h=0.02, scale_y=0.78), rig, "Hips"))
    parts.append(bind(blob("Buckle", 0.03, J["hips"] + Vector((0, -0.1, 0.02)), "Gold", scale=(1.2, 0.5, 1.0), subdiv=2), rig, "Hips"))
    for s, ang in ((0, 0), (1, 1), (-1, 1)):
        y = -0.095 if ang == 0 else -0.04
        x = s * 0.09
        tasset = prism("Tasset", [(-0.045, 0.0), (0.045, 0.0), (0.04, -0.13), (0.0, -0.16), (-0.04, -0.13)], 0.012, "Primary", bevel=0.004,
                       loc=J["hips"] + Vector((x, y, 0.0)), rot=(8, 0, s * 35))
        parts.append(bind(tasset, rig, "Hips"))
    for s, side in ((1, "L"), (-1, "R")):
        sh = J["shoulder" + side]
        for layer in range(3):
            pr = 0.098 - layer * 0.012
            pad = lathe("Pauldron", [(0.0, 0.075 - layer * 0.03), (pr * 0.6, 0.07 - layer * 0.03), (pr, 0.03 - layer * 0.03), (pr * 1.02, -0.01 - layer * 0.03), (pr * 0.8, -0.03 - layer * 0.03)],
                        "Primary" if layer % 2 == 0 else "Dark", segs=18, center=sh + Vector((s * (0.012 + layer * 0.01), 0, 0)))
            parts.append(bind(pad, rig, "UpperArm" + side))
        parts.append(bind(ring("PauldronTrim", sh.z - 0.01, 0.1, "Gold", h=0.008, center=(sh.x + s * 0.012, sh.y, 0)), rig, "UpperArm" + side))
        for k in range(2):
            parts.append(bind(tube("PauldronSpike", [sh + Vector((s * (0.03 + k * 0.03), 0.01 * k, 0.05)), sh + Vector((s * (0.09 + k * 0.03), 0.03, 0.15 - k * 0.03))],
                                   [0.02, 0.0], "Horn", segs=7), rig, "UpperArm" + side))
        parts.append(bind(tube("Bracer", [J["elbow" + side] + Vector((0, 0, -0.02)), J["forearm" + side], J["wrist" + side]], [0.046, 0.047, 0.036], "Primary", segs=14), rig, "LowerArm" + side))
        parts.append(bind(ring("BracerTrim", J["wrist" + side].z + 0.012, 0.037, "Gold", h=0.006, center=(J["wrist" + side].x, J["wrist" + side].y, 0)), rig, "LowerArm" + side))
        parts.append(bind(tube("BracerSpike", [J["forearm" + side] + Vector((s * 0.04, 0.01, 0)), J["forearm" + side] + Vector((s * 0.085, 0.03, 0.03))], [0.014, 0.0], "Horn", segs=6), rig, "LowerArm" + side))
        parts.append(bind(tube("Greave", [J["knee" + side] + Vector((0, 0, 0.02)), J["calf" + side], J["ankle" + side] + Vector((0, 0, -0.01))], [0.056, 0.06, 0.042], "Dark", segs=14), rig, "LowerLeg" + side))
        parts.append(bind(blob("Kneecap", 0.042, J["knee" + side] + Vector((0, -0.035, 0.01)), "Primary", scale=(1, 0.65, 1.15), subdiv=2), rig, "LowerLeg" + side))
        parts.append(bind(tube("Boot", [J["ankle" + side] + Vector((0, 0.025, 0.0)), J["toe" + side] + Vector((0, -0.01, 0.0))], [0.045, 0.034], "Dark", segs=12, flat=0.7), rig, "Foot" + side))
        for k in (-1, 0, 1):
            toe = J["toe" + side] + Vector((k * 0.018, -0.03, -0.005))
            parts.append(bind(tube("ToeClaw", [toe, toe + Vector((0, -0.035, -0.01))], [0.009, 0.0], "Horn", segs=5), rig, "Foot" + side))
    sword(rig, parts, J, "R", length=0.66)
    add_wings(rig, parts, J, 0.78, "Wing", "Horn")
    add_tail(rig, parts, J["hips"] + Vector((0, 0.08, -0.02)), "Skin", "Horn", 0.78)
    finish_character("demon", rig, parts, "biped", weight=1.0)


def princess():
    J = humanoid_joints(h=0.98, shoulder=0.13, hip=0.07)
    head_r = 0.145
    head_c = J["headBase"] + Vector((0, -0.01, head_r * 0.95))
    rig = humanoid_rig(J, head_c + Vector((0, 0, head_r)))
    parts = [bind(humanoid_body(J, "SkinPale", female=True, bulk=0.95), rig, BODY_BONES)]
    for side in "LR":
        for p in hand(J, side, "SkinPale", scale=0.85):
            parts.append(bind(p, rig, "Hand" + side))
    parts.append(bind(anime_head("Head", head_c, head_r, "FacePrincess", chin=0.36, cheek=0.03), rig, "Head"))
    for p in head_features(head_c, head_r, "SkinPale", ear_len=0.7):
        parts.append(bind(p, rig, "Head"))
    for p in anime_hair(head_c, head_r, "HairPrincess", seed=11, length=0.8, back=0.9, spikes=0.6, bangs=9, ahoge=True):
        parts.append(bind(p, rig, "Head"))
    # Long ribbon-like strands down the back to the hips, and side locks framing the face.
    for i in range(11):
        x = (i - 5) * 0.026
        top = head_c + Vector((x, head_r * 0.7, head_r * 0.35))
        bottom = top + Vector((x * 0.6, 0.12, -0.72 + abs(x) * 0.8))
        pts = curve_pts(top, bottom, (0, 0.1, 0.02), 7, sway=(0.012 * math.sin(i * 1.7), 0, 0))
        parts.append(bind(tube("LongHair", pts, [0.055, 0.05, 0.046, 0.04, 0.032, 0.02, 0.0], "HairPrincess", segs=8, flat=0.3, ref=(0, 1, 0)),
                          rig, ["Head", "Chest", "Spine", "Hips"]))
    for s in (1, -1):
        top = head_c + Vector((s * head_r * 0.86, -head_r * 0.35, head_r * 0.1))
        lock = curve_pts(top, top + Vector((s * 0.04, -0.02, -0.34)), (s * 0.03, -0.02, 0), 6)
        parts.append(bind(tube("SideLock", lock, [0.036, 0.032, 0.028, 0.022, 0.014, 0.0], "HairPrincess", segs=8, flat=0.45, twist=0.8), rig, ["Head", "Chest"]))
        parts.append(bind(curved_horn(head_c + Vector((s * head_r * 0.5, 0.01, head_r * 0.78)), s, 0.28, "Horn", curl=0.85, thickness=0.031), rig, "Head"))
        parts.append(bind(blob("Earring", 0.012, head_c + Vector((s * head_r * 0.98, 0.02, -head_r * 0.28)), "Glow", subdiv=1, faceted=True), rig, "Head"))
    cz = head_c.z + head_r * 0.93
    parts.append(bind(ring("Crown", cz, 0.08, "Gold", h=0.018, center=(head_c.x, head_c.y + 0.01, 0)), rig, "Head"))
    for k in range(8):
        a = 2 * math.pi * k / 8
        b = head_c + Vector((math.sin(a) * 0.078, 0.01 + math.cos(a) * 0.078, head_r * 0.93 + 0.016))
        parts.append(bind(tube("CrownSpike", [b, b + Vector((0, 0, 0.065 if k % 2 == 0 else 0.04))], [0.011, 0.0], "Gold", segs=5), rig, "Head"))
    parts.append(bind(blob("CrownGem", 0.018, head_c + Vector((0, -0.078, head_r * 0.93 + 0.03)), "Glow", subdiv=1, faceted=True), rig, "Head"))

    # Gown: corset bodice with lacing, ruffled collar, open overskirt over frilled petticoats, bell sleeves, bow.
    bod = skin("Bodice", [(J["hips"] + Vector((0, 0, 0.03)), (0.123, 0.088)), (J["waist"], (0.074, 0.064)), (J["spine"], (0.086, 0.07)),
                          (J["chest"], (0.1, 0.082)), (J["neck"] + Vector((0, 0, -0.055)), (0.06, 0.05))], [(0, 1), (1, 2), (2, 3), (3, 4)], "Cloth", subsurf=2)
    parts.append(bind(bod, rig, ["Hips", "Spine", "Chest"]))
    for k in range(5):
        z = J["waist"].z - 0.04 + k * 0.04
        for s in (1, -1):
            parts.append(bind(tube("Lacing", [Vector((-s * 0.02, -0.083, z)), Vector((s * 0.02, -0.084, z + 0.04))], [0.004, 0.004], "Gold", segs=5), rig, ["Spine", "Chest"]))
    collar = lathe("Collar", [(0.06, J["neck"].z - 0.04), (0.09, J["neck"].z - 0.02), (0.11, J["neck"].z + 0.0)], "Lace", segs=32,
                   wave=lambda t, i: 1.0 + 0.1 * math.sin(t * 16) * (i / 2), center=(0, J["neck"].y + 0.01, 0))
    parts.append(bind(collar, rig, "Chest"))
    waist = J["hips"].z + 0.04
    frill = lambda t, i: 1.0 + (0.05 * math.sin(t * 14) if i >= 4 else 0.0)
    parts.append(bind(lathe("Petticoat", [(0.0, waist), (0.11, waist), (0.2, waist - 0.18), (0.3, waist - 0.36), (0.38, 0.06), (0.41, 0.03), (0.38, 0.0), (0.0, 0.0)],
                            "Lace", segs=40, wave=frill), rig, "Hips"))
    skirt = lathe("Skirt", [(0.118, waist + 0.01), (0.16, waist - 0.1), (0.24, waist - 0.25), (0.33, waist - 0.4), (0.4, waist - 0.5), (0.425, 0.04)],
                  "Cloth", segs=36, arc=(305, 595), wave=lambda t, i: 1.0 + 0.04 * math.sin(t * 9) * (i / 5))
    parts.append(bind(skirt, rig, "Hips"))
    parts.append(bind(lathe("Hem", [(0.42, 0.06), (0.432, 0.045), (0.42, 0.03)], "Gold", segs=36, arc=(305, 595)), rig, "Hips"))
    parts.append(bind(ring("WaistTrim", waist, 0.12, "Gold", h=0.01, scale_y=0.8), rig, "Hips"))
    for s in (1, -1):   # back bow
        loop = curve_pts(Vector((0, 0.09, waist + 0.01)), Vector((s * 0.12, 0.13, waist + 0.06)), (0, 0.04, 0.06), 5)
        parts.append(bind(tube("Bow", loop, [0.02, 0.04, 0.045, 0.035, 0.0], "Cloth", segs=8, flat=0.35, ref=(0, 1, 0)), rig, "Hips"))
        tail = curve_pts(Vector((s * 0.02, 0.1, waist)), Vector((s * 0.07, 0.16, waist - 0.3)), (s * 0.02, 0.03, 0), 5)
        parts.append(bind(tube("BowTail", tail, [0.02, 0.03, 0.032, 0.03, 0.0], "Cloth", segs=8, flat=0.25, ref=(0, 1, 0)), rig, "Hips"))
    for s, side in ((1, "L"), (-1, "R")):
        parts.append(bind(blob("PuffSleeve", 0.062, J["shoulder" + side] + Vector((s * 0.02, 0, -0.02)), "Cloth", scale=(1, 1, 0.9), subdiv=3, lumps=0.05), rig, "UpperArm" + side))
        sleeve = lathe("BellSleeve", [(0.03, 0.0), (0.04, -0.08), (0.06, -0.15), (0.075, -0.18)], "Lace", segs=18,
                       wave=lambda t, i: 1.0 + 0.08 * math.sin(t * 10) * (i / 3))
        e = J["elbow" + side]
        xform(sleeve, (0, 0, 0), (0, 0, 0))
        xform(sleeve, e + Vector((0, 0, -0.005)))
        parts.append(bind(sleeve, rig, "LowerArm" + side))
    parts.append(bind(ring("Necklace", J["neck"].z - 0.045, 0.052, "Gold", h=0.005, center=(0, J["neck"].y, 0)), rig, "Chest"))
    parts.append(bind(blob("Pendant", 0.018, J["neck"] + Vector((0, -0.058, -0.075)), "Glow", subdiv=1, faceted=True), rig, "Chest"))
    add_wings(rig, parts, J, 0.44, "Wing", "Horn", droop=0.04)
    add_tail(rig, parts, J["hips"] + Vector((0, 0.12, 0.0)), "SkinPale", "Cloth", 0.56, heart=True)
    finish_character("princess", rig, parts, "biped", weight=0.6, grace=1.0)


def crusader():
    J = humanoid_joints(h=1.0, shoulder=0.16, hip=0.085)
    head_r = 0.14
    head_c = J["headBase"] + Vector((0, -0.01, head_r * 0.95))
    rig = humanoid_rig(J, head_c + Vector((0, 0, head_r)))
    parts = [bind(humanoid_body(J, "Chain", bulk=1.12, subsurf=1), rig, BODY_BONES)]
    for side in "LR":
        for p in hand(J, side, "Metal", scale=1.15):
            parts.append(bind(p, rig, "Hand" + side))
    helm = lathe("Helm", [(0.0, head_r * 1.18), (0.07, head_r * 1.1), (0.12, head_r * 0.82), (0.148, head_r * 0.2), (0.148, -head_r * 0.75),
                          (0.135, -head_r * 0.92), (0.0, -head_r * 0.92)], "Metal", segs=28, center=head_c)
    parts.append(bind(helm, rig, "Head"))
    parts.append(bind(tube("Crest", curve_pts(head_c + Vector((0, -0.13, head_r * 0.6)), head_c + Vector((0, 0.15, head_r * 0.2)), (0, 0, head_r * 0.62), 7),
                           [0.012, 0.016, 0.018, 0.018, 0.016, 0.012, 0.008], "Gold", segs=6, flat=2.0, ref=(1, 0, 0)), rig, "Head"))
    parts.append(bind(bevel_box("Visor", (0.2, 0.03, 0.022), head_c + Vector((0, -0.145, 0.012)), "Dark", bevel=0.005), rig, "Head"))
    for k in range(4):
        parts.append(bind(bevel_box("Breath", (0.012, 0.02, 0.03), head_c + Vector((-0.03 + k * 0.02, -0.146, -0.06)), "Dark", bevel=0.003), rig, "Head"))
    plume = tube("Plume", curve_pts(head_c + Vector((0, 0.02, head_r * 1.15)), head_c + Vector((0, 0.3, head_r * 0.5)), (0, 0.05, head_r * 0.6), 6),
                 [0.03, 0.05, 0.055, 0.045, 0.03, 0.0], "Primary", segs=9, flat=0.4, ref=(1, 0, 0))
    parts.append(bind(plume, rig, "Head"))
    parts.append(bind(plate_shell("Breastplate", J, "Metal", grow=0.004, subsurf=1), rig, ["Hips", "Spine", "Chest"]))
    parts.append(bind(lathe("Gorget", [(0.06, J["neck"].z - 0.02), (0.085, J["neck"].z - 0.05), (0.1, J["neck"].z - 0.07)], "Metal", segs=20, center=(0, J["neck"].y, 0)), rig, "Chest"))
    parts.append(bind(lathe("Mail", [(0.12, J["hips"].z + 0.02), (0.135, J["hips"].z - 0.08), (0.15, J["hips"].z - 0.22)], "Chain", segs=24,
                            wave=lambda t, i: 1.0 + 0.03 * math.sin(t * 12) * i), rig, "Hips"))
    for front in (True, False):
        y = -0.1 if front else 0.1
        tab = tube("Tabard", [J["chest"] + Vector((0, y, 0.04)), J["hips"] + Vector((0, y * 1.05, 0)), J["hips"] + Vector((0, y * 1.12, -0.28))],
                   [0.085, 0.09, 0.088], "White", segs=10, flat=0.1, ref=(0, 1, 0))
        parts.append(bind(tab, rig, ["Chest", "Spine", "Hips"]))
    parts.append(bind(bevel_box("CrossV", (0.03, 0.012, 0.22), J["spine"] + Vector((0, -0.113, 0.0)), "Gold", bevel=0.004), rig, "Spine"))
    parts.append(bind(bevel_box("CrossH", (0.13, 0.012, 0.03), J["spine"] + Vector((0, -0.113, 0.045)), "Gold", bevel=0.004), rig, "Spine"))
    cape = lathe("Cape", [(0.12, J["neck"].z - 0.03), (0.15, J["chest"].z), (0.17, J["spine"].z), (0.2, J["hips"].z - 0.1), (0.22, J["hips"].z - 0.32)],
                 "Primary", segs=16, arc=(30, 150), wave=lambda t, i: 1.0 + 0.07 * math.sin(t * 14) * (i / 4), center=(0, 0.05, 0))
    parts.append(bind(cape, rig, ["Chest", "Spine", "Hips"]))
    for s, side in ((1, "L"), (-1, "R")):
        sh = J["shoulder" + side]
        for layer in range(2):
            pr = 0.1 - layer * 0.014
            parts.append(bind(lathe("Pauldron", [(0.0, 0.07 - layer * 0.03), (pr * 0.6, 0.066 - layer * 0.03), (pr, 0.03 - layer * 0.03), (pr * 1.02, -0.015 - layer * 0.03), (pr * 0.78, -0.035 - layer * 0.03)],
                                    "Metal", segs=18, center=sh + Vector((s * (0.012 + layer * 0.012), 0, 0))), rig, "UpperArm" + side))
        parts.append(bind(ring("Rim", sh.z - 0.015, 0.102, "Gold", h=0.007, center=(sh.x + s * 0.012, sh.y, 0)), rig, "UpperArm" + side))
        parts.append(bind(tube("Vambrace", [J["elbow" + side], J["forearm" + side], J["wrist" + side]], [0.048, 0.049, 0.04], "Metal", segs=14), rig, "LowerArm" + side))
        parts.append(bind(tube("Greave", [J["knee" + side] + Vector((0, 0, 0.02)), J["calf" + side], J["ankle" + side]], [0.055, 0.058, 0.042], "Metal", segs=14), rig, "LowerLeg" + side))
        parts.append(bind(blob("Poleyn", 0.045, J["knee" + side] + Vector((0, -0.035, 0.01)), "Metal", scale=(1, 0.6, 1.1), subdiv=2), rig, "LowerLeg" + side))
        parts.append(bind(tube("Sabaton", [J["ankle" + side], J["toe" + side]], [0.046, 0.036], "Metal", segs=12, flat=0.7), rig, "Foot" + side))
    outline = [(-0.13, 0.12), (0.13, 0.12), (0.125, -0.05), (0.07, -0.18), (0.0, -0.26), (-0.07, -0.18), (-0.125, -0.05)]
    base = J["elbowL"] + Vector((0.075, -0.06, -0.05))
    parts.append(bind(prism("Shield", outline, 0.03, "Primary", bevel=0.01, loc=base, rot=(0, 0, 90)), rig, "LowerArmL"))
    parts.append(bind(prism("ShieldCrossV", [(-0.015, 0.09), (0.015, 0.09), (0.015, -0.2), (-0.015, -0.2)], 0.036, "Gold", bevel=0.004, loc=base, rot=(0, 0, 90)), rig, "LowerArmL"))
    parts.append(bind(prism("ShieldCrossH", [(-0.09, 0.03), (0.09, 0.03), (0.09, 0.0), (-0.09, 0.0)], 0.036, "Gold", bevel=0.004, loc=base, rot=(0, 0, 90)), rig, "LowerArmL"))
    sword(rig, parts, J, "R", length=0.58)
    finish_character("human", rig, parts, "biped", weight=0.9)


def ghoul():
    J = humanoid_joints(h=0.95, shoulder=0.14, hip=0.075, hunch=0.07)
    head_r = 0.12
    head_c = J["headBase"] + Vector((0, -0.05, head_r * 0.85))
    rig = humanoid_rig(J, head_c + Vector((0, 0, head_r)))
    parts = [bind(humanoid_body(J, "Primary", gaunt=True, subsurf=1), rig, BODY_BONES)]
    for side in "LR":
        for p in hand(J, side, "Primary", scale=1.25, claws="Horn"):
            parts.append(bind(p, rig, "Hand" + side))
    skull = blob("Head", head_r, head_c, "Primary", scale=(0.9, 1.05, 1.0), subdiv=3, lumps=0.06, seed=2)
    parts.append(bind(skull, rig, "Head"))
    parts.append(bind(blob("Jaw", head_r * 0.6, head_c + Vector((0, -0.06, -0.085)), "Secondary", scale=(1.0, 0.9, 0.55), subdiv=2), rig, "Head"))
    for k in range(6):
        x = (k - 2.5) * 0.016
        parts.append(bind(tube("Tooth", [head_c + Vector((x, -0.105, -0.06)), head_c + Vector((x, -0.11, -0.085))], [0.006, 0.0], "Teeth", segs=4), rig, "Head"))
    for s in (1, -1):
        parts.append(bind(blob("Socket", 0.03, head_c + Vector((s * 0.045, -0.095, 0.012)), "Dark", subdiv=2), rig, "Head"))
        parts.append(bind(blob("Eye", 0.016, head_c + Vector((s * 0.045, -0.115, 0.012)), "Eye", subdiv=2), rig, "Head"))
    for k in range(5):   # ribs
        z = J["chest"].z + 0.01 - k * 0.035
        for s in (1, -1):
            pts = [Vector((s * 0.008, -0.062, z)), Vector((s * 0.058, -0.048, z - 0.01)), Vector((s * 0.075, 0.0, z - 0.02))]
            parts.append(bind(tube("Rib", [p + Vector((0, J["chest"].y, 0)) for p in pts], [0.008, 0.009, 0.006], "Secondary", segs=6), rig, "Chest"))
    for k in range(6):   # vertebrae ridge
        p = J["hips"].lerp(J["neck"], k / 5) + Vector((0, 0.06, 0))
        parts.append(bind(blob("Vertebra", 0.016, p, "Secondary", subdiv=1), rig, "Spine" if k < 3 else "Chest"))
    rags = lathe("Rags", [(0.085, J["hips"].z + 0.02), (0.1, J["hips"].z - 0.1), (0.11, J["hips"].z - 0.2)], "Secondary", segs=20,
                 wave=lambda t, i: 1.0 + 0.12 * math.sin(t * 7) * (i / 2))
    for v in rags.data.vertices:   # tattered hem
        if v.co.z < J["hips"].z - 0.15:
            v.co.z += 0.05 * abs(math.sin(v.co.x * 60 + v.co.y * 40))
    parts.append(bind(rags, rig, "Hips"))
    finish_character("humanoid", rig, parts, "biped", weight=0.8, hunch=1.0, claws=True)


def merchant_imp():
    J = humanoid_joints(h=0.62, shoulder=0.11, hip=0.06)
    head_r = 0.15
    head_c = J["headBase"] + Vector((0, -0.01, head_r * 0.9))
    rig = humanoid_rig(J, head_c + Vector((0, 0, head_r)))
    parts = [bind(humanoid_body(J, "Skin", bulk=1.15), rig, BODY_BONES)]
    for side in "LR":
        for p in hand(J, side, "Skin", scale=0.9):
            parts.append(bind(p, rig, "Hand" + side))
    parts.append(bind(anime_head("Head", head_c, head_r, "FaceImp", chin=0.2, cheek=0.05), rig, "Head"))
    for p in head_features(head_c, head_r, "Skin", ear_len=1.4):
        parts.append(bind(p, rig, "Head"))
    for s in (1, -1):
        parts.append(bind(curved_horn(head_c + Vector((s * head_r * 0.5, 0, head_r * 0.7)), s, 0.17, "Horn", curl=0.5, thickness=0.026), rig, "Head"))
    hood = lathe("Hood", [(0.0, head_r * 1.22), (head_r * 0.8, head_r * 0.97), (head_r * 1.13, head_r * 0.2), (head_r * 1.1, -head_r * 0.35)], "Cloth", segs=22,
                 center=head_c + Vector((0, 0.03, 0)))
    for v in hood.data.vertices:
        rel = v.co - head_c
        if rel.y < -0.4 * head_r and rel.z < head_r * 0.7:
            v.co = head_c + rel * Vector((1, 0.4, 1))
    parts.append(bind(hood, rig, "Head"))
    parts.append(bind(lathe("Coat", [(0.11, J["chest"].z + 0.03), (0.12, J["spine"].z), (0.15, J["hips"].z - 0.06), (0.17, J["hips"].z - 0.16)], "Cloth", segs=20,
                            wave=lambda t, i: 1.0 + 0.05 * math.sin(t * 8) * (i / 3)), rig, ["Chest", "Spine", "Hips"]))
    parts.append(bind(ring("Sash", J["hips"].z + 0.01, 0.13, "Gold", h=0.014, scale_y=0.85), rig, "Hips"))
    parts.append(bind(blob("CoinPouch", 0.04, J["hipL"] + Vector((0.07, -0.04, 0.0)), "Leather", scale=(1, 1, 1.2), subdiv=2), rig, "Hips"))
    pack = bevel_box("Backpack", (0.26, 0.18, 0.3), J["chest"] + Vector((0, 0.16, 0.0)), "Wood", bevel=0.02)
    parts.append(bind(pack, rig, "Chest"))
    for k in range(3):
        parts.append(bind(bevel_box("PackBand", (0.28, 0.2, 0.025), J["chest"] + Vector((0, 0.16, -0.1 + k * 0.1)), "Gold", bevel=0.006), rig, "Chest"))
    parts.append(bind(blob("Bedroll", 0.06, J["chest"] + Vector((0, 0.16, 0.2)), "Cloth", scale=(2.3, 1, 1), subdiv=2), rig, "Chest"))
    parts.append(bind(lathe("Pot", [(0.0, 0.0), (0.05, 0.02), (0.06, 0.06), (0.04, 0.1), (0.045, 0.11), (0.0, 0.1)], "Stone", segs=12,
                            center=J["chest"] + Vector((0.13, 0.17, -0.17))), rig, "Chest"))
    parts.append(bind(tube("ScrollCase", [J["chest"] + Vector((-0.12, 0.2, -0.15)), J["chest"] + Vector((-0.12, 0.2, 0.18))], [0.025, 0.025], "Leather", segs=8), rig, "Chest"))
    lantern = blob("Lantern", 0.04, J["handR"] + Vector((0, -0.02, -0.07)), "Fire", subdiv=2)
    parts.append(bind(lantern, rig, "HandR"))
    for k in range(4):
        a = math.pi / 2 * k
        bar = tube("LanternCage", [J["handR"] + Vector((math.cos(a) * 0.045, -0.02 + math.sin(a) * 0.045, -0.02)), J["handR"] + Vector((math.cos(a) * 0.045, -0.02 + math.sin(a) * 0.045, -0.12))],
                   [0.005, 0.005], "Metal", segs=4)
        parts.append(bind(bar, rig, "HandR"))
    parts.append(bind(lathe("LanternCap", [(0.0, -0.005), (0.055, -0.02), (0.05, -0.03)], "Metal", segs=10, center=J["handR"] + Vector((0, -0.02, 0))), rig, "HandR"))
    add_tail(rig, parts, J["hips"] + Vector((0, 0.08, -0.01)), "Skin", "Horn", 0.42)
    finish_character("merchant", rig, parts, "biped", weight=0.7)


def beast():
    rig = Rig()
    P = {"tail": Vector((0, 0.42, 0.56)), "hips": Vector((0, 0.3, 0.52)), "belly": Vector((0, 0.06, 0.48)), "chest": Vector((0, -0.2, 0.54)),
         "neck": Vector((0, -0.36, 0.65)), "head": Vector((0, -0.48, 0.71)), "muzzle": Vector((0, -0.6, 0.67)), "snout": Vector((0, -0.7, 0.65))}
    legs = {}
    for s, side in ((1, "L"), (-1, "R")):
        legs["fs" + side] = Vector((s * 0.1, -0.2, 0.46)); legs["fm" + side] = Vector((s * 0.115, -0.17, 0.34))
        legs["fe" + side] = Vector((s * 0.11, -0.19, 0.22)); legs["fp" + side] = Vector((s * 0.11, -0.24, 0.03))
        legs["bh" + side] = Vector((s * 0.1, 0.3, 0.46)); legs["bt" + side] = Vector((s * 0.12, 0.36, 0.36))
        legs["bk" + side] = Vector((s * 0.11, 0.4, 0.24)); legs["bp" + side] = Vector((s * 0.11, 0.3, 0.03))
    nodes = [(P["hips"], (0.12, 0.13)), (P["tail"], 0.05), (P["belly"], (0.1, 0.11)), (P["chest"], (0.135, 0.15)), (P["neck"], 0.08),
             (P["head"], (0.085, 0.095)), (P["muzzle"], (0.05, 0.055)), (P["snout"], (0.035, 0.04))]
    edges = [(0, 1), (0, 2), (2, 3), (3, 4), (4, 5), (5, 6), (6, 7)]
    for side in "LR":
        b = len(nodes)
        nodes += [(legs["fs" + side], 0.06), (legs["fm" + side], 0.05), (legs["fe" + side], 0.035), (legs["fp" + side], 0.036),
                  (legs["bh" + side], 0.07), (legs["bt" + side], 0.06), (legs["bk" + side], 0.04), (legs["bp" + side], 0.036)]
        edges += [(3, b), (b, b + 1), (b + 1, b + 2), (b + 2, b + 3), (0, b + 4), (b + 4, b + 5), (b + 5, b + 6), (b + 6, b + 7)]
    rig.add("Hips", P["hips"], P["belly"])
    rig.add("Spine", P["belly"], P["chest"], "Hips")
    rig.add("Chest", P["chest"], P["neck"], "Spine")
    rig.add("Head", P["neck"], P["snout"], "Chest")
    for side in "LR":
        rig.add("Leg" + side, legs["fs" + side], legs["fe" + side], "Chest")
        rig.add("Shin" + side, legs["fe" + side], legs["fp" + side], "Leg" + side)
        rig.add("Leg2" + side, legs["bh" + side], legs["bk" + side], "Hips")
        rig.add("Shin2" + side, legs["bk" + side], legs["bp" + side], "Leg2" + side)
    tail_pts = curve_pts(P["tail"], P["tail"] + Vector((0, 0.42, -0.08)), (0, 0, 0.12), 6)
    rig.add("Tail1", tail_pts[0], tail_pts[3], "Hips")
    rig.add("Tail2", tail_pts[3], tail_pts[5], "Tail1")
    bones = ["Hips", "Spine", "Chest", "Head"] + [b + s for s in "LR" for b in ("Leg", "Shin", "Leg2", "Shin2")]
    parts = [bind(skin("Body", nodes, edges, "Primary", subsurf=1), rig, bones)]
    parts.append(bind(tube("Tail", tail_pts, [0.055, 0.06, 0.055, 0.045, 0.03, 0.0], "Secondary", segs=9), rig, ["Tail1", "Tail2"]))
    rnd = random.Random(4)
    for k in range(16):   # mane
        a = math.pi * (k / 15) - math.pi / 2
        base = P["neck"] + Vector((math.sin(a) * 0.08, 0.02 + rnd.uniform(-0.03, 0.03), 0.04 + math.cos(a) * 0.05))
        tip = base + Vector((math.sin(a) * 0.08, 0.1 + rnd.uniform(0, 0.06), 0.05))
        parts.append(bind(tube("Mane", curve_pts(base, tip, (0, 0, 0.03), 4), [0.03, 0.025, 0.012, 0.0], "Secondary", segs=6, flat=0.5), rig, "Chest"))
    for k in range(6):
        t = k / 5
        base = P["hips"].lerp(P["chest"], t) + Vector((0, 0, 0.11 + 0.02 * math.sin(t * math.pi)))
        parts.append(bind(tube("Ridge", [base, base + Vector((0, 0.05, 0.07))], [0.022, 0.0], "Secondary", segs=6, flat=0.4, ref=(1, 0, 0)), rig, "Spine" if t < 0.6 else "Chest"))
    parts.append(bind(blob("Jaw", 0.045, P["muzzle"] + Vector((0, 0.01, -0.045)), "Primary", scale=(1.0, 2.0, 0.5), subdiv=2), rig, "Head"))
    parts.append(bind(blob("Nose", 0.02, P["snout"] + Vector((0, -0.035, 0.012)), "Dark", subdiv=2), rig, "Head"))
    for s, side in ((1, "L"), (-1, "R")):
        ear = curve_pts(P["head"] + Vector((s * 0.05, 0.03, 0.06)), P["head"] + Vector((s * 0.08, 0.07, 0.17)), (s * 0.01, 0, 0), 4)
        parts.append(bind(tube("Ear", ear, [0.036, 0.03, 0.015, 0.0], "Secondary", segs=7, flat=0.45, ref=(0, 1, 0)), rig, "Head"))
        parts.append(bind(blob("Eye", 0.018, P["head"] + Vector((s * 0.048, -0.075, 0.03)), "Eye", subdiv=2), rig, "Head"))
        for k in range(3):
            x = s * (0.012 + k * 0.012)
            parts.append(bind(tube("Fang", [P["muzzle"] + Vector((x, -0.05 + k * 0.02, -0.03)), P["muzzle"] + Vector((x, -0.05 + k * 0.02, -0.065 + k * 0.01))], [0.007, 0.0], "Teeth", segs=5), rig, "Head"))
        for paw, bone in (("fp", "Shin"), ("bp", "Shin2")):
            for k in (-1, 0, 1):
                p = legs[paw + side] + Vector((k * 0.015, -0.03, -0.005))
                parts.append(bind(tube("Claw", [p, p + Vector((0, -0.03, -0.012))], [0.007, 0.0], "Horn", segs=5), rig, bone + side))
    finish_character("beast", rig, parts, "quadruped")


def flyer():
    rig = Rig()
    c = Vector((0, 0, 0.85))
    rig.add("Body", c + Vector((0, 0.05, -0.12)), c + Vector((0, 0, 0.1)))
    rig.add("Head", c + Vector((0, -0.03, 0.1)), c + Vector((0, -0.06, 0.28)), "Body")
    parts = [bind(skin("Body", [(c + Vector((0, 0.03, -0.11)), 0.065), (c, (0.12, 0.11)), (c + Vector((0, -0.02, 0.1)), 0.08)], [(0, 1), (1, 2)], "Primary", subsurf=2), rig, ["Body", "Head"])]
    hc = c + Vector((0, -0.04, 0.17))
    parts.append(bind(blob("Head", 0.085, hc, "Primary", scale=(1.05, 1, 0.95), subdiv=3), rig, "Head"))
    parts.append(bind(blob("Snout", 0.03, hc + Vector((0, -0.075, -0.02)), "Primary", scale=(1.2, 1, 0.8), subdiv=2), rig, "Head"))
    rnd = random.Random(8)
    for k in range(10):   # fur tuft
        a = rnd.uniform(0, 2 * math.pi)
        base = c + Vector((math.cos(a) * 0.08, 0.02 + math.sin(a) * 0.06, 0.08))
        parts.append(bind(tube("Fur", [base, base + Vector((math.cos(a) * 0.04, 0.03, 0.05))], [0.02, 0.0], "Secondary", segs=5), rig, "Body"))
    for s, side in ((1, "L"), (-1, "R")):
        ear = curve_pts(hc + Vector((s * 0.05, 0, 0.05)), hc + Vector((s * 0.1, 0.02, 0.2)), (s * 0.02, 0, 0), 4)
        parts.append(bind(tube("Ear", ear, [0.04, 0.035, 0.018, 0.0], "Secondary", segs=7, flat=0.45, ref=(0, 1, 0)), rig, "Head"))
        parts.append(bind(blob("Eye", 0.02, hc + Vector((s * 0.035, -0.075, 0.015)), "Eye", subdiv=2), rig, "Head"))
        parts.append(bind(tube("Fang", [hc + Vector((s * 0.015, -0.095, -0.035)), hc + Vector((s * 0.015, -0.098, -0.06))], [0.006, 0.0], "Teeth", segs=4), rig, "Head"))
        root = c + Vector((s * 0.08, 0.02, 0.04))
        wparts, wrist, tip = bat_wing(root, s, 0.58, "Secondary", "Primary")
        rig.add("Wing" + side, root, wrist, "Body")
        rig.add("WingTip" + side, wrist, tip, "Wing" + side)
        for wp in wparts:
            parts.append(bind(wp, rig, ["Wing" + side, "WingTip" + side]))
        parts.append(bind(tube("Foot", [c + Vector((s * 0.04, 0, -0.1)), c + Vector((s * 0.05, -0.02, -0.2))], [0.015, 0.0], "Horn", segs=5), rig, "Body"))
    rig.add("Tail1", c + Vector((0, 0.06, -0.08)), c + Vector((0, 0.18, -0.25)), "Body")
    parts.append(bind(tube("Tail", [c + Vector((0, 0.06, -0.08)), c + Vector((0, 0.14, -0.18)), c + Vector((0, 0.2, -0.3))], [0.03, 0.02, 0.0], "Primary", segs=6), rig, "Tail1"))
    finish_character("flyer", rig, parts, "flyer")


def insect():
    rig = Rig()
    th = Vector((0, -0.05, 0.3))
    ab = Vector((0, 0.3, 0.36))
    hd = Vector((0, -0.25, 0.3))
    rig.add("Body", th + Vector((0, 0.08, 0)), th + Vector((0, -0.1, 0)))
    rig.add("Abdomen", th + Vector((0, 0.1, 0)), ab + Vector((0, 0.2, 0)), "Body")
    rig.add("Head", th + Vector((0, -0.12, 0)), hd + Vector((0, -0.1, 0)), "Body")
    parts = [bind(blob("Thorax", 0.15, th, "Primary", scale=(1.0, 1.1, 0.75), subdiv=3), rig, "Body")]
    parts.append(bind(blob("Abdomen", 0.22, ab, "Secondary", scale=(0.95, 1.2, 0.85), subdiv=3, lumps=0.03), rig, "Abdomen"))
    for k in range(3):
        parts.append(bind(blob("Band", 0.05, ab + Vector((0, -0.08 + k * 0.1, 0.17)), "Primary", scale=(2.3, 0.45, 0.4), subdiv=2), rig, "Abdomen"))
    parts.append(bind(blob("Marking", 0.04, ab + Vector((0, 0.12, 0.17)), "Glow", scale=(1.4, 1.6, 0.4), subdiv=2), rig, "Abdomen"))
    parts.append(bind(blob("Head", 0.085, hd, "Primary", scale=(1.1, 1, 0.85), subdiv=3), rig, "Head"))
    for s in (1, -1):
        for (x, z, r) in ((0.028, 0.045, 0.016), (0.055, 0.025, 0.012), (0.02, 0.005, 0.011), (0.045, -0.005, 0.009)):
            parts.append(bind(blob("Eye", r, hd + Vector((s * x, -0.075, z)), "Eye", subdiv=1), rig, "Head"))
        fang = curve_pts(hd + Vector((s * 0.025, -0.07, -0.03)), hd + Vector((s * 0.012, -0.11, -0.1)), (s * 0.015, -0.01, 0), 4)
        parts.append(bind(tube("Fang", fang, [0.016, 0.013, 0.008, 0.0], "Horn", segs=6), rig, "Head"))
        palp = curve_pts(hd + Vector((s * 0.05, -0.06, -0.01)), hd + Vector((s * 0.09, -0.14, -0.06)), (s * 0.02, 0, 0.04), 4)
        parts.append(bind(tube("Palp", palp, [0.012, 0.011, 0.009, 0.007], "Primary", segs=6), rig, "Head"))
    for i, y in enumerate([-0.15, -0.06, 0.03, 0.12]):
        suffix = "" if i == 0 else str(i + 1)
        for s, side in ((1, "L"), (-1, "R")):
            hip = th + Vector((s * 0.1, y, 0.02))
            knee = hip + Vector((s * 0.17, (y + 0.02) * 0.6, 0.2))
            ankle = knee + Vector((s * 0.12, (y + 0.02) * 0.6, -0.3))
            foot = ankle + Vector((s * 0.04, (y + 0.02) * 0.3, -0.2))
            rig.add("Leg" + suffix + side, hip, knee, "Body")
            rig.add("Shin" + suffix + side, knee, foot, "Leg" + suffix + side)
            leg = tube("Leg", [hip, hip.lerp(knee, 0.5), knee, ankle, foot], [0.03, 0.026, 0.024, 0.016, 0.0], "Primary", segs=8)
            parts.append(bind(leg, rig, ["Leg" + suffix + side, "Shin" + suffix + side]))
            parts.append(bind(blob("Joint", 0.026, knee, "Secondary", subdiv=1), rig, "Leg" + suffix + side))
            for k in range(3):   # bristles
                pt = hip.lerp(knee, 0.3 + k * 0.25)
                parts.append(bind(tube("Bristle", [pt, pt + Vector((0, 0.01, 0.03))], [0.004, 0.0], "Dark", segs=3), rig, "Leg" + suffix + side))
    finish_character("insect", rig, parts, "spider")


def golem():
    J = humanoid_joints(h=1.15, shoulder=0.26, hip=0.13)
    rig = humanoid_rig(J, J["headBase"] + Vector((0, 0, 0.2)))
    parts = []
    rnd = random.Random(5)

    def rock(name, bone, center, size, role="Primary"):
        b = blob(name, 1.0, center, role, scale=size, subdiv=2, lumps=0.18, seed=rnd.uniform(0, 50), faceted=True)
        parts.append(bind(b, rig, bone))

    rock("Torso", "Chest", J["chest"] + Vector((0, 0, 0.02)), (0.25, 0.18, 0.21))
    rock("Belly", "Spine", J["spine"], (0.19, 0.15, 0.14))
    rock("Pelvis", "Hips", J["hips"], (0.2, 0.15, 0.11), "Secondary")
    rock("Head", "Head", J["headBase"] + Vector((0, -0.03, 0.08)), (0.1, 0.1, 0.09))
    rock("Brow", "Head", J["headBase"] + Vector((0, -0.08, 0.13)), (0.11, 0.05, 0.03), "Secondary")
    for s, side in ((1, "L"), (-1, "R")):
        rock("Shoulder", "UpperArm" + side, J["shoulder" + side] + Vector((s * 0.03, 0, 0.03)), (0.14, 0.13, 0.13), "Secondary")
        rock("Arm", "UpperArm" + side, J["bicep" + side], (0.085, 0.085, 0.12))
        rock("Forearm", "LowerArm" + side, J["forearm" + side], (0.1, 0.1, 0.13), "Secondary")
        rock("Fist", "Hand" + side, J["hand" + side], (0.11, 0.11, 0.1))
        rock("Thigh", "UpperLeg" + side, J["thigh" + side], (0.105, 0.105, 0.14), "Secondary")
        rock("Shin", "LowerLeg" + side, J["calf" + side], (0.095, 0.095, 0.14))
        rock("Foot", "Foot" + side, (J["ankle" + side] + J["toe" + side]) / 2, (0.085, 0.12, 0.05), "Secondary")
        for k in range(2):   # crystals growing from the shoulders
            base = J["shoulder" + side] + Vector((s * (0.05 + k * 0.05), 0.04, 0.1))
            parts.append(bind(tube("Crystal", [base, base + Vector((s * 0.04, 0.03, 0.14 - k * 0.04))], [0.035, 0.0], "Glow", segs=5, smooth=False), rig, "UpperArm" + side))
    for k in range(3):   # back crystals
        base = J["chest"] + Vector(((k - 1) * 0.08, 0.15, 0.05))
        parts.append(bind(tube("Crystal", [base, base + Vector(((k - 1) * 0.04, 0.08, 0.18))], [0.04, 0.0], "Glow", segs=5, smooth=False), rig, "Chest"))
    for k in range(5):   # glowing cracks
        p = J["chest"] + Vector((rnd.uniform(-0.13, 0.13), -0.175, rnd.uniform(-0.12, 0.09)))
        parts.append(bind(tube("Crack", [p, p + Vector((rnd.uniform(-0.05, 0.05), -0.01, rnd.uniform(-0.08, 0.08)))], [0.012, 0.008], "Glow", segs=5), rig, "Chest"))
    parts.append(bind(bevel_box("EyeSlit", (0.12, 0.02, 0.02), J["headBase"] + Vector((0, -0.125, 0.09)), "Eye", bevel=0.004), rig, "Head"))
    finish_character("golem", rig, parts, "biped", weight=0.7, heavy=True)


def ooze():
    root = link(bpy.data.objects.new("Root", None))
    body = blob("Body", 0.34, (0, 0, 0.0), "Primary", scale=(1.15, 1.1, 0.72), subdiv=4, lumps=0.07, seed=3)
    for v in body.data.vertices:
        if v.co.z < -0.08:
            v.co.z = -0.08 + (v.co.z + 0.08) * 0.25
    xform(body, (0, 0, 0.13))
    body.parent = root
    for (x, y, z, r) in ((0.1, 0.0, 0.18, 0.09), (-0.12, 0.08, 0.12, 0.06), (0.0, 0.12, 0.25, 0.05)):
        b = blob("Core@Body", r, (x, y, z), "Secondary", subdiv=2)
        b.parent = root
    for k in range(5):   # drips
        a = 2 * math.pi * k / 5
        d = blob("Drip@Body", 0.05, (math.cos(a) * 0.34, math.sin(a) * 0.3, 0.06), "Primary", scale=(1, 1, 0.7), subdiv=2)
        d.parent = root
    for s in (1, -1):
        e = blob("Eye@Body", 0.045, (s * 0.1, -0.3, 0.22), "Eye", scale=(1, 0.7, 1.15), subdiv=2)
        e.parent = root
    front_marker(root)
    export(out("blob"))


# =============================================================================================
# Props
# =============================================================================================

def prop_root():
    return link(bpy.data.objects.new("Root", None))


def own(root, *obs):
    for o in obs:
        o.parent = root
    return obs[0] if len(obs) == 1 else obs


def pivot(ob, p):
    ob.data.transform(Matrix.Translation(-Vector(p)))
    ob.location = p
    return ob


def chest(open_):
    r = prop_root()
    for k in range(4):
        own(r, bevel_box("Plank", (0.6, 0.4, 0.075), (0, 0, 0.045 + k * 0.078), "Wood", bevel=0.012))
    for x in (-0.24, 0.24):
        own(r, bevel_box("Band", (0.045, 0.42, 0.33), (x, 0, 0.165), "Gold", bevel=0.006))
    for x in (-0.29, 0.29):
        for y in (-0.19, 0.19):
            own(r, bevel_box("Corner", (0.05, 0.05, 0.33), (x, y, 0.165), "Metal", bevel=0.008))
    lid_parts = []
    lid = tube("Lid", [(-0.3, 0, 0.32), (0.3, 0, 0.32)], [0.21, 0.21], "Wood", segs=16, flat=0.55)
    for v in lid.data.vertices:
        v.co.z = max(v.co.z, 0.32)
    lid_parts.append(lid)
    for x in (-0.24, 0.24):
        band = tube("LidBand@Lid", [(x - 0.022, 0, 0.32), (x + 0.022, 0, 0.32)], [0.215, 0.215], "Gold", segs=16, flat=0.57)
        for v in band.data.vertices:
            v.co.z = max(v.co.z, 0.32)
        lid_parts.append(band)
    lid_parts.append(bevel_box("Lock@Lid", (0.08, 0.04, 0.1), (0, -0.22, 0.31), "Gold", bevel=0.01))
    for o in lid_parts:
        pivot(o, (0, 0.2, 0.32))
        own(r, o)
        if open_:
            o.rotation_euler = (math.radians(-105), 0, 0)
    if open_:
        own(r, blob("Treasure", 0.22, (0, 0, 0.3), "Gold", scale=(1.25, 0.8, 0.25), subdiv=2, lumps=0.25, seed=4, faceted=True))
        for k in range(3):
            own(r, blob("Gem", 0.03, (-0.12 + k * 0.12, -0.05 + 0.05 * k, 0.36), "Glow", subdiv=1, faceted=True))
    front_marker(r)
    export(out("chest_open" if open_ else "chest"))


def shrine():
    r = prop_root()
    own(r, bevel_box("Step", (0.72, 0.72, 0.08), (0, 0, 0.04), "Stone", bevel=0.015))
    own(r, bevel_box("Step", (0.56, 0.56, 0.08), (0, 0, 0.12), "Stone", bevel=0.015))
    own(r, lathe("Column", [(0.17, 0.16), (0.14, 0.22), (0.11, 0.3), (0.1, 0.6), (0.13, 0.8), (0.19, 0.86), (0.2, 0.9)], "Stone", segs=8, smooth=False))
    own(r, lathe("Bowl", [(0.2, 0.9), (0.24, 0.95), (0.2, 0.97), (0.0, 0.95)], "Gold", segs=16))
    crystal = blob("Crystal", 0.12, (0, 0, 1.18), "Glow", scale=(0.8, 0.8, 1.6), subdiv=1, faceted=True)
    pivot(crystal, (0, 0, 1.18))
    own(r, crystal)
    front_marker(r)
    export(out("shrine"))


def fountain():
    r = prop_root()
    own(r, lathe("Basin", [(0.0, 0.0), (0.46, 0.0), (0.48, 0.04), (0.46, 0.26), (0.48, 0.3), (0.42, 0.31), (0.4, 0.12), (0.0, 0.12)], "Stone", segs=10, smooth=False))
    own(r, lathe("Water", [(0.0, 0.24), (0.41, 0.24)], "Water", segs=20))
    own(r, lathe("Column", [(0.1, 0.12), (0.07, 0.3), (0.06, 0.6), (0.08, 0.66)], "Stone", segs=10))
    own(r, lathe("Bowl", [(0.08, 0.66), (0.22, 0.72), (0.24, 0.78), (0.2, 0.79), (0.0, 0.74)], "Stone", segs=12))
    own(r, lathe("BowlWater", [(0.0, 0.77), (0.19, 0.77)], "Water", segs=16))
    crystal = blob("Crystal", 0.05, (0, 0, 0.9), "Water", subdiv=2)
    pivot(crystal, (0, 0, 0.9))
    own(r, crystal)
    front_marker(r)
    export(out("fountain"))


def portal():
    r = prop_root()
    pts = [(math.cos(math.pi * t / 16) * 0.56, 0, 0.95 + math.sin(math.pi * t / 16) * 0.6) for t in range(17)]
    own(r, tube("Arch", pts, [0.09] * 17, "Stone", segs=8, ref=(0, 1, 0), smooth=False))
    for x in (-0.56, 0.56):
        own(r, lathe("Pillar", [(0.12, 0.0), (0.12, 0.08), (0.09, 0.12), (0.085, 0.9), (0.11, 0.96)], "Stone", segs=8, center=(x, 0, 0), smooth=False))
    for k in range(1, 16, 2):
        a = math.pi * k / 16
        own(r, blob("Rune", 0.035, (math.cos(a) * 0.56, -0.08, 0.95 + math.sin(a) * 0.6), "Glow", subdiv=1, faceted=True))
    disc = lathe("Crystal", [(0.0, 0.0), (0.45, 0.0)], "Glow", segs=24)
    xform(disc, (0, 0, 0), (90, 0, 0))
    for v in disc.data.vertices:
        v.co.z *= 1.35
    xform(disc, (0, 0.0, 0.95))
    pivot(disc, (0, 0, 0.95))
    own(r, disc)
    own(r, bevel_box("Step", (1.5, 0.7, 0.08), (0, 0, 0.04), "Stone", bevel=0.02))
    front_marker(r)
    export(out("portal"))


def throne():
    r = prop_root()
    own(r, bevel_box("Dais", (1.1, 1.0, 0.1), (0, 0.05, 0.05), "Stone", bevel=0.02))
    own(r, bevel_box("Dais", (0.9, 0.8, 0.1), (0, 0.08, 0.15), "Stone", bevel=0.02))
    own(r, bevel_box("Seat", (0.62, 0.5, 0.16), (0, 0.05, 0.36), "Gold", bevel=0.03))
    own(r, bevel_box("Cushion", (0.54, 0.44, 0.07), (0, 0.04, 0.47), "Cloth", bevel=0.03))
    back = [(-0.33, 0.0), (0.33, 0.0), (0.33, 0.85), (0.22, 1.05), (0.0, 1.25), (-0.22, 1.05), (-0.33, 0.85)]
    own(r, prism("Back", back, 0.1, "Gold", bevel=0.02, loc=(0, 0.32, 0.44)))
    inner = [(-0.25, 0.05), (0.25, 0.05), (0.25, 0.8), (0.16, 0.97), (0.0, 1.12), (-0.16, 0.97), (-0.25, 0.8)]
    own(r, prism("BackCloth", inner, 0.04, "Cloth", bevel=0.01, loc=(0, 0.26, 0.44)))
    for s in (1, -1):
        own(r, tube("Arm", [(s * 0.32, 0.25, 0.46), (s * 0.34, 0.0, 0.52), (s * 0.34, -0.2, 0.5), (s * 0.33, -0.25, 0.42)], [0.045, 0.05, 0.05, 0.04], "Gold", segs=10))
        own(r, tube("Spire", [(s * 0.3, 0.32, 1.3), (s * 0.3, 0.32, 1.62)], [0.04, 0.0], "Gold", segs=8))
        own(r, blob("SpireGem", 0.035, (s * 0.3, 0.27, 1.3), "Glow", subdiv=1, faceted=True))
    own(r, blob("CrestGem", 0.06, (0, 0.26, 1.48), "Glow", subdiv=1, faceted=True))
    front_marker(r)
    export(out("throne"))


def pillar():
    r = prop_root()
    flute = lambda t, i: 1.0 + (0.06 * math.cos(t * 16) if 4 <= i <= 7 else 0.0)
    own(r, lathe("Column", [(0.0, 0.0), (0.32, 0.0), (0.32, 0.1), (0.27, 0.14), (0.24, 0.2), (0.22, 0.5), (0.215, 0.9), (0.21, 1.4), (0.2, 1.85),
                            (0.24, 1.93), (0.3, 2.0), (0.32, 2.12), (0.32, 2.2), (0.0, 2.2)], "Stone", segs=32, wave=flute))
    own(r, ring("Band", 1.6, 0.226, "Gold", h=0.03, segs=32))
    front_marker(r)
    export(out("pillar"))


def brazier():
    r = prop_root()
    for i in range(3):
        a = 2 * math.pi * i / 3
        own(r, tube("Leg", [(math.cos(a) * 0.12, math.sin(a) * 0.12, 0.66), (math.cos(a) * 0.18, math.sin(a) * 0.18, 0.35), (math.cos(a) * 0.24, math.sin(a) * 0.24, 0.0)],
                    [0.022, 0.02, 0.025], "Dark", segs=6))
    own(r, lathe("Bowl", [(0.06, 0.62), (0.18, 0.66), (0.27, 0.78), (0.29, 0.84), (0.26, 0.85), (0.0, 0.76)], "Metal", segs=16))
    rnd = random.Random(9)
    fire = []
    for k in range(6):
        a = 2 * math.pi * k / 6 + rnd.uniform(-0.3, 0.3)
        base = Vector((math.cos(a) * 0.1, math.sin(a) * 0.1, 0.8))
        h = rnd.uniform(0.25, 0.42)
        fire.append(tube("Fire", [base, base + Vector((math.cos(a) * 0.03, math.sin(a) * 0.03, h * 0.5)), base + Vector((-math.cos(a) * 0.02, 0, h))], [0.07, 0.05, 0.0], "Fire", segs=7))
    fire.append(tube("Fire", [Vector((0, 0, 0.8)), Vector((0, 0, 1.05)), Vector((0.02, 0, 1.3))], [0.11, 0.07, 0.0], "Fire", segs=8))
    f = join(fire, "Fire")
    pivot(f, (0, 0, 0.8))
    own(r, f)
    front_marker(r)
    export(out("brazier"))


def stairs():
    r = prop_root()
    for i in range(6):
        own(r, bevel_box("Step", (0.9, 0.17, 0.12), (0, 0.4 - i * 0.16, -0.08 - i * 0.13), "Stone", bevel=0.015))
    own(r, bevel_box("WallL", (0.06, 1.0, 0.95), (0.47, 0, -0.47), "Dark", bevel=0.0))
    own(r, bevel_box("WallR", (0.06, 1.0, 0.95), (-0.47, 0, -0.47), "Dark", bevel=0.0))
    own(r, bevel_box("WallB", (1.0, 0.06, 0.95), (0, 0.47, -0.47), "Dark", bevel=0.0))
    own(r, bevel_box("Pit", (1.0, 1.0, 0.05), (0, 0, -0.95), "Dark", bevel=0.0))
    for x in (-0.47, 0.47):
        own(r, bevel_box("Lip", (0.08, 1.0, 0.05), (x, 0, 0.0), "Stone", bevel=0.01))
    front_marker(r)
    export(out("stairs"))


def spikes():
    r = prop_root()
    rnd = random.Random(2)
    own(r, bevel_box("Plate", (0.8, 0.8, 0.03), (0, 0, 0.015), "Dark", bevel=0.01))
    for x in (-0.25, 0, 0.25):
        for y in (-0.25, 0, 0.25):
            h = rnd.uniform(0.16, 0.26)
            own(r, tube("Spike", [(x, y, 0.02), (x + rnd.uniform(-0.02, 0.02), y, h)], [0.04, 0.0], "Metal", segs=4, smooth=False))
    front_marker(r)
    export(out("spikes"))


def rocks():
    r = prop_root()
    for k, (x, y, s) in enumerate([(-0.2, -0.1, 0.18), (0.18, 0.12, 0.14), (0.05, -0.25, 0.1), (-0.05, 0.25, 0.12), (0.25, -0.15, 0.08)]):
        own(r, blob("Rock", s, (x, y, s * 0.4), "Stone", scale=(1, 0.9, 0.7), subdiv=1, lumps=0.25, seed=k * 3.1, faceted=True))
    front_marker(r)
    export(out("rocks"))


def artifact():
    r = prop_root()
    crystal = blob("Crystal", 0.16, (0, 0, 0.55), "Glow", scale=(0.75, 0.75, 1.4), subdiv=1, faceted=True)
    pivot(crystal, (0, 0, 0.55))
    own(r, crystal)
    halo = ring("Halo@Crystal", 0.55, 0.21, "Gold", h=0.012)
    pivot(halo, (0, 0, 0.55))
    own(r, halo)
    for k in range(4):
        a = math.pi / 2 * k
        spike = tube("Prong@Crystal", [(math.cos(a) * 0.2, math.sin(a) * 0.2, 0.55), (math.cos(a) * 0.15, math.sin(a) * 0.15, 0.75)], [0.018, 0.0], "Gold", segs=5)
        pivot(spike, (0, 0, 0.55))
        own(r, spike)
    own(r, lathe("Base", [(0.0, 0.0), (0.22, 0.0), (0.24, 0.03), (0.2, 0.06), (0.0, 0.06)], "Gold", segs=16))
    front_marker(r)
    export(out("artifact"))


BUILDERS = {
    "demon": archdemon, "princess": princess, "human": crusader, "humanoid": ghoul, "merchant": merchant_imp,
    "beast": beast, "flyer": flyer, "insect": insect, "golem": golem, "blob": ooze,
    "chest": lambda: chest(False), "chest_open": lambda: chest(True), "shrine": shrine, "fountain": fountain,
    "portal": portal, "throne": throne, "pillar": pillar, "brazier": brazier, "stairs": stairs, "spikes": spikes,
    "rocks": rocks, "artifact": artifact,
}

if __name__ == "__main__":
    clear()
    for name, build in BUILDERS.items():
        if ONLY and name not in ONLY:
            continue
        try:
            build()
        except Exception as e:
            import traceback
            traceback.print_exc()
            print("FAILED", name, e)
            clear()
    print("DONE")
