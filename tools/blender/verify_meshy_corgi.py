"""Reopen the saved Meshy scene and compare its geometry to a fresh GLB import."""
import hashlib
import json
import math
import struct
from pathlib import Path

import bpy
from mathutils import Vector

folder = Path(bpy.data.filepath).parent
report = json.loads((folder / 'import-report.json').read_text(encoding='utf-8'))
source = Path(report['source_file'])
assert hashlib.sha256(source.read_bytes()).hexdigest() == report['source_sha256']
scene = bpy.context.scene
asset = bpy.data.collections['MESHY | original generated asset']
meshes = sorted((obj for obj in asset.objects if obj.type == 'MESH'), key=lambda obj: obj.name)
assert len(meshes) == report['mesh_objects']
assert sum(len(obj.data.vertices) for obj in meshes) == report['vertices']
assert sum(len(obj.data.polygons) for obj in meshes) == report['polygons']
assert sum(obj.type == 'ARMATURE' for obj in asset.objects) == report['armatures']
assert len(bpy.data.actions) == report['actions']
assert scene.render.film_transparent
assert len([obj for obj in scene.objects if obj.type == 'CAMERA']) == 4
assert scene.camera.name == 'Camera | three-quarter'
images = {image.name: image for image in bpy.data.images}
for expected in report['textures']:
    image = images[expected['name']]
    assert list(image.size) == expected['size']
    assert image.has_data and image.packed_file is not None
    assert image.colorspace_settings.name == expected['colorspace']
for expected in report['materials']:
    material = bpy.data.materials[expected['name']]
    assert material.use_nodes == expected['use_nodes']
    for node in expected['image_nodes']:
        actual = material.node_tree.nodes[node['node']]
        assert actual.image.name == node['image']
        assert any(socket.is_linked for socket in actual.outputs) == node['linked']
points = [obj.matrix_world @ Vector(corner) for obj in meshes for corner in obj.bound_box]
lo = [min(point[axis] for point in points) for axis in range(3)]
hi = [max(point[axis] for point in points) for axis in range(3)]
for actual, expected in zip(lo + hi, sum(report['normalization']['normalized_bounds'], [])):
    assert abs(actual - expected) < 1e-5
for name in report['views']:
    assert name in report['renders_completed']
    assert (folder / (name + '.png')).stat().st_size > 10000


def geometry_digest(obj):
    mesh = obj.data
    digest = hashlib.sha256()
    for vertex in mesh.vertices:
        assert all(math.isfinite(value) for value in vertex.co)
        digest.update(struct.pack('<3f', *vertex.co))
    for face in mesh.polygons:
        digest.update(struct.pack('<I', len(face.vertices)))
        digest.update(struct.pack('<' + 'I' * len(face.vertices), *face.vertices))
    for layer in mesh.uv_layers:
        for loop in layer.data:
            assert all(math.isfinite(value) for value in loop.uv)
            digest.update(struct.pack('<2f', *loop.uv))
    return digest.hexdigest()


saved_geometry = sorted(geometry_digest(obj) for obj in meshes)
before = set(bpy.data.objects)
bpy.ops.import_scene.gltf(filepath=str(source))
fresh = [obj for obj in bpy.data.objects if obj not in before and obj.type == 'MESH']
assert sorted(geometry_digest(obj) for obj in fresh) == saved_geometry, 'Saved geometry or UVs differ from the original GLB.'
checks = {
    'status': 'passed',
    'saved_blend_reopened': True,
    'source_sha256': report['source_sha256'],
    'geometry_and_uv_match_fresh_glb_import': True,
    'geometry_digests': saved_geometry,
    'mesh_objects': len(meshes),
    'vertices': report['vertices'],
    'triangles': sum(item['loop_triangles'] for item in report['meshes']),
    'packed_textures': len(report['textures']),
    'material_texture_links_preserved': True,
    'armatures': report['armatures'],
    'actions': report['actions'],
    'views': report['renders_completed'],
    'scope': 'Static generated asset, ready for subsequent rigging; no interaction animations yet.',
}
(folder / 'scene-checks.json').write_text(json.dumps(checks, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print('MESHY_SAVED_SCENE_VERIFIED', json.dumps(checks), flush=True)
