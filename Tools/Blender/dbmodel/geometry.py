"""Modeling, rigging and export helpers shared by the Depthbound model builders."""
import bpy
import bmesh
import math
from mathutils import Matrix, Vector, Euler, noise

PALETTE = {
    "Primary": (0.55, 0.1, 0.1, 1), "Secondary": (0.3, 0.3, 0.3, 1), "Skin": (0.62, 0.16, 0.16, 1),
    "SkinPale": (0.96, 0.82, 0.86, 1), "Horn": (0.12, 0.09, 0.1, 1), "Eye": (1.0, 0.75, 0.2, 1),
    "Glow": (0.75, 0.3, 1.0, 1), "Metal": (0.66, 0.67, 0.72, 1), "Gold": (0.92, 0.72, 0.2, 1),
    "Wood": (0.42, 0.25, 0.12, 1), "Stone": (0.45, 0.42, 0.48, 1), "Cloth": (0.45, 0.06, 0.24, 1),
    "Dark": (0.07, 0.06, 0.08, 1), "Water": (0.25, 0.5, 0.9, 1), "Fire": (1.0, 0.5, 0.1, 1),
    "Hair": (0.16, 0.06, 0.2, 1), "White": (0.92, 0.92, 0.88, 1), "Wing": (0.3, 0.05, 0.12, 1),
    "Leather": (0.2, 0.12, 0.08, 1), "FaceDemon": (0.62, 0.16, 0.16, 1), "FacePrincess": (0.96, 0.82, 0.86, 1),
    "FaceImp": (0.7, 0.18, 0.14, 1), "HairPrincess": (0.24, 0.07, 0.3, 1), "Chain": (0.4, 0.41, 0.45, 1),
    "Lace": (0.95, 0.9, 0.95, 1), "Teeth": (0.95, 0.92, 0.82, 1), "Stocking": (0.12, 0.05, 0.16, 1),
    "Bone": (0.86, 0.82, 0.7, 1), "Fur": (0.24, 0.2, 0.22, 1), "Robe": (0.1, 0.3, 0.28, 1),
}
# Roles whose surfaces get a cube-projected UV for the detail textures painted in Unity.
DETAIL_ROLES = {"Robe", "Stocking", "Primary", "Secondary", "Metal", "Gold", "Cloth", "Leather", "Wood", "Stone", "Chain", "Wing", "Dark", "White", "Horn"}
MATS = {}


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
    for a in list(bpy.data.actions):
        bpy.data.actions.remove(a)


def link(ob):
    bpy.context.scene.collection.objects.link(ob)
    return ob


def select_only(ob):
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob


def cube_uv(ob, size=0.22):
    """Cube-project UVs (consistent texel density) so Unity's detail textures tile evenly."""
    try:
        select_only(ob)
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.cube_project(cube_size=size, correct_aspect=True, clip_to_bounds=False, scale_to_bounds=False)
        bpy.ops.object.mode_set(mode="OBJECT")
    except Exception as e:
        print("cube_uv failed:", e)
        if bpy.context.object and bpy.context.object.mode != "OBJECT":
            bpy.ops.object.mode_set(mode="OBJECT")


def finalize(ob, role, smooth=True):
    me = ob.data
    me.materials.clear()
    me.materials.append(mat(role))
    me.polygons.foreach_set("use_smooth", [smooth] * len(me.polygons))
    if role in DETAIL_ROLES and not me.uv_layers:
        cube_uv(ob)
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
    m = Matrix.Translation(Vector(loc)) @ Euler([math.radians(a) for a in rot]).to_matrix().to_4x4() @ Matrix.Diagonal(Vector(scale)).to_4x4()
    ob.data.transform(m)
    return ob


# =============================================================================================
# Shapes
# =============================================================================================

def tube(name, pts, radii, role, segs=10, flat=1.0, ref=(0, 0, 1), smooth=True, twist=0.0):
    """Tapered tube through points. radius 0 = pointed tip. flat < 1 squashes it into a ribbon."""
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
        tw = twist * i / max(1, n - 1)
        a, b = a * math.cos(tw) + b * math.sin(tw), b * math.cos(tw) - a * math.sin(tw)
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


def curve_pts(a, b, bend, count=5, sway=(0, 0, 0)):
    """Points along a quadratic curve from a to b, bulging by `bend` (a vector)."""
    a, b, bend, sway = Vector(a), Vector(b), Vector(bend), Vector(sway)
    out = []
    for i in range(count):
        t = i / (count - 1)
        out.append(a.lerp(b, t) + bend * (4 * t * (1 - t)) + sway * math.sin(t * math.pi * 2))
    return out


def lathe(name, profile, role, segs=24, wave=None, center=(0, 0, 0), smooth=True, arc=None):
    """Surface of revolution around Z. profile = [(radius, z)]; wave(angle, i) scales radius;
    arc=(a0, a1) in degrees makes an open partial revolution (capes, open overskirts)."""
    bm = bmesh.new()
    rings = []
    c = Vector(center)
    closed = arc is None
    a0, a1 = (0.0, 2 * math.pi) if closed else (math.radians(arc[0]), math.radians(arc[1]))
    count = segs if closed else segs + 1
    for i, (r, z) in enumerate(profile):
        if r <= 1e-5:
            rings.append([bm.verts.new(c + Vector((0, 0, z)))])
            continue
        ring = []
        for k in range(count):
            t = a0 + (a1 - a0) * k / segs
            rr = r * (wave(t, i) if wave else 1.0)
            ring.append(bm.verts.new(c + Vector((math.cos(t) * rr, math.sin(t) * rr, z))))
        rings.append(ring)
    span = segs if closed else segs
    for i in range(len(rings) - 1):
        A, B = rings[i], rings[i + 1]
        if len(A) == 1 and len(B) == 1:
            continue
        for k in range(span):
            k1 = (k + 1) % count
            if len(B) == 1:
                bm.faces.new((A[k], A[k1], B[0]))
            elif len(A) == 1:
                bm.faces.new((A[0], B[k1], B[k]))
            else:
                bm.faces.new((A[k], A[k1], B[k1], B[k]))
    if closed:
        if len(rings[0]) > 1:
            bm.faces.new(rings[0])
        if len(rings[-1]) > 1:
            bm.faces.new(rings[-1])
    ob = from_bm(name, bm, role, smooth)
    if not closed:
        s = ob.modifiers.new("Solid", "SOLIDIFY")
        s.thickness = 0.012
        s.offset = 0
        apply_modifiers(ob)
        finalize(ob, role, smooth)
    return ob


def bevel_box(name, size, loc, role, bevel=0.02, rot=(0, 0, 0), segments=2):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=segments, profile=0.5, affect="EDGES", clamp_overlap=True)
    ob = from_bm(name, bm, role, smooth=False)
    return xform(ob, loc, rot)


def prism(name, outline, depth, role, bevel=0.012, loc=(0, 0, 0), rot=(0, 0, 0)):
    """Extrude a 2D outline (x, z) along Y with bevelled edges."""
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


# =============================================================================================
# Rigging and export
# =============================================================================================

class Rig:
    def __init__(self):
        self.bones = []

    def add(self, name, head, tail, parent=None, deform=True):
        self.bones.append((name, Vector(head), Vector(tail), parent, deform))
        return name

    def get(self, name):
        return next(b for b in self.bones if b[0] == name)

    def has(self, name):
        return any(b[0] == name for b in self.bones)


def build_armature(rig):
    data = bpy.data.armatures.new("Armature")
    ob = link(bpy.data.objects.new("Armature", data))
    select_only(ob)
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
    """One bone name = rigid; a list = smooth blend of the two nearest bones."""
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


def export(path, animated=False):
    if not animated:   # props: bake AO across all their parts
        from . import shade
        bpy.context.view_layer.update()
        shade.bake_ao_many([o for o in bpy.context.scene.objects if o.type == "MESH"], dist=0.25)
    bpy.ops.object.select_all(action="DESELECT")
    for o in bpy.context.scene.objects:
        o.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"EMPTY", "MESH", "ARMATURE"},
        apply_scale_options="FBX_SCALE_ALL", bake_space_transform=False,
        axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE", add_leaf_bones=False,
        use_mesh_modifiers=True, use_armature_deform_only=False,
        bake_anim=animated, bake_anim_use_all_actions=animated, bake_anim_use_nla_strips=False,
        bake_anim_use_all_bones=True, bake_anim_force_startend_keying=True, bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0, armature_nodetype="NULL", path_mode="STRIP",
        colors_type="LINEAR", prioritize_active_color=True,
    )
    print("exported", path)
    clear()
