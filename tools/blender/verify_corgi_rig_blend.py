"""Verify the reopened native project has real armature and facial deformation."""
import hashlib
import json
from pathlib import Path
import bpy
import numpy as np
from mathutils import Quaternion

folder=Path(bpy.data.filepath).parent
manifest=json.loads((folder/'rig-manifest.json').read_text())
source=folder/'corgi-rigged.glb'
assert hashlib.sha256(source.read_bytes()).hexdigest()==manifest['rigged_sha256']
rigs=[obj for obj in bpy.data.objects if obj.type=='ARMATURE']
assert len(rigs)==1 and len(rigs[0].data.bones)==24
meshes=[obj for obj in bpy.data.objects if obj.type=='MESH']
skins=[obj for obj in meshes if any(mod.type=='ARMATURE' and mod.object==rigs[0] for mod in obj.modifiers)]
assert skins
required=set(manifest['required_shape_keys'])
names=set(name for obj in skins if obj.data.shape_keys for name in obj.data.shape_keys.key_blocks.keys())
assert required<=names,(required,names)
actions=[action.name for action in bpy.data.actions]
assert all(any(name in action for action in actions) for name in ('Idle','Blink','Pet','Eat','PlayBall','Sleep','Wake')),actions
images=[image for image in bpy.data.images if image.source=='FILE']
assert len(images)>=4 and all(image.packed_file is not None for image in images)
for image in images:
    # Reopened packed images load lazily; read pixels before testing buffers.
    pixels=list(image.pixels[:4])
    assert len(pixels)==4 and image.has_data and min(image.size)>0,image.name
for obj in skins:
    assert obj.data.uv_layers
    for vertex in obj.data.vertices:
        total=sum(group.weight for group in vertex.groups)
        assert abs(total-1)<.0003,(obj.name,vertex.index,total)


def evaluated(obj):
    bpy.context.view_layer.update()
    item=obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    return np.array([tuple(item.matrix_world@vertex.co) for vertex in item.data.vertices])


obj=max(skins,key=lambda mesh:len(mesh.data.vertices))
before=evaluated(obj)
bone=rigs[0].pose.bones['Head']
bone.rotation_mode='QUATERNION'
bone.rotation_quaternion=bone.rotation_quaternion@Quaternion((1,0,0),.10)
after=evaluated(obj)
bone_motion=float(np.linalg.norm(after-before,axis=1).max())
assert bone_motion>.02,'Armature has bones but no actual evaluated deformation.'
face=None;face_motion=0
for candidate in skins:
    keys=candidate.data.shape_keys
    if not keys or 'Blink.L' not in keys.key_blocks:continue
    key=keys.key_blocks['Blink.L']
    basis=keys.key_blocks[0]
    displacement=max((a.co-b.co).length for a,b in zip(key.data,basis.data))
    if displacement<.005:continue
    before=evaluated(candidate);key.value=1;after=evaluated(candidate)
    face_motion=float(np.linalg.norm(after-before,axis=1).max())
    if face_motion>.005:face=candidate.name;break
assert face is not None,'Facial keys have no actual evaluated effect.'
report={'status':'passed','native_blend_reopened':True,'native_file':str(Path(bpy.data.filepath)),'source_glb_sha256':manifest['rigged_sha256'],'armatures':1,'bones':24,'skinned_objects':len(skins),'shape_keys':sorted(required),'actions':actions,'packed_images':len(images),'actual_bone_motion':bone_motion,'actual_blink_motion':face_motion,'scope':'Native import, reopened data, real skin and shape deformation; all clip motion/contact was independently verified in the GLB.'}
(folder/'native-checks.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
print('CORGI_NATIVE_RIG_VERIFIED',json.dumps(report),flush=True)
