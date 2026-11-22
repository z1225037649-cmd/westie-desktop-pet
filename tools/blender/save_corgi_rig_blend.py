"""Import the authored animated GLB and save a native editable Blender project.

Run in Blender when its launch permission/approval service is available. This
does not alter the original Meshy file or application/release artifacts.
"""
import json
from pathlib import Path
import bpy
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'art/corgi-image-to-3d/rigged'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(OUT/'corgi-rigged.glb'))
scene=bpy.context.scene
rigs=[obj for obj in bpy.data.objects if obj.type=='ARMATURE']
assert rigs,'Animated GLB import did not create an armature.'
assert any(obj.type=='MESH' and obj.data.shape_keys for obj in bpy.data.objects),'Facial morphs failed to import.'
for image in bpy.data.images:
    if image.has_data and image.packed_file is None:image.pack()
    if image.source=='FILE':assert image.packed_file is not None
scene.render.fps=24
scene.frame_start=0;scene.frame_end=120;scene.frame_set(0)
scene.render.engine='CYCLES';scene.cycles.samples=48
scene.render.film_transparent=True
scene.render.resolution_x=720;scene.render.resolution_y=720;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGBA'
world=bpy.data.worlds.new('Corgi | soft environment');world.use_nodes=True;scene.world=world
world.node_tree.nodes['Background'].inputs['Color'].default_value=(.65,.70,.78,1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value=.35


def aim(obj,target):obj.rotation_euler=(Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler()


for name,location,energy,size in [('Key',(-3,-4,5),420,4),('Fill',(3,-2,3),180,3.5),('Rim',(1,3,4),260,3)]:
    light=bpy.data.lights.new(name,'AREA');light.energy=energy;light.shape='DISK';light.size=size
    obj=bpy.data.objects.new(name,light);scene.collection.objects.link(obj);obj.location=location;aim(obj,(0,-.15,.95))
camera=bpy.data.cameras.new('Corgi | review camera');obj=bpy.data.objects.new(camera.name,camera)
scene.collection.objects.link(obj);obj.location=(3.3,-6.3,2.0);aim(obj,(0,-.15,.93));camera.type='ORTHO';camera.ortho_scale=2.65;scene.camera=obj
scene['source_glb']='corgi-rigged.glb'
scene['scope']='24-joint rig, split lips, upper/lower eyelid morphs and seven imported GLB animations. Use Animation/NLA Editor to select a clip.'
for obj in bpy.context.selected_objects:obj.select_set(False)
rigs[0].select_set(True);bpy.context.view_layer.objects.active=rigs[0];rigs[0].show_in_front=True
bpy.context.preferences.filepaths.save_version=0
path=OUT/'corgi-rigged.blend'
bpy.ops.wm.save_as_mainfile(filepath=str(path),compress=True)
report={'status':'native project saved','path':str(path),'blender_version':bpy.app.version_string,'armatures':len(rigs),'bones':sum(len(obj.data.bones) for obj in rigs),'actions':[action.name for action in bpy.data.actions],'packed_images':len([image for image in bpy.data.images if image.packed_file]),'native_scene_reopen_required':True}
(OUT/'blender-save-report.json').write_text(json.dumps(report,indent=2)+'\n')
print('CORGI_NATIVE_PROJECT_SAVED',str(path),flush=True)
