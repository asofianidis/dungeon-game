"""
Builds every 3D model for Depthbound and exports FBX files for Unity.

Usage (from the repo root):
    blender -b --factory-startup --python Tools/Blender/build_models.py -- Assets/Resources/Models

Characters are modeled, not assembled from primitives:
- Bodies are sculpted with a Skin modifier over a joint graph, then subdivided (smooth organic forms).
- Heads are reshaped spheres with a UV-mapped face; the anime face texture is painted in Unity
  (material role "Face<Variant>").
- Hair clumps, horns, tails and wing fingers are tapered, curved tubes; gowns and moldings are lathed;
  hard-surface parts are bevelled.
- Each character has an armature. Bodies are weighted to their nearest bones; rigid accessories
  (hair, horns, helmets, weapons) are bound to a single bone. The game animates the bones procedurally.

Conventions
- 1 Blender unit = 1 tile. Models stand on z=0 and face -Y. An empty named "Front" marks the front.
- Material names are roles (Primary, Secondary, Skin, Hair, Eye, Glow, ...); the game swaps in its own
  materials and tints Primary/Secondary per monster.
- Props are exported without armatures; their animated parts (Lid, Crystal, Fire) have their origin
  at the pivot. A part's logical parent may be encoded in its name as "Part@Parent".
"""
import bpy
import bmesh
import math
import os
import random
import sys
from mathutils import Matrix, Vector, Euler, noise

OUT = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else "Models"
os.makedirs(OUT, exist_ok=True)

PALETTE = {
    "Primary": (0.55, 0.1, 0.1, 1), "Secondary": (0.3, 0.3, 0.3, 1), "Skin": (0.62, 0.16, 0.16, 1),
    "SkinPale": (0.96, 0.82, 0.86, 1), "Horn": (0.12, 0.09, 0.1, 1), "Eye": (1.0, 0.75, 0.2, 1),
    "Glow": (0.75, 0.3, 1.0, 1), "Metal": (0.66, 0.67, 0.72, 1), "Gold": (0.92, 0.72, 0.2, 1),
    "Wood": (0.42, 0.25, 0.12, 1), "Stone": (0.45, 0.42, 0.48, 1), "Cloth": (0.45, 0.06, 0.24, 1),
    "Dark": (0.07, 0.06, 0.08, 1), "Water": (0.25, 0.5, 0.9, 1), "Fire": (1.0, 0.5, 0.1, 1),
    "Hair": (0.16, 0.06, 0.2, 1), "White": (0.92, 0.92, 0.88, 1), "Wing": (0.3, 0.05, 0.12, 1),
    "Leather": (0.2, 0.12, 0.08, 1), "FaceDemon": (0.62, 0.16, 0.16, 1), "FacePrincess": (0.96, 0.82, 0.86, 1),
    "FaceImp": (0.7, 0.18, 0.14, 1), "HairPrincess": (0.24, 0.07, 0.3, 1),
}
MATS = {}
random.seed(7)


# =============================================================================================
# Scene helpers
# =============================================================================================

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
    for a in list(bpy.data.armatures):
        bpy.data.armatures.remove(a)


def link(ob):
    bpy.context.scene.collection.objects.link(ob)
    return ob


def finalize(ob, role, smooth=True):
    me = ob.data
    me.materials.clear()
    me.materials.append(mat(role))
    me.polygons.foreach_set("use_smooth", [smooth] * len(me.polygons))
    return ob


def from_bm(name, bm, role, smooth=True):
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    return finalize(link(bpy.data.objects.new(name, me)), role, smooth)


def apply_modifiers(ob):
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    old = ob.data
    ob.modifiers.clear()
    ob.data = me
    bpy.data.meshes.remove(old)


def xform(ob, loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1)):
    """Bake a transform into the mesh (objects stay at identity so weights use world space)."""
    m = Matrix.Translation(Vector(loc)) @ Euler([math.radians(a) for a in rot]).to_matrix().to_4x4() @ Matrix.Diagonal(Vector(scale)).to_4x4()
    ob.data.transform(m)
    return ob


def mirror_x(ob, name):
    me = ob.data.copy()
    me.transform(Matrix.Diagonal(Vector((-1, 1, 1))).to_4x4())
    me.flip_normals()
    c = link(bpy.data.objects.new(name, me))
    return c


# =============================================================================================
# Modeling primitives (organic and hard-surface)
# =============================================================================================

def skin(name, nodes, edges, role, subsurf=2):
    """Organic mesh from a joint graph: nodes = [(pos, radius or (rx, ry)), ...]."""
    me = bpy.data.meshes.new(name)
    me.from_pydata([Vector(n[0]) for n in nodes], edges, [])
    me.update()
    bm = bmesh.new()
    bm.from_mesh(me)
    sk = bm.verts.layers.skin.verify()
    bm.verts.ensure_lookup_table()
    for i, v in enumerate(bm.verts):
        r = nodes[i][1]
        v[sk].radius = r if isinstance(r, tuple) else (r, r)
        v[sk].use_root = i == 0
    bm.to_mesh(me)
    bm.free()
    ob = link(bpy.data.objects.new(name, me))
    m = ob.modifiers.new("Skin", "SKIN")
    m.branch_smoothing = 0.7
    m.use_smooth_shade = True
    s = ob.modifiers.new("Sub", "SUBSURF")
    s.levels = subsurf
    s.render_levels = subsurf
    apply_modifiers(ob)
    return finalize(ob, role)


def tube(name, pts, radii, role, segs=10, flat=1.0, ref=(0, 0, 1), smooth=True):
    """A tapered tube through points (horns, hair clumps, tails, limbs). radius 0 = pointed tip."""
    pts = [Vector(p) for p in pts]
    bm = bmesh.new()
    rings = []
    n = len(pts)
    for i, p in enumerate(pts):
        d = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        r_ref = Vector(ref)
        if abs(d.dot(r_ref)) > 0.95:
            r_ref = Vector((1, 0, 0)) if abs(d.x) < 0.9 else Vector((0, 1, 0))
        a = d.cross(r_ref).normalized()
        b = d.cross(a).normalized()
        r = radii[i]
        if r <= 1e-5:
            rings.append([bm.verts.new(p)])
        else:
            rings.append([bm.verts.new(p + a * math.cos(t) * r + b * math.sin(t) * r * flat)
                          for t in [2 * math.pi * k / segs for k in range(segs)]])
    for i in range(n - 1):
        A, B = rings[i], rings[i + 1]
        if len(A) == 1 and len(B) == 1:
            continue
        for k in range(segs):
            if len(B) == 1:
                bm.faces.new((A[k], A[(k + 1) % segs], B[0]))
            elif len(A) == 1:
                bm.faces.new((A[0], B[(k + 1) % segs], B[k]))
            else:
                bm.faces.new((A[k], A[(k + 1) % segs], B[(k + 1) % segs], B[k]))
    if len(rings[0]) > 1:
        bm.faces.new(rings[0])
    if len(rings[-1]) > 1:
        bm.faces.new(rings[-1])
    return from_bm(name, bm, role, smooth)


def lathe(name, profile, role, segs=24, wave=None, center=(0, 0, 0), smooth=True):
    """Surface of revolution around Z. profile = [(radius, z), ...]; wave(angle, i) scales radius."""
    bm = bmesh.new()
    rings = []
    c = Vector(center)
    for i, (r, z) in enumerate(profile):
        if r <= 1e-5:
            rings.append([bm.verts.new(c + Vector((0, 0, z)))])
            continue
        ring = []
        for k in range(segs):
            t = 2 * math.pi * k / segs
            rr = r * (wave(t, i) if wave else 1.0)
            ring.append(bm.verts.new(c + Vector((math.cos(t) * rr, math.sin(t) * rr, z))))
        rings.append(ring)
    for i in range(len(rings) - 1):
        A, B = rings[i], rings[i + 1]
        for k in range(segs):
            if len(A) == 1 and len(B) == 1:
                continue
            if len(B) == 1:
                bm.faces.new((A[k], A[(k + 1) % segs], B[0]))
            elif len(A) == 1:
                bm.faces.new((A[0], B[(k + 1) % segs], B[k]))
            else:
                bm.faces.new((A[k], A[(k + 1) % segs], B[(k + 1) % segs], B[k]))
    if len(rings[0]) > 1:
        bm.faces.new(rings[0])
    if len(rings[-1]) > 1:
        bm.faces.new(rings[-1])
    return from_bm(name, bm, role, smooth)


def bevel_box(name, size, loc, role, bevel=0.02, rot=(0, 0, 0), segments=2):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=segments, profile=0.5, affect="EDGES", clamp_overlap=True)
    ob = from_bm(name, bm, role, smooth=False)
    return xform(ob, loc, rot)


def prism(name, outline, depth, role, bevel=0.012, loc=(0, 0, 0), rot=(0, 0, 0)):
    """Extrude a 2D outline (x, z) along Y with bevelled edges (shields, blades, throne backs)."""
    bm = bmesh.new()
    vs = [bm.verts.new((x, -depth / 2, z)) for x, z in outline]
    f = bm.faces.new(vs)
    ext = bmesh.ops.extrude_face_region(bm, geom=[f])
    moved = [e for e in ext["geom"] if isinstance(e, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, vec=Vector((0, depth, 0)), verts=moved)
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=1, profile=0.5, affect="EDGES", clamp_overlap=True)
    ob = from_bm(name, bm, role, smooth=False)
    return xform(ob, loc, rot)


def blob(name, radius, loc, role, scale=(1, 1, 1), subdiv=3, lumps=0.0, seed=0.0, smooth=True, faceted=False):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=radius)
    for v in bm.verts:
        if lumps > 0:
            n = noise.noise(v.co * (3.0 / radius) + Vector((seed, seed * 2, seed * 3)))
            v.co *= 1.0 + lumps * n
        v.co = Vector((v.co.x * scale[0], v.co.y * scale[1], v.co.z * scale[2]))
    ob = from_bm(name, bm, role, smooth and not faceted)
    return xform(ob, loc)


def membrane(name, points, role, thickness=0.012):
    """A thin flat surface from a polygon (wing membranes), given thickness with Solidify."""
    bm = bmesh.new()
    vs = [bm.verts.new(p) for p in points]
    bm.faces.new(vs)
    ob = from_bm(name, bm, role, smooth=True)
    s = ob.modifiers.new("Solid", "SOLIDIFY")
    s.thickness = thickness
    s.offset = 0
    apply_modifiers(ob)
    return finalize(ob, role)


def anime_head(name, center, r, role, chin=0.32):
    """A head with a soft anime jaw and a front-projected face UV (the face is painted in Unity)."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=28, v_segments=18, radius=r)
    for v in bm.verts:
        x, y, z = v.co
        if z < 0:
            k = 1.0 + (z / r) * chin       # taper the jaw toward a small chin
            v.co.x *= k
            v.co.y *= 1.0 + (z / r) * chin * 0.5
        v.co.x *= 1.02
        v.co.z *= 1.06
        if y < 0:
            v.co.y *= 0.9                  # flatter face
    uv = bm.loops.layers.uv.new("UVMap")
    for f in bm.faces:
        for loop in f.loops:
            co = loop.vert.co
            if co.y < -0.12 * r:
                loop[uv].uv = (0.5 + co.x / (2.04 * r), 0.5 + co.z / (2.12 * r))
            else:
                loop[uv].uv = (0.03, 0.03)  # plain skin corner of the face texture
    ob = from_bm(name, bm, role)
    return xform(ob, center)


# =============================================================================================
# Rigging
# =============================================================================================

class Rig:
    def __init__(self):
        self.bones = []

    def add(self, name, head, tail, parent=None, deform=True):
        self.bones.append((name, Vector(head), Vector(tail), parent, deform))
        return name

    def get(self, name):
        return next(b for b in self.bones if b[0] == name)


def build_armature(rig):
    data = bpy.data.armatures.new("Armature")
    ob = link(bpy.data.objects.new("Armature", data))
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for (n, h, t, p, d) in rig.bones:
        eb = data.edit_bones.new(n)
        eb.head = h
        eb.tail = t
        eb.use_deform = d
        if p:
            eb.parent = data.edit_bones[p]
            eb.use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    return ob


def seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.dot(ab), 1e-9)))
    return (p - (a + ab * t)).length


def bind(ob, rig, bones):
    """Weight a part to bones: one name = rigid, a list = smooth blend of the two nearest bones."""
    if isinstance(bones, str):
        g = ob.vertex_groups.new(name=bones)
        g.add([v.index for v in ob.data.vertices], 1.0, "REPLACE")
        return ob
    groups = {b: ob.vertex_groups.new(name=b) for b in bones}
    segs = [(b, rig.get(b)[1], rig.get(b)[2]) for b in bones]
    for v in ob.data.vertices:
        d = sorted(((seg_dist(v.co, a, c), n) for n, a, c in segs))[:2]
        if len(d) == 1 or d[1][0] > d[0][0] * 3.0:
            groups[d[0][1]].add([v.index], 1.0, "REPLACE")
            continue
        w0, w1 = 1.0 / (d[0][0] + 0.004) ** 3, 1.0 / (d[1][0] + 0.004) ** 3
        s = w0 + w1
        groups[d[0][1]].add([v.index], w0 / s, "REPLACE")
        groups[d[1][1]].add([v.index], w1 / s, "ADD")
    return ob


def join(parts, name):
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    ob = parts[0]
    ob.name = name
    return ob


def front_marker(parent=None):
    e = link(bpy.data.objects.new("Front", None))
    e.location = (0, -0.6, 0.5)
    if parent is not None:
        e.parent = parent
    return e


def finish_character(name, rig, parts):
    arm = build_armature(rig)
    body = join(parts, "Mesh")
    mod = body.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    body.parent = arm
    front_marker(arm)
    export(name)


def export(name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in bpy.context.scene.objects:
        o.select_set(True)
    path = os.path.join(OUT, name + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"EMPTY", "MESH", "ARMATURE"},
        apply_scale_options="FBX_SCALE_ALL", bake_space_transform=False,
        axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE", add_leaf_bones=False,
        use_mesh_modifiers=True, use_armature_deform_only=False, bake_anim=False,
        armature_nodetype="NULL", path_mode="STRIP",
    )
    print("exported", path)
    clear()


# =============================================================================================
# Humanoid anatomy
# =============================================================================================

def humanoid_joints(h=1.0, shoulder=0.16, hip=0.08, hunch=0.0):
    J = {
        "hips": (0, 0, 0.56 * h), "spine": (0, -0.005 - hunch * 0.4, 0.68 * h), "chest": (0, -0.01 - hunch, 0.8 * h),
        "neck": (0, -0.01 - hunch * 1.4, 0.92 * h), "headBase": (0, -0.01 - hunch * 1.6, 0.97 * h),
    }
    sh = J["chest"][2] + 0.07 * h
    for s, side in ((1, "L"), (-1, "R")):
        J["shoulder" + side] = (s * shoulder, J["chest"][1], sh)
        J["elbow" + side] = (s * (shoulder + 0.035), J["chest"][1] + 0.025, sh - 0.2 * h)
        J["wrist" + side] = (s * (shoulder + 0.05), J["chest"][1] - 0.02, sh - 0.38 * h)
        J["hand" + side] = (s * (shoulder + 0.055), J["chest"][1] - 0.035, sh - 0.45 * h)
        J["hip" + side] = (s * hip, 0, 0.53 * h)
        J["knee" + side] = (s * (hip + 0.005), -0.02, 0.29 * h)
        J["ankle" + side] = (s * (hip + 0.008), 0.01, 0.06 * h)
        J["toe" + side] = (s * (hip + 0.01), -0.085, 0.025 * h)
    return {k: Vector(v) for k, v in J.items()}


def humanoid_body(J, role, female=False, bulk=1.0, gaunt=False):
    t = 0.55 if gaunt else 1.0
    nodes, edges, idx = [], [], {}

    def node(key, r):
        idx[key] = len(nodes)
        nodes.append((J[key], r))

    node("hips", ((0.115 if female else 0.1) * bulk * t, 0.075 * bulk * t))
    node("spine", ((0.075 if female else 0.09) * bulk * t, 0.062 * bulk * t))
    node("chest", ((0.095 if female else 0.115) * bulk * t, 0.075 * bulk * t))
    node("neck", 0.036 * bulk * (0.8 if gaunt else 1.0))
    for side in "LR":
        node("shoulder" + side, (0.045 if female else 0.055) * bulk * t ** 0.5)
        node("elbow" + side, (0.03 if female else 0.04) * bulk * t ** 0.5)
        node("wrist" + side, (0.026 if female else 0.032) * bulk * t ** 0.5)
        node("hand" + side, (0.028 if female else 0.036) * bulk)
        node("hip" + side, (0.062 if female else 0.068) * bulk * t ** 0.5)
        node("knee" + side, (0.04 if female else 0.048) * bulk * t ** 0.5)
        node("ankle" + side, (0.03 if female else 0.034) * bulk * t ** 0.5)
        node("toe" + side, (0.026 if female else 0.03) * bulk)
    E = [("hips", "spine"), ("spine", "chest"), ("chest", "neck")]
    for s in "LR":
        E += [("chest", "shoulder" + s), ("shoulder" + s, "elbow" + s), ("elbow" + s, "wrist" + s), ("wrist" + s, "hand" + s),
              ("hips", "hip" + s), ("hip" + s, "knee" + s), ("knee" + s, "ankle" + s), ("ankle" + s, "toe" + s)]
    edges = [(idx[a], idx[b]) for a, b in E]
    return skin("Body", nodes, edges, role)


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


def spiky_hair(center, r, role, clumps=11, length=1.0, back_length=1.0, seed=1):
    """Anime hair: a cap plus tapered clumps sweeping out and down."""
    rnd = random.Random(seed)
    parts = []
    cap = blob("HairCap", r * 1.08, center, role, scale=(1.0, 1.0, 0.98), subdiv=3)
    # Open the cap at the face: pull front-lower vertices inside the head.
    for v in cap.data.vertices:
        rel = v.co - Vector(center)
        if rel.y < -0.35 * r and rel.z < 0.35 * r:
            v.co = Vector(center) + rel * 0.75
    parts.append(cap)
    for i in range(clumps):
        a = 2 * math.pi * i / clumps + rnd.uniform(-0.15, 0.15)
        dirx, diry = math.sin(a), math.cos(a)          # diry > 0 is the back of the head
        if diry < -0.55:
            continue                                   # keep the face clear (bangs are added separately)
        base = Vector(center) + Vector((dirx * r * 0.85, diry * r * 0.85, r * 0.45))
        L = r * rnd.uniform(0.9, 1.3) * (back_length if diry > 0.3 else length)
        p1 = base + Vector((dirx * L * 0.45, diry * L * 0.45, -L * 0.2))
        p2 = base + Vector((dirx * L * 0.75, diry * L * 0.7, -L * 0.75))
        parts.append(tube("Hair", [base, p1, p2], [r * 0.32, r * 0.2, 0.0], role, segs=8, flat=0.55))
    for k in range(5):                                 # bangs
        x = (k - 2) * r * 0.32
        base = Vector(center) + Vector((x, -r * 0.7, r * 0.62))
        tip = Vector(center) + Vector((x * 1.15 + rnd.uniform(-0.02, 0.02), -r * 1.02, r * 0.18))
        mid = (base + tip) / 2 + Vector((0, -r * 0.18, 0.02))
        parts.append(tube("Bang", [base, mid, tip], [r * 0.2, r * 0.14, 0.0], role, segs=8, flat=0.5))
    return parts


def curved_horn(base, side, length, role, curl=1.0, thickness=0.05):
    pts, radii = [], []
    for i in range(9):
        t = i / 8
        ang = t * 2.2 * curl
        pts.append(Vector(base) + Vector((side * (0.15 * t + 0.06 * math.sin(ang)) * length,
                                          (0.55 * t - 0.25 * math.sin(ang * 0.8)) * length * 0.6,
                                          (0.55 * math.sin(ang * 0.7) + 0.2 * t) * length)))
        radii.append(thickness * (1 - t) + 0.002)
    radii[-1] = 0.0
    return tube("Horn", pts, radii, role, segs=10)


def bat_wing(shoulder, side, span, role_membrane, role_bone, droop=0.0):
    """A bat wing: three finger bones with a scalloped membrane between them."""
    s = side
    root = Vector(shoulder)
    wrist = root + Vector((s * span * 0.38, span * 0.18, span * 0.28))
    tips = [wrist + Vector((s * span * 0.62, span * 0.1, span * 0.32 - droop)),
            wrist + Vector((s * span * 0.66, span * 0.12, -span * 0.08 - droop)),
            wrist + Vector((s * span * 0.38, span * 0.1, -span * 0.42 - droop))]
    low = root + Vector((s * span * 0.08, span * 0.12, -span * 0.48))
    parts = [tube("WingArm", [root, wrist], [0.022, 0.016], role_bone, segs=6)]
    for tp in tips:
        parts.append(tube("WingFinger", [wrist, (wrist + tp) / 2 + Vector((0, 0, span * 0.04)), tp], [0.014, 0.01, 0.0], role_bone, segs=6))
    poly = [root, wrist, tips[0]]
    for a, b in ((tips[0], tips[1]), (tips[1], tips[2]), (tips[2], low)):
        mid = (a + b) / 2
        inward = (wrist - mid).normalized() * span * 0.1
        poly += [mid + inward, b]
    poly = poly[:-1] + [low]
    parts.append(membrane("Membrane", [p + Vector((0, 0.005, 0)) for p in poly], role_membrane))
    return parts, wrist, tips[1]


def demon_tail(root, role, tip_role, length=0.7, heart=False):
    pts, radii = [], []
    for i in range(8):
        t = i / 7
        pts.append(Vector(root) + Vector((0.08 * math.sin(t * 3.0), length * (0.35 * t + 0.4 * t * t), -length * 0.55 * t + length * 0.45 * t * t * t)))
        radii.append(0.035 * (1 - t * 0.7))
    parts = [tube("Tail", pts, radii, role, segs=8)]
    tip = pts[-1]
    d = (pts[-1] - pts[-2]).normalized()
    w = 0.08 if heart else 0.07
    spade = tube("TailTip", [tip - d * 0.01, tip + d * 0.05, tip + d * 0.12], [0.0, w, 0.0], tip_role, segs=8, flat=0.25, ref=(1, 0, 0))
    parts.append(spade)
    return parts, pts


# =============================================================================================
# Characters
# =============================================================================================

def archdemon():
    J = humanoid_joints(h=1.0, shoulder=0.17, hip=0.085)
    head_r = 0.15
    head_c = J["headBase"] + Vector((0, -0.01, head_r * 0.95))
    rig = humanoid_rig(J, head_c + Vector((0, 0, head_r)))
    body = bind(humanoid_body(J, "Skin", bulk=1.08), rig, BODY_BONES)
    parts = [body]
    head = bind(anime_head("Head", head_c, head_r, "FaceDemon", chin=0.28), rig, "Head")
    parts.append(head)
    for p in spiky_hair(head_c, head_r, "Hair", clumps=12, length=0.9, back_length=1.1, seed=3):
        parts.append(bind(p, rig, "Head"))
    for s in (1, -1):
        parts.append(bind(curved_horn(head_c + Vector((s * head_r * 0.55, 0.0, head_r * 0.7)), s, 0.42, "Horn", curl=1.0, thickness=0.045), rig, "Head"))

    # Armor: a breastplate shell over the torso, pauldrons, gauntlets, greaves and a belt.
    shell = skin("Armor", [(J["hips"] + Vector((0, 0, 0.02)), (0.118, 0.088)), (J["spine"], (0.104, 0.078)),
                           (J["chest"], (0.132, 0.092)), (J["neck"] + Vector((0, 0, -0.04)), (0.07, 0.06))],
                 [(0, 1), (1, 2), (2, 3)], "Primary", subsurf=2)
    parts.append(bind(shell, rig, ["Hips", "Spine", "Chest"]))
    parts.append(bind(lathe("Belt", [(0.122, J["hips"].z + 0.04), (0.125, J["hips"].z), (0.12, J["hips"].z - 0.03)], "Gold", segs=20,
                            center=(0, 0, 0)), rig, "Hips"))
    for s, side in ((1, "L"), (-1, "R")):
        sh = J["shoulder" + side]
        pad = lathe("Pauldron", [(0.0, 0.07), (0.05, 0.065), (0.085, 0.03), (0.095, -0.01), (0.07, -0.03)], "Primary", segs=16, center=sh + Vector((s * 0.01, 0, 0)))
        parts.append(bind(pad, rig, "UpperArm" + side))
        parts.append(bind(tube("PauldronSpike", [sh + Vector((s * 0.04, 0, 0.05)), sh + Vector((s * 0.1, 0.02, 0.13))], [0.022, 0.0], "Horn", segs=6), rig, "UpperArm" + side))
        parts.append(bind(tube("Gauntlet", [J["elbow" + side] + Vector((0, 0, -0.03)), J["wrist" + side]], [0.047, 0.043], "Primary", segs=12), rig, "LowerArm" + side))
        parts.append(bind(tube("Greave", [J["knee" + side] + Vector((0, 0, 0.02)), J["ankle" + side] + Vector((0, 0, -0.02))], [0.058, 0.046], "Dark", segs=12), rig, "LowerLeg" + side))
        parts.append(bind(tube("Boot", [J["ankle" + side] + Vector((0, 0.02, 0.0)), J["toe" + side] + Vector((0, -0.01, 0.0))], [0.045, 0.034], "Dark", segs=10, flat=0.7), rig, "Foot" + side))
        parts.append(bind(blob("Kneepad", 0.04, J["knee" + side] + Vector((0, -0.035, 0)), "Primary", scale=(1, 0.6, 1.1), subdiv=2), rig, "LowerLeg" + side))
    tabard = tube("Loincloth", [J["hips"] + Vector((0, -0.09, -0.02)), J["hips"] + Vector((0, -0.1, -0.16)), J["hips"] + Vector((0, -0.08, -0.28))],
                  [0.07, 0.065, 0.06], "Cloth", segs=10, flat=0.12, ref=(0, 1, 0))
    parts.append(bind(tabard, rig, "Hips"))

    # Sword in the right hand, held forward.
    h = J["handR"]
    blade = prism("Blade", [(-0.018, 0.0), (0.018, 0.0), (0.014, 0.52), (0.0, 0.6), (-0.014, 0.52)], 0.012, "Metal", bevel=0.004,
                  loc=h + Vector((0, -0.04, 0.02)), rot=(-75, 0, 0))
    parts.append(bind(blade, rig, "HandR"))
    parts.append(bind(bevel_box("Guard", (0.14, 0.03, 0.025), h + Vector((0, -0.03, 0.0)), "Gold", bevel=0.006), rig, "HandR"))
    parts.append(bind(tube("Grip", [h + Vector((0, 0.04, -0.02)), h + Vector((0, -0.02, 0.01))], [0.016, 0.016], "Leather", segs=6), rig, "HandR"))

    # Wings and tail.
    for s, side in ((1, "L"), (-1, "R")):
        root = J["chest"] + Vector((s * 0.06, 0.07, 0.06))
        wparts, wrist, tip = bat_wing(root, s, 0.75, "Wing", "Horn")
        rig.add("Wing" + side, root, wrist, "Chest")
        rig.add("WingTip" + side, wrist, tip, "Wing" + side)
        for wp in wparts:
            parts.append(bind(wp, rig, ["Wing" + side, "WingTip" + side]))
    tparts, tpts = demon_tail(J["hips"] + Vector((0, 0.08, -0.02)), "Skin", "Horn", length=0.75)
    rig.add("Tail1", tpts[0], tpts[3], "Hips")
    rig.add("Tail2", tpts[3], tpts[5], "Tail1")
    rig.add("Tail3", tpts[5], tpts[7], "Tail2")
    for tp in tparts:
        parts.append(bind(tp, rig, ["Tail1", "Tail2", "Tail3"]))
    finish_character("demon", rig, parts)


def princess():
    J = humanoid_joints(h=0.98, shoulder=0.13, hip=0.07)
    head_r = 0.145
    head_c = J["headBase"] + Vector((0, -0.01, head_r * 0.95))
    rig = humanoid_rig(J, head_c + Vector((0, 0, head_r)))
    parts = [bind(humanoid_body(J, "SkinPale", female=True, bulk=0.95), rig, BODY_BONES)]
    parts.append(bind(anime_head("Head", head_c, head_r, "FacePrincess", chin=0.36), rig, "Head"))
    hair = spiky_hair(head_c, head_r, "HairPrincess", clumps=10, length=0.8, back_length=0.9, seed=11)
    for p in hair:
        parts.append(bind(p, rig, "Head"))
    # Long flowing hair down the back, with two side locks framing the face.
    for i in range(7):
        x = (i - 3) * 0.035
        top = head_c + Vector((x, head_r * 0.6, head_r * 0.2))
        pts = [top + Vector((0, 0, 0)), top + Vector((x * 0.4, 0.06, -0.2)), top + Vector((x * 0.6, 0.09, -0.42)),
               top + Vector((x * 0.7 + 0.01 * math.sin(i), 0.08, -0.62))]
        parts.append(bind(tube("LongHair", pts, [0.05, 0.045, 0.035, 0.0], "HairPrincess", segs=8, flat=0.5, ref=(0, 1, 0)), rig, ["Head", "Chest", "Spine"]))
    for s in (1, -1):
        top = head_c + Vector((s * head_r * 0.85, -head_r * 0.3, 0.02))
        parts.append(bind(tube("SideLock", [top, top + Vector((s * 0.02, -0.02, -0.15)), top + Vector((s * 0.03, -0.03, -0.3))],
                               [0.035, 0.028, 0.0], "HairPrincess", segs=8, flat=0.5), rig, "Head"))
        parts.append(bind(curved_horn(head_c + Vector((s * head_r * 0.5, 0.01, head_r * 0.75)), s, 0.26, "Horn", curl=0.8, thickness=0.03), rig, "Head"))
    # Crown.
    cz = head_c.z + head_r * 0.92
    parts.append(bind(lathe("Crown", [(0.075, cz - 0.015), (0.08, cz), (0.08, cz + 0.02), (0.075, cz + 0.03)], "Gold", segs=20, center=(head_c.x, head_c.y + 0.01, 0)), rig, "Head"))
    for k in range(6):
        a = 2 * math.pi * k / 6
        b = head_c + Vector((math.sin(a) * 0.075, 0.01 + math.cos(a) * 0.075, head_r * 0.92 + 0.02))
        parts.append(bind(tube("CrownSpike", [b, b + Vector((0, 0, 0.06 if k % 2 == 0 else 0.04))], [0.012, 0.0], "Gold", segs=5), rig, "Head"))
    parts.append(bind(blob("CrownGem", 0.016, head_c + Vector((0, -0.075, head_r * 0.92 + 0.03)), "Glow", subdiv=1, faceted=True), rig, "Head"))

    # Gown: fitted bodice, a lathed skirt with frilled hem, puff sleeves, gloves, trims.
    bod = skin("Bodice", [(J["hips"] + Vector((0, 0, 0.03)), (0.12, 0.085)), (J["spine"], (0.085, 0.068)), (J["chest"], (0.1, 0.08)),
                          (J["neck"] + Vector((0, 0, -0.055)), (0.06, 0.05))], [(0, 1), (1, 2), (2, 3)], "Cloth", subsurf=2)
    parts.append(bind(bod, rig, ["Hips", "Spine", "Chest"]))
    waist = J["hips"].z + 0.04
    frill = lambda t, i: 1.0 + (0.06 * math.sin(t * 12) if i >= 5 else 0.0)
    skirt = lathe("Skirt", [(0.0, waist + 0.01), (0.11, waist), (0.17, waist - 0.12), (0.25, waist - 0.28), (0.33, waist - 0.42),
                            (0.39, waist - 0.5), (0.41, 0.02), (0.36, 0.0), (0.0, 0.0)], "Cloth", segs=36, wave=frill)
    parts.append(bind(skirt, rig, "Hips"))
    parts.append(bind(lathe("Petticoat", [(0.42, 0.06), (0.43, 0.03), (0.4, 0.01)], "White", segs=36, wave=lambda t, i: 1.0 + 0.05 * math.sin(t * 12 + 0.3)), rig, "Hips"))
    parts.append(bind(lathe("WaistTrim", [(0.112, waist + 0.012), (0.118, waist), (0.112, waist - 0.012)], "Gold", segs=24), rig, "Hips"))
    for s, side in ((1, "L"), (-1, "R")):
        parts.append(bind(blob("PuffSleeve", 0.06, J["shoulder" + side] + Vector((s * 0.02, 0, -0.02)), "Cloth", scale=(1, 1, 0.9), subdiv=3), rig, "UpperArm" + side))
        parts.append(bind(tube("Glove", [J["elbow" + side] + Vector((0, 0, 0.02)), J["wrist" + side], J["hand" + side]], [0.032, 0.028, 0.03], "White", segs=10), rig, ["LowerArm" + side, "Hand" + side]))
    parts.append(bind(lathe("Necklace", [(0.05, J["neck"].z - 0.03), (0.056, J["neck"].z - 0.04), (0.05, J["neck"].z - 0.05)], "Gold", segs=20,
                            center=(0, J["neck"].y, 0)), rig, "Chest"))
    parts.append(bind(blob("Pendant", 0.017, J["neck"] + Vector((0, -0.055, -0.07)), "Glow", subdiv=1, faceted=True), rig, "Chest"))

    for s, side in ((1, "L"), (-1, "R")):
        root = J["chest"] + Vector((s * 0.05, 0.065, 0.05))
        wparts, wrist, tip = bat_wing(root, s, 0.42, "Wing", "Horn", droop=0.04)
        rig.add("Wing" + side, root, wrist, "Chest")
        rig.add("WingTip" + side, wrist, tip, "Wing" + side)
        for wp in wparts:
            parts.append(bind(wp, rig, ["Wing" + side, "WingTip" + side]))
    tparts, tpts = demon_tail(J["hips"] + Vector((0, 0.12, 0.0)), "SkinPale", "Cloth", length=0.55, heart=True)
    rig.add("Tail1", tpts[0], tpts[3], "Hips")
    rig.add("Tail2", tpts[3], tpts[5], "Tail1")
    rig.add("Tail3", tpts[5], tpts[7], "Tail2")
    for tp in tparts:
        parts.append(bind(tp, rig, ["Tail1", "Tail2", "Tail3"]))
    finish_character("princess", rig, parts)


def crusader():
    J = humanoid_joints(h=1.0, shoulder=0.16, hip=0.085)
    head_r = 0.14
    head_c = J["headBase"] + Vector((0, -0.01, head_r * 0.95))
    rig = humanoid_rig(J, head_c + Vector((0, 0, head_r)))
    parts = [bind(humanoid_body(J, "Metal", bulk=1.12), rig, BODY_BONES)]
    # Great helm with a visor slit and a plume.
    helm = lathe("Helm", [(0.0, head_r * 1.15), (0.07, head_r * 1.08), (0.12, head_r * 0.8), (0.145, head_r * 0.2), (0.145, -head_r * 0.75),
                          (0.13, -head_r * 0.9), (0.0, -head_r * 0.9)], "Metal", segs=24, center=head_c)
    parts.append(bind(helm, rig, "Head"))
    parts.append(bind(bevel_box("Visor", (0.2, 0.03, 0.025), head_c + Vector((0, -0.14, 0.01)), "Dark", bevel=0.005), rig, "Head"))
    parts.append(bind(bevel_box("VisorRidge", (0.02, 0.02, 0.2), head_c + Vector((0, -0.15, -0.02)), "Gold", bevel=0.004), rig, "Head"))
    plume = tube("Plume", [head_c + Vector((0, 0, head_r * 1.1)), head_c + Vector((0, 0.06, head_r * 1.5)), head_c + Vector((0, 0.18, head_r * 1.3)), head_c + Vector((0, 0.24, head_r * 0.7))],
                 [0.03, 0.045, 0.035, 0.0], "Primary", segs=8, flat=0.4, ref=(1, 0, 0))
    parts.append(bind(plume, rig, "Head"))
    # Plate armor.
    shell = skin("Breastplate", [(J["hips"] + Vector((0, 0, 0.02)), (0.12, 0.09)), (J["spine"], (0.108, 0.082)), (J["chest"], (0.135, 0.095)),
                                 (J["neck"] + Vector((0, 0, -0.04)), (0.07, 0.065))], [(0, 1), (1, 2), (2, 3)], "Metal", subsurf=2)
    parts.append(bind(shell, rig, ["Hips", "Spine", "Chest"]))
    for front in (True, False):
        y = -0.1 if front else 0.1
        tab = tube("Tabard", [J["chest"] + Vector((0, y, 0.04)), J["hips"] + Vector((0, y * 1.05, 0)), J["hips"] + Vector((0, y * 1.1, -0.26))],
                   [0.085, 0.09, 0.085], "White", segs=10, flat=0.1, ref=(0, 1, 0))
        parts.append(bind(tab, rig, ["Chest", "Spine", "Hips"]))
    parts.append(bind(bevel_box("CrossV", (0.03, 0.012, 0.2), J["spine"] + Vector((0, -0.112, 0.0)), "Gold", bevel=0.004), rig, "Spine"))
    parts.append(bind(bevel_box("CrossH", (0.12, 0.012, 0.03), J["spine"] + Vector((0, -0.112, 0.04)), "Gold", bevel=0.004), rig, "Spine"))
    cape = tube("Cape", [J["neck"] + Vector((0, 0.08, -0.02)), J["spine"] + Vector((0, 0.13, 0)), J["hips"] + Vector((0, 0.16, -0.25))],
                [0.13, 0.15, 0.17], "Primary", segs=12, flat=0.08, ref=(0, 1, 0))
    parts.append(bind(cape, rig, ["Chest", "Spine", "Hips"]))
    for s, side in ((1, "L"), (-1, "R")):
        sh = J["shoulder" + side]
        parts.append(bind(lathe("Pauldron", [(0.0, 0.07), (0.055, 0.065), (0.09, 0.03), (0.1, -0.015), (0.075, -0.035)], "Metal", segs=16, center=sh + Vector((s * 0.012, 0, 0))), rig, "UpperArm" + side))
        parts.append(bind(tube("Vambrace", [J["elbow" + side], J["wrist" + side]], [0.048, 0.044], "Metal", segs=12), rig, "LowerArm" + side))
        parts.append(bind(tube("Sabaton", [J["ankle" + side], J["toe" + side]], [0.046, 0.036], "Metal", segs=10, flat=0.7), rig, "Foot" + side))
    # Kite shield on the left arm, sword in the right hand.
    shield = prism("Shield", [(-0.13, 0.12), (0.13, 0.12), (0.12, -0.08), (0.0, -0.24), (-0.12, -0.08)], 0.03, "Primary", bevel=0.01,
                   loc=J["elbowL"] + Vector((0.07, -0.06, -0.05)), rot=(0, 0, 90))
    parts.append(bind(shield, rig, "LowerArmL"))
    parts.append(bind(prism("ShieldRim", [(-0.02, 0.1), (0.02, 0.1), (0.02, -0.2), (-0.02, -0.2)], 0.036, "Gold", bevel=0.004,
                            loc=J["elbowL"] + Vector((0.07, -0.06, -0.05)), rot=(0, 0, 90)), rig, "LowerArmL"))
    h = J["handR"]
    parts.append(bind(prism("Blade", [(-0.016, 0.0), (0.016, 0.0), (0.012, 0.48), (0.0, 0.55), (-0.012, 0.48)], 0.01, "Metal", bevel=0.003,
                            loc=h + Vector((0, -0.04, 0.02)), rot=(-75, 0, 0)), rig, "HandR"))
    parts.append(bind(bevel_box("Guard", (0.13, 0.03, 0.022), h + Vector((0, -0.03, 0.0)), "Gold", bevel=0.005), rig, "HandR"))
    finish_character("human", rig, parts)


def ghoul():
    J = humanoid_joints(h=0.95, shoulder=0.14, hip=0.075, hunch=0.07)
    head_r = 0.12
    head_c = J["headBase"] + Vector((0, -0.05, head_r * 0.85))
    rig = humanoid_rig(J, head_c + Vector((0, 0, head_r)))
    parts = [bind(humanoid_body(J, "Primary", gaunt=True), rig, BODY_BONES)]
    skull = blob("Head", head_r, head_c, "Primary", scale=(0.9, 1.05, 1.0), subdiv=3, lumps=0.06, seed=2)
    parts.append(bind(skull, rig, "Head"))
    parts.append(bind(blob("Jaw", head_r * 0.6, head_c + Vector((0, -0.06, -0.08)), "Secondary", scale=(1.0, 0.9, 0.55), subdiv=2), rig, "Head"))
    for s in (1, -1):
        parts.append(bind(blob("Eye", 0.022, head_c + Vector((s * 0.045, -0.105, 0.01)), "Eye", subdiv=2), rig, "Head"))
    for k in range(4):   # ribs
        z = J["chest"].z - 0.02 - k * 0.04
        for s in (1, -1):
            pts = [Vector((s * 0.01, -0.06, z)), Vector((s * 0.06, -0.05, z - 0.01)), Vector((s * 0.075, 0.0, z - 0.02))]
            parts.append(bind(tube("Rib", [p + Vector((0, J["chest"].y, 0)) for p in pts], [0.008, 0.008, 0.006], "Secondary", segs=6), rig, "Chest"))
    for s, side in ((1, "L"), (-1, "R")):
        h = J["hand" + side]
        for k in range(3):
            parts.append(bind(tube("Claw", [h + Vector(((k - 1) * 0.015, -0.01, 0)), h + Vector(((k - 1) * 0.02, -0.05, -0.08))], [0.008, 0.0], "Horn", segs=5), rig, "Hand" + side))
    rag = tube("Rags", [J["hips"] + Vector((0, -0.08, 0)), J["hips"] + Vector((0.01, -0.09, -0.18))], [0.07, 0.06], "Secondary", segs=8, flat=0.15, ref=(0, 1, 0))
    parts.append(bind(rag, rig, "Hips"))
    finish_character("humanoid", rig, parts)


def merchant_imp():
    J = humanoid_joints(h=0.62, shoulder=0.11, hip=0.06)
    head_r = 0.15
    head_c = J["headBase"] + Vector((0, -0.01, head_r * 0.9))
    rig = humanoid_rig(J, head_c + Vector((0, 0, head_r)))
    parts = [bind(humanoid_body(J, "Skin", bulk=1.15), rig, BODY_BONES)]
    parts.append(bind(anime_head("Head", head_c, head_r, "FaceImp", chin=0.2), rig, "Head"))
    for s in (1, -1):
        parts.append(bind(curved_horn(head_c + Vector((s * head_r * 0.5, 0, head_r * 0.7)), s, 0.16, "Horn", curl=0.5, thickness=0.025), rig, "Head"))
        ear = tube("Ear", [head_c + Vector((s * head_r * 0.9, 0, 0)), head_c + Vector((s * head_r * 1.5, 0.02, 0.05))], [0.035, 0.0], "Skin", segs=6, flat=0.4, ref=(0, 1, 0))
        parts.append(bind(ear, rig, "Head"))
    hood = lathe("Hood", [(0.0, head_r * 1.2), (head_r * 0.8, head_r * 0.95), (head_r * 1.12, head_r * 0.2), (head_r * 1.1, -head_r * 0.3)], "Cloth", segs=20,
                 center=head_c + Vector((0, 0.03, 0)))
    for v in hood.data.vertices:   # open the hood at the face
        rel = v.co - head_c
        if rel.y < -0.4 * head_r and rel.z < head_r * 0.7:
            v.co = head_c + rel * Vector((1, 0.4, 1))
    parts.append(bind(hood, rig, "Head"))
    pack = bevel_box("Backpack", (0.26, 0.18, 0.3), J["chest"] + Vector((0, 0.15, 0.0)), "Wood", bevel=0.02)
    parts.append(bind(pack, rig, "Chest"))
    parts.append(bind(bevel_box("PackStrap", (0.28, 0.2, 0.03), J["chest"] + Vector((0, 0.15, 0.05)), "Gold", bevel=0.006), rig, "Chest"))
    parts.append(bind(blob("Bedroll", 0.06, J["chest"] + Vector((0, 0.15, 0.19)), "Cloth", scale=(2.2, 1, 1), subdiv=2), rig, "Chest"))
    lantern = blob("Lantern", 0.04, J["handR"] + Vector((0, -0.02, -0.06)), "Fire", subdiv=2)
    parts.append(bind(lantern, rig, "HandR"))
    parts.append(bind(tube("LanternCage", [J["handR"] + Vector((0, -0.02, -0.01)), J["handR"] + Vector((0, -0.02, -0.11))], [0.05, 0.05], "Metal", segs=6, smooth=False), rig, "HandR"))
    finish_character("merchant", rig, parts)


def beast():
    rig = Rig()
    P = {"tail": Vector((0, 0.42, 0.56)), "hips": Vector((0, 0.3, 0.52)), "belly": Vector((0, 0.06, 0.5)), "chest": Vector((0, -0.2, 0.53)),
         "neck": Vector((0, -0.36, 0.64)), "head": Vector((0, -0.48, 0.7)), "snout": Vector((0, -0.66, 0.65))}
    legs = {}
    for s, side in ((1, "L"), (-1, "R")):
        legs["fs" + side] = Vector((s * 0.1, -0.2, 0.46)); legs["fe" + side] = Vector((s * 0.11, -0.18, 0.26)); legs["fp" + side] = Vector((s * 0.11, -0.24, 0.03))
        legs["bh" + side] = Vector((s * 0.1, 0.3, 0.46)); legs["bk" + side] = Vector((s * 0.11, 0.38, 0.26)); legs["bp" + side] = Vector((s * 0.11, 0.3, 0.03))
    nodes = [(P["hips"], (0.12, 0.13)), (P["tail"], 0.05), (P["belly"], (0.11, 0.12)), (P["chest"], (0.13, 0.14)), (P["neck"], 0.075), (P["head"], (0.09, 0.1)), (P["snout"], (0.045, 0.05))]
    edges = [(0, 1), (0, 2), (2, 3), (3, 4), (4, 5), (5, 6)]
    for side in "LR":
        base = len(nodes)
        nodes += [(legs["fs" + side], 0.055), (legs["fe" + side], 0.04), (legs["fp" + side], 0.038),
                  (legs["bh" + side], 0.065), (legs["bk" + side], 0.045), (legs["bp" + side], 0.038)]
        edges += [(3, base), (base, base + 1), (base + 1, base + 2), (0, base + 3), (base + 3, base + 4), (base + 4, base + 5)]
    rig.add("Hips", P["hips"], P["belly"])
    rig.add("Spine", P["belly"], P["chest"], "Hips")
    rig.add("Chest", P["chest"], P["neck"], "Spine")
    rig.add("Head", P["neck"], P["snout"], "Chest")
    for side in "LR":
        rig.add("Leg" + side, legs["fs" + side], legs["fe" + side], "Chest")
        rig.add("Shin" + side, legs["fe" + side], legs["fp" + side], "Leg" + side)
        rig.add("Leg2" + side, legs["bh" + side], legs["bk" + side], "Hips")
        rig.add("Shin2" + side, legs["bk" + side], legs["bp" + side], "Leg2" + side)
    tail_pts = [P["tail"], P["tail"] + Vector((0, 0.15, 0.06)), P["tail"] + Vector((0, 0.3, 0.02)), P["tail"] + Vector((0, 0.4, -0.06))]
    rig.add("Tail1", tail_pts[0], tail_pts[2], "Hips")
    rig.add("Tail2", tail_pts[2], tail_pts[3], "Tail1")
    bones = ["Hips", "Spine", "Chest", "Head"] + [b + s for s in "LR" for b in ("Leg", "Shin", "Leg2", "Shin2")]
    parts = [bind(skin("Body", nodes, edges, "Primary"), rig, bones)]
    parts.append(bind(tube("Tail", tail_pts, [0.05, 0.045, 0.035, 0.0], "Primary", segs=8), rig, ["Tail1", "Tail2"]))
    for k in range(7):   # bristling ridge
        t = k / 6
        base = P["hips"].lerp(P["neck"], t) + Vector((0, 0, 0.11 + 0.03 * math.sin(t * math.pi)))
        parts.append(bind(tube("Ridge", [base, base + Vector((0, 0.05, 0.08))], [0.025, 0.0], "Secondary", segs=6, flat=0.4, ref=(1, 0, 0)),
                          rig, "Spine" if t < 0.6 else "Chest"))
    for s in (1, -1):
        parts.append(bind(tube("Ear", [P["head"] + Vector((s * 0.05, 0.02, 0.06)), P["head"] + Vector((s * 0.07, 0.05, 0.16))], [0.035, 0.0], "Secondary", segs=6, flat=0.5, ref=(0, 1, 0)), rig, "Head"))
        parts.append(bind(blob("Eye", 0.018, P["head"] + Vector((s * 0.045, -0.08, 0.03)), "Eye", subdiv=2), rig, "Head"))
        parts.append(bind(tube("Fang", [P["snout"] + Vector((s * 0.02, -0.01, -0.03)), P["snout"] + Vector((s * 0.02, -0.01, -0.07))], [0.008, 0.0], "White", segs=5), rig, "Head"))
    finish_character("beast", rig, parts)


def flyer():
    rig = Rig()
    c = Vector((0, 0, 0.85))
    rig.add("Body", c + Vector((0, 0.05, -0.12)), c + Vector((0, 0, 0.1)))
    rig.add("Head", c + Vector((0, -0.03, 0.1)), c + Vector((0, -0.06, 0.28)), "Body")
    parts = [bind(skin("Body", [(c + Vector((0, 0.03, -0.1)), 0.07), (c, (0.12, 0.11)), (c + Vector((0, -0.02, 0.1)), 0.08)], [(0, 1), (1, 2)], "Primary"), rig, ["Body", "Head"])]
    hc = c + Vector((0, -0.04, 0.17))
    parts.append(bind(blob("Head", 0.085, hc, "Primary", scale=(1.05, 1, 0.95), subdiv=3), rig, "Head"))
    for s, side in ((1, "L"), (-1, "R")):
        parts.append(bind(tube("Ear", [hc + Vector((s * 0.05, 0, 0.05)), hc + Vector((s * 0.09, 0.02, 0.17))], [0.035, 0.0], "Secondary", segs=6, flat=0.5, ref=(0, 1, 0)), rig, "Head"))
        parts.append(bind(blob("Eye", 0.02, hc + Vector((s * 0.035, -0.075, 0.01)), "Eye", subdiv=2), rig, "Head"))
        root = c + Vector((s * 0.08, 0.02, 0.04))
        wparts, wrist, tip = bat_wing(root, s, 0.55, "Secondary", "Primary")
        rig.add("Wing" + side, root, wrist, "Body")
        rig.add("WingTip" + side, wrist, tip, "Wing" + side)
        for wp in wparts:
            parts.append(bind(wp, rig, ["Wing" + side, "WingTip" + side]))
        parts.append(bind(tube("Foot", [c + Vector((s * 0.04, 0, -0.1)), c + Vector((s * 0.05, -0.02, -0.2))], [0.015, 0.0], "Horn", segs=5), rig, "Body"))
    rig.add("Tail1", c + Vector((0, 0.06, -0.08)), c + Vector((0, 0.18, -0.25)), "Body")
    parts.append(bind(tube("Tail", [c + Vector((0, 0.06, -0.08)), c + Vector((0, 0.14, -0.18)), c + Vector((0, 0.2, -0.3))], [0.03, 0.02, 0.0], "Primary", segs=6), rig, "Tail1"))
    finish_character("flyer", rig, parts)


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
    for k in range(3):   # banding on the abdomen
        parts.append(bind(blob("Band", 0.05, ab + Vector((0, -0.06 + k * 0.09, 0.17)), "Primary", scale=(2.2, 0.5, 0.4), subdiv=2), rig, "Abdomen"))
    parts.append(bind(blob("Head", 0.085, hd, "Primary", scale=(1.1, 1, 0.85), subdiv=3), rig, "Head"))
    for s in (1, -1):
        for (x, z) in ((0.03, 0.04), (0.055, 0.02), (0.02, 0.0)):
            parts.append(bind(blob("Eye", 0.014, hd + Vector((s * x, -0.075, z)), "Eye", subdiv=1), rig, "Head"))
        parts.append(bind(tube("Fang", [hd + Vector((s * 0.025, -0.07, -0.03)), hd + Vector((s * 0.02, -0.1, -0.09))], [0.014, 0.0], "Horn", segs=6), rig, "Head"))
    for i, y in enumerate([-0.15, -0.06, 0.03, 0.12]):
        suffix = "" if i == 0 else str(i + 1)
        for s, side in ((1, "L"), (-1, "R")):
            hip = th + Vector((s * 0.1, y, 0.02))
            knee = hip + Vector((s * 0.17, (y + 0.02) * 0.6, 0.18))
            foot = knee + Vector((s * 0.15, (y + 0.02) * 0.8, -0.48))
            rig.add("Leg" + suffix + side, hip, knee, "Body")
            rig.add("Shin" + suffix + side, knee, foot, "Leg" + suffix + side)
            leg = tube("Leg", [hip, knee, foot], [0.028, 0.022, 0.0], "Primary", segs=7)
            parts.append(bind(leg, rig, ["Leg" + suffix + side, "Shin" + suffix + side]))
    finish_character("insect", rig, parts)


def golem():
    J = humanoid_joints(h=1.15, shoulder=0.26, hip=0.13)
    rig = humanoid_rig(J, J["headBase"] + Vector((0, 0, 0.2)))
    parts = []
    rnd = random.Random(5)

    def rock(name, bone, center, size, role="Primary"):
        b = blob(name, 1.0, center, role, scale=size, subdiv=2, lumps=0.18, seed=rnd.uniform(0, 50), faceted=True)
        parts.append(bind(b, rig, bone))

    rock("Torso", "Chest", J["chest"] + Vector((0, 0, 0.02)), (0.24, 0.17, 0.2))
    rock("Belly", "Spine", J["spine"], (0.18, 0.14, 0.14))
    rock("Pelvis", "Hips", J["hips"], (0.19, 0.14, 0.11), "Secondary")
    rock("Head", "Head", J["headBase"] + Vector((0, -0.03, 0.08)), (0.1, 0.1, 0.09))
    for s, side in ((1, "L"), (-1, "R")):
        rock("Shoulder", "UpperArm" + side, J["shoulder" + side] + Vector((s * 0.03, 0, 0.02)), (0.13, 0.12, 0.12), "Secondary")
        rock("Arm", "UpperArm" + side, (J["shoulder" + side] + J["elbow" + side]) / 2, (0.08, 0.08, 0.12))
        rock("Forearm", "LowerArm" + side, (J["elbow" + side] + J["wrist" + side]) / 2, (0.09, 0.09, 0.13), "Secondary")
        rock("Fist", "Hand" + side, J["hand" + side], (0.1, 0.1, 0.09))
        rock("Thigh", "UpperLeg" + side, (J["hip" + side] + J["knee" + side]) / 2, (0.1, 0.1, 0.14), "Secondary")
        rock("Shin", "LowerLeg" + side, (J["knee" + side] + J["ankle" + side]) / 2, (0.09, 0.09, 0.13))
        rock("Foot", "Foot" + side, (J["ankle" + side] + J["toe" + side]) / 2, (0.08, 0.11, 0.05), "Secondary")
    for k in range(4):   # glowing cracks
        p = J["chest"] + Vector((rnd.uniform(-0.12, 0.12), -0.165, rnd.uniform(-0.1, 0.08)))
        parts.append(bind(tube("Crack", [p, p + Vector((rnd.uniform(-0.05, 0.05), -0.01, rnd.uniform(-0.08, 0.08)))], [0.012, 0.008], "Glow", segs=5), rig, "Chest"))
    parts.append(bind(bevel_box("EyeSlit", (0.12, 0.02, 0.02), J["headBase"] + Vector((0, -0.12, 0.09)), "Eye", bevel=0.004), rig, "Head"))
    finish_character("golem", rig, parts)


def ooze():
    """Blob: a lumpy, translucent-looking ooze with a core and eyes (no skeleton; squashed in code)."""
    root = link(bpy.data.objects.new("Root", None))
    body = blob("Body", 0.34, (0, 0, 0.0), "Primary", scale=(1.15, 1.1, 0.72), subdiv=4, lumps=0.07, seed=3)
    for v in body.data.vertices:   # sag the lower half onto the ground
        if v.co.z < -0.08:
            v.co.z = -0.08 + (v.co.z + 0.08) * 0.25
    xform(body, (0, 0, 0.13))
    body.parent = root
    for (x, y, z, r) in ((0.1, 0.0, 0.18, 0.09), (-0.12, 0.08, 0.12, 0.06), (0.0, 0.12, 0.25, 0.05)):
        b = blob("Core@Body", r, (x, y, z), "Secondary", subdiv=2)
        b.parent = root
    for s in (1, -1):
        e = blob("Eye@Body", 0.045, (s * 0.1, -0.3, 0.2), "Eye", scale=(1, 0.7, 1.15), subdiv=2)
        e.parent = root
    front_marker(root)
    export("blob")


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
    """Move an object's origin to p (for parts the game rotates)."""
    ob.data.transform(Matrix.Translation(-Vector(p)))
    ob.location = p
    return ob


def chest(open_):
    r = prop_root()
    for k in range(4):   # planked body
        own(r, bevel_box("Plank", (0.6, 0.4, 0.075), (0, 0, 0.045 + k * 0.078), "Wood", bevel=0.012))
    for x in (-0.24, 0.24):
        own(r, bevel_box("Band", (0.045, 0.42, 0.33), (x, 0, 0.165), "Gold", bevel=0.006))
    for x in (-0.29, 0.29):
        for y in (-0.19, 0.19):
            own(r, bevel_box("Corner", (0.05, 0.05, 0.33), (x, y, 0.165), "Metal", bevel=0.008))
    lid = tube("Lid", [(-0.3, 0, 0.32), (0.3, 0, 0.32)], [0.21, 0.21], "Wood", segs=16, flat=0.55, ref=(0, 0, 1), smooth=True)
    for v in lid.data.vertices:   # keep the rounded top half only
        if v.co.z < 0.32:
            v.co.z = 0.32
    pivot(lid, (0, 0.2, 0.32))
    own(r, lid)
    for x in (-0.24, 0.24):
        band = tube("LidBand@Lid", [(x - 0.022, 0, 0.32), (x + 0.022, 0, 0.32)], [0.215, 0.215], "Gold", segs=16, flat=0.57, smooth=True)
        for v in band.data.vertices:
            if v.co.z < 0.32:
                v.co.z = 0.32
        pivot(band, (0, 0.2, 0.32))
        own(r, band)
    lock = bevel_box("Lock@Lid", (0.08, 0.04, 0.1), (0, -0.22, 0.31), "Gold", bevel=0.01)
    pivot(lock, (0, 0.2, 0.32))
    own(r, lock)
    if open_:
        for o in (lid, lock) + tuple(o for o in bpy.context.scene.objects if o.name.startswith("LidBand")):
            o.rotation_euler = (math.radians(-105), 0, 0)
        own(r, blob("Treasure", 0.22, (0, 0, 0.3), "Gold", scale=(1.25, 0.8, 0.25), subdiv=2, lumps=0.25, seed=4, faceted=True))
        for k in range(3):
            own(r, blob("Gem", 0.03, (-0.12 + k * 0.12, -0.05 + 0.05 * k, 0.36), "Glow", subdiv=1, faceted=True))
    front_marker(r)
    export("chest_open" if open_ else "chest")


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
    export("shrine")


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
    export("fountain")


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
    xform(disc, (0, 0, 0), (90, 0, 0), (1.0, 1.0, 1.0))
    for v in disc.data.vertices:
        v.co.z = v.co.z * 1.35
    xform(disc, (0, 0.0, 0.95))
    pivot(disc, (0, 0, 0.95))
    own(r, disc)
    own(r, bevel_box("Step", (1.5, 0.7, 0.08), (0, 0, 0.04), "Stone", bevel=0.02))
    front_marker(r)
    export("portal")


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
    export("throne")


def pillar():
    r = prop_root()
    flute = lambda t, i: 1.0 + (0.06 * math.cos(t * 16) if 3 <= i <= 6 else 0.0)
    own(r, lathe("Column", [(0.0, 0.0), (0.32, 0.0), (0.32, 0.1), (0.27, 0.14), (0.24, 0.2), (0.22, 0.5), (0.21, 1.2), (0.2, 1.85),
                            (0.24, 1.93), (0.3, 2.0), (0.32, 2.12), (0.32, 2.2), (0.0, 2.2)], "Stone", segs=32, wave=flute))
    own(r, lathe("Band", [(0.225, 1.55), (0.235, 1.6), (0.225, 1.65)], "Gold", segs=32))
    front_marker(r)
    export("pillar")


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
    export("brazier")


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
    export("stairs")


def spikes():
    r = prop_root()
    rnd = random.Random(2)
    own(r, bevel_box("Plate", (0.8, 0.8, 0.03), (0, 0, 0.015), "Dark", bevel=0.01))
    for x in (-0.25, 0, 0.25):
        for y in (-0.25, 0, 0.25):
            h = rnd.uniform(0.16, 0.26)
            own(r, tube("Spike", [(x, y, 0.02), (x + rnd.uniform(-0.02, 0.02), y, h)], [0.04, 0.0], "Metal", segs=4, smooth=False))
    front_marker(r)
    export("spikes")


def rocks():
    r = prop_root()
    for k, (x, y, s) in enumerate([(-0.2, -0.1, 0.18), (0.18, 0.12, 0.14), (0.05, -0.25, 0.1), (-0.05, 0.25, 0.12), (0.25, -0.15, 0.08)]):
        own(r, blob("Rock", s, (x, y, s * 0.4), "Stone", scale=(1, 0.9, 0.7), subdiv=1, lumps=0.25, seed=k * 3.1, faceted=True))
    front_marker(r)
    export("rocks")


def artifact():
    r = prop_root()
    crystal = blob("Crystal", 0.16, (0, 0, 0.55), "Glow", scale=(0.75, 0.75, 1.4), subdiv=1, faceted=True)
    pivot(crystal, (0, 0, 0.55))
    own(r, crystal)
    halo = lathe("Halo@Crystal", [(0.2, 0.54), (0.22, 0.55), (0.2, 0.56), (0.18, 0.55)], "Gold", segs=24)
    pivot(halo, (0, 0, 0.55))
    own(r, halo)
    for k in range(4):
        a = math.pi / 2 * k
        spike = tube("Prong@Crystal", [(math.cos(a) * 0.2, math.sin(a) * 0.2, 0.55), (math.cos(a) * 0.15, math.sin(a) * 0.15, 0.75)], [0.018, 0.0], "Gold", segs=5)
        pivot(spike, (0, 0, 0.55))
        own(r, spike)
    own(r, lathe("Base", [(0.0, 0.0), (0.22, 0.0), (0.24, 0.03), (0.2, 0.06), (0.0, 0.06)], "Gold", segs=16))
    front_marker(r)
    export("artifact")


if __name__ == "__main__":
    clear()
    builders = [archdemon, princess, crusader, ghoul, merchant_imp, beast, flyer, insect, golem, ooze,
                lambda: chest(False), lambda: chest(True), shrine, fountain, portal, throne, pillar, brazier, stairs, spikes, rocks, artifact]
    for build in builders:
        try:
            build()
        except Exception as e:
            import traceback
            traceback.print_exc()
            print("FAILED", getattr(build, "__name__", "?"), e)
            clear()
    print("DONE")
