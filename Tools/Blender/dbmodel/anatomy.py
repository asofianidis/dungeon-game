"""
Sculpted anatomy for Depthbound's characters, built on dbmodel.sdf fields.

Humanoids are about six heads tall and stand in a slight A-pose (arms `spread` degrees from the
body, so the arms never fuse with the hips). The body is one field: pelvis, glutes, waist, rib
cage, chest or bust, trapezius, deltoids, neck, tapered limbs with muscle bulges, knees and feet.
Heads, hands, hair and horns are separate finer fields; wings are bones plus billowed membranes.
"""
import math

import bpy
import numpy as np
from mathutils import Vector

from . import sdf
from .geometry import cube_uv, finalize, link, mat, DETAIL_ROLES

Y = Vector((0, 1, 0))


def V(*a):
    return Vector(a)


def joints(H, female=False, spread=14.0, hunch=0.0, bulk=1.0):
    """Joint positions (keys match geometry.Rig layouts used by humanoid_rig)."""
    sx = (0.088 if female else 0.104) * H * (0.85 + 0.15 * bulk)
    hx = (0.056 if female else 0.052) * H * (0.9 + 0.1 * bulk)
    J = {
        "hips": V(0, 0, 0.53 * H), "waist": V(0, -0.002 * H, 0.6 * H), "spine": V(0, -0.004 * H - hunch * 0.3 * H, 0.65 * H),
        "chest": V(0, -0.006 * H - hunch * H, 0.71 * H), "neck": V(0, -0.004 * H - hunch * 1.5 * H, 0.83 * H),
        "headBase": V(0, -0.004 * H - hunch * 1.7 * H, 0.865 * H),
    }
    a = math.radians(spread)
    for s, side in ((1, "L"), (-1, "R")):
        sh = V(s * sx, J["chest"].y * 0.6, 0.795 * H - hunch * 0.15 * H)
        el = sh + V(s * math.sin(a) * 0.175 * H, 0.012 * H, -math.cos(a) * 0.175 * H)
        wr = el + V(s * math.sin(a) * 0.15 * H * 0.85, -0.016 * H, -math.cos(a) * 0.15 * H)
        d = (wr - el).normalized()
        J["shoulder" + side] = sh
        J["elbow" + side] = el
        J["wrist" + side] = wr
        J["hand" + side] = wr + d * 0.05 * H
        J["hip" + side] = V(s * hx, 0.0, 0.49 * H)
        J["knee" + side] = V(s * hx * 0.92, -0.008 * H, 0.275 * H)
        J["ankle" + side] = V(s * hx * 0.88, 0.012 * H, 0.046 * H)
        J["toe" + side] = V(s * hx * 0.95, -0.075 * H, 0.014 * H)
    J["H"] = H
    return J


def lerp(a, b, t):
    return a + (b - a) * t


def body_field(J, role, female=False, bulk=1.0, gaunt=False, bust=1.0, belly=0.0, h=None, legs=True, feet=True, arms=True, torso=True, pecs=True):
    """The body (no head or hands) as one smooth field. torso/arms/legs select parts, so garments
    can be shaped as shells of just the torso or just the legs."""
    H = J["H"]
    f = female
    t = 0.62 if gaunt else 1.0         # torso thickness
    lt = 0.72 if gaunt else 1.0        # limb thickness
    B = bulk
    F = sdf.Field(role, h or 0.0062 * H)
    kt = 0.032 * H
    if torso:
        F.ellipsoid(J["hips"] + V(0, 0.004 * H, -0.005 * H), (V(0.098, 0.066, 0.06) if f else V(0.08, 0.06, 0.058)) * H * B * t)
        for s in (1, -1):
            g = V(0.056, 0.048, 0.058) if f else V(0.042, 0.04, 0.046)
            F.ellipsoid(J["hips"] + V(s * (0.046 if f else 0.042) * H, 0.032 * H, -0.035 * H), g * H * B * t, k=0.02 * H)
        F.ellipsoid(J["waist"], (V(0.068, 0.05, 0.07) if f else V(0.084, 0.058, 0.075)) * H * B * t, k=kt)
        if belly:
            F.ellipsoid(J["waist"] + V(0, -0.02 * H * belly, -0.01 * H), V(0.085, 0.075, 0.08) * H * belly * B, k=kt)
        F.ellipsoid(J["chest"] + V(0, 0.004 * H, -0.012 * H), (V(0.08, 0.058, 0.085) if f else V(0.104, 0.066, 0.1)) * H * B * t, k=kt)
        if f:
            for s in (1, -1):
                r = 0.04 * H * bust
                c = J["chest"] + V(s * 0.039 * H, -0.028 * H - r * 0.3, -0.014 * H)
                ax = V(s, -0.3, 0).normalized()
                F.ellipsoid(c, (r * 1.06, r * 0.8, r * 0.9), k=0.034 * H,
                            axes=(ax, V(0, 0, 1).cross(ax).normalized() * -1, V(0, 0, 1)))
        elif not gaunt:
            for s in (1, -1):
                F.ellipsoid(J["chest"] + V(s * 0.046 * H, -0.044 * H * B, 0.03 * H), V(0.052, 0.026, 0.038) * H * B, k=0.02 * H)
            for k in range(3):   # abdominal definition
                for s in (1, -1):
                    F.ellipsoid(J["waist"] + V(s * 0.02 * H, -0.05 * H * B, 0.045 * H - k * 0.032 * H), V(0.018, 0.012, 0.014) * H * B, k=0.012 * H)
        F.ellipsoid(V(0, J["chest"].y + 0.012 * H, 0.79 * H), (V(0.08, 0.045, 0.036) if f else V(0.1, 0.052, 0.046)) * H * B, k=kt)
        neck_r = (0.031 if f else 0.042) * H * (0.8 if gaunt else 1.0) * (0.9 + 0.1 * B)
        F.cone(V(0, J["chest"].y + 0.008 * H, 0.79 * H), J["headBase"] + V(0, 0.004 * H, 0.012 * H), neck_r, neck_r * 0.88, k=0.022 * H)
    for s, side in ((1, "L"), (-1, "R")):
        sh, el, wr = J["shoulder" + side], J["elbow" + side], J["wrist" + side]
        if arms:
            if torso:   # clavicle
                F.cone(V(s * 0.02 * H, J["chest"].y - 0.012 * H, 0.79 * H), sh + V(0, -0.005 * H, 0.008 * H), 0.016 * H * B, 0.026 * H * B, k=0.02 * H)
            dr = (V(0.036, 0.04, 0.038) if f else V(0.048, 0.05, 0.047)) * H * B * lt
            F.ellipsoid(sh + V(s * 0.004 * H, 0.0, -0.004 * H), dr, k=0.026 * H)
            ar = [0.031, 0.027, 0.02, 0.0225, 0.0155] if f else [0.04, 0.039, 0.029, 0.034, 0.022]
            ar = [r * H * B * lt for r in ar]
            bic = lerp(sh, el, 0.45) + V(0, (-0.006 if not f else 0) * H, 0)
            fa = lerp(el, wr, 0.3)
            F.chain([sh + V(0, 0, -0.02 * H), bic, el, fa, wr], ar, k=0.016 * H)
        if not legs:
            continue
        hp, kn, an, to = J["hip" + side], J["knee" + side], J["ankle" + side], J["toe" + side]
        lr = [0.066, 0.06, 0.049, 0.034, 0.037, 0.027, 0.019] if f else [0.064, 0.06, 0.05, 0.037, 0.041, 0.031, 0.022]
        lr = [r * H * B * lt for r in lr]
        pts = [hp + V(s * 0.004 * H, 0, 0.01 * H), lerp(hp, kn, 0.25), lerp(hp, kn, 0.62), kn,
               lerp(kn, an, 0.28) + V(0, 0.012 * H, 0), lerp(kn, an, 0.62) + V(0, 0.004 * H, 0), an]
        F.chain(pts, lr, k=0.022 * H)
        F.ellipsoid(kn + V(0, -0.022 * H * lt, 0.004 * H), V(0.022, 0.014, 0.024) * H * B * lt, k=0.012 * H)
        if feet:
            fr = 0.021 * H * B * lt
            F.sphere(an + V(0, 0.022 * H, -0.024 * H), fr * 1.05, k=0.014 * H)
            F.chain([an + V(0, 0, -0.012 * H), to + V(0, 0.01 * H, 0.004 * H), to + V(0, -0.038 * H, -0.004 * H)],
                    [fr, fr * 0.85, fr * 0.55], k=0.014 * H, flat=(V(0, 0, 1), 0.62))
    return F


# =============================================================================================
# Hands
# =============================================================================================

def hand(J, side, role, scale=1.0, claws=None, fist=0.0, h=None):
    """A relaxed hand (palm toward the thigh) with jointed fingers; claws=role adds talons."""
    H = J["H"]
    s = 1 if side == "L" else -1
    w = J["wrist" + side]
    d = (J["hand" + side] - w).normalized()
    nin = -s * d.cross(Y)
    nin.normalize()
    across = nin.cross(d).normalized()
    if across.y > 0:
        across = -across          # index finger toward the front (-Y)
    L = 0.1 * H * scale
    F = sdf.Field(role, h or 0.0016 * H * scale)
    F.ellipsoid(w + d * 0.27 * L + nin * 0.01 * L, (0.27 * L, 0.2 * L, 0.085 * L), axes=(d, across, nin))
    F.ellipsoid(w + d * 0.1 * L - across * 0.12 * L + nin * 0.04 * L, (0.13 * L, 0.08 * L, 0.07 * L), k=0.05 * L, axes=(d, across, nin))
    curl = [10 + 50 * fist, 25 + 60 * fist, 35 + 50 * fist]
    for i, (off, ln) in enumerate(zip((-0.16, -0.053, 0.053, 0.16), (0.4, 0.44, 0.41, 0.33))):
        fan = math.radians((i - 1.5) * 4)
        base = w + d * 0.5 * L + across * off * L
        dirv = (d * math.cos(fan) + across * math.sin(fan)).normalized()
        pts, cur = [base], base
        for seg, c in zip((0.42, 0.33, 0.25), curl):
            a = math.radians(c)
            sd = (dirv * math.cos(a) + nin * math.sin(a)).normalized()
            cur = cur + sd * ln * seg * L
            pts.append(cur)
        F.chain(pts, [0.05 * L, 0.046 * L, 0.041 * L, 0.033 * L], k=0.012 * L)
        if claws:
            tipd = (pts[-1] - pts[-2]).normalized()
            F.cone(pts[-1] - tipd * 0.02 * L, pts[-1] + tipd * 0.22 * L + nin * 0.06 * L, 0.035 * L, 0.002 * L, k=0.01 * L, role=claws)
    tb = w + d * 0.15 * L - across * 0.17 * L + nin * 0.05 * L
    tdir = (d * 0.55 - across * 0.45 + nin * 0.55).normalized()
    t1 = tb + tdir * 0.22 * L
    t2 = t1 + (tdir + nin * 0.4 + d * 0.3).normalized() * 0.18 * L
    F.chain([tb, t1, t2], [0.06 * L, 0.05 * L, 0.04 * L], k=0.02 * L)
    if claws:
        F.cone(t2, t2 + (t2 - t1).normalized() * 0.18 * L, 0.035 * L, 0.002 * L, k=0.01 * L, role=claws)
    return F


# =============================================================================================
# Heads
# =============================================================================================

def head_field(c, r, role, ears=1.0, jaw=1.0, nose=1.0, chin=1.0, h=None, ear_role=None):
    """An anime head centered at c with cranium radius r. Pass ears=0 for none."""
    F = sdf.Field(role, h or r * 0.032)
    # Cranium (top at +1.08r) and the back of the skull.
    F.sphere(c + V(0, 0.06 * r, 0.14 * r), 0.94 * r)
    F.ellipsoid(c + V(0, 0.22 * r, 0.05 * r), (0.86 * r, 0.8 * r, 0.85 * r), k=0.2 * r)
    # Face: wide at the cheekbones, receding along a V-shaped jawline to a small chin at -r.
    F.ellipsoid(c + V(0, -0.22 * r, -0.2 * r), (0.82 * r, 0.7 * r, 0.62 * r), k=0.2 * r)
    F.ellipsoid(c + V(0, -0.3 * r, -0.56 * r * chin), (0.52 * r * jaw, 0.55 * r, 0.44 * r * chin), k=0.22 * r)
    chin_p = c + V(0, -0.6 * r, -0.94 * r * chin)
    for s in (1, -1):
        F.cone(c + V(s * 0.66 * r * jaw, 0.05 * r, -0.35 * r), chin_p + V(s * 0.08 * r, 0.02 * r, 0.02 * r), 0.2 * r, 0.1 * r, k=0.15 * r)
        F.ellipsoid(c + V(s * 0.45 * r, -0.45 * r, -0.42 * r), (0.28 * r, 0.24 * r, 0.26 * r), k=0.16 * r)
    F.sphere(chin_p, 0.1 * r, k=0.16 * r)
    if nose:
        F.cone(c + V(0, -0.87 * r, -0.2 * r), c + V(0, -0.95 * r, -0.38 * r), 0.03 * r * nose, 0.055 * r * nose, k=0.06 * r)
    F.cone(c + V(0, 0.2 * r, -0.6 * r), c + V(0, 0.22 * r, -1.4 * r), 0.3 * r, 0.3 * r, k=0.05 * r)
    if ears:
        for s in (1, -1):
            base = c + V(s * 0.84 * r, 0.1 * r, -0.16 * r)
            tip = c + V(s * (1.15 + 0.45 * ears) * r, (0.28 + 0.2 * ears) * r, (0.2 + 0.32 * ears) * r)
            mid = lerp(base, tip, 0.45) + V(s * 0.04 * r, 0.04 * r, -0.04 * r)
            n = V(s * 0.25, -1, 0.15).normalized()
            F.chain([base, mid, tip], [0.15 * r, 0.11 * r, 0.012 * r], k=0.06 * r, flat=(n, 0.32), role=ear_role)
    return F


def face_uv(ob, c, r):
    """Front-projected face UV (matches FaceFactory: u = 0.5 + x/2.04r, v = 0.5 + z/2.12r)."""
    me = ob.data
    uv = me.uv_layers.get("UVMap") or me.uv_layers.new(name="UVMap")
    for p in me.polygons:
        cen = p.center
        front = cen.y < c.y - 0.12 * r
        for li in p.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            if front:
                uv.data[li].uv = (0.5 + (co.x - c.x) / (2.04 * r), 0.5 + (co.z - c.z) / (2.12 * r))
            else:
                uv.data[li].uv = (0.02, 0.02)
    return ob


def with_uv(ob):
    """Cube-project UVs if any of the object's materials uses a detail texture."""
    if any(m and m.name.split(".")[0] in DETAIL_ROLES for m in ob.data.materials) and not ob.data.uv_layers:
        cube_uv(ob)
    return ob


def make(F, name, faces=None, decimate=1.0, faceted=False):
    """Mesh a field. faces: target polygon budget (decimates down to roughly this many)."""
    return with_uv(sdf.mesh(F, name, decimate=decimate, faceted=faceted, target=faces))


# =============================================================================================
# Hair
# =============================================================================================

def _slerp(a, b, t):
    a, b = a.normalized(), b.normalized()
    d = max(-1.0, min(1.0, a.dot(b)))
    th = math.acos(d)
    if th < 1e-4:
        return a
    return (a * math.sin((1 - t) * th) + b * math.sin(t * th)) / math.sin(th)


class Hair:
    """Sculpted anime hair: a scalp mass and flattened locks that flow over the skull and fall."""

    def __init__(self, c, r, role, h=None, k=None):
        self.c = c + V(0, 0.05 * r, 0.08 * r)   # cranium center
        self.r = r
        self.R = r * 1.08
        self.F = sdf.Field(role, h or r * 0.034)
        self.k = k if k is not None else 0.035 * r

    def scalp(self, hairline=0.45, fringe_depth=0.75, sides=1.0):
        c, r = self.c, self.r
        self.F.ellipsoid(c + V(0, 0.05 * r, -0.02 * r), (self.R * sides, self.R * 1.03, self.R))
        # Open the face below the hairline.
        self.F.ellipsoid(c + V(0, -0.9 * r, -0.25 * r - (0.45 - hairline) * r), (0.8 * r, fringe_depth * r, 0.8 * r), k=0.06 * r, op="subtract")
        self.F.ellipsoid(c + V(0, -0.35 * r, -1.05 * r), (1.0 * r, 0.8 * r, 0.62 * r), k=0.08 * r, op="subtract")
        return self

    def fringe(self, bottom=0.05, teeth=7, depth=0.28, part=0.0, thick=0.07, swoop=0.0, grooves=False):
        """Bangs as one sculpted sheet over the forehead with a pointed, jagged edge.
        bottom: height of the tips (in r, relative to the cranium center); depth: tooth length."""
        c, r, R = self.c, self.r, self.R
        shell_r = R * 1.015

        def jag(x):
            u = (x / (1.6 * r) + 0.5) * teeth + part + swoop * (x / r)
            tri = 1.0 - np.abs(2.0 * (u - np.floor(u)) - 1.0)
            return tri

        def f(x, y, z):
            dx, dy, dz = x - c.x, y - c.y, z - c.z
            rad = np.sqrt(dx * dx + dy * dy * 0.92 + dz * dz)
            d = np.abs(rad - shell_r) - thick * r
            front = dy + 0.18 * r                       # only the front of the head
            zc = (bottom + depth * (1 - jag(x)) - 0.12 * (dx / r) ** 2) * r
            cut = zc - dz                               # keep above the jagged edge
            return np.maximum(np.maximum(d, front), cut)
        lo = np.array([c.x - 1.3 * r, c.y - 1.3 * r, c.z - 0.6 * r])
        hi = np.array([c.x + 1.3 * r, c.y + 0.2 * r, c.z + 1.3 * r])
        self.F.add((f, lo, hi), k=0.03 * r)
        if grooves:
            for i in range(teeth + 1):
                u = (i - part) / teeth
                x = (u - 0.5) * 1.6 * r
                top = c + V(x * 0.35, -0.55 * r, 1.0 * r)
                low = c + V(x * 1.05, -1.06 * r, (bottom + depth * 0.9) * r)
                mid = c + (top.lerp(low, 0.5) - c).normalized() * R * 1.05
                self.F.chain([top, mid, low], [0.02 * r, 0.03 * r, 0.025 * r], k=0.02 * r, op="subtract")
        return self

    def mass(self, length, width=1.0, flare=0.2, depth=0.12, teeth=9, tooth=0.12, front_cut=0.15, thick=0.22):
        """Long back hair: a curved sheet from the skull down to `length` below the head center,
        flaring out, with a jagged hem of pointed locks."""
        c, r, R = self.c, self.r, self.R
        L = length

        def f(x, y, z):
            dz = c.z - z                          # distance below the head center
            t = np.clip(dz / L, 0, 1)
            spread = R * (1.0 + flare * t) * width
            dx, dy = x - c.x, y - (c.y + 0.12 * r + 0.25 * r * t)
            ang = np.arctan2(dx, dy)              # 0 = straight back
            rad = np.sqrt(dx * dx * (0.95 / width) ** 2 + dy * dy)
            lim = math.pi * (0.6 - front_cut) * (1 - 0.3 * t)
            edge = np.sqrt(np.clip(1 - (ang / lim) ** 2, 0, 1))   # lens-shaped cross-section
            d = np.abs(rad - spread) - thick * r * (1 - 0.4 * t) * (0.3 + 0.7 * edge)
            d = d - 0.035 * r * np.cos(ang * 9)   # soft lock grooves
            side = (np.abs(ang) - lim) * R
            u = ang / (2 * math.pi) * teeth * 2 + 0.5
            tri = 1.0 - np.abs(2.0 * (u - np.floor(u)) - 1.0)
            bottom = (z - (c.z - L - tooth * L * tri))
            top = z - c.z
            out = np.maximum(d, np.maximum(side, -bottom))
            return np.maximum(out, top)
        lo = np.array([c.x - 2.0 * R, c.y - 1.4 * R, c.z - L * 1.2])
        hi = np.array([c.x + 2.0 * R, c.y + 2.4 * R, c.z + 0.2 * r])
        self.F.add((f, lo, hi), k=0.08 * r)
        return self

    def lock(self, root_dir, over_dir, drop, width, thick=0.36, length=0.0, sway=V(0, 0, 0), curl=0.0, taper=0.05, n=7, lift=0.04, normal=None):
        """A lock rooted at root_dir on the skull, flowing over the surface to over_dir, then
        falling `drop` (a vector) with optional sway/curl. width in units of r. normal fixes the
        flattening direction of the falling part (default: outward from the head)."""
        c, R, r = self.c, self.R, self.r
        pts, norms = [], []
        m = 4
        for i in range(m):
            t = i / (m - 1)
            dirv = _slerp(V(*root_dir), V(*over_dir), t)
            rad = R * (1.0 + lift + 0.05 * math.sin(math.pi * t))
            pts.append(c + dirv * rad)
            norms.append(dirv)
        if drop.length > 1e-6:
            start = pts[-1]
            out = norms[-1].copy()
            out.z = 0
            out = out.normalized() if out.length > 1e-4 else V(0, 1, 0)
            if normal is not None:
                out = Vector(normal).normalized()
            for i in range(1, n + 1):
                t = i / n
                p = start + drop * t + sway * math.sin(t * math.pi) + out * (curl * r * math.sin(t * math.pi * 0.5))
                pts.append(p)
                norms.append(out)
        radii = []
        N = len(pts)
        for i in range(N):
            t = i / (N - 1)
            radii.append(max(0.004 * r, width * r * (1 - (1 - taper) * t ** 1.5)))
        radii[-1] = 0.006 * r
        flats = [(norms[i], thick) for i in range(N - 1)]
        self.F.chain(pts, radii, k=self.k, flat=flats)
        return self

    def bang(self, x, length=1.0, width=0.2, swoop=0.0, part=0.0):
        """A fringe lock from the crown to the forehead; x in [-1, 1] across the face."""
        c, r, R = self.c, self.r, self.R
        root = V(x * 0.35 + part, -0.35, 0.94)
        tip_z = 0.36 - 0.42 * length
        tip = c + V((x * 0.62 + swoop) * r, -1.0 * r * (1.05 - 0.15 * abs(x)), tip_z * r)
        pts, norms = [], []
        rootp = c + root.normalized() * R * 1.03
        for i in range(6):
            t = i / 5
            p = rootp.lerp(tip, t)
            dirv = (p - c).normalized()
            rad = max(R * (1.04 + 0.04 * math.sin(math.pi * t)), (p - c).length)
            p = c + dirv * rad
            pts.append(p)
            norms.append(dirv)
        radii = [width * r * k for k in (0.8, 1.0, 0.95, 0.75, 0.42, 0.04)]
        self.F.chain(pts, radii, k=self.k * 0.6, flat=[(n, 0.34) for n in norms[:-1]])
        return self

    def strand(self, pts, radii, flat=None):
        self.F.chain(pts, radii, k=self.k, flat=flat)
        return self


# =============================================================================================
# Horns, tails, wings
# =============================================================================================

def horn_points(base, side, length, curl=1.0, back=0.6, rise=1.0):
    pts = []
    for i in range(12):
        t = i / 11
        ang = t * 2.2 * curl
        pts.append(base + V(side * (0.15 * t + 0.06 * math.sin(ang)) * length,
                            (0.55 * t - 0.25 * math.sin(ang * 0.8)) * length * back,
                            (0.55 * math.sin(ang * 0.7) + 0.2 * t) * length * rise))
    return pts


def horn(F, base, side, length, thickness, curl=1.0, back=0.6, rise=1.0, ridges=True, role=None):
    pts = horn_points(base, side, length, curl, back, rise)
    radii = []
    for i in range(len(pts)):
        t = i / (len(pts) - 1)
        rr = thickness * (1 - t) ** 0.85 + 0.0015
        radii.append(rr)
    F.chain(pts, radii, k=thickness * 0.15, role=role)
    if ridges:
        for i in range(1, 9, 2):
            t = i / 11
            F.sphere(pts[i], radii[i] * 1.1, k=radii[i] * 0.35, role=role)
    return pts


def tail(F, root, length, radius, role=None, tip_role=None, heart=True, sway=0.08, H=1.0):
    pts, radii = [], []
    for i in range(10):
        t = i / 9
        pts.append(root + V(sway * math.sin(t * 3.0), length * (0.35 * t + 0.4 * t * t), -length * 0.55 * t + length * 0.45 * t ** 3))
        radii.append(radius * (1 - t * 0.75))
    F.chain(pts, radii, k=radius * 0.6, role=role)
    tip = pts[-1]
    d = (pts[-1] - pts[-2]).normalized()
    side = d.cross(V(0, 0, 1)).normalized() if abs(d.z) < 0.95 else V(1, 0, 0)
    n = side.cross(d).normalized()
    w = radius * (2.4 if heart else 2.0)
    if heart:
        for s in (1, -1):
            ax = (d + side * s * 0.6).normalized()
            F.ellipsoid(tip + d * w * 0.55 + side * s * w * 0.45, (w * 0.62, w * 0.55, w * 0.16), k=w * 0.15, role=tip_role,
                        axes=(ax, n.cross(ax).normalized(), n))
        F.cone(tip + d * w * 0.6, tip + d * w * 1.9, w * 0.72, 0.003, k=w * 0.2, role=tip_role, flat=(n, 0.24))
    else:
        F.cone(tip, tip + d * w * 1.6, w * 0.7, 0.003, k=w * 0.2, role=tip_role, flat=(n, 0.3))
    return pts


def wing(root, side, span, bone_role, membrane_role, droop=0.0, h=None, bone_r=0.02):
    """Bat wing: (bones field, membrane object, wrist, finger tip). Rest pose spreads up and out."""
    s = side
    root = Vector(root)
    wrist = root + V(s * span * 0.36, span * 0.2, span * 0.3)
    elbow = lerp(root, wrist, 0.5) + V(0, span * 0.06, -span * 0.04)
    tips = [wrist + V(s * span * 0.6, span * 0.12, span * 0.34 - droop),
            wrist + V(s * span * 0.66, span * 0.14, -span * 0.06 - droop),
            wrist + V(s * span * 0.4, span * 0.12, -span * 0.4 - droop)]
    low = root + V(s * span * 0.05, span * 0.1, -span * 0.42)
    F = sdf.Field(bone_role, h or span * 0.007)
    F.chain([root, elbow, wrist], [bone_r * 1.15, bone_r, bone_r * 0.9], k=bone_r * 0.5)
    F.sphere(wrist, bone_r * 1.25, k=bone_r * 0.5)
    F.cone(wrist, wrist + V(s * 0.012, -0.02, span * 0.1), bone_r * 0.8, 0.001, k=bone_r * 0.3)
    fingers = []
    for tp in tips:
        mid = lerp(wrist, tp, 0.5) + V(0, 0, span * 0.04)
        F.chain([wrist, mid, tp], [bone_r * 0.75, bone_r * 0.55, 0.002], k=bone_r * 0.3)
        F.sphere(mid, bone_r * 0.62, k=bone_r * 0.3)
        fingers.append((wrist, mid, tp))
    mem = membrane_mesh(root, elbow, wrist, fingers, low, s, span, membrane_role)
    return F, mem, wrist, tips[1]


def _qbez(a, b, c, t):
    return a * (1 - t) ** 2 + b * 2 * (1 - t) * t + c * t * t


def membrane_mesh(root, elbow, wrist, fingers, low, s, span, role, nt=8, ns=5, billow=0.035, scallop=0.22, thickness=0.006):
    import bmesh
    from .geometry import apply_modifiers
    bm = bmesh.new()
    back = V(0, 1, 0)

    def panel(A, B, scal):
        """A(t), B(t) boundary curves; fills between them, scalloping the far edge."""
        grid = []
        for i in range(nt + 1):
            t = i / nt
            row = []
            for j in range(ns + 1):
                u = j / ns
                tt = t * (1 - scal * math.sin(math.pi * u) * t ** 3) if scal else t
                p = A(tt).lerp(B(tt), u) + back * (billow * span * math.sin(math.pi * u) * math.sin(math.pi * min(1, tt * 1.2)))
                row.append(bm.verts.new(p))
            grid.append(row)
        for i in range(nt):
            for j in range(ns):
                bm.faces.new((grid[i][j], grid[i][j + 1], grid[i + 1][j + 1], grid[i + 1][j]))

    arm = lambda t: _qbez(root, elbow, wrist, t)
    edge = lambda t: root.lerp(low, t)
    panel(arm, edge, 0.0)
    rays = [lambda t, f=f: _qbez(f[0], f[1], f[2], t) for f in fingers] + [lambda t: wrist.lerp(low, t)]
    for i in range(len(rays) - 1):
        panel(rays[i], rays[i + 1], scallop)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.002)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    me = bpy.data.meshes.new("Membrane")
    bm.to_mesh(me)
    bm.free()
    ob = link(bpy.data.objects.new("Membrane", me))
    sub = ob.modifiers.new("Sub", "SUBSURF")
    sub.levels = 1
    so = ob.modifiers.new("Solid", "SOLIDIFY")
    so.thickness = thickness
    so.offset = 0
    apply_modifiers(ob)
    finalize(ob, role)
    return ob
