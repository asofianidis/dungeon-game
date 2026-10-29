"""
Finishing passes for sculpted models: per-vertex ambient occlusion and tint gradients baked into a
color attribute (multiplied into the albedo by Unity's Depthbound/Lit shader), automatic skin
weights, and weight transfer from the body to clothing and hair.
"""
import math

import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

TINT = "Tint"
COLOR = "Col"


def _co(ob):
    co = np.zeros(len(ob.data.vertices) * 3)
    ob.data.vertices.foreach_get("co", co)
    return co.reshape(-1, 3)


def _normals(ob):
    n = np.zeros(len(ob.data.vertices) * 3)
    ob.data.vertices.foreach_get("normal", n)
    return n.reshape(-1, 3)


def ensure_tint(ob):
    me = ob.data
    a = me.color_attributes.get(TINT)
    if a is None:
        a = me.color_attributes.new(TINT, "FLOAT_COLOR", "POINT")
        a.data.foreach_set("color", [1.0] * (4 * len(me.vertices)))
    return a


def tint(ob, fn):
    """fn(P: (N,3) array) -> (N,) or (N,3) multiplier; combined with any existing tint."""
    a = ensure_tint(ob)
    P = _co(ob)
    m = np.asarray(fn(P), dtype=np.float64)
    if m.ndim == 1:
        m = np.repeat(m[:, None], 3, 1)
    cur = np.zeros(len(P) * 4)
    a.data.foreach_get("color", cur)
    cur = cur.reshape(-1, 4)
    cur[:, :3] *= np.clip(m, 0.0, 1.0)
    a.data.foreach_set("color", cur.ravel())
    return ob


def gradient(axis_from, axis_to, lo, hi):
    """Tint function: lo at point axis_from, hi at axis_to (linear along the segment)."""
    a, b = np.array(axis_from, float), np.array(axis_to, float)
    ab = b - a
    L2 = max(ab.dot(ab), 1e-9)

    def fn(P):
        t = np.clip(((P - a) @ ab) / L2, 0, 1)
        return lo + (hi - lo) * t
    return fn


def _hemisphere(n, seed=3):
    rng = np.random.RandomState(seed)
    out = []
    golden = math.pi * (3 - math.sqrt(5))
    for i in range(n):
        u = (i + 0.5) / n
        r = math.sqrt(u)                      # cosine-weighted
        th = golden * i + rng.uniform(0, 0.2)
        out.append((r * math.cos(th), r * math.sin(th), math.sqrt(max(0.0, 1 - u))))
    return np.array(out)


def bake_ao_many(obs, **kw):
    """AO for a set of objects (e.g. a prop's parts) occluding each other, in world space."""
    verts, polys = [], []
    for o in obs:
        mw = np.array(o.matrix_world)
        P = _co(o)
        W = (np.c_[P, np.ones(len(P))] @ mw.T)[:, :3]
        base = len(verts)
        verts.extend(Vector(p) for p in W)
        polys.extend(tuple(base + i for i in p.vertices) for p in o.data.polygons)
    if not polys:
        return
    bvh = BVHTree.FromPolygons(verts, polys, epsilon=0.0)
    for o in obs:
        bake_ao(o, bvh=bvh, world=True, **kw)


def bake_ao(ob, rays=20, dist=0.2, strength=0.75, floor=0.35, bvh=None, world=False):
    """Per-vertex AO by ray casting against the whole (joined) model; writes the 'Col' attribute."""
    me = ob.data
    P = _co(ob)
    N = _normals(ob)
    if world:
        mw = np.array(ob.matrix_world)
        P = (np.c_[P, np.ones(len(P))] @ mw.T)[:, :3]
        N = N @ np.array(ob.matrix_world.to_3x3().inverted().transposed()).T
        N /= np.maximum(np.linalg.norm(N, axis=1), 1e-9)[:, None]
    if bvh is None:
        polys = [tuple(p.vertices) for p in me.polygons]
        bvh = BVHTree.FromPolygons([Vector(p) for p in P], polys, epsilon=0.0)
    H = _hemisphere(rays)
    ao = np.ones(len(P))
    up = np.array([0.0, 0.0, 1.0])
    for i in range(len(P)):
        n = N[i]
        t = np.cross(n, up if abs(n[2]) < 0.9 else np.array([1.0, 0, 0]))
        t /= max(np.linalg.norm(t), 1e-9)
        b = np.cross(n, t)
        o = Vector(P[i] + n * 0.0025)
        hits = 0.0
        for d in H:
            w = t * d[0] + b * d[1] + n * d[2]
            loc, _, _, hd = bvh.ray_cast(o, Vector(w), dist)
            if loc is not None:
                hits += 1.0 - (hd / dist) ** 2 * 0.6
        ao[i] = 1.0 - strength * hits / rays
    # One smoothing pass over mesh edges.
    E = np.zeros(len(me.edges) * 2, dtype=np.int64)
    me.edges.foreach_get("vertices", E)
    E = E.reshape(-1, 2)
    acc = ao.copy()
    cnt = np.ones(len(ao))
    np.add.at(acc, E[:, 0], ao[E[:, 1]])
    np.add.at(acc, E[:, 1], ao[E[:, 0]])
    np.add.at(cnt, E[:, 0], 1)
    np.add.at(cnt, E[:, 1], 1)
    ao = np.clip(acc / cnt, floor, 1.0)

    tint_attr = me.color_attributes.get(TINT)
    col = np.ones((len(P), 4))
    if tint_attr is not None:
        tc = np.zeros(len(P) * 4)
        tint_attr.data.foreach_get("color", tc)
        col = tc.reshape(-1, 4).copy()
    col[:, :3] *= ao[:, None]
    col[:, 3] = 1.0
    a = me.color_attributes.get(COLOR) or me.color_attributes.new(COLOR, "FLOAT_COLOR", "POINT")
    a.data.foreach_set("color", col.ravel())
    if tint_attr is not None:
        me.color_attributes.remove(tint_attr)
    me.color_attributes.active_color = me.color_attributes[COLOR]
    try:
        me.color_attributes.render_color_index = me.color_attributes.active_color_index
    except Exception:
        pass
    return ob


# =============================================================================================
# Skin weights
# =============================================================================================

def weights_array(ob, bones):
    """Dense (N, len(bones)) weights for an object's vertex groups."""
    W = np.zeros((len(ob.data.vertices), len(bones)))
    gi = {g.index: bones.index(g.name) for g in ob.vertex_groups if g.name in bones}
    for v in ob.data.vertices:
        for g in v.groups:
            j = gi.get(g.group)
            if j is not None:
                W[v.index, j] = g.weight
    return W


def write_weights(ob, bones, W, prune=0.01):
    for g in list(ob.vertex_groups):
        ob.vertex_groups.remove(g)
    W = np.where(W < prune, 0.0, W)
    s = W.sum(1)
    W = W / np.where(s > 0, s, 1)[:, None]
    for j, b in enumerate(bones):
        idx = np.nonzero(W[:, j] > 0)[0]
        if len(idx) == 0:
            continue
        g = ob.vertex_groups.new(name=b)
        col = W[:, j]
        # Group vertices by (rounded) weight to keep the number of API calls down.
        q = np.round(col[idx], 3)
        for w in np.unique(q):
            g.add(idx[q == w].tolist(), float(w), "REPLACE")


def heat_weights(body, arm, skip=()):
    """Blender's automatic (bone heat) weights for the body; bones in `skip` don't deform it."""
    saved = {}
    for b in arm.data.bones:
        saved[b.name] = b.use_deform
        if any(b.name.startswith(s) for s in skip):
            b.use_deform = False
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    for b in arm.data.bones:
        b.use_deform = saved[b.name]
    # Drop the parent/modifier parent_set added; finish_character joins and re-adds them.
    for m in list(body.modifiers):
        body.modifiers.remove(m)
    body.parent = None
    bones = [b.name for b in arm.data.bones if b.use_deform]
    W = weights_array(body, bones)
    missing = W.sum(1) < 1e-4
    return bones, W, missing


def transfer_weights(dst, sources, bones, allowed=None, k=6):
    """Give dst the inverse-distance-weighted average of the nearest source vertices' weights."""
    P = []
    Ws = []
    for src in sources:
        P.append(_co(src))
        Ws.append(weights_array(src, bones))
    P = np.concatenate(P)
    W = np.concatenate(Ws)
    if allowed is not None:
        mask = np.array([b in allowed for b in bones])
        W = W * mask[None, :]
        keep = W.sum(1) > 1e-6
        P, W = P[keep], W[keep]
    kd = KDTree(len(P))
    for i, p in enumerate(P):
        kd.insert(Vector(p), i)
    kd.balance()
    D = _co(dst)
    out = np.zeros((len(D), len(bones)))
    for i, p in enumerate(D):
        acc = np.zeros(len(bones))
        tot = 0.0
        for _, j, d in kd.find_n(Vector(p), k):
            w = 1.0 / (d + 0.004) ** 2
            acc += W[j] * w
            tot += w
        out[i] = acc / max(tot, 1e-9)
    write_weights(dst, bones, out)
    return dst


def nearest_bone_weights(P, rig, bones):
    """Smooth blend of the two nearest bone segments (fallback where bone heat fails)."""
    segs = [(np.array(rig.get(b)[1]), np.array(rig.get(b)[2])) for b in bones]
    D = np.zeros((len(P), len(bones)))
    for j, (a, c) in enumerate(segs):
        ab = c - a
        t = np.clip(((P - a) @ ab) / max(ab @ ab, 1e-9), 0, 1)
        D[:, j] = np.linalg.norm(P - (a + t[:, None] * ab), axis=1)
    order = np.argsort(D, axis=1)[:, :2]
    W = np.zeros_like(D)
    rows = np.arange(len(P))
    d0, d1 = D[rows, order[:, 0]], D[rows, order[:, 1]]
    w0, w1 = 1.0 / (d0 + 0.004) ** 3, 1.0 / (d1 + 0.004) ** 3
    far = d1 > d0 * 3.0
    w1 = np.where(far, 0.0, w1)
    W[rows, order[:, 0]] = w0 / (w0 + w1)
    W[rows, order[:, 1]] += w1 / (w0 + w1)
    return W


def rigid(ob, bone):
    for g in list(ob.vertex_groups):
        ob.vertex_groups.remove(g)
    g = ob.vertex_groups.new(name=bone)
    g.add([v.index for v in ob.data.vertices], 1.0, "REPLACE")
    return ob


def weight_fn(ob, bones, fn):
    """fn(P) -> (N, len(bones)) weights."""
    write_weights(ob, bones, np.asarray(fn(_co(ob))))
    return ob
