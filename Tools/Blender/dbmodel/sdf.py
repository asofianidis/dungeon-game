"""
Signed-distance-field sculpting for Depthbound's organic models.

A Field is a list of primitives (spheres, ellipsoids, tapered round cones, ribbons, shells of other
fields, custom functions) combined with smooth unions and subtractions. It is sampled on a voxel
grid, meshed with surface nets, relaxed and re-projected onto the true surface, decimated, and given
normals taken from the field gradient, so bodies come out as one seamless sculpted surface instead
of separate primitives. Faces are assigned material roles by whichever primitive they belong to.
"""
import math

import bmesh
import bpy
import numpy as np
from mathutils import Vector

BIG = 1.0e3


def _v(p):
    return np.asarray(tuple(p), dtype=np.float64)


def smin(a, b, k):
    if k <= 0:
        return np.minimum(a, b)
    h = np.clip(0.5 + 0.5 * (b - a) / k, 0.0, 1.0)
    return b + (a - b) * h - k * h * (1.0 - h)


def smax(a, b, k):
    return -smin(-a, -b, k)


# =============================================================================================
# Primitives: each returns (fn(x, y, z) -> distance, lo, hi). x, y, z are broadcastable arrays.
# =============================================================================================

def sphere(c, r):
    c = _v(c)
    return (lambda x, y, z: np.sqrt((x - c[0]) ** 2 + (y - c[1]) ** 2 + (z - c[2]) ** 2) - r), c - r, c + r


def ellipsoid(c, radii, axes=None):
    """axes: optional (ax, ay, az) world-space unit vectors of the local frame."""
    c, rr = _v(c), _v(radii)
    R = np.eye(3) if axes is None else np.array([_v(a) / np.linalg.norm(_v(a)) for a in axes]).T

    def f(x, y, z):
        dx, dy, dz = x - c[0], y - c[1], z - c[2]
        lx = (R[0, 0] * dx + R[1, 0] * dy + R[2, 0] * dz) / rr[0]
        ly = (R[0, 1] * dx + R[1, 1] * dy + R[2, 1] * dz) / rr[1]
        lz = (R[0, 2] * dx + R[1, 2] * dy + R[2, 2] * dz) / rr[2]
        k0 = np.sqrt(lx * lx + ly * ly + lz * lz)
        k1 = np.sqrt((lx / rr[0]) ** 2 + (ly / rr[1]) ** 2 + (lz / rr[2]) ** 2)
        return k0 * (k0 - 1.0) / np.maximum(k1, 1e-9)
    e = rr.max()
    return f, c - e, c + e


def round_cone(a, b, r1, r2, flat=None):
    """Capsule from a (radius r1) to b (radius r2). flat=(normal, factor) squashes it into a ribbon."""
    a, b = _v(a), _v(b)
    ba = b - a
    l2 = float(ba.dot(ba))
    if l2 < 1e-12:
        return sphere(a, max(r1, r2))
    rr = r1 - r2
    if l2 <= rr * rr * 1.0001:   # one end swallows the other
        return sphere(a, r1) if r1 > r2 else sphere(b, r2)
    a2 = l2 - rr * rr
    il2 = 1.0 / l2
    n = fk = None
    if flat is not None:
        n = _v(flat[0])
        n = n / np.linalg.norm(n)
        fk = 1.0 / max(flat[1], 1e-3) - 1.0

    def f(x, y, z):
        px, py, pz = x - a[0], y - a[1], z - a[2]
        if n is not None:
            d = (px * n[0] + py * n[1] + pz * n[2]) * fk
            px, py, pz = px + d * n[0], py + d * n[1], pz + d * n[2]
        yv = px * ba[0] + py * ba[1] + pz * ba[2]
        zv = yv - l2
        qx, qy, qz = px * l2 - ba[0] * yv, py * l2 - ba[1] * yv, pz * l2 - ba[2] * yv
        x2 = qx * qx + qy * qy + qz * qz
        y2 = yv * yv * l2
        z2 = zv * zv * l2
        k = math.copysign(1.0, rr) * rr * rr * x2
        d1 = np.sqrt(x2 + z2) * il2 - r2
        d2 = np.sqrt(x2 + y2) * il2 - r1
        d3 = (np.sqrt(np.maximum(x2 * a2 * il2, 0.0)) + yv * rr) * il2 - r1
        return np.where(np.sign(zv) * a2 * z2 > k, d1, np.where(np.sign(yv) * a2 * y2 < k, d2, d3))
    m = max(r1, r2)
    return f, np.minimum(a, b) - m, np.maximum(a, b) + m


def box(c, half, axes=None, round_=0.0):
    c, hb = _v(c), _v(half) - round_
    R = np.eye(3) if axes is None else np.array([_v(a) / np.linalg.norm(_v(a)) for a in axes]).T

    def f(x, y, z):
        dx, dy, dz = x - c[0], y - c[1], z - c[2]
        qx = np.abs(R[0, 0] * dx + R[1, 0] * dy + R[2, 0] * dz) - hb[0]
        qy = np.abs(R[0, 1] * dx + R[1, 1] * dy + R[2, 1] * dz) - hb[1]
        qz = np.abs(R[0, 2] * dx + R[1, 2] * dy + R[2, 2] * dz) - hb[2]
        out = np.sqrt(np.maximum(qx, 0) ** 2 + np.maximum(qy, 0) ** 2 + np.maximum(qz, 0) ** 2)
        return out + np.minimum(np.maximum(qx, np.maximum(qy, qz)), 0) - round_
    e = float(np.linalg.norm(_v(half)))
    return f, c - e, c + e


def torus(c, R, r, axis=(0, 0, 1)):
    c, n = _v(c), _v(axis) / np.linalg.norm(_v(axis))

    def f(x, y, z):
        dx, dy, dz = x - c[0], y - c[1], z - c[2]
        h = dx * n[0] + dy * n[1] + dz * n[2]
        rad = np.sqrt(np.maximum(dx * dx + dy * dy + dz * dz - h * h, 0.0))
        return np.sqrt((rad - R) ** 2 + h * h) - r
    e = R + r
    return f, c - e, c + e


def plane(p, n):
    """Half-space; negative on the side the normal points away from."""
    p, n = _v(p), _v(n) / np.linalg.norm(_v(n))
    return (lambda x, y, z: (x - p[0]) * n[0] + (y - p[1]) * n[1] + (z - p[2]) * n[2]), np.full(3, -BIG), np.full(3, BIG)


# =============================================================================================
# Noise (tileless value noise) for rocks, fur and tattered cloth
# =============================================================================================

_PERM = np.random.RandomState(7).permutation(256)
_PERM = np.concatenate([_PERM, _PERM]).astype(np.int64)
_VALS = np.random.RandomState(11).uniform(-1, 1, 256)


def _hash(i, j, k):
    return _VALS[_PERM[(_PERM[(_PERM[i & 255] + j) & 255] + k) & 255]]


def noise(x, y, z, freq=1.0, seed=0.0):
    x, y, z = np.broadcast_arrays(x * freq + seed * 17.3, y * freq + seed * 5.1, z * freq + seed * 11.7)
    xi, yi, zi = np.floor(x).astype(np.int64), np.floor(y).astype(np.int64), np.floor(z).astype(np.int64)
    fx, fy, fz = x - xi, y - yi, z - zi
    ux, uy, uz = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy), fz * fz * (3 - 2 * fz)
    out = 0.0
    for di in (0, 1):
        for dj in (0, 1):
            for dk in (0, 1):
                w = (ux if di else 1 - ux) * (uy if dj else 1 - uy) * (uz if dk else 1 - uz)
                out = out + w * _hash(xi + di, yi + dj, zi + dk)
    return out


def fbm(x, y, z, freq=1.0, octaves=3, seed=0.0):
    out, amp, norm = 0.0, 1.0, 0.0
    for o in range(octaves):
        out = out + noise(x, y, z, freq * (2 ** o), seed + o * 3.7) * amp
        norm += amp
        amp *= 0.5
    return out / norm


# =============================================================================================
# Fields
# =============================================================================================

class Group:
    def __init__(self, prims, op, k, role):
        self.prims = prims          # list of (fn, lo, hi), combined with a hard min
        self.op = op                # "union" | "subtract" | "intersect"
        self.k = k
        self.role = role
        self.lo = np.min([p[1] for p in prims], axis=0)
        self.hi = np.max([p[2] for p in prims], axis=0)

    def eval(self, x, y, z):
        d = None
        for fn, _, _ in self.prims:
            v = fn(x, y, z)
            d = v if d is None else np.minimum(d, v)
        return d


class Field:
    def __init__(self, role="Primary", h=0.006):
        self.role = role
        self.h = h
        self.groups = []
        self.paints = []
        self.post = []

    # --- building -----------------------------------------------------------------------------
    def add(self, prims, k=0.0, op="union", role=None):
        if isinstance(prims, tuple):
            prims = [prims]
        self.groups.append(Group(prims, op, k, role))
        return self

    def sphere(self, c, r, k=0.0, role=None, op="union"):
        return self.add(sphere(c, r), k, op, role)

    def ellipsoid(self, c, radii, k=0.0, role=None, axes=None, op="union"):
        return self.add(ellipsoid(c, radii, axes), k, op, role)

    def cone(self, a, b, r1, r2, k=0.0, role=None, flat=None, op="union"):
        return self.add(round_cone(a, b, r1, r2, flat), k, op, role)

    def chain(self, pts, radii, k=0.0, role=None, flat=None, op="union"):
        """Tapered tube through points (round cones joined at shared spheres)."""
        prims = []
        for i in range(len(pts) - 1):
            fl = flat[i] if isinstance(flat, list) else flat
            prims.append(round_cone(pts[i], pts[i + 1], radii[i], radii[i + 1], fl))
        return self.add(prims, k, op, role)

    def paint(self, region, role):
        """region(x, y, z) < 0 marks faces that take `role` (applied after primitive roles)."""
        self.paints.append((region, role))
        return self

    def displace(self, fn):
        """fn(x, y, z) is added to the distance near the surface (noise, fur, chisel)."""
        self.post.append(fn)
        return self

    # --- evaluation ---------------------------------------------------------------------------
    def bounds(self):
        us = [g for g in self.groups if g.op == "union"]
        lo = np.min([g.lo for g in us], axis=0)
        hi = np.max([g.hi for g in us], axis=0)
        return lo, hi

    def eval(self, x, y, z):
        """Evaluate at arbitrary broadcastable coordinates (no bounding-box culling)."""
        shape = np.broadcast(x, y, z).shape
        D = np.full(shape, BIG)
        for g in self.groups:
            d = g.eval(x, y, z)
            if g.op == "union":
                D = smin(D, d, g.k)
            elif g.op == "subtract":
                D = smax(D, -d, g.k)
            else:
                D = smax(D, d, g.k)
        for fn in self.post:
            D = D + fn(x, y, z)
        return D

    def grid(self, pad=None):
        h = self.h
        lo, hi = self.bounds()
        pad = pad if pad is not None else 3 * h
        lo, hi = lo - pad, hi + pad
        n = np.ceil((hi - lo) / h).astype(int) + 1
        xs, ys, zs = (lo[i] + np.arange(n[i]) * h for i in range(3))
        D = np.full((n[0], n[1], n[2]), BIG)
        for g in self.groups:
            if g.op == "intersect":
                D = smax(D, g.eval(xs[:, None, None], ys[None, :, None], zs[None, None, :]), g.k)
                continue
            m = 2.0 * g.k + 3 * h   # smooth blends reach beyond k where the distance is approximate
            i0, i1 = (np.searchsorted(a, v) for a, v in ((xs, g.lo[0] - m), (xs, g.hi[0] + m)))
            j0, j1 = (np.searchsorted(a, v) for a, v in ((ys, g.lo[1] - m), (ys, g.hi[1] + m)))
            k0, k1 = (np.searchsorted(a, v) for a, v in ((zs, g.lo[2] - m), (zs, g.hi[2] + m)))
            if i1 <= i0 or j1 <= j0 or k1 <= k0:
                continue
            d = g.eval(xs[i0:i1, None, None], ys[None, j0:j1, None], zs[None, None, k0:k1])
            sub = D[i0:i1, j0:j1, k0:k1]
            D[i0:i1, j0:j1, k0:k1] = smin(sub, d, g.k) if g.op == "union" else smax(sub, -d, g.k)
        if self.post:
            near = np.abs(D) < 0.05
            X, Y, Z = np.meshgrid(xs, ys, zs, indexing="ij")
            add = np.zeros_like(D)
            for fn in self.post:
                add = add + fn(X, Y, Z)
            D = np.where(near, D + add, D)
        return D, lo, h

    def roles_at(self, P):
        """Material role for each point: the nearest union group's role, then paint regions."""
        x, y, z = P[:, 0], P[:, 1], P[:, 2]
        best = np.full(len(P), BIG)
        role = np.array([self.role] * len(P), dtype=object)
        for g in self.groups:
            if g.op != "union":
                continue
            d = g.eval(x, y, z)
            take = d < best - 1e-9
            best = np.where(take, d, best)
            role[take] = g.role or self.role
        for region, r in self.paints:
            role[region(x, y, z) < 0] = r
        return role


# =============================================================================================
# Meshing
# =============================================================================================

_EDGES = [((0, 0, 0), (1, 0, 0)), ((0, 1, 0), (1, 1, 0)), ((0, 0, 1), (1, 0, 1)), ((0, 1, 1), (1, 1, 1)),
          ((0, 0, 0), (0, 1, 0)), ((1, 0, 0), (1, 1, 0)), ((0, 0, 1), (0, 1, 1)), ((1, 0, 1), (1, 1, 1)),
          ((0, 0, 0), (0, 0, 1)), ((1, 0, 0), (1, 0, 1)), ((0, 1, 0), (0, 1, 1)), ((1, 1, 0), (1, 1, 1))]


def surface_nets(D):
    """Quad mesh of the zero level set. Returns (verts in grid units, quads)."""
    inside = D < 0
    nx, ny, nz = D.shape
    anyin = np.zeros((nx - 1, ny - 1, nz - 1), bool)
    allin = np.ones_like(anyin)
    for i in (0, 1):
        for j in (0, 1):
            for k in (0, 1):
                c = inside[i:nx - 1 + i, j:ny - 1 + j, k:nz - 1 + k]
                anyin |= c
                allin &= c
    mixed = anyin & ~allin
    cells = np.argwhere(mixed)
    vid = np.full(mixed.shape, -1, np.int64)
    vid[mixed] = np.arange(len(cells))
    ci, cj, ck = cells.T
    acc = np.zeros((len(cells), 3))
    cnt = np.zeros(len(cells))
    for a, b in _EDGES:
        da = D[ci + a[0], cj + a[1], ck + a[2]]
        db = D[ci + b[0], cj + b[1], ck + b[2]]
        m = (da < 0) != (db < 0)
        t = np.where(m, da / np.where(m, da - db, 1.0), 0.0)
        pa, pb = np.array(a, float), np.array(b, float)
        acc += (pa[None, :] + t[:, None] * (pb - pa)[None, :]) * m[:, None]
        cnt += m
    verts = cells + acc / np.maximum(cnt, 1)[:, None]
    quads = []
    shape = D.shape
    for axis in range(3):
        u, v = [ax for ax in range(3) if ax != axis]
        sa = [slice(None)] * 3
        sb = [slice(None)] * 3
        sa[axis] = slice(0, -1)
        sb[axis] = slice(1, None)
        ia = inside[tuple(sa)]
        change = ia != inside[tuple(sb)]
        pts = np.argwhere(change)
        keep = (pts[:, u] >= 1) & (pts[:, u] <= shape[u] - 2) & (pts[:, v] >= 1) & (pts[:, v] <= shape[v] - 2)
        pts = pts[keep]
        flip = ia[tuple(pts.T)]

        def cell(du, dv):
            q = pts.copy()
            q[:, u] -= du
            q[:, v] -= dv
            return vid[tuple(q.T)]
        quad = np.stack([cell(1, 1), cell(0, 1), cell(0, 0), cell(1, 0)], 1)
        if axis == 1:
            flip = ~flip
        quad[flip] = quad[flip][:, ::-1]
        quads.append(quad)
    return verts, np.concatenate(quads)


def sample(D, P):
    """Trilinear sample of D (grid units) at points P, plus the gradient."""
    n = np.array(D.shape) - 1
    p = np.clip(P, 0, n - 1e-6)
    i0 = np.floor(p).astype(np.int64)
    t = p - i0
    i1 = np.minimum(i0 + 1, n)
    out = np.zeros(len(P))
    g = np.zeros((len(P), 3))
    for di in (0, 1):
        for dj in (0, 1):
            for dk in (0, 1):
                ii = i1[:, 0] if di else i0[:, 0]
                jj = i1[:, 1] if dj else i0[:, 1]
                kk = i1[:, 2] if dk else i0[:, 2]
                val = D[ii, jj, kk]
                wx = t[:, 0] if di else 1 - t[:, 0]
                wy = t[:, 1] if dj else 1 - t[:, 1]
                wz = t[:, 2] if dk else 1 - t[:, 2]
                out += val * wx * wy * wz
                g[:, 0] += val * (1 if di else -1) * wy * wz
                g[:, 1] += val * wx * (1 if dj else -1) * wz
                g[:, 2] += val * wx * wy * (1 if dk else -1)
    return out, g


def relax(verts, quads, D, iterations=3, factor=0.5):
    """Laplacian relaxation alternating with projection back onto the surface."""
    E = np.concatenate([quads[:, [0, 1]], quads[:, [1, 2]], quads[:, [2, 3]], quads[:, [3, 0]]])
    for _ in range(iterations):
        acc = np.zeros_like(verts)
        cnt = np.zeros(len(verts))
        np.add.at(acc, E[:, 0], verts[E[:, 1]])
        np.add.at(acc, E[:, 1], verts[E[:, 0]])
        np.add.at(cnt, E[:, 0], 1)
        np.add.at(cnt, E[:, 1], 1)
        avg = acc / np.maximum(cnt, 1)[:, None]
        verts = verts + factor * (avg - verts)
        for _ in range(2):
            d, g = sample(D, verts)
            gl2 = np.maximum((g * g).sum(1), 1e-9)
            verts = verts - (d / gl2)[:, None] * g
    return verts


def mesh(field, name, decimate=1.0, faceted=False, roles=True, smooth_iter=3, target=None):
    """Mesh a field into a Blender object with role materials and gradient normals.
    target: triangle budget; overrides `decimate`."""
    from .geometry import link, mat, apply_modifiers   # local import: geometry imports nothing from here
    D, lo, h = field.grid()
    gv, quads = surface_nets(D)
    if len(quads) == 0:
        raise RuntimeError("empty field: " + name)
    if smooth_iter:
        gv = relax(gv, quads, D, smooth_iter)
    verts = lo + gv * h
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts.tolist(), [], quads.tolist())
    me.update()
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=h * 0.05)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(me)
    bm.free()
    ob = link(bpy.data.objects.new(name, me))
    if target:
        decimate = min(1.0, target / max(1.0, 2.0 * len(me.polygons)))
    if decimate < 0.999:
        m = ob.modifiers.new("Dec", "DECIMATE")
        m.ratio = decimate
        m.use_collapse_triangulate = False
        apply_modifiers(ob)
    me = ob.data
    # Roles
    me.materials.clear()
    if roles:
        cent = np.array([tuple(p.center) for p in me.polygons]) if len(me.polygons) else np.zeros((0, 3))
        rs = field.roles_at(cent) if len(cent) else []
        names = [field.role] + sorted(set(rs) - {field.role})
    else:
        rs, names = [], [field.role]
    for r in names:
        me.materials.append(mat(r))
    if len(rs):
        index = {r: i for i, r in enumerate(names)}
        me.polygons.foreach_set("material_index", [index[r] for r in rs])
    me.polygons.foreach_set("use_smooth", [not faceted] * len(me.polygons))
    if not faceted:
        set_gradient_normals(ob, D, lo, h)
    return ob


def set_gradient_normals(ob, D, lo, h):
    me = ob.data
    co = np.zeros(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    P = (co.reshape(-1, 3) - lo) / h
    _, g = sample(D, P)
    g /= np.maximum(np.linalg.norm(g, axis=1), 1e-9)[:, None]
    # Fall back to the mesh normal where the grid gradient is unreliable (thin parts).
    vn = np.zeros(len(me.vertices) * 3)
    me.vertices.foreach_get("normal", vn)
    vn = vn.reshape(-1, 3)
    bad = (g * vn).sum(1) < 0.3
    g[bad] = vn[bad]
    me.normals_split_custom_set_from_vertices([Vector(n) for n in g])


def shell(src, offset, thickness, region=None, lo=None, hi=None):
    """Primitive: a skin of `src` (a Field) at `offset` from its surface, cut by region(x,y,z) < 0."""
    a, b = src.bounds()
    lo = _v(lo) if lo is not None else a - offset - thickness
    hi = _v(hi) if hi is not None else b + offset + thickness

    def f(x, y, z):
        d = np.abs(src.eval(x, y, z) - offset) - thickness
        if region is not None:
            d = np.maximum(d, region(x, y, z))
        return d
    return f, lo, hi


def slab(src, offset, region=None, lo=None, hi=None):
    """Primitive: `src` grown by offset (solid), cut by region. Good for armor plates and boots."""
    a, b = src.bounds()
    lo = _v(lo) if lo is not None else a - offset
    hi = _v(hi) if hi is not None else b + offset

    def f(x, y, z):
        d = src.eval(x, y, z) - offset
        if region is not None:
            d = np.maximum(d, region(x, y, z))
        return d
    return f, lo, hi


def zband(z0, z1):
    """Region between two heights."""
    return lambda x, y, z: np.maximum(z0 - z, z - z1)
