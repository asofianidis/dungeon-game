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

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)

for i, name in enumerate(names):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(src, name + ".fbx"))
    new = [o for o in bpy.data.objects if o not in before]
    for o in new:
        if o.parent is None:
            o.location.x += (i - (len(names) - 1) / 2) * 1.4

scene = bpy.context.scene
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
cam.location = (0, -8, 2.2)
cam.rotation_euler = (math.radians(80), 0, math.radians(0))
scene.camera = cam
bpy.ops.render.render(write_still=True)
print("rendered", out)
