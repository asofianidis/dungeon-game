"""
Builds every 3D model for Depthbound and exports FBX files (with animation clips) for Unity.

Usage (from the repo root):
    blender -b --factory-startup --python Tools/Blender/build_models.py -- Assets/Resources/Models [name ...]

Pass model names after the output folder to rebuild only those (e.g. "demon princess").

Characters and creatures are sculpted as signed distance fields (dbmodel/sdf.py), meshed with
surface nets, decimated to a triangle budget and given normals from the field gradient:
- Bodies are one seamless anatomical field (dbmodel/anatomy.py): pelvis, rib cage, bust or pecs,
  deltoids, tapered limbs with muscle bulges, knees and feet. Heads have a V-shaped jaw and a
  front-projected face UV painted in Unity (material role "Face<Variant>"); hands have jointed fingers.
- Hair is a scalp mass with a jagged fringe sheet and flattened locks; horns, tails and wings are
  fields too (wing membranes are billowed meshes with scalloped edges).
- Clothing and armor are shells or slabs of the body field cut by regions (dbmodel/garments.py), so
  they conform to the figure: bodices, plate, greaves, flared folded skirts, capes, sleeves, trims.
- Bodies get bone-heat skin weights; clothing copies the body's weights; accessories are rigid.
  Keyframed clips (Idle, Walk, Attack, Cast, Hit, Death) come from dbmodel/anim.py.
- Every model gets per-vertex ambient occlusion and tint baked into a color attribute
  (dbmodel/shade.py) that Unity's Depthbound/Lit shader multiplies into the albedo.

Conventions: 1 unit = 1 tile, models stand on z=0 facing -Y, an empty named "Front" marks the front,
materials are role names, and prop parts may encode their parent as "Part@Parent".
"""
import math
import os
import random
import sys

import bpy
from mathutils import Matrix, Vector


def V3(*a):
    return Vector(a)


sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np  # noqa: E402
from dbmodel.geometry import (Rig, bevel_box, bind, blob, build_armature, clear, curve_pts, export,  # noqa: E402
                              front_marker, join, lathe, link, prism, tube, xform)
from dbmodel import anim, sdf, shade  # noqa: E402
from dbmodel import anatomy as A  # noqa: E402
from dbmodel import garments as G  # noqa: E402

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = ARGS[0] if ARGS else "Models"
ONLY = set(ARGS[1:])
os.makedirs(OUT, exist_ok=True)


def out(name):
    return os.path.join(OUT, name + ".fbx")


def finish_sculpted(name, rig, body, parts, kind, ao=True, heat_skip=("Wing",), **style):
    """Rig a sculpted character: bone-heat weights for the body, then each part by its spec:
    ("rigid", bone), ("bind", [bones]) nearest-bone blend, ("transfer", allowed or None, [extra
    sources]) copied from the body, or ("fn", fn(P, bones) -> W). Bakes AO, adds clips, exports."""
    arm = build_armature(rig)
    bones, W, missing = shade.heat_weights(body, arm, skip=heat_skip)
    if missing.any():
        print("heat weights missing on", int(missing.sum()), "verts; using nearest bones there")
        P = shade._co(body)
        NW = shade.nearest_bone_weights(P, rig, bones)
        W[missing] = NW[missing]
    shade.write_weights(body, bones, W)
    for ob, spec in parts:
        kind_ = spec[0]
        if kind_ == "rigid":
            shade.rigid(ob, spec[1])
        elif kind_ == "bind":
            bind(ob, rig, spec[1])
        elif kind_ == "transfer":
            allowed = spec[1] if len(spec) > 1 else None
            extra = spec[2] if len(spec) > 2 else []
            shade.transfer_weights(ob, [body] + list(extra), bones, allowed)
        elif kind_ == "fn":
            shade.weight_fn(ob, bones, lambda P: spec[1](P, bones))
    obs = [body] + [p[0] for p in parts]
    for o in obs:
        shade.ensure_tint(o)
    mesh = join(obs, "Mesh")
    if ao:
        shade.bake_ao(mesh)
    mod = mesh.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    mesh.parent = arm
    print(name, "triangles:", sum(len(p.vertices) - 2 for p in mesh.data.polygons), "verts:", len(mesh.data.vertices))
    front_marker(arm)
    anim.add_clips(arm, kind, **style)
    export(out(name), animated=True)


def sculpted_rig(J, head_top):
    return humanoid_rig(J, head_top)


def skirt_weights(J, z_top, z_hem, follow=0.4):
    """Skirt follows the hips, and each side partly follows its thigh toward the hem."""
    H = J["H"]

    def fn(P, bones):
        W = np.zeros((len(P), len(bones)))
        t = np.clip((z_top - P[:, 2]) / max(z_top - z_hem, 1e-3), 0, 1) ** 1.3 * follow
        side = 0.5 + 0.5 * np.tanh(P[:, 0] / (0.03 * H))
        W[:, bones.index("Hips")] = 1 - t
        W[:, bones.index("UpperLegL")] = t * side
        W[:, bones.index("UpperLegR")] = t * (1 - side)
        return W
    return fn


HAIR_BONES = ["Head", "Neck", "Chest", "Spine", "Hips"]


# =============================================================================================
# Humanoid anatomy
# =============================================================================================

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




def ring(name, z, r, role, h=0.012, center=(0, 0, 0), segs=24, scale_y=1.0):
    ob = lathe(name, [(r * 0.97, z + h), (r, z + h * 0.5), (r, z - h * 0.5), (r * 0.97, z - h)], role, segs=segs, center=(0, 0, 0))
    if scale_y != 1.0:
        xform(ob, (0, 0, 0), (0, 0, 0), (1, scale_y, 1))
    return xform(ob, center)


# =============================================================================================
# Characters
# =============================================================================================

def sculpt_sword(J, side="R", length=0.62, role="Metal", tilt=15.0):
    """A bevelled sword held in the fist, blade forward and slightly up (rigid to the hand)."""
    h = J["hand" + side] + (J["hand" + side] - J["wrist" + side]).normalized() * 0.006
    a = math.radians(90 - tilt)
    b = V3(0, -math.sin(a), math.cos(a))
    rot = (90 - tilt, 0, 0)
    obs = [prism("Blade", [(-0.02, 0.0), (0.02, 0.0), (0.017, length * 0.84), (0.0, length), (-0.017, length * 0.84)], 0.011, role, bevel=0.004,
                 loc=h + b * 0.05, rot=rot),
           prism("Fuller", [(-0.005, 0.04), (0.005, 0.04), (0.004, length * 0.7), (-0.004, length * 0.7)], 0.013, "Dark", bevel=0.0,
                 loc=h + b * 0.05, rot=rot)]
    obs.append(tube("Guard", curve_pts(h + b * 0.045 + V3(-0.075, 0, 0), h + b * 0.045 + V3(0.075, 0, 0), b * 0.02, 5),
                    [0.011, 0.013, 0.015, 0.013, 0.011], "Gold", segs=8))
    obs.append(tube("Grip", [h - b * 0.055, h + b * 0.04], [0.013, 0.014], "Leather", segs=8))
    obs.append(blob("Pommel", 0.017, h - b * 0.07, "Glow", subdiv=1, faceted=True))
    return [(o, ("rigid", "Hand" + side)) for o in obs]


def head_center(J, r, k=0.55):
    return J["headBase"] + V3(0, -0.008 * J["H"], k * r)


def archdemon():
    """The Archdemon (player): a muscular demon lord in red plate with gold trim."""
    H = 1.3
    J = A.joints(H, female=False, spread=16, bulk=1.08)
    r = 0.08 * H
    hc = head_center(J, r)
    rig = sculpted_rig(J, hc + V3(0, 0, r * 1.1))
    parts = []
    BF = A.body_field(J, "Skin", bulk=1.08)
    tpts = A.tail(BF, V3(0, 0.05 * H, 0.5 * H), 0.7, 0.02 * H, tip_role="Horn", heart=False)
    rig.add("Tail1", tpts[0], tpts[3], "Hips")
    rig.add("Tail2", tpts[3], tpts[6], "Tail1")
    rig.add("Tail3", tpts[6], tpts[9], "Tail2")
    body = A.make(BF, "Body", faces=8000)
    torso = A.body_field(J, "Primary", bulk=1.08, arms=False, legs=False, pecs=False, h=0.0045 * H)
    arms = A.body_field(J, "Primary", bulk=1.08, torso=False, legs=False, h=0.004 * H)
    legs = A.body_field(J, "Dark", bulk=1.08, torso=False, arms=False, h=0.004 * H)

    head = A.make(A.head_field(hc, r, "FaceDemon", ears=1.0, jaw=1.1, nose=1.1, ear_role="Skin"), "Head", faces=9000)
    A.face_uv(head, hc, r)
    A.face_normals(head, hc, r)
    parts.append((head, ("rigid", "Head")))
    for side in "LR":
        parts.append((A.make(A.hand(J, side, "Skin", scale=1.08, claws="Horn", fist=0.25), "Hand", faces=1500), ("rigid", "Hand" + side)))

    hair = A.Hair(hc, r, "Hair")
    hair.scalp(hairline=0.6)
    hair.fringe(bottom=0.32, teeth=5, depth=0.42, part=0.2, swoop=0.25, thick=0.09)
    for i in range(9):   # spikes swept back
        th = math.radians(-110 + 220 * i / 8)
        hair.lock((math.sin(th) * 0.5, -0.2 + math.cos(th) * 0.55, 0.8), (math.sin(th) * 0.9, 0.2 + math.cos(th) * 0.75, 0.05),
                  V3(math.sin(th) * 0.06 * H, 0.08 * H + 0.03 * H * math.cos(th), -0.06 * H - 0.05 * H * abs(math.sin(th))),
                  0.34, thick=0.45, n=3, taper=0.02, normal=V3(math.sin(th) * 0.4, math.cos(th) * 0.4, 1.0))
    hm = A.make(hair.F, "Hair", faces=5000)
    parts.append((hm, ("rigid", "Head")))
    HF = sdf.Field("Horn", h=0.003 * H)
    for s in (1, -1):
        A.horn(HF, hc + V3(s * r * 0.55, 0.0, r * 0.72), s, 0.24 * H, 0.034 * H, curl=1.0, back=0.75)
    parts.append((A.make(HF, "Horn", faces=2400), ("rigid", "Head")))

    # Breastplate with a gold keel and trim, banded faulds, belt and buckle.
    ztop = 0.8 * H
    zw = J["waist"].z - 0.005 * H
    BP = sdf.Field("Dark", h=0.0045 * H)
    BP.add(sdf.slab(torso, 0.012 * H, G.zrange(zw, ztop)))
    BP.add(sdf.slab(torso, 0.016 * H, G.region_and(G.zrange(J["chest"].z - 0.005 * H, ztop - 0.012 * H), G.front(-0.02 * H))), role="Dark")
    BP.add(sdf.shell(torso, 0.0185 * H, 0.0022 * H, G.region_and(G.near_z(lambda x, y: J["chest"].z - 0.003 * H + 0.0 * x, 0.003 * H), G.front(-0.02 * H))), role="Gold")
    keel = [V3(0, G.surface_y(torso, 0, z, 0.012 * H), z) for z in np.linspace(zw + 0.01 * H, ztop - 0.02 * H, 5)]
    BP.chain(keel, [0.006 * H] * 5, k=0.004 * H, role="Gold")
    BP.add(sdf.shell(torso, 0.015 * H, 0.003 * H, G.zrange(ztop - 0.012 * H, ztop - 0.002 * H)), role="Gold")
    for k in range(2):
        z0 = J["hips"].z + 0.01 * H + k * 0.03 * H
        BP.add(sdf.slab(torso, 0.011 * H + k * 0.001 * H, G.zrange(z0, z0 + 0.026 * H)), role="Dark" if k == 0 else "Primary")
    BP.add(sdf.slab(torso, 0.016 * H, G.zrange(J["hips"].z - 0.03 * H, J["hips"].z + 0.008 * H)), role="Leather")
    by = G.surface_y(torso, 0, J["hips"].z - 0.01 * H, 0.016 * H)
    BP.add(sdf.box(V3(0, by, J["hips"].z - 0.011 * H), (0.024 * H, 0.008 * H, 0.02 * H), round_=0.004 * H), k=0.003 * H, role="Gold")
    parts.append((A.make(BP, "Breastplate", faces=4500), ("transfer", ["Hips", "Spine", "Chest"])))

    # Tassets and a hanging loincloth.
    TS = sdf.Field("Primary", h=0.004 * H)
    zb = J["hips"].z - 0.03 * H
    for s, ang in ((1, 40), (-1, 40), (1, 75), (-1, 75)):
        a = math.radians(ang)
        ctr = V3(s * math.sin(a) * 0.1 * H, -math.cos(a) * 0.075 * H, zb - 0.055 * H)
        out = V3(s * math.sin(a), -math.cos(a), 0.0)
        down = V3(0, 0, -1)
        TS.add(sdf.box(ctr + out * 0.006 * H, (0.036 * H, 0.004 * H, 0.06 * H), axes=(out.cross(down).normalized(), out, (out * 0.25 + down).normalized() * -1), round_=0.003 * H))
    for sgn in (-1, 1):
        y0 = (G.surface_y(torso, 0, zb, 0.02 * H, front=sgn < 0) or 0)
        top = V3(0, y0 + sgn * 0.004 * H, zb - 0.1 * H)
        tilt = V3(0, sgn * 0.12, 1).normalized()
        TS.add(sdf.box(top, (0.042 * H, 0.004 * H, 0.105 * H), axes=(V3(1, 0, 0), tilt.cross(V3(1, 0, 0)), tilt), round_=0.003 * H), role="Primary")
        TS.add(sdf.box(top + V3(0, sgn * 0.002 * H, -0.098 * H), (0.044 * H, 0.0055 * H, 0.008 * H), axes=(V3(1, 0, 0), tilt.cross(V3(1, 0, 0)), tilt), round_=0.002 * H), role="Gold")
    parts.append((A.make(TS, "Tassets", faces=2000), ("fn", skirt_weights(J, zb, zb - 0.24 * H, follow=0.6))))

    # Layered pauldrons with spikes, bracers, greaves, knee cops and sabatons with talons.
    for s, side in ((1, "L"), (-1, "R")):
        sh, el, wr = J["shoulder" + side], J["elbow" + side], J["wrist" + side]
        PD = sdf.Field("Primary", h=0.0035 * H)
        for k in range(3):
            c = sh + V3(s * (0.014 + 0.014 * k) * H, 0, (0.012 - 0.03 * k) * H)
            rr = V3(0.066, 0.066, 0.04) * H * (1 - 0.08 * k)
            PD.add(G.dome(c, rr, 0.004 * H, cut=G.region_and(G.zrange(c.z - 0.012 * H, 9), G.side(s, abs(sh.x) - 0.035 * H))),
                   role="Primary" if k != 1 else "Dark")
        PD.add(sdf.torus(sh + V3(s * 0.012 * H, 0, 0.004 * H), 0.064 * H, 0.004 * H, axis=(0, 0, 1)), role="Gold")
        for k in range(2):
            b0 = sh + V3(s * (0.03 + 0.03 * k) * H, 0.008 * H * k, 0.05 * H - 0.012 * H * k)
            PD.cone(b0, b0 + V3(s * 0.05 * H, 0.025 * H, 0.09 * H - 0.025 * H * k), 0.014 * H, 0.001 * H, k=0.006 * H, role="Horn")
        parts.append((A.make(PD, "Pauldron", faces=2200), ("rigid", "UpperArm" + side)))
        BR = sdf.Field("Primary", h=0.0035 * H)
        BR.add(sdf.slab(arms, 0.008 * H, G.region_and(G.along(el, wr, 0.12, 0.92), G.side(s, 0.04 * H))))
        BR.add(sdf.shell(arms, 0.011 * H, 0.0025 * H, G.region_and(G.along(el, wr, 0.84, 0.9), G.side(s, 0.04 * H))), role="Gold")
        fa = A.lerp(el, wr, 0.45)
        out = V3(s, 0.15, 0).normalized()
        BR.cone(fa + out * 0.03 * H, fa + out * 0.075 * H + V3(0, 0.03 * H, 0.03 * H), 0.012 * H, 0.001 * H, k=0.006 * H, role="Horn")
        parts.append((A.make(BR, "Bracer", faces=1400), ("rigid", "LowerArm" + side)))
        kn, an = J["knee" + side], J["ankle" + side]
        GR = sdf.Field("Dark", h=0.0035 * H)
        GR.add(sdf.slab(legs, 0.008 * H, G.region_and(G.zrange(0.065 * H, kn.z - 0.01 * H), G.side(s, 0.0))))
        GR.add(G.dome(kn + V3(0, -0.03 * H, 0.004 * H), V3(0.036, 0.022, 0.04) * H, 0.004 * H, cut=G.front(0.02 * H)), k=0.004 * H, role="Primary")
        GR.cone(kn + V3(0, -0.05 * H, 0.0), kn + V3(0, -0.075 * H, 0.05 * H), 0.01 * H, 0.001 * H, k=0.004 * H, role="Horn")
        parts.append((A.make(GR, "Greave", faces=1500), ("rigid", "LowerLeg" + side)))
        SB = sdf.Field("Dark", h=0.0032 * H)
        SB.add(sdf.slab(legs, 0.009 * H, G.region_and(G.zrange(-1.0, 0.07 * H), G.side(s, 0.0))))
        to = J["toe" + side]
        for k in (-1, 0, 1):
            tp = to + V3(k * 0.016 * H, -0.045 * H, -0.004 * H)
            SB.cone(tp, tp + V3(k * 0.004 * H, -0.03 * H, -0.008 * H), 0.008 * H, 0.001 * H, k=0.004 * H, role="Horn")
        parts.append((A.make(SB, "Sabaton", faces=1200), ("rigid", "Foot" + side)))
    CL = sdf.Field("Gold", h=0.003 * H)
    CL.add(sdf.torus(V3(0, J["chest"].y + 0.006 * H, 0.81 * H), 0.05 * H, 0.006 * H, axis=(0, -0.2, 1)))
    parts.append((A.make(CL, "Gorget", faces=700), ("rigid", "Chest")))
    parts += sculpt_sword(J, "R", length=0.66)
    for s, side in ((1, "L"), (-1, "R")):
        yb = G.surface_y(torso, s * 0.04 * H, J["chest"].z + 0.04 * H, 0.012 * H, front=False)
        root = V3(s * 0.04 * H, yb + 0.004 * H, J["chest"].z + 0.04 * H)
        WF, mem, wrist, tip = A.wing(root, s, 0.8, "Horn", "Wing", bone_r=0.02)
        rig.add("Wing" + side, root, wrist, "Chest")
        rig.add("WingTip" + side, wrist, tip, "Wing" + side)
        parts.append((A.make(WF, "WingBone", faces=1100), ("bind", ["Wing" + side, "WingTip" + side])))
        parts.append((mem, ("bind", ["Wing" + side, "WingTip" + side])))
    finish_sculpted("demon", rig, body, parts, "biped", weight=1.0, spread=16)

def princess():
    """The succubus princess: slender figure, corset gown with a sweetheart neckline, layered
    frilled skirt, off-shoulder puff sleeves with detached bell cuffs, thigh-high stockings and
    heeled boots, long sculpted hair with a jagged fringe, curled horns, tiara, wings, heart tail."""
    H = 1.2
    J = A.joints(H, female=True, spread=14)
    r = 0.083 * H
    hc = head_center(J, r)
    rig = sculpted_rig(J, hc + V3(0, 0, r * 1.1))
    parts = []

    BF = A.body_field(J, "SkinPale", female=True, bust=1.35)
    BF.paint(lambda x, y, z: z - 0.37 * H, "Stocking")
    tail_root = V3(0, 0.05 * H, 0.5 * H)
    tpts = A.tail(BF, tail_root, 0.5, 0.016 * H, tip_role="Cloth", heart=True)
    rig.add("Tail1", tpts[0], tpts[3], "Hips")
    rig.add("Tail2", tpts[3], tpts[6], "Tail1")
    rig.add("Tail3", tpts[6], tpts[9], "Tail2")
    body = A.make(BF, "Body", faces=8000)

    torso = A.body_field(J, "Cloth", female=True, bust=1.35, arms=False, legs=False, h=0.004 * H)
    legs = A.body_field(J, "Dark", female=True, torso=False, arms=False, h=0.004 * H)

    head = A.make(A.head_field(hc, r, "FacePrincess", ears=0.55, ear_role="SkinPale"), "Head", faces=9000)
    A.face_uv(head, hc, r)
    A.face_normals(head, hc, r)
    parts.append((head, ("rigid", "Head")))
    for side in "LR":
        parts.append((A.make(A.hand(J, side, "SkinPale", scale=1.0), "Hand", faces=1200), ("rigid", "Hand" + side)))

    hair = A.Hair(hc, r, "HairPrincess")
    hair.scalp(hairline=0.5)
    hair.fringe(bottom=0.12, teeth=8, depth=0.3, part=0.35, swoop=0.1)
    for i in range(9):   # long back hair: broad locks fanned around the back of the head
        th = math.radians(-100 + 200 * i / 8)
        L = 0.5 * H * (0.82 + 0.18 * math.cos(th))
        hair.lock((math.sin(th) * 0.7, 0.35 + math.cos(th) * 0.45, 0.62), (math.sin(th) * 0.88, 0.3 + math.cos(th) * 0.7, -0.25),
                  V3(math.sin(th) * 0.05, 0.03 + 0.03 * math.cos(th), -L), 0.38, thick=0.42, curl=0.15, n=6,
                  sway=V3(0.012 * math.sin(i * 2.1), 0.01, 0), normal=V3(math.sin(th), math.cos(th), 0))
    for s in (1, -1):    # side locks framing the face
        hair.lock((s * 0.72, -0.42, 0.55), (s * 0.97, -0.32, -0.2), V3(s * 0.02, -0.02, -0.28 * H), 0.26, thick=0.34, curl=0.1, n=4,
                  normal=V3(s, -0.35, 0))
    hair.strand([hc + V3(0, -0.2 * r, 1.12 * r), hc + V3(0.1 * r, -0.4 * r, 1.45 * r), hc + V3(0.35 * r, -0.55 * r, 1.5 * r), hc + V3(0.42 * r, -0.4 * r, 1.3 * r)],
                [0.07 * r, 0.06 * r, 0.045 * r, 0.025 * r], flat=(V3(1, 0, 0), 0.45))
    hm = A.make(hair.F, "Hair", faces=7500)
    shade.tint(hm, shade.gradient(hc + V3(0, 0, r), hc + V3(0, 0, -0.55 * H), 1.0, 0.82))
    parts.append((hm, ("transfer", HAIR_BONES, [head])))

    HF = sdf.Field("Horn", h=0.0028 * H)
    for s in (1, -1):
        A.horn(HF, hc + V3(s * r * 0.5, 0.05 * r, r * 0.72), s, 0.15 * H, 0.022 * H, curl=1.15, back=1.0, rise=0.8)
    parts.append((A.make(HF, "Horn", faces=1600), ("rigid", "Head")))
    CF = sdf.Field("Gold", h=0.0022 * H)
    cc = hc + V3(0, 0.0, 0.86 * r)
    CF.add(sdf.torus(cc, 0.72 * r, 0.05 * r, axis=(0, -0.3, 1)))
    for k in range(7):
        a = math.radians(-60 + 20 * k)
        b = cc + V3(math.sin(a) * 0.72 * r, -math.cos(a) * 0.69 * r, 0.21 * r * math.cos(a) - 0.05 * r)
        hgt = (0.6 if k == 3 else 0.38 if k % 2 == 1 else 0.26) * r
        CF.cone(b, b + V3(0, -0.04 * r, hgt), 0.07 * r, 0.004 * r, k=0.03 * r)
    parts.append((A.make(CF, "Crown", faces=1000), ("rigid", "Head")))
    gem = blob("CrownGem", 0.008 * H, cc + V3(0, -0.72 * r, 0.22 * r), "Glow", subdiv=1, faceted=True)
    parts.append((gem, ("rigid", "Head")))
    for s in (1, -1):
        parts.append((blob("Earring", 0.007 * H, hc + V3(s * r * 0.95, 0.06 * r, -0.42 * r), "Glow", subdiv=1, faceted=True), ("rigid", "Head")))

    # Corset bodice from the hips to a sweetheart neckline, with gold trims and front lacing.
    zc = J["chest"].z

    def neckline(x, y):
        return zc + 0.012 * H - 0.03 * H * np.exp(-(x / (0.014 * H)) ** 2) + 0.05 * H * np.clip(y / (0.05 * H), 0, 1)
    zb = J["hips"].z - 0.01 * H
    BOD = sdf.Field("Cloth", h=0.0032 * H)
    # Solid layer grown from the torso, so the neckline closes onto the skin instead of standing off it.
    BOD.add(sdf.slab(torso, 0.0055 * H, G.region_and(G.ztop(neckline), G.zrange(zb, 2.0))))
    parts.append((A.make(BOD, "Bodice", faces=3200), ("transfer", ["Hips", "Spine", "Chest"])))
    TR = sdf.Field("Gold", h=0.0026 * H)
    TR.add(sdf.shell(torso, 0.0062 * H, 0.0026 * H, G.near_z(lambda x, y: neckline(x, y) - 0.003 * H, 0.004 * H)))
    for k in range(4):
        z = J["waist"].z - 0.035 * H + k * 0.025 * H
        y0 = G.surface_y(torso, 0.012 * H, z, 0.0068 * H)
        y1 = G.surface_y(torso, 0.012 * H, z + 0.022 * H, 0.0068 * H)
        for s in (1, -1):
            TR.cone(V3(-s * 0.012 * H, y0, z), V3(s * 0.012 * H, y1, z + 0.022 * H), 0.0028 * H, 0.0028 * H)
    parts.append((A.make(TR, "Trim", faces=1500), ("transfer", ["Hips", "Spine", "Chest"])))

    # Skirt: a frilled lace petticoat under a folded overskirt, gold waistband, back bow.
    zt = J["hips"].z + 0.03 * H
    hem_f, hem_b = 0.37 * H, 0.31 * H
    SK = sdf.Field("Cloth", h=0.003 * H)
    SK.add(G.skirt((0, 0.006 * H), zt, (0.102 * H, 0.074 * H), (0.2 * H, 0.18 * H), G.hem_front_back(hem_f, hem_b), folds=9, fold_amp=0.1, thick=0.0045 * H))
    parts.append((A.make(SK, "Skirt", faces=3600), ("fn", skirt_weights(J, zt, hem_b))))
    PT = sdf.Field("Lace", h=0.003 * H)
    PT.add(G.skirt((0, 0.006 * H), zt - 0.02 * H, (0.096 * H, 0.069 * H), (0.186 * H, 0.166 * H), G.hem_front_back(hem_f - 0.025 * H, hem_b - 0.025 * H),
                   folds=13, fold_amp=0.035, thick=0.004 * H, ruffle=0.05, ruffle_freq=30, phase=0.7))
    parts.append((A.make(PT, "Petticoat", faces=3600), ("fn", skirt_weights(J, zt, hem_b - 0.025 * H))))
    WB = sdf.Field("Gold", h=0.0026 * H)
    WB.add(G.skirt((0, 0.006 * H), zt + 0.004 * H, (0.106 * H, 0.078 * H), (0.108 * H, 0.08 * H), lambda a: zt - 0.012 * H + 0.0 * np.asarray(a),
                   folds=1, fold_amp=0.0, thick=0.004 * H))
    parts.append((A.make(WB, "Waistband", faces=700), ("rigid", "Hips")))
    BW = sdf.Field("Cloth", h=0.003 * H)
    yb = G.surface_y(torso, 0.0, zt, 0.01 * H, front=False)
    bc = V3(0, yb + 0.012 * H, zt)
    for s in (1, -1):
        BW.chain([bc, bc + V3(s * 0.04 * H, 0.015 * H, 0.03 * H), bc + V3(s * 0.075 * H, 0.012 * H, 0.012 * H), bc + V3(s * 0.06 * H, 0.008 * H, -0.02 * H), bc],
                 [0.008 * H, 0.016 * H, 0.016 * H, 0.012 * H, 0.008 * H], k=0.004 * H, flat=(V3(0, 1, 0), 0.3))
        BW.chain([bc, bc + V3(s * 0.02 * H, 0.02 * H, -0.08 * H), bc + V3(s * 0.035 * H, 0.03 * H, -0.17 * H)], [0.008 * H, 0.014 * H, 0.016 * H],
                 k=0.004 * H, flat=(V3(0, 1, 0), 0.25))
    BW.sphere(bc, 0.012 * H, k=0.004 * H)
    parts.append((A.make(BW, "Bow", faces=1000), ("rigid", "Hips")))

    # Off-shoulder puff sleeves, detached bell sleeves with lace cuffs, stocking lace tops.
    LC = sdf.Field("Lace", h=0.0026 * H)
    for side in "LR":
        sh, el, wr = J["shoulder" + side], J["elbow" + side], J["wrist" + side]
        d = (wr - el).normalized()
        PF = sdf.Field("Cloth", h=0.003 * H)
        PF.add(G.ruffled_ellipsoid(A.lerp(sh, el, 0.24), (0.037 * H, 0.039 * H, 0.035 * H), amp=0.1, freq=9))
        parts.append((A.make(PF, "Puff", faces=900), ("rigid", "UpperArm" + side)))
        SL = sdf.Field("Cloth", h=0.003 * H)
        SL.add(G.tube_shell(A.lerp(el, wr, 0.08), wr + d * 0.02 * H, 0.025 * H, 0.04 * H, 0.0032 * H))
        parts.append((A.make(SL, "Sleeve", faces=900), ("rigid", "LowerArm" + side)))
        CU = sdf.Field("Lace", h=0.0026 * H)
        CU.add(G.tube_shell(wr + d * 0.012 * H, wr + d * 0.05 * H, 0.04 * H, 0.05 * H, 0.0026 * H, ruffle=0.12, freq=14))
        parts.append((A.make(CU, "Cuff", faces=800), ("rigid", "LowerArm" + side)))
    LC.add(sdf.shell(legs, 0.003 * H, 0.0028 * H, G.zrange(0.355 * H, 0.378 * H)))
    parts.append((A.make(LC, "StockingTop", faces=1000), ("transfer", ["UpperLegL", "UpperLegR"])))

    BT = sdf.Field("Dark", h=0.003 * H)
    BT.add(sdf.slab(legs, 0.0055 * H, G.zrange(-1.0, 0.14 * H)))
    for side in "LR":
        an = J["ankle" + side]
        BT.add(sdf.box(an + V3(0, 0.03 * H, -0.03 * H), (0.011 * H, 0.012 * H, 0.02 * H), round_=0.004 * H), k=0.004 * H)
        BT.add(sdf.torus(an + V3(0, 0.0, 0.088 * H), 0.025 * H, 0.004 * H), k=0.002 * H, role="Gold")
    parts.append((A.make(BT, "Boots", faces=1800), ("transfer", ["LowerLegL", "LowerLegR", "FootL", "FootR"])))

    NK = sdf.Field("Dark", h=0.0022 * H)
    NK.add(sdf.torus(V3(0, 0.002 * H, 0.846 * H), 0.03 * H, 0.0035 * H))
    parts.append((A.make(NK, "Choker", faces=400), ("rigid", "Neck")))
    py = G.surface_y(torso, 0.0, 0.82 * H, 0.0, span=0.3)
    parts.append((blob("Pendant", 0.008 * H, V3(0, (py or -0.03 * H) - 0.006 * H, 0.818 * H), "Glow", subdiv=1, faceted=True), ("rigid", "Chest")))

    # Wings rooted between the shoulder blades.
    for s, side in ((1, "L"), (-1, "R")):
        yb = G.surface_y(torso, s * 0.035 * H, J["chest"].z + 0.04 * H, 0.0, front=False)
        root = V3(s * 0.035 * H, yb + 0.004 * H, J["chest"].z + 0.04 * H)
        WF, mem, wrist, tip = A.wing(root, s, 0.44, "Horn", "Wing", droop=0.04, bone_r=0.012)
        rig.add("Wing" + side, root, wrist, "Chest")
        rig.add("WingTip" + side, wrist, tip, "Wing" + side)
        parts.append((A.make(WF, "WingBone", faces=900), ("bind", ["Wing" + side, "WingTip" + side])))
        parts.append((mem, ("bind", ["Wing" + side, "WingTip" + side])))
    finish_sculpted("princess", rig, body, parts, "biped", weight=0.6, grace=1.0, spread=14)

def height_weights(stops):
    """Blend bones by height: stops = [(bone, z), ...] from top to bottom."""
    def fn(P, bones):
        W = np.zeros((len(P), len(bones)))
        z = P[:, 2]
        names = [s[0] for s in stops]
        zs = [s[1] for s in stops]
        for i, (b, zb) in enumerate(stops):
            j = bones.index(b)
            if i == 0:
                w = np.clip((z - zs[1]) / max(zb - zs[1], 1e-6), 0, 1) if len(stops) > 1 else np.ones(len(P))
            elif i == len(stops) - 1:
                w = np.clip((zs[i - 1] - z) / max(zs[i - 1] - zb, 1e-6), 0, 1)
            else:
                up = np.clip((zs[i - 1] - z) / max(zs[i - 1] - zb, 1e-6), 0, 1)
                dn = np.clip((z - zs[i + 1]) / max(zb - zs[i + 1], 1e-6), 0, 1)
                w = np.where(z >= zb, up, dn)
            W[:, j] = np.maximum(W[:, j], w)
        return W
    return fn


def plate_limbs(parts, J, H, arms, legs, role="Metal", trim="Gold", couters=True):
    """Rerebraces, couters, vambraces, cuisses, poleyns, greaves and sabatons."""
    for s, side in ((1, "L"), (-1, "R")):
        sh, el, wr = J["shoulder" + side], J["elbow" + side], J["wrist" + side]
        UA = sdf.Field(role, h=0.0035 * H)
        UA.add(sdf.slab(arms, 0.007 * H, G.region_and(G.along(sh, el, 0.4, 0.9), G.side(s, 0.04 * H))))
        parts.append((A.make(UA, "Rerebrace", faces=800), ("rigid", "UpperArm" + side)))
        LA = sdf.Field(role, h=0.0035 * H)
        LA.add(sdf.slab(arms, 0.008 * H, G.region_and(G.along(el, wr, 0.1, 0.93), G.side(s, 0.04 * H))))
        if couters:
            ax = (wr - el).normalized()
            LA.add(G.dome(el + V3(0, 0.012 * H, 0), V3(0.034, 0.034, 0.03) * H, 0.004 * H, cut=G.back(-0.01 * H)), k=0.003 * H)
        LA.add(sdf.shell(arms, 0.011 * H, 0.0024 * H, G.region_and(G.along(el, wr, 0.85, 0.91), G.side(s, 0.04 * H))), role=trim)
        parts.append((A.make(LA, "Vambrace", faces=1200), ("rigid", "LowerArm" + side)))
        kn, hp = J["knee" + side], J["hip" + side]
        UL = sdf.Field(role, h=0.0035 * H)
        UL.add(sdf.slab(legs, 0.008 * H, G.region_and(G.zrange(kn.z + 0.025 * H, hp.z - 0.05 * H), G.side(s, 0.0))))
        UL.add(G.dome(kn + V3(0, -0.028 * H, 0.006 * H), V3(0.038, 0.024, 0.042) * H, 0.004 * H, cut=G.front(0.018 * H)), k=0.004 * H)
        parts.append((A.make(UL, "Cuisse", faces=1200), ("fn", lambda P, bones, s=side, kz=kn.z: _knee_split(P, bones, s, kz, H))))
        LL = sdf.Field(role, h=0.0035 * H)
        LL.add(sdf.slab(legs, 0.008 * H, G.region_and(G.zrange(0.07 * H, kn.z - 0.012 * H), G.side(s, 0.0))))
        LL.add(sdf.shell(legs, 0.011 * H, 0.0024 * H, G.region_and(G.zrange(0.07 * H, 0.078 * H), G.side(s, 0.0))), role=trim)
        parts.append((A.make(LL, "Greave", faces=1000), ("rigid", "LowerLeg" + side)))
        FT = sdf.Field(role, h=0.0032 * H)
        FT.add(sdf.slab(legs, 0.009 * H, G.region_and(G.zrange(-1.0, 0.075 * H), G.side(s, 0.0))))
        parts.append((A.make(FT, "Sabaton", faces=900), ("rigid", "Foot" + side)))


def _knee_split(P, bones, side, kz, H):
    W = np.zeros((len(P), len(bones)))
    t = np.clip((P[:, 2] - (kz - 0.01 * H)) / (0.05 * H), 0, 1)
    W[:, bones.index("UpperLeg" + side)] = t
    W[:, bones.index("LowerLeg" + side)] = 1 - t
    return W


def pauldrons(parts, J, H, role, trim, layers=3, spikes=0, spike_role="Horn", flat=0.04):
    for s, side in ((1, "L"), (-1, "R")):
        sh = J["shoulder" + side]
        PD = sdf.Field(role, h=0.0035 * H)
        for k in range(layers):
            c = sh + V3(s * (0.014 + 0.014 * k) * H, 0, (0.012 - 0.03 * k) * H)
            rr = V3(0.064, 0.064, flat) * H * (1 - 0.08 * k)
            PD.add(G.dome(c, rr, 0.004 * H, cut=G.region_and(G.zrange(c.z - 0.012 * H, 9), G.side(s, abs(sh.x) - 0.035 * H))))
        PD.add(sdf.torus(sh + V3(s * 0.014 * H, 0, 0.0), 0.063 * H, 0.0042 * H, axis=(0, 0, 1)), role=trim)
        for k in range(spikes):
            b0 = sh + V3(s * (0.03 + 0.03 * k) * H, 0.008 * H * k, 0.045 * H - 0.012 * H * k)
            PD.cone(b0, b0 + V3(s * 0.05 * H, 0.025 * H, 0.09 * H - 0.025 * H * k), 0.014 * H, 0.001 * H, k=0.006 * H, role=spike_role)
        parts.append((A.make(PD, "Pauldron", faces=1800), ("rigid", "UpperArm" + side)))


def crusader():
    """A templar of the human army in full plate: great helm with crest and plume, tabard with a
    gold cross, red cape, kite shield and longsword."""
    H = 1.28
    J = A.joints(H, female=False, spread=16, bulk=1.1)
    r = 0.08 * H
    hc = head_center(J, r)
    rig = sculpted_rig(J, hc + V3(0, 0, r * 1.1))
    parts = []
    body = A.make(A.body_field(J, "Chain", bulk=1.1), "Body", faces=7000)
    torso = A.body_field(J, "Metal", bulk=1.1, arms=False, legs=False, pecs=False, h=0.0045 * H)
    arms = A.body_field(J, "Metal", bulk=1.1, torso=False, legs=False, h=0.004 * H)
    legs = A.body_field(J, "Metal", bulk=1.1, torso=False, arms=False, h=0.004 * H)
    for side in "LR":
        parts.append((A.make(A.hand(J, side, "Metal", scale=1.15, fist=0.45), "Gauntlet", faces=1200), ("rigid", "Hand" + side)))

    HM = sdf.Field("Metal", h=0.0032 * H)
    HM.cone(hc + V3(0, 0.04 * r, -0.8 * r), hc + V3(0, 0.04 * r, 0.25 * r), 1.04 * r, 1.0 * r)
    HM.sphere(hc + V3(0, 0.04 * r, 0.22 * r), 1.0 * r, k=0.12 * r)
    HM.add(sdf.box(hc + V3(0, -1.05 * r, 0.02 * r), (0.62 * r, 0.3 * r, 0.05 * r)), op="subtract", k=0.01 * r)
    HM.add(sdf.box(hc + V3(0, -1.0 * r, -0.2 * r), (0.06 * r, 0.12 * r, 0.25 * r), round_=0.03 * r), k=0.02 * r)
    for k in range(3):
        for j in range(2):
            HM.sphere(hc + V3(0.25 * r + 0.15 * r * j, -1.0 * r, -0.35 * r - 0.14 * r * k), 0.045 * r, op="subtract")
    HM.add(sdf.torus(hc + V3(0, 0.04 * r, -0.78 * r), 1.03 * r, 0.06 * r), role="Gold")
    HM.add(sdf.torus(hc + V3(0, 0.04 * r, 0.12 * r), 1.03 * r, 0.04 * r), role="Gold")
    crest = [hc + V3(0, 0.04 * r, 0.22 * r) + V3(0, -math.cos(a), math.sin(a)) * 1.03 * r for a in np.linspace(0.35, math.pi - 0.2, 7)]
    HM.chain(crest, [0.07 * r] * 7, k=0.03 * r, flat=(V3(1, 0, 0), 0.4), role="Gold")
    parts.append((A.make(HM, "Helm", faces=4000), ("rigid", "Head")))
    PL = sdf.Field("Primary", h=0.003 * H)
    top = hc + V3(0, 0.1 * r, 1.25 * r)
    PL.chain([top, top + V3(0, 0.12 * H, 0.03 * H), top + V3(0, 0.22 * H, -0.06 * H), top + V3(0, 0.26 * H, -0.18 * H)],
             [0.02 * H, 0.04 * H, 0.035 * H, 0.008 * H], k=0.01 * H, flat=(V3(1, 0, 0), 0.35))
    parts.append((A.make(PL, "Plume", faces=1200), ("rigid", "Head")))

    zw = J["waist"].z - 0.02 * H
    BP = sdf.Field("Metal", h=0.0045 * H)
    BP.add(sdf.slab(torso, 0.013 * H, G.zrange(zw, 0.8 * H)))
    keel = [V3(0, G.surface_y(torso, 0, z, 0.013 * H), z) for z in np.linspace(zw + 0.01 * H, 0.78 * H, 5)]
    BP.chain(keel, [0.005 * H] * 5, k=0.006 * H)
    BP.add(sdf.shell(torso, 0.016 * H, 0.003 * H, G.zrange(0.786 * H, 0.798 * H)), role="Gold")
    for k in range(3):
        z0 = J["hips"].z - 0.055 * H + k * 0.03 * H
        BP.add(sdf.slab(torso, 0.016 * H - k * 0.0015 * H, G.zrange(z0, z0 + 0.028 * H)))
    cy = G.surface_y(torso, 0, J["chest"].z, 0.013 * H)
    BP.add(sdf.box(V3(0, cy - 0.003 * H, J["chest"].z - 0.01 * H), (0.011 * H, 0.004 * H, 0.06 * H)), role="Gold")
    BP.add(sdf.box(V3(0, cy - 0.003 * H, J["chest"].z + 0.018 * H), (0.04 * H, 0.004 * H, 0.011 * H)), role="Gold")
    BP.add(sdf.slab(torso, 0.02 * H, G.zrange(J["hips"].z - 0.068 * H, J["hips"].z - 0.052 * H)), role="Leather")
    parts.append((A.make(BP, "Cuirass", faces=4200), ("transfer", ["Hips", "Spine", "Chest"])))
    TB = sdf.Field("White", h=0.0035 * H)
    zt = J["hips"].z - 0.06 * H
    for sgn in (-1, 1):
        y0 = G.surface_y(torso, 0, zt, 0.024 * H, front=sgn < 0) or 0
        tilt = V3(0, sgn * 0.1, 1).normalized()
        ctr = V3(0, y0 + sgn * 0.006 * H, zt - 0.11 * H)
        ax = (V3(1, 0, 0), tilt.cross(V3(1, 0, 0)), tilt)
        TB.add(sdf.box(ctr, (0.05 * H, 0.0035 * H, 0.115 * H), axes=ax, round_=0.002 * H))
        TB.add(sdf.box(ctr + V3(0, sgn * 0.003 * H, -0.105 * H), (0.052 * H, 0.0045 * H, 0.007 * H), axes=ax, round_=0.002 * H), role="Gold")
        if sgn < 0:
            TB.add(sdf.box(ctr + V3(0, -0.004 * H, 0.02 * H), (0.008 * H, 0.003 * H, 0.06 * H), axes=ax), role="Primary")
            TB.add(sdf.box(ctr + V3(0, -0.004 * H, 0.045 * H), (0.03 * H, 0.003 * H, 0.008 * H), axes=ax), role="Primary")
    parts.append((A.make(TB, "Tabard", faces=1200), ("fn", skirt_weights(J, zt, zt - 0.23 * H, follow=0.7))))
    CP = sdf.Field("Primary", h=0.0035 * H)
    CP.add(G.cape((0, 0.025 * H), 0.8 * H, 0.2 * H, (0.13 * H, 0.09 * H), (0.22 * H, 0.2 * H), 1.15, folds=9, fold_amp=0.09, thick=0.0045 * H))
    parts.append((A.make(CP, "Cape", faces=2600), ("fn", height_weights([("Chest", 0.76 * H), ("Spine", 0.62 * H), ("Hips", 0.45 * H)]))))
    pauldrons(parts, J, H, "Metal", "Gold", layers=3)
    plate_limbs(parts, J, H, arms, legs)
    outline = [(-0.13, 0.12), (0.13, 0.12), (0.125, -0.05), (0.07, -0.18), (0.0, -0.26), (-0.07, -0.18), (-0.125, -0.05)]
    el, wr = J["elbowL"], J["wristL"]
    base = A.lerp(el, wr, 0.45) + V3(0.06, -0.02, 0.0)
    for o in (prism("Shield", outline, 0.03, "Primary", bevel=0.01, loc=base, rot=(0, 0, 90)),
              prism("ShieldRim", [(x * 1.06, z * 1.05 + 0.004) for x, z in outline], 0.022, "Gold", bevel=0.006, loc=base + V3(-0.003, 0, 0), rot=(0, 0, 90)),
              prism("ShieldCrossV", [(-0.015, 0.09), (0.015, 0.09), (0.015, -0.2), (-0.015, -0.2)], 0.036, "Gold", bevel=0.004, loc=base, rot=(0, 0, 90)),
              prism("ShieldCrossH", [(-0.09, 0.03), (0.09, 0.03), (0.09, 0.0), (-0.09, 0.0)], 0.036, "Gold", bevel=0.004, loc=base, rot=(0, 0, 90))):
        parts.append((o, ("rigid", "LowerArmL")))
    parts += sculpt_sword(J, "R", length=0.6)
    finish_sculpted("human", rig, body, parts, "biped", weight=0.9, spread=16)


def ghoul():
    """An emaciated ghoul: gaunt hunched body with exposed ribs and spine, skull face with sunken
    glowing eyes, long talons, stringy hair and tattered rags."""
    H = 1.22
    J = A.joints(H, female=False, spread=20, hunch=0.06, bulk=0.9)
    r = 0.075 * H
    hc = head_center(J, r) + V3(0, -0.03 * H, -0.01 * H)
    rig = sculpted_rig(J, hc + V3(0, 0, r * 1.1))
    parts = []
    BF = A.body_field(J, "Primary", gaunt=True, bulk=0.9)
    for k in range(5):
        z = J["chest"].z + 0.035 * H - k * 0.024 * H
        for s in (1, -1):
            pts = []
            for x in (0.008, 0.026, 0.042):
                y = G.surface_y(BF, s * x * H, z - x * 0.25 * H, -0.002 * H)
                if y is not None:
                    pts.append(V3(s * x * H, y, z - x * 0.25 * H))
            if len(pts) >= 2:
                BF.chain(pts, [0.0055 * H, 0.0065 * H, 0.005 * H][:len(pts)], k=0.006 * H, role="Bone")
    for k in range(7):
        z = J["hips"].z + 0.02 * H + k * 0.04 * H
        y = G.surface_y(BF, 0, z, -0.003 * H, front=False)
        if y is not None:
            BF.sphere(V3(0, y, z), 0.009 * H, k=0.006 * H, role="Bone")
    body = A.make(BF, "Body", faces=7000)
    HF = A.head_field(hc, r, "Primary", ears=0.35, jaw=0.82, nose=0, chin=1.05)
    for s in (1, -1):
        HF.sphere(hc + V3(s * 0.34 * r, -0.86 * r, -0.05 * r), 0.2 * r, op="subtract", k=0.06 * r)
        HF.ellipsoid(hc + V3(s * 0.55 * r, -0.62 * r, -0.32 * r), (0.16 * r, 0.1 * r, 0.09 * r), k=0.08 * r, role="Bone")
    HF.add(sdf.box(hc + V3(0, -0.9 * r, -0.62 * r), (0.32 * r, 0.2 * r, 0.035 * r)), op="subtract", k=0.02 * r)
    HF.cone(hc + V3(0, -0.92 * r, -0.28 * r), hc + V3(0, -0.92 * r, -0.4 * r), 0.06 * r, 0.03 * r, op="subtract")
    HF.paint(lambda x, y, z: np.minimum(
        np.sqrt((np.abs(x - hc.x) - 0.34 * r) ** 2 + (y - hc.y + 0.86 * r) ** 2 + (z - hc.z + 0.05 * r) ** 2) - 0.22 * r,
        np.maximum(np.abs(z - hc.z + 0.62 * r) - 0.05 * r, np.abs(x - hc.x) - 0.3 * r) + 0.0 * y), "Dark")
    head = A.make(HF, "Head", faces=6000)
    parts.append((head, ("rigid", "Head")))
    TE = sdf.Field("Teeth", h=0.0018 * H)
    for i in range(7):
        x = (i - 3) * 0.075 * r
        yy = -0.93 * r + 0.06 * r * abs(i - 3) / 3
        TE.cone(hc + V3(x, yy, -0.56 * r), hc + V3(x, yy - 0.01 * r, -0.66 * r), 0.03 * r, 0.004 * r)
        TE.cone(hc + V3(x + 0.035 * r, yy + 0.01 * r, -0.69 * r), hc + V3(x + 0.035 * r, yy, -0.6 * r), 0.028 * r, 0.004 * r)
    parts.append((A.make(TE, "Teeth", faces=900), ("rigid", "Head")))
    for s in (1, -1):
        parts.append((blob("Eye", 0.06 * r, hc + V3(s * 0.34 * r, -0.78 * r, -0.05 * r), "Eye", subdiv=2), ("rigid", "Head")))
    HR = A.Hair(hc, r, "Hair", k=0.01 * r)
    for i in range(7):
        th = math.radians(-120 + 240 * i / 6)
        HR.lock((math.sin(th) * 0.4, math.cos(th) * 0.4, 0.85), (math.sin(th) * 0.9, math.cos(th) * 0.9, -0.1),
                V3(math.sin(th) * 0.02, 0.02, -0.16 * H * (0.7 + 0.3 * math.sin(i * 2.3))), 0.08, thick=0.6, n=4, lift=0.0)
    parts.append((A.make(HR.F, "Hair", faces=1800), ("transfer", HAIR_BONES, [head])))
    for side in "LR":
        parts.append((A.make(A.hand(J, side, "Primary", scale=1.3, claws="Horn", fist=0.2), "Hand", faces=1500), ("rigid", "Hand" + side)))
    zt = J["hips"].z + 0.025 * H

    def tatter(a):
        a = np.asarray(a)
        return 0.33 * H + 0.04 * H * np.sin(a * 5 + 0.4) + 0.025 * H * np.sin(a * 13 + 1.1) + 0.015 * H * np.sin(a * 23)
    RG = sdf.Field("Secondary", h=0.0032 * H)
    RG.add(G.skirt((0, 0.0), zt, (0.07 * H, 0.054 * H), (0.12 * H, 0.105 * H), tatter, folds=7, fold_amp=0.12, thick=0.004 * H, ruffle=0.04, ruffle_freq=17))
    RG.add(sdf.torus(V3(0, 0.002 * H, zt - 0.006 * H), 0.068 * H, 0.006 * H, axis=(0, 0.05, 1)), role="Leather")
    parts.append((A.make(RG, "Rags", faces=2600), ("fn", skirt_weights(J, zt, 0.33 * H, follow=0.55))))
    finish_sculpted("humanoid", rig, body, parts, "biped", weight=0.8, hunch=1.0, claws=True, spread=20)


def merchant_imp():
    """The wandering imp merchant: pot-bellied, big-eared, in a hooded robe with a gold sash,
    an overloaded backpack and a lantern."""
    H = 0.66
    J = A.joints(H, female=False, spread=20, bulk=1.25)
    r = 0.105
    hc = head_center(J, r, 0.45)
    rig = sculpted_rig(J, hc + V3(0, 0, r * 1.1))
    parts = []
    BF = A.body_field(J, "Skin", bulk=1.25, belly=1.0)
    tpts = A.tail(BF, V3(0, 0.06 * H, 0.5 * H), 0.4, 0.016, tip_role="Horn", heart=False)
    rig.add("Tail1", tpts[0], tpts[3], "Hips")
    rig.add("Tail2", tpts[3], tpts[6], "Tail1")
    rig.add("Tail3", tpts[6], tpts[9], "Tail2")
    body = A.make(BF, "Body", faces=6000)
    torso = A.body_field(J, "Cloth", bulk=1.25, belly=1.0, arms=False, legs=False, h=0.004)
    legs = A.body_field(J, "Leather", bulk=1.25, torso=False, arms=False, h=0.0032)
    head = A.make(A.head_field(hc, r, "FaceImp", ears=1.5, jaw=1.12, chin=0.9, nose=1.4, ear_role="Skin"), "Head", faces=8000)
    A.face_uv(head, hc, r)
    A.face_normals(head, hc, r)
    parts.append((head, ("rigid", "Head")))
    for side in "LR":
        parts.append((A.make(A.hand(J, side, "Skin", scale=1.25, fist=0.3), "Hand", faces=1000), ("rigid", "Hand" + side)))
    HF = sdf.Field("Horn", h=0.0024)
    for s in (1, -1):
        A.horn(HF, hc + V3(s * r * 0.5, 0.0, r * 0.75), s, 0.11, 0.018, curl=0.6, ridges=False)
    parts.append((A.make(HF, "Horn", faces=900), ("rigid", "Head")))
    HD = sdf.Field("Robe", h=0.003)
    hcc = hc + V3(0, 0.06 * r, 0.05 * r)
    HD.add(G.dome(hcc, (1.17 * r, 1.2 * r, 1.17 * r), 0.045 * r, cut=G.zrange(hc.z - 0.85 * r, 9)))
    HD.ellipsoid(hc + V3(0, -1.05 * r, -0.25 * r), (0.78 * r, 0.55 * r, 0.82 * r), op="subtract", k=0.05 * r)
    HD.cone(hcc + V3(0, 1.0 * r, 0.3 * r), hcc + V3(0, 1.55 * r, -0.4 * r), 0.3 * r, 0.03 * r, k=0.15 * r)
    HD.add(sdf.torus(hc + V3(0, -0.2 * r, -0.05 * r), 0.98 * r, 0.05 * r, axis=(0, -1, 0.25)), role="Gold", k=0.01 * r)
    parts.append((A.make(HD, "Hood", faces=2200), ("rigid", "Head")))
    zr = J["waist"].z
    RB = sdf.Field("Robe", h=0.0035)
    RB.add(sdf.shell(torso, 0.008 * H, 0.004 * H, G.zrange(zr - 0.01 * H, 0.8 * H)))
    RB.add(G.skirt((0, 0.0), zr + 0.004 * H, (0.135 * H, 0.12 * H), (0.17 * H, 0.155 * H), G.hem_front_back(0.25 * H, 0.22 * H), folds=8, fold_amp=0.08,
                   thick=0.005 * H), k=0.006 * H)
    RB.add(sdf.shell(torso, 0.016 * H, 0.006 * H, G.zrange(zr - 0.008 * H, zr + 0.02 * H)), role="Gold")
    upper = height_weights([("Chest", 0.75 * H), ("Spine", 0.65 * H), ("Hips", 0.55 * H)])
    lower = skirt_weights(J, zr, 0.22 * H, follow=0.5)
    parts.append((A.make(RB, "Robe", faces=3200), ("fn", lambda P, bones: np.where((P[:, 2] > zr + 0.01 * H)[:, None], upper(P, bones), lower(P, bones)))))
    BT = sdf.Field("Leather", h=0.003)
    BT.add(sdf.slab(legs, 0.006 * H, G.zrange(-1.0, 0.1 * H)))
    parts.append((A.make(BT, "Shoes", faces=1000), ("transfer", ["LowerLegL", "LowerLegR", "FootL", "FootR"])))
    BK = sdf.Field("Wood", h=0.004)
    yb = G.surface_y(torso, 0, J["chest"].z, 0.0, front=False)
    pc = V3(0, yb + 0.1, J["chest"].z + 0.02)
    BK.add(sdf.box(pc, (0.12, 0.075, 0.14), round_=0.02))
    for k in (-1, 0, 1):
        BK.add(sdf.box(pc + V3(0, 0, k * 0.085), (0.124, 0.079, 0.012), round_=0.006), role="Gold")
    BK.cone(pc + V3(-0.14, 0.0, 0.17), pc + V3(0.14, 0.0, 0.17), 0.045, 0.045, role="Cloth")
    BK.add(sdf.torus(pc + V3(0.0, 0.0, 0.17), 0.046, 0.006, axis=(1, 0, 0)), role="Leather")
    BK.sphere(pc + V3(0.12, 0.03, -0.12), 0.05, role="Stone")
    BK.cone(pc + V3(-0.12, 0.04, -0.15), pc + V3(-0.12, 0.04, 0.2), 0.022, 0.022, role="Leather")
    for s in (1, -1):
        BK.add(sdf.shell(torso, 0.012 * H, 0.004 * H, G.region_and(G.along(V3(s * 0.05 * H, 0, 0.45 * H), V3(s * 0.05 * H, 0, 0.82 * H), 0, 1),
                                                                    lambda x, y, z, s=s: np.abs(x - s * 0.05 * H) - 0.012 * H)), role="Leather")
    parts.append((A.make(BK, "Backpack", faces=2600), ("rigid", "Chest")))
    hR = J["handR"]
    LT = sdf.Field("Metal", h=0.0022)
    lc = hR + V3(0, -0.02, -0.08)
    for k in range(4):
        a = math.pi / 2 * k + math.pi / 4
        LT.cone(lc + V3(math.cos(a) * 0.04, math.sin(a) * 0.04, 0.05), lc + V3(math.cos(a) * 0.04, math.sin(a) * 0.04, -0.05), 0.005, 0.005)
    LT.cone(lc + V3(0, 0, 0.05), lc + V3(0, 0, 0.075), 0.048, 0.01, k=0.005)
    LT.add(sdf.torus(lc + V3(0, 0, -0.05), 0.04, 0.007))
    LT.add(sdf.torus(hR + V3(0, -0.02, 0.0), 0.016, 0.004, axis=(1, 0, 0)))
    parts.append((A.make(LT, "Lantern", faces=1200), ("rigid", "HandR")))
    parts.append((blob("Flame", 0.03, lc, "Fire", scale=(1, 1, 1.3), subdiv=2), ("rigid", "HandR")))
    parts.append((blob("CoinPouch", 0.035, J["hipL"] + V3(0.05, -0.05, 0.0), "Leather", scale=(1, 1, 1.2), subdiv=2), ("rigid", "Hips")))
    finish_sculpted("merchant", rig, body, parts, "biped", weight=0.7, spread=20)


def beast():
    """A hellhound: deep-chested, tucked waist, muscular haunches, spiked spine and mane,
    a heavy muzzle with fangs, digitigrade legs with clawed paws, and a whip tail."""
    rig = Rig()
    F = sdf.Field("Primary", h=0.0065)
    F.ellipsoid(V3(0, -0.16, 0.5), (0.12, 0.2, 0.14))
    F.ellipsoid(V3(0, -0.2, 0.43), (0.095, 0.13, 0.12), k=0.06)
    F.ellipsoid(V3(0, 0.1, 0.52), (0.085, 0.17, 0.085), k=0.08)
    for s in (1, -1):
        F.ellipsoid(V3(s * 0.06, 0.3, 0.5), (0.072, 0.12, 0.11), k=0.05)
        F.ellipsoid(V3(s * 0.08, -0.19, 0.5), (0.055, 0.09, 0.11), k=0.04)
    F.cone(V3(0, -0.28, 0.56), V3(0, -0.45, 0.7), 0.11, 0.078, k=0.05)
    F.ellipsoid(V3(0, -0.5, 0.73), (0.09, 0.1, 0.08), k=0.04)
    F.cone(V3(0, -0.56, 0.715), V3(0, -0.71, 0.68), 0.062, 0.04, k=0.03)
    F.cone(V3(0, -0.53, 0.66), V3(0, -0.68, 0.645), 0.05, 0.03, k=0.025)
    for s in (1, -1):
        F.ellipsoid(V3(s * 0.04, -0.56, 0.765), (0.03, 0.04, 0.016), k=0.02)
        F.chain([V3(s * 0.045, -0.48, 0.77), V3(s * 0.07, -0.44, 0.84), V3(s * 0.085, -0.42, 0.9)], [0.03, 0.022, 0.003], k=0.012,
                flat=(V3(0, 1, 0.3), 0.35), role="Secondary")
    F.sphere(V3(0, -0.735, 0.69), 0.024, k=0.01, role="Dark")
    legs = {}
    for s, side in ((1, "L"), (-1, "R")):
        fs, fe, fw, fp = V3(s * 0.09, -0.18, 0.5), V3(s * 0.1, -0.13, 0.32), V3(s * 0.1, -0.19, 0.12), V3(s * 0.1, -0.235, 0.03)
        F.chain([fs, fe, A.lerp(fe, fw, 0.4), fw, fp], [0.068, 0.052, 0.042, 0.032, 0.03], k=0.03)
        hp, st, hk, bp = V3(s * 0.075, 0.3, 0.48), V3(s * 0.1, 0.22, 0.31), V3(s * 0.1, 0.4, 0.15), V3(s * 0.1, 0.36, 0.03)
        F.chain([hp, A.lerp(hp, st, 0.5) + V3(0, 0.02, 0), st, hk, bp], [0.085, 0.072, 0.05, 0.034, 0.03], k=0.03)
        for paw in (fp, bp):
            F.ellipsoid(paw + V3(0, -0.02, -0.005), (0.032, 0.042, 0.02), k=0.012, role="Secondary")
            for k in (-1.5, -0.5, 0.5, 1.5):
                t = paw + V3(k * 0.014, -0.055, -0.012)
                F.cone(t, t + V3(k * 0.003, -0.022, -0.01), 0.007, 0.0015, k=0.004, role="Horn")
        legs[side] = (fs, fe, fp, hp, st, bp)
    tail = [V3(0, 0.42, 0.55), V3(0, 0.55, 0.62), V3(0, 0.68, 0.61), V3(0, 0.79, 0.53)]
    F.chain(tail, [0.04, 0.03, 0.02, 0.012], k=0.03)
    F.cone(tail[-1], tail[-1] + V3(0, 0.08, -0.06), 0.03, 0.002, k=0.02, role="Secondary", flat=(V3(1, 0, 0), 0.4))
    for k in range(9):   # spine spikes
        t = k / 8
        base = V3(0, 0.36 - 0.6 * t, 0.6 + 0.06 * math.sin(t * math.pi) + 0.03 * t)
        F.cone(base, base + V3(0, 0.05, 0.07 + 0.03 * math.sin(t * math.pi)), 0.016, 0.001, k=0.01, role="Horn")
    rnd = random.Random(4)
    for k in range(16):   # mane
        a = math.pi * (k / 15) - math.pi / 2
        base = V3(math.sin(a) * 0.075, -0.33 + rnd.uniform(-0.03, 0.03), 0.62 + math.cos(a) * 0.05)
        tip = base + V3(math.sin(a) * 0.06, 0.11 + rnd.uniform(0, 0.05), 0.05)
        F.chain([base, (base + tip) / 2 + V3(0, 0, 0.025), tip], [0.034, 0.026, 0.003], k=0.012, role="Fur", flat=(V3(math.cos(a), 0, -math.sin(a)), 0.45))
    rig.add("Hips", V3(0, 0.3, 0.5), V3(0, 0.06, 0.5))
    rig.add("Spine", V3(0, 0.06, 0.5), V3(0, -0.2, 0.52), "Hips")
    rig.add("Chest", V3(0, -0.2, 0.52), V3(0, -0.4, 0.64), "Spine")
    rig.add("Head", V3(0, -0.42, 0.66), V3(0, -0.7, 0.68), "Chest")
    for side in "LR":
        fs, fe, fp, hp, st, bp = legs[side]
        rig.add("Leg" + side, fs, fe, "Chest")
        rig.add("Shin" + side, fe, fp, "Leg" + side)
        rig.add("Leg2" + side, hp, st, "Hips")
        rig.add("Shin2" + side, st, bp, "Leg2" + side)
    rig.add("Tail1", tail[0], tail[2], "Hips")
    rig.add("Tail2", tail[2], tail[3] + V3(0, 0.08, -0.06), "Tail1")
    body = A.make(F, "Body", faces=9000)
    parts = []
    TF = sdf.Field("Teeth", h=0.002)
    for s in (1, -1):
        for k in range(3):
            x = s * (0.014 + k * 0.011)
            TF.cone(V3(x * 1.2, -0.7 + k * 0.025, 0.665), V3(x * 1.2, -0.7 + k * 0.025, 0.625 + k * 0.006), 0.008 - k * 0.0015, 0.0008)
        parts.append((blob("Eye", 0.016, V3(s * 0.052, -0.575, 0.75), "Eye", scale=(1, 0.7, 0.8), subdiv=2), ("rigid", "Head")))
    parts.append((A.make(TF, "Fangs", faces=500), ("rigid", "Head")))
    finish_sculpted("beast", rig, body, parts, "quadruped", heat_skip=())


def flyer():
    """A giant bat: furry body, oversized ears, pug snout with fangs, membranous wings."""
    rig = Rig()
    c = V3(0, 0, 0.85)
    hc = c + V3(0, -0.04, 0.17)
    F = sdf.Field("Primary", h=0.0045)
    F.ellipsoid(c, (0.1, 0.09, 0.125))
    F.ellipsoid(c + V3(0, -0.02, 0.07), (0.085, 0.075, 0.07), k=0.04)
    F.sphere(hc, 0.078, k=0.04)
    F.cone(hc + V3(0, -0.06, -0.02), hc + V3(0, -0.105, -0.035), 0.04, 0.028, k=0.02)
    F.ellipsoid(hc + V3(0, -0.11, -0.022), (0.022, 0.01, 0.02), k=0.008, role="Dark")
    for s in (1, -1):
        F.chain([hc + V3(s * 0.04, 0.0, 0.05), hc + V3(s * 0.075, 0.01, 0.13), hc + V3(s * 0.085, 0.02, 0.21)], [0.04, 0.03, 0.004], k=0.02,
                flat=(V3(0, 1, 0), 0.3), role="Secondary")
        F.chain([c + V3(s * 0.04, 0, -0.1), c + V3(s * 0.05, -0.01, -0.17), c + V3(s * 0.05, -0.03, -0.2)], [0.018, 0.012, 0.006], k=0.02)
    F.cone(c + V3(0, 0.06, -0.08), c + V3(0, 0.18, -0.25), 0.03, 0.004, k=0.02)
    F.displace(lambda x, y, z: 0.004 * sdf.fbm(x, y, z, freq=60.0, octaves=2) * np.clip((z - (c.z + 0.12)) * -20 + 1, 0, 1))
    body = A.make(F, "Body", faces=6000)
    rig.add("Body", c + V3(0, 0.05, -0.12), c + V3(0, 0, 0.1))
    rig.add("Head", c + V3(0, -0.03, 0.1), c + V3(0, -0.06, 0.28), "Body")
    rig.add("Tail1", c + V3(0, 0.06, -0.08), c + V3(0, 0.18, -0.25), "Body")
    parts = []
    for s, side in ((1, "L"), (-1, "R")):
        parts.append((blob("Eye", 0.017, hc + V3(s * 0.034, -0.07, 0.016), "Eye", subdiv=2), ("rigid", "Head")))
        root = c + V3(s * 0.075, 0.02, 0.04)
        WF, mem, wrist, tip = A.wing(root, s, 0.58, "Primary", "Secondary", bone_r=0.014)
        rig.add("Wing" + side, root, wrist, "Body")
        rig.add("WingTip" + side, wrist, tip, "Wing" + side)
        parts.append((A.make(WF, "WingBone", faces=900), ("bind", ["Wing" + side, "WingTip" + side])))
        parts.append((mem, ("bind", ["Wing" + side, "WingTip" + side])))
    TF = sdf.Field("Teeth", h=0.0015)
    for s in (1, -1):
        TF.cone(hc + V3(s * 0.016, -0.098, -0.045), hc + V3(s * 0.017, -0.1, -0.07), 0.006, 0.0008)
    parts.append((A.make(TF, "Fangs", faces=300), ("rigid", "Head")))
    finish_sculpted("flyer", rig, body, parts, "flyer")


def insect():
    """A giant cave spider: armored cephalothorax, banded abdomen with a glowing hourglass,
    eight jointed legs with knobbed joints, chelicerae with fangs, and a cluster of eyes."""
    rig = Rig()
    th, ab, hd = V3(0, -0.05, 0.3), V3(0, 0.3, 0.38), V3(0, -0.22, 0.31)
    F = sdf.Field("Primary", h=0.006)
    F.ellipsoid(th, (0.13, 0.15, 0.085))
    F.ellipsoid(hd, (0.085, 0.07, 0.065), k=0.04)
    F.cone(th + V3(0, 0.12, 0.0), ab + V3(0, -0.2, -0.03), 0.04, 0.05, k=0.03)
    F.ellipsoid(ab, (0.19, 0.24, 0.17), k=0.03, role="Secondary")
    for k in range(4):
        F.add(G.dome(ab + V3(0, -0.14 + k * 0.09, 0.0), (0.185 - abs(k - 1.5) * 0.02, 0.05, 0.168 - abs(k - 1.5) * 0.02), 0.006,
                     cut=G.zrange(ab.z - 0.06, 9)), k=0.01, role="Primary")
    F.paint(lambda x, y, z: np.maximum(np.abs(x) - (0.03 + 0.4 * np.abs(y - ab.y - 0.02) ** 1.2), np.maximum(np.abs(y - ab.y - 0.02) - 0.13, ab.z + 0.1 - z)), "Glow")
    rig.add("Body", th + V3(0, 0.08, 0), th + V3(0, -0.1, 0))
    rig.add("Abdomen", th + V3(0, 0.1, 0), ab + V3(0, 0.2, 0), "Body")
    rig.add("Head", th + V3(0, -0.12, 0), hd + V3(0, -0.1, 0), "Body")
    legs = []
    for i, y in enumerate([-0.14, -0.06, 0.02, 0.1]):
        suffix = "" if i == 0 else str(i + 1)
        for s, side in ((1, "L"), (-1, "R")):
            hip = th + V3(s * 0.1, y, 0.0)
            spread = (y + 0.02) * 0.9
            knee = hip + V3(s * 0.17, spread, 0.2)
            ankle = knee + V3(s * 0.13, spread, -0.28)
            foot = ankle + V3(s * 0.05, spread * 0.3, -0.21)
            F.chain([hip, A.lerp(hip, knee, 0.5), knee, A.lerp(knee, ankle, 0.5), ankle, foot], [0.03, 0.028, 0.024, 0.02, 0.016, 0.004], k=0.012)
            F.sphere(knee, 0.03, k=0.01, role="Secondary")
            F.sphere(ankle, 0.022, k=0.008, role="Secondary")
            rig.add("Leg" + suffix + side, hip, knee, "Body")
            rig.add("Shin" + suffix + side, knee, foot, "Leg" + suffix + side)
    for s in (1, -1):
        F.chain([hd + V3(s * 0.025, -0.05, -0.02), hd + V3(s * 0.03, -0.09, -0.06), hd + V3(s * 0.02, -0.1, -0.1)], [0.022, 0.018, 0.012], k=0.012)
        F.cone(hd + V3(s * 0.02, -0.1, -0.1), hd + V3(s * 0.005, -0.12, -0.14), 0.01, 0.001, k=0.004, role="Horn")
        F.chain([hd + V3(s * 0.05, -0.05, -0.01), hd + V3(s * 0.09, -0.12, -0.03), hd + V3(s * 0.1, -0.16, -0.07)], [0.011, 0.01, 0.008], k=0.008)
    body = A.make(F, "Body", faces=9000)
    parts = []
    for s in (1, -1):
        for (x, z, rr) in ((0.026, 0.05, 0.017), (0.052, 0.033, 0.012), (0.018, 0.015, 0.011), (0.045, 0.006, 0.009)):
            parts.append((blob("Eye", rr, hd + V3(s * x, -0.062, z), "Eye", subdiv=2), ("rigid", "Head")))
    finish_sculpted("insect", rig, body, parts, "spider", heat_skip=())


def golem():
    """A rock golem: chiselled stone plates over a glowing core, with crystal growths."""
    H = 1.35
    J = A.joints(H, female=False, spread=22, bulk=1.5)
    rig = sculpted_rig(J, J["headBase"] + V3(0, 0, 0.2))
    core = A.make(A.body_field(J, "Glow", bulk=1.05, h=0.009 * H), "Core", faces=3000)
    parts = []
    rnd = random.Random(5)

    def rock(name, bone, center, size, role="Primary", faces=500):
        RF = sdf.Field(role, h=min(size) * 0.12)
        RF.add(sdf.box(center, V3(*size) * 0.95, round_=min(size) * 0.35))
        seed = rnd.uniform(0, 50)
        RF.displace(lambda x, y, z, sd=seed, sz=min(size): sz * 0.22 * sdf.fbm(x, y, z, freq=3.2 / sz, octaves=2, seed=sd))
        parts.append((A.make(RF, name, faces=faces, faceted=True), ("rigid", bone)))

    rock("Torso", "Chest", J["chest"] + V3(0, 0, 0.02 * H), (0.17 * H, 0.12 * H, 0.13 * H), faces=900)
    rock("Belly", "Spine", J["spine"] + V3(0, -0.01 * H, 0), (0.12 * H, 0.1 * H, 0.07 * H))
    rock("Pelvis", "Hips", J["hips"], (0.13 * H, 0.1 * H, 0.07 * H), "Secondary")
    rock("Head", "Head", J["headBase"] + V3(0, -0.02 * H, 0.06 * H), (0.065 * H, 0.065 * H, 0.06 * H), faces=400)
    rock("Brow", "Head", J["headBase"] + V3(0, -0.07 * H, 0.1 * H), (0.075 * H, 0.03 * H, 0.02 * H), "Secondary", faces=200)
    for s, side in ((1, "L"), (-1, "R")):
        rock("Shoulder", "UpperArm" + side, J["shoulder" + side] + V3(s * 0.025 * H, 0, 0.03 * H), (0.085 * H, 0.08 * H, 0.08 * H), "Secondary")
        rock("Arm", "UpperArm" + side, A.lerp(J["shoulder" + side], J["elbow" + side], 0.6), (0.055 * H, 0.055 * H, 0.075 * H))
        rock("Forearm", "LowerArm" + side, A.lerp(J["elbow" + side], J["wrist" + side], 0.5), (0.065 * H, 0.065 * H, 0.08 * H), "Secondary")
        rock("Fist", "Hand" + side, J["hand" + side], (0.07 * H, 0.07 * H, 0.065 * H))
        rock("Thigh", "UpperLeg" + side, A.lerp(J["hip" + side], J["knee" + side], 0.5), (0.07 * H, 0.07 * H, 0.09 * H), "Secondary")
        rock("Shin", "LowerLeg" + side, A.lerp(J["knee" + side], J["ankle" + side], 0.5), (0.065 * H, 0.065 * H, 0.09 * H))
        rock("Foot", "Foot" + side, A.lerp(J["ankle" + side], J["toe" + side], 0.5) + V3(0, 0, -0.01 * H), (0.055 * H, 0.08 * H, 0.03 * H), "Secondary")
        CR = sdf.Field("Glow", h=0.004 * H)
        for k in range(3):
            b0 = J["shoulder" + side] + V3(s * (0.03 + k * 0.03) * H, 0.03 * H, 0.08 * H)
            CR.cone(b0, b0 + V3(s * 0.03 * H, 0.02 * H, (0.11 - k * 0.025) * H), 0.022 * H, 0.002 * H)
        parts.append((A.make(CR, "Crystal", faces=300, faceted=True), ("rigid", "UpperArm" + side)))
    CR = sdf.Field("Glow", h=0.004 * H)
    for k in range(3):
        b0 = J["chest"] + V3((k - 1) * 0.06 * H, 0.1 * H, 0.05 * H)
        CR.cone(b0, b0 + V3((k - 1) * 0.03 * H, 0.07 * H, 0.14 * H), 0.03 * H, 0.002 * H)
    parts.append((A.make(CR, "Crystal", faces=300, faceted=True), ("rigid", "Chest")))
    parts.append((bevel_box("EyeSlit", (0.07 * H, 0.012 * H, 0.012 * H), J["headBase"] + V3(0, -0.085 * H, 0.065 * H), "Eye", bevel=0.003), ("rigid", "Head")))
    finish_sculpted("golem", rig, core, parts, "biped", weight=0.7, heavy=True, spread=22)


def ooze():
    """A gelatinous ooze: a wobbling mound pooling at the base, with drips, embedded bubbles
    and two glowing eyes."""
    root = link(bpy.data.objects.new("Root", None))
    F = sdf.Field("Primary", h=0.008)
    F.ellipsoid(V3(0, 0, 0.17), (0.36, 0.33, 0.21))
    F.ellipsoid(V3(0, 0, 0.04), (0.44, 0.41, 0.05), k=0.08)
    F.ellipsoid(V3(0.05, 0.04, 0.3), (0.2, 0.18, 0.14), k=0.1)
    for k in range(7):
        a = 2 * math.pi * k / 7 + 0.3
        F.sphere(V3(math.cos(a) * 0.38, math.sin(a) * 0.35, 0.06), 0.06, k=0.06)
    for (x, y, z, rr) in ((0.18, 0.05, 0.3, 0.055), (-0.2, 0.1, 0.24, 0.045), (0.0, 0.2, 0.33, 0.04), (-0.1, -0.25, 0.2, 0.035)):
        F.sphere(V3(x, y, z), rr, k=0.03, role="Secondary")
    F.displace(lambda x, y, z: 0.012 * sdf.fbm(x, y, z, freq=6.0, octaves=2, seed=3.0))
    body = A.make(F, "Body", faces=5000)
    body.parent = root
    for s in (1, -1):
        e = blob("Eye@Body", 0.045, (s * 0.1, -0.29, 0.25), "Eye", scale=(1, 0.7, 1.15), subdiv=2)
        e.parent = root
        p = blob("Pupil@Body", 0.02, (s * 0.1, -0.325, 0.25), "Dark", scale=(0.6, 0.4, 1.2), subdiv=2)
        p.parent = root
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
        RF = sdf.Field("Stone", h=s * 0.1)
        RF.add(sdf.box((x, y, s * 0.35), (s, s * 0.85, s * 0.6), round_=s * 0.35))
        RF.displace(lambda px, py, pz, sd=k * 3.1, sz=s: sz * 0.3 * sdf.fbm(px, py, pz, freq=2.5 / sz, octaves=2, seed=sd))
        own(r, A.make(RF, "Rock", faces=260, faceted=True))
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
