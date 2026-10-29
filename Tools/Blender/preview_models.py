"""
Renders a lineup of the exported FBX models to a PNG for a quick visual check.

Usage:
    blender -b --factory-startup --python Tools/Blender/preview_models.py -- Assets/Resources/Models out.png name1 name2 ...
"""
import bpy
import math
import os
import sys

args = sys.argv[sys.argv.index("--") + 1:]
src, out, names = args[0], args[1], args[2:]
yaw = 0.0
pose = None
for n in list(names):
    if n.startswith("yaw="):
        yaw = float(n[4:])
        names.remove(n)
    elif n.startswith("pose="):
        clip, frame = n[5:].split(":")
        pose = (clip, int(frame))
        names.remove(n)

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)

for i, name in enumerate(names):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(src, name + ".fbx"))
    new = [o for o in bpy.data.objects if o not in before]
    # Offset through a parent empty: animated armatures key their own object transform.
    holder = bpy.data.objects.new("holder_" + name, None)
    bpy.context.scene.collection.objects.link(holder)
    holder.location.x = (i - (len(names) - 1) / 2) * 1.4
    for o in new:
        if o.parent is None:
            o.parent = holder

scene = bpy.context.scene
if pose:
    for ob in bpy.data.objects:
        if ob.type != "ARMATURE":
            continue
        act = next((a for a in bpy.data.actions if a.name.endswith(pose[0]) and ob.name.split(".")[0] in a.name), None) \
            or next((a for a in bpy.data.actions if a.name.endswith(pose[0])), None)
        if act:
            ob.animation_data_create()
            ob.animation_data.action = act
            try:
                if act.slots:
                    ob.animation_data.action_slot = act.slots[0]
            except Exception:
                pass
    scene.frame_set(pose[1])
    print("actions:", [a.name for a in bpy.data.actions][:12])
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "MATERIAL"
scene.render.resolution_x = 220 * max(1, len(names))
scene.render.resolution_y = 420
scene.render.filepath = out
scene.world = bpy.data.worlds.new("w") if scene.world is None else scene.world

cam_data = bpy.data.cameras.new("cam")
cam_data.type = "ORTHO"
cam_data.ortho_scale = 1.4 * max(1, len(names)) * 1.05
cam = bpy.data.objects.new("cam", cam_data)
scene.collection.objects.link(cam)
dist = 8.0
cam.location = (math.sin(math.radians(yaw)) * dist, -math.cos(math.radians(yaw)) * dist, 2.6 if yaw else 2.2)
cam.rotation_euler = (math.radians(76 if yaw else 80), 0, math.radians(yaw))
scene.camera = cam
bpy.ops.render.render(write_still=True)
print("rendered", out)
