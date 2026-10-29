"""
Clothing and armor shaped from the body: shells of a body field cut by regions (bodices, armor
plates, stockings, boots), flared skirts with folds and ruffled hems, sleeves, bands and trims.
All return dbmodel.sdf primitives (fn, lo, hi) to add to a Field.
"""
import math

import numpy as np
from mathutils import Vector

from . import sdf


def _a(v):
    return np.array(tuple(v), dtype=np.float64)


def region_and(*regions):
    def f(x, y, z):
        out = regions[0](x, y, z)
        for r in regions[1:]:
            out = np.maximum(out, r(x, y, z))
        return out
    return f


def zrange(z0, z1):
    return lambda x, y, z: np.maximum(z0 - z, z - z1)


def ztop(fn):
    """Region below a height that varies over (x, y): fn(x, y) -> z."""
    return lambda x, y, z: z - fn(x, y)


def zbottom(fn):
    return lambda x, y, z: fn(x, y) - z


def near_z(fn, half):
    """Band within `half` of the height fn(x, y)."""
    return lambda x, y, z: np.abs(z - fn(x, y)) - half


def surface_y(field, x, z, offset=0.0, front=True, span=0.5):
    """y of the field's (offset) surface at (x, z), seen from the front (-Y) or back."""
    ys = np.linspace(-span, span, 800) if front else np.linspace(span, -span, 800)
    d = field.eval(np.full_like(ys, x), ys, np.full_like(ys, z)) - offset
    idx = np.nonzero(d < 0)[0]
    if len(idx) == 0:
        return None
    i = idx[0]
    if i == 0:
        return float(ys[0])
    t = d[i - 1] / (d[i - 1] - d[i])
    return float(ys[i - 1] + (ys[i] - ys[i - 1]) * t)


def skirt(center, z_top, top_r, bottom_r, hem, folds=9, fold_amp=0.08, thick=0.004, phase=0.0, ruffle=0.0, ruffle_freq=24,
          drop=0.0):
    """A flared skirt shell around a vertical axis through center (x, y).
    top_r/bottom_r: (rx, ry) radii at the waist and at the hem; hem(ang) -> z of the hem, where ang
    is 0 at the front (-Y). Folds deepen toward the hem; ruffle adds fine waves near the hem."""
    cx, cy = center[0], center[1]
    z_mean = hem(0.0) * 0.5 + hem(math.pi) * 0.5
    span = max(z_top - z_mean, 1e-3)

    def f(x, y, z):
        dx, dy = x - cx, y - cy
        ang = np.arctan2(dx, -dy)
        t = np.clip((z_top - z) / span, 0.0, 1.3)
        tt = t ** 0.85
        rx = top_r[0] + (bottom_r[0] - top_r[0]) * tt
        ry = top_r[1] + (bottom_r[1] - top_r[1]) * tt
        R = rx * ry / np.sqrt((ry * np.sin(ang)) ** 2 + (rx * np.cos(ang)) ** 2)
        R = R * (1.0 + fold_amp * t * np.sin(folds * ang + phase))
        hz = hem(ang)
        if ruffle:
            near = np.clip(1.0 - (z - hz) / (span * 0.25), 0, 1)
            R = R * (1.0 + ruffle * near * np.sin(ruffle_freq * ang))
        rho = np.sqrt(dx * dx + dy * dy)
        d = np.abs(rho - R) - thick
        hem_wave = hz + drop * np.sin(folds * ang + phase) * t
        return np.maximum(d, np.maximum(z - z_top, hem_wave - z))
    rmax = max(bottom_r) * (1 + fold_amp + ruffle) + thick
    lo = np.array([cx - rmax, cy - rmax, min(hem(0.0), hem(math.pi)) - span * 0.4])
    hi = np.array([cx + rmax, cy + rmax, z_top + thick])
    return f, lo, hi


def hem_front_back(z_front, z_back, scallop=0.0, count=9):
    def hem(ang):
        a = np.asarray(ang)
        base = z_front + (z_back - z_front) * (1 - np.cos(a)) * 0.5
        if scallop:
            base = base - scallop * np.abs(np.sin(a * count * 0.5))
        return base
    return hem


def tube_shell(a, b, r1, r2, thick, ruffle=0.0, freq=10, cap_a=True, cap_b=True):
    """Open sleeve/cuff: a hollow tapered tube from a to b, cut flat at both ends."""
    a, b = _a(a), _a(b)
    ax = b - a
    L = float(np.linalg.norm(ax))
    ax = ax / L
    ref = np.array([0.0, 0.0, 1.0]) if abs(ax[2]) < 0.9 else np.array([1.0, 0.0, 0.0])
    u = np.cross(ax, ref)
    u /= np.linalg.norm(u)
    v = np.cross(ax, u)

    def f(x, y, z):
        px, py, pz = x - a[0], y - a[1], z - a[2]
        h = px * ax[0] + py * ax[1] + pz * ax[2]
        qx, qy, qz = px - h * ax[0], py - h * ax[1], pz - h * ax[2]
        rho = np.sqrt(qx * qx + qy * qy + qz * qz)
        t = np.clip(h / L, 0, 1)
        R = r1 + (r2 - r1) * t ** 1.4
        if ruffle:
            ang = np.arctan2(qx * u[0] + qy * u[1] + qz * u[2], qx * v[0] + qy * v[1] + qz * v[2])
            R = R * (1.0 + ruffle * t * t * np.sin(freq * ang))
        d = np.abs(rho - R) - thick
        if cap_a:
            d = np.maximum(d, -h)
        if cap_b:
            d = np.maximum(d, h - L)
        return d
    m = max(r1, r2) * (1 + ruffle) + thick
    lo = np.minimum(a, b) - m
    hi = np.maximum(a, b) + m
    return f, lo, hi


def ribbon_loop(center, side, size, n_normal):
    """Points for a bow loop leaving `center` toward `side`."""
    c = Vector(center)
    pts = []
    for i in range(7):
        t = i / 6
        a = t * math.pi * 2
        pts.append(c + side * (size * 0.5 * (1 - math.cos(a))) + Vector((0, 0, size * 0.35 * math.sin(a))))
    return pts


def ruffled_ellipsoid(c, radii, amp=0.08, freq=8):
    """Solid puffy ellipsoid with vertical ruffles (puff sleeves, collars)."""
    base, lo, hi = sdf.ellipsoid(c, radii)
    c = _a(c)

    def f(x, y, z):
        ang = np.arctan2(x - c[0], y - c[1])
        return base(x, y, z) - amp * min(radii) * np.sin(freq * ang)
    return f, lo - amp * max(radii), hi + amp * max(radii)


def dome(c, radii, thick, cut=None, axes=None, ridges=0, ridge_amp=0.0):
    """Hollow ellipsoid shell (pauldrons, knee cops, helms); cut(x, y, z) < 0 keeps a region."""
    base, lo, hi = sdf.ellipsoid(c, radii, axes)
    c0 = _a(c)

    def f(x, y, z):
        d = np.abs(base(x, y, z)) - thick
        if ridges:
            ang = np.arctan2(x - c0[0], y - c0[1])
            d = d - ridge_amp * np.maximum(np.sin(ang * ridges), 0) ** 4
        if cut is not None:
            d = np.maximum(d, cut(x, y, z))
        return d
    m = thick + ridge_amp
    return f, lo - m, hi + m


def along(a, b, t0, t1):
    """Region of points whose projection on segment a->b lies in [t0, t1]."""
    a, b = _a(a), _a(b)
    ab = b - a
    L2 = float(ab @ ab)

    def f(x, y, z):
        t = ((x - a[0]) * ab[0] + (y - a[1]) * ab[1] + (z - a[2]) * ab[2]) / L2
        return np.maximum(t0 - t, t - t1) * np.sqrt(L2)
    return f


def side(sign, margin=0.0):
    """Region on one side of the body (x * sign > margin)."""
    return lambda x, y, z: margin - x * sign


def front(margin=0.0):
    return lambda x, y, z: y + margin


def back(margin=0.0):
    return lambda x, y, z: margin - y


def plane_cut(p, n):
    """Keep the side of the plane the normal points to."""
    p, n = _a(p), _a(n) / np.linalg.norm(_a(n))
    return lambda x, y, z: -((x - p[0]) * n[0] + (y - p[1]) * n[1] + (z - p[2]) * n[2])


def cape(center, z_top, z_hem, top_r, bottom_r, half_angle, folds=7, fold_amp=0.1, thick=0.004):
    """A cape: the back part of a flared skirt shell hung from the shoulders."""
    f0, lo, hi = skirt(center, z_top, top_r, bottom_r, hem_front_back(z_hem, z_hem), folds=folds, fold_amp=fold_amp, thick=thick)
    cx, cy = center[0], center[1]

    def f(x, y, z):
        ang = np.arctan2(x - cx, -(y - cy))
        off = np.pi - np.abs(ang)                 # 0 at the back
        return np.maximum(f0(x, y, z), (off - half_angle) * top_r[0])
    return f, lo, hi