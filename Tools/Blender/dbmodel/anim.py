"""
Keyframed animation clips for Depthbound's rigs.

Poses are written as rotations about the CHARACTER's axes (Blender world space at rest, character
facing -Y): X = the character's left-to-right axis, Y = front-to-back, Z = up. They are converted to
each bone's local frame, so the same motion code works for any skeleton built from our rig layouts.

Sign conventions (right-handed, character facing -Y):
- About +X: negative swings a hanging limb forward; positive bends a knee (foot goes back) and leans
  the spine forward.
- About +Y: negative raises the LEFT (+X) arm/wing outward; positive raises the right one.
- About +Z: twists (positive turns the chest toward its left).
"""
import bpy
import math
from mathutils import Quaternion, Vector

FPS = 24
X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))

# Clip lengths in frames.
LENGTH = {"Idle": 48, "Walk": 20, "Attack": 14, "Cast": 15, "Hit": 9, "Death": 26}


def ease(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def env(t, a, b):
    """0 before a, eases to 1 at b."""
    return ease((t - a) / max(b - a, 1e-6))


def pulse(t, a, peak, b):
    """Rises from a to peak, falls back to 0 at b."""
    if t <= peak:
        return env(t, a, peak)
    return 1.0 - env(t, peak, b)


class Pose:
    def __init__(self):
        self.rot = {}
        self.loc = {}

    def r(self, bone, axis, deg):
        if abs(deg) > 1e-4:
            self.rot.setdefault(bone, []).append((axis, deg))

    def mv(self, bone, vec):
        self.loc[bone] = self.loc.get(bone, Vector((0, 0, 0))) + Vector(vec)


def bake(arm, name, fn, frames, loop):
    """Sample fn(t in [0,1]) -> Pose into a new action on the armature."""
    if arm.animation_data is None:
        arm.animation_data_create()
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm.animation_data.action = act
    bones = arm.pose.bones
    rest = {b.name: arm.data.bones[b.name].matrix_local.to_3x3() for b in bones}
    for pb in bones:
        pb.rotation_mode = "QUATERNION"
    last = frames if loop else frames - 1
    for f in range(0, last + 1):
        t = (f / frames) if loop else f / max(1, frames - 1)
        pose = fn(t)
        for pb in bones:
            q = Quaternion()
            m = rest[pb.name]
            for axis, deg in pose.rot.get(pb.name, []):
                local = (m.inverted() @ axis).normalized()
                q = Quaternion(local, math.radians(deg)) @ q
            pb.rotation_quaternion = q
            off = pose.loc.get(pb.name)
            pb.location = (m.inverted() @ off) if off is not None else Vector((0, 0, 0))
            pb.keyframe_insert("rotation_quaternion", frame=f, group=pb.name)
            pb.keyframe_insert("location", frame=f, group=pb.name)
    return act


def add_clips(arm, kind, **style):
    """Author every clip for a rig kind: biped, quadruped, spider, flyer."""
    makers = {"biped": biped, "quadruped": quadruped, "spider": spider, "flyer": flyer}[kind]
    for clip in ("Idle", "Walk", "Attack", "Cast", "Hit", "Death"):
        bake(arm, clip, lambda t, c=clip: makers(c, t, **style), LENGTH[clip], loop=clip in ("Idle", "Walk"))
    arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.rotation_quaternion = Quaternion()
        pb.location = (0, 0, 0)


def wings_tail(p, t, clip, flap=1.0, fold=0.0):
    """Secondary motion shared by every rig with wings and/or a tail."""
    tau = 2 * math.pi
    if clip == "Walk":
        w = math.sin(tau * t * 2) * 10 * flap
    elif clip == "Idle":
        w = math.sin(tau * t * 2) * 7 * flap
    else:
        w = math.sin(tau * t) * 14 * flap
    for side, s in (("L", 1), ("R", -1)):
        p.r("Wing" + side, Z, s * (14 + 16 * fold))          # swept back
        p.r("Wing" + side, Y, -s * w)
        p.r("WingTip" + side, Y, -s * w * 0.7)
    sway = 2.0 if clip == "Walk" else 1.0
    for i in range(1, 4):
        p.r("Tail%d" % i, Z, math.sin(tau * t * sway - i * 0.9) * (9 + 4 * i))
        p.r("Tail%d" % i, X, math.cos(tau * t * sway - i * 0.9) * 4)


# =============================================================================================
# Bipeds: Archdemon, princess, crusader, ghoul, imp, golem
# =============================================================================================

def biped(clip, t, weight=1.0, hunch=0.0, grace=0.0, claws=False, heavy=False, spread=0.0):
    """weight: motion amplitude; grace: princess-style poise; claws: two-handed swipes; heavy: golem;
    spread: how far (degrees) the rest pose already holds the arms away from the body."""
    p = Pose()
    tau = 2 * math.pi
    breathe = math.sin(tau * t * (1 if clip == "Idle" else 2))
    a = weight
    hang = 9 + 4 * grace - spread

    # Arms rest slightly away from the body.
    p.r("UpperArmL", Y, -hang)
    p.r("UpperArmR", Y, hang)
    p.r("LowerArmL", X, -14 - 10 * grace)
    p.r("LowerArmR", X, -14 - 10 * grace)
    if hunch:
        p.r("Spine", X, 10 * hunch)
        p.r("Neck", X, -12 * hunch)
        p.r("UpperArmL", X, -18 * hunch)
        p.r("UpperArmR", X, -18 * hunch)

    if clip == "Idle":
        p.mv("Hips", (0, 0, 0.004 * breathe))
        p.r("Chest", X, -1.6 * breathe)
        p.r("Head", Z, 7 * math.sin(tau * t + 0.8) * (0.6 if heavy else 1))
        p.r("Head", X, -2 * breathe)
        p.r("Hips", Y, 1.5 * math.sin(tau * t))
        p.r("UpperArmL", Y, -1.5 * breathe)
        p.r("UpperArmR", Y, 1.5 * breathe)
        if grace:
            # A poised gesture: the right hand rises gently as if speaking.
            g = 0.5 + 0.5 * math.sin(tau * t - 1.2)
            p.r("UpperArmR", X, -35 - 12 * g)
            p.r("LowerArmR", X, -55 - 15 * g)
            p.r("UpperArmR", Z, 18)
            p.r("UpperArmL", X, -12)
            p.r("Head", Y, 4 * math.sin(tau * t))
        wings_tail(p, t, clip, flap=0.6)

    elif clip == "Walk":
        ph = tau * t
        stride = (24 if heavy else 32) * a
        for side, off in (("L", 0.0), ("R", math.pi)):
            u = math.sin(ph + off)
            swing = math.cos(ph + off)
            p.r("UpperLeg" + side, X, -stride * u)
            p.r("LowerLeg" + side, X, max(0.0, swing) * (55 if not heavy else 40) * a + 6)
            p.r("Foot" + side, X, -max(0.0, swing) * 18 * a + max(0.0, -u) * 10)
            p.r("UpperArm" + side, X, stride * 0.85 * u * (0.4 if claws else 1.0))
            p.r("LowerArm" + side, X, -12 * max(0.0, u) * a)
        p.mv("Hips", (0, 0, 0.022 * math.cos(2 * ph) * a - 0.01))
        p.r("Hips", Z, 7 * math.sin(ph) * a)
        p.r("Hips", Y, 3 * math.cos(ph) * a)
        p.r("Spine", X, 6 + 3 * hunch)
        p.r("Chest", Z, -9 * math.sin(ph) * a)
        p.r("Head", Z, 5 * math.sin(ph) * a)
        p.r("Head", X, -3)
        wings_tail(p, t, clip, flap=0.8, fold=0.3)

    elif clip == "Attack":
        wind = env(t, 0.0, 0.35)          # anticipation
        hit = env(t, 0.35, 0.5)           # fast strike
        back = env(t, 0.55, 1.0)          # recovery
        strike = wind * (1 - hit) * -165 + hit * 25
        strike *= (1 - back)
        p.r("UpperArmR", X, strike)
        p.r("LowerArmR", X, (-55 * wind * (1 - hit) - 10 * hit) * (1 - back))
        p.r("Chest", Z, (22 * wind - 48 * hit) * (1 - back))
        p.r("Spine", X, (-8 * wind + 18 * hit) * (1 - back))
        p.r("Hips", Z, (-6 * wind + 10 * hit) * (1 - back))
        p.r("UpperLegL", X, -24 * hit * (1 - back))
        p.r("LowerLegR", X, 22 * hit * (1 - back))
        p.r("UpperArmL", X, (25 * hit - 10 * wind) * (1 - back))
        p.r("Head", X, (-6 * wind + 8 * hit) * (1 - back))
        if claws:
            p.r("UpperArmL", X, strike * 0.9)
            p.r("UpperArmL", Y, -30 * hit * (1 - back))
        wings_tail(p, t, clip, flap=1.2, fold=-0.3 * wind)

    elif clip == "Cast":
        charge = env(t, 0.0, 0.4)
        release = env(t, 0.4, 0.55)
        back = env(t, 0.65, 1.0)
        k = 1 - back
        for side, s in (("L", 1), ("R", -1)):
            p.r("UpperArm" + side, X, (-40 * charge - 45 * release) * k)
            p.r("UpperArm" + side, Z, s * (25 * charge - 15 * release) * k)
            p.r("LowerArm" + side, X, (-60 * charge + 45 * release) * k)
        p.r("Spine", X, (-10 * charge + 16 * release) * k)
        p.r("Chest", X, (-6 * charge + 6 * release) * k)
        p.r("Head", X, (-8 * charge + 10 * release) * k)
        p.mv("Hips", (0, 0, -0.02 * release * k))
        wings_tail(p, t, clip, flap=1.4, fold=-0.6 * charge * k)

    elif clip == "Hit":
        e = pulse(t, 0.0, 0.25, 1.0)
        p.r("Spine", X, -16 * e)
        p.r("Chest", X, -10 * e)
        p.r("Head", X, -18 * e)
        p.r("Head", Z, 10 * e)
        p.r("UpperArmL", Y, -25 * e)
        p.r("UpperArmR", Y, 25 * e)
        p.mv("Hips", (0, 0.04 * e, -0.015 * e))
        wings_tail(p, t, clip, flap=1.5)

    elif clip == "Death":
        kneel = env(t, 0.0, 0.45)
        fall = env(t, 0.3, 1.0)
        for side, s in (("L", 1), ("R", -1)):
            p.r("UpperLeg" + side, X, -70 * kneel)
            p.r("LowerLeg" + side, X, 110 * kneel)
            p.r("UpperArm" + side, Y, -s * 45 * fall)
            p.r("UpperArm" + side, X, -30 * fall)
        p.mv("Hips", (0, 0.18 * fall, -0.33 * kneel))
        p.r("Hips", X, -75 * fall)
        p.r("Spine", X, 10 * fall)
        p.r("Head", X, -25 * fall)
        p.r("Head", Z, 25 * fall)
        for s in ("L", "R"):
            p.r("Wing" + s, Z, (1 if s == "L" else -1) * 40 * fall)
            p.r("Wing" + s, Y, (-1 if s == "L" else 1) * -30 * fall)
    return p


# =============================================================================================
# Quadruped (wolf-beast): front legs Leg/Shin, hind legs Leg2/Shin2
# =============================================================================================

def quadruped(clip, t):
    p = Pose()
    tau = 2 * math.pi
    breathe = math.sin(tau * t)
    if clip == "Idle":
        p.r("Chest", X, 1.5 * breathe)
        p.r("Head", X, 3 * breathe)
        p.r("Head", Z, 10 * math.sin(tau * t + 1.0))
        wings_tail(p, t, clip)
    elif clip == "Walk":
        ph = tau * t
        for leg, shin, off, bendSign in (("LegL", "ShinL", 0.0, -1), ("LegR", "ShinR", math.pi, -1),
                                         ("Leg2L", "Shin2L", math.pi, 1), ("Leg2R", "Shin2R", 0.0, 1)):
            u = math.sin(ph + off)
            p.r(leg, X, -30 * u)
            p.r(shin, X, bendSign * -max(0.0, math.cos(ph + off)) * 45)
        p.mv("Hips", (0, 0, 0.02 * math.cos(2 * ph)))
        p.r("Spine", Z, 5 * math.sin(ph))
        p.r("Chest", Z, -5 * math.sin(ph))
        p.r("Head", X, 5 * math.cos(2 * ph))
        wings_tail(p, t, clip)
    elif clip in ("Attack", "Cast"):
        crouch = env(t, 0.0, 0.35)
        lunge = env(t, 0.35, 0.5)
        back = env(t, 0.6, 1.0)
        k = 1 - back
        p.mv("Hips", (0, (0.08 * crouch - 0.16 * lunge) * k, (-0.05 * crouch + 0.03 * lunge) * k))
        p.r("Hips", X, (8 * crouch - 12 * lunge) * k)
        p.r("Chest", X, (-10 * crouch + 18 * lunge) * k)
        p.r("Head", X, (-25 * crouch + 30 * lunge) * k)
        p.r("LegL", X, (15 * crouch - 45 * lunge) * k)
        p.r("LegR", X, (15 * crouch - 40 * lunge) * k)
        p.r("Leg2L", X, (-10 * crouch + 25 * lunge) * k)
        p.r("Leg2R", X, (-10 * crouch + 25 * lunge) * k)
        wings_tail(p, t, clip)
    elif clip == "Hit":
        e = pulse(t, 0.0, 0.3, 1.0)
        p.r("Chest", X, -12 * e)
        p.r("Head", X, -20 * e)
        p.r("Head", Z, 15 * e)
        p.mv("Hips", (0, 0.05 * e, 0))
    elif clip == "Death":
        e = env(t, 0.0, 0.7)
        p.r("Hips", Y, 85 * e)
        p.mv("Hips", (0, 0, -0.32 * e))
        for leg in ("LegL", "LegR", "Leg2L", "Leg2R"):
            p.r(leg, X, -25 * e)
        p.r("Head", X, 20 * e)
    return p


# =============================================================================================
# Spider: Leg/Shin, Leg2/Shin2 ... Leg4/Shin4 on each side
# =============================================================================================

def spider(clip, t):
    p = Pose()
    tau = 2 * math.pi
    legs = [("" if i == 1 else str(i)) for i in range(1, 5)]
    if clip == "Idle":
        b = math.sin(tau * t * 2)
        p.r("Abdomen", X, 4 * b)
        p.mv("Body", (0, 0, 0.006 * b))
        for i, idx in enumerate(legs):
            for side, s in (("L", 1), ("R", -1)):
                p.r("Leg" + idx + side, Y, -s * 3 * math.sin(tau * t + i))
    elif clip == "Walk":
        ph = tau * t
        for i, idx in enumerate(legs):
            for side, s in (("L", 1), ("R", -1)):
                off = (i % 2) * math.pi + (math.pi if side == "R" else 0)
                u = math.sin(ph + off)
                p.r("Leg" + idx + side, Z, s * 22 * u)
                p.r("Leg" + idx + side, Y, -s * 16 * max(0.0, math.cos(ph + off)))
        p.mv("Body", (0, 0, 0.012 * math.cos(2 * ph)))
        p.r("Abdomen", Z, 6 * math.sin(ph))
    elif clip in ("Attack", "Cast"):
        rear = env(t, 0.0, 0.4)
        strike = env(t, 0.4, 0.55)
        back = env(t, 0.65, 1.0)
        k = 1 - back
        p.r("Body", X, (-25 * rear + 30 * strike) * k)
        p.r("Head", X, (-10 * rear + 25 * strike) * k)
        for side, s in (("L", 1), ("R", -1)):
            p.r("Leg" + side, Y, -s * (55 * rear - 40 * strike) * k)
            p.r("Leg2" + side, Y, -s * (25 * rear - 15 * strike) * k)
    elif clip == "Hit":
        e = pulse(t, 0.0, 0.3, 1.0)
        p.r("Body", X, -15 * e)
        p.mv("Body", (0, 0.04 * e, 0))
    elif clip == "Death":
        e = env(t, 0.0, 0.8)
        p.mv("Body", (0, 0, -0.18 * e))
        for idx in legs:
            for side, s in (("L", 1), ("R", -1)):
                p.r("Leg" + idx + side, Y, -s * 50 * e)
                p.r("Shin" + idx + side, Y, -s * 70 * e)
        p.r("Body", Y, 20 * e)
    return p


# =============================================================================================
# Flyer (bat): Body, Head, Wing/WingTip, Tail1
# =============================================================================================

def flyer(clip, t):
    p = Pose()
    tau = 2 * math.pi
    rate = 4 if clip == "Idle" else 6 if clip == "Walk" else 3
    f = math.sin(tau * t * rate)
    amp = 45
    if clip == "Attack" or clip == "Cast":
        dive = pulse(t, 0.0, 0.45, 1.0)
        p.r("Body", X, 35 * dive)
        p.mv("Body", (0, -0.15 * dive, -0.12 * dive))
        amp = 30 * (1 - dive) + 10
    elif clip == "Hit":
        e = pulse(t, 0.0, 0.3, 1.0)
        p.r("Body", X, -30 * e)
        p.mv("Body", (0, 0.06 * e, 0.04 * e))
    elif clip == "Death":
        e = env(t, 0.0, 0.8)
        p.mv("Body", (0, 0, -0.75 * e))
        p.r("Body", Y, 120 * e)
        amp = 45 * (1 - e)
    p.mv("Body", (0, 0, 0.05 * math.cos(tau * t * rate)))
    for side, s in (("L", 1), ("R", -1)):
        p.r("Wing" + side, Y, -s * amp * f)
        p.r("WingTip" + side, Y, -s * amp * 0.6 * math.sin(tau * t * rate - 0.6))
    p.r("Head", X, 6 * math.sin(tau * t * rate + 1.2))
    p.r("Tail1", X, 12 * math.sin(tau * t * rate - 1.0))
    return p
