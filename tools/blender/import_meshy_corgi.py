"""Import the actual Meshy GLB, preserve its asset data, and render review views.

Blender 4.5:
  blender --background --factory-startup --python-exit-code 1 \
    --python tools/blender/import_meshy_corgi.py -- --input model.glb

Heading is an explicit rotation around Blender's Z axis, in degrees. Zero
assumes the imported character faces -Y; inspect the first preview before
choosing a different value. The GLB itself is never overwritten.
"""
import argparse
import hashlib
import json
import math
import struct
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector


ROOT = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser()
parser.add_argument('--input', default=str(ROOT / 'art/corgi-image-to-3d/source/corgi-meshy.glb'))
parser.add_argument('--output', default=str(ROOT / 'art/corgi-image-to-3d/blender'))
parser.add_argument('--size', type=int, default=900)
parser.add_argument('--samples', type=int, default=64)
parser.add_argument('--heading', type=float, default=0.0)
parser.add_argument('--no-render', action='store_true')
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
source = Path(args.input).resolve()
output = Path(args.output).resolve()
assert source.is_file(), f'Missing generated GLB: {source}'
assert args.size > 0 and args.samples > 0
assert math.isfinite(args.heading)
output.mkdir(parents=True, exist_ok=True)
raw = source.read_bytes()
assert len(raw) >= 20, 'GLB is truncated.'
magic, version, declared_length = struct.unpack_from('<4sII', raw, 0)
assert magic == b'glTF' and version == 2, 'Expected a binary glTF 2.0 asset.'
assert declared_length == len(raw), 'GLB file length differs from its header.'
json_size, chunk_type = struct.unpack_from('<II', raw, 12)
assert chunk_type == 0x4E4F534A and 20 + json_size <= len(raw), 'Missing GLB JSON chunk.'
glb_json = json.loads(raw[20:20 + json_size].decode('utf-8').rstrip('\x00 \r\n\t'))
# Blender is not a network downloader: reject unresolved image/buffer URIs
# before import, while allowing embedded data URIs and bundled local files.
external_files = []
for item in glb_json.get('images', []) + glb_json.get('buffers', []):
    uri = item.get('uri')
    if not uri or uri.startswith('data:'):
        continue
    assert '://' not in uri, f'GLB depends on a remote resource: {uri}'
    dependency = source.parent / uri
    assert dependency.is_file(), f'Missing GLB dependency: {dependency}'
    external_files.append(str(dependency.resolve()))
assert glb_json.get('meshes'), 'Generated asset contains no mesh.'
assert glb_json.get('images'), 'Expected the selected textured GLB, but it contains no images.'

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
before_objects = set(bpy.data.objects)
bpy.ops.import_scene.gltf(filepath=str(source))
imported = [obj for obj in bpy.data.objects if obj not in before_objects]
mesh_objects = [obj for obj in imported if obj.type == 'MESH']
assert mesh_objects, 'Blender imported no mesh objects.'
asset_collection = bpy.data.collections.new('MESHY | original generated asset')
scene.collection.children.link(asset_collection)
for obj in imported:
    for collection in list(obj.users_collection):
        collection.objects.unlink(obj)
    asset_collection.objects.link(obj)

material_data = []
used_materials = sorted({mat for obj in mesh_objects for mat in obj.data.materials if mat}, key=lambda mat: mat.name)
used_images = set()
for material in used_materials:
    nodes = list(material.node_tree.nodes) if material.use_nodes and material.node_tree else []
    texture_nodes = []
    for node in nodes:
        if node.type != 'TEX_IMAGE':
            continue
        assert node.image is not None, f'Material image node is unresolved: {material.name}/{node.name}'
        used_images.add(node.image)
        texture_nodes.append({'node': node.name, 'image': node.image.name, 'linked': any(socket.is_linked for socket in node.outputs)})
    material_data.append({'name': material.name, 'use_nodes': material.use_nodes, 'image_nodes': texture_nodes})
assert used_images, 'The imported meshes have no image textures.'
images = []
for image in sorted(used_images, key=lambda item: item.name):
    width, height = image.size
    assert width > 0 and height > 0 and image.has_data, f'Image failed to load: {image.name} ({image.filepath})'
    if image.source == 'FILE' and image.packed_file is None:
        resolved = Path(bpy.path.abspath(image.filepath))
        assert resolved.is_file(), f'Missing texture file: {resolved}'
    if image.packed_file is None:
        image.pack()
    assert image.packed_file is not None, f'Could not pack image: {image.name}'
    images.append({'name': image.name, 'size': [width, height], 'channels': image.channels, 'colorspace': image.colorspace_settings.name, 'packed': True})


def bounds(objects):
    points = [obj.matrix_world @ Vector(corner) for obj in objects for corner in obj.bound_box]
    assert points
    lo = Vector(tuple(min(point[axis] for point in points) for axis in range(3)))
    hi = Vector(tuple(max(point[axis] for point in points) for axis in range(3)))
    assert all(math.isfinite(value) for value in (*lo, *hi))
    return lo, hi


bpy.context.view_layer.update()
original_min, original_max = bounds(mesh_objects)
original_height = original_max.z - original_min.z
assert original_height > 1e-8, 'Generated model has zero height.'
parent = bpy.data.objects.new('MESHY | normalization (uniform scale + heading)', None)
asset_collection.objects.link(parent)
imported_set = set(imported)
for obj in imported:
    if obj.parent not in imported_set:
        world = obj.matrix_world.copy()
        obj.parent = parent
        obj.matrix_world = world
center_xy = Vector(((original_min.x + original_max.x) / 2, (original_min.y + original_max.y) / 2, original_min.z))
uniform_scale = 2.0 / original_height
normalization = Matrix.Rotation(math.radians(args.heading), 4, 'Z') @ Matrix.Scale(uniform_scale, 4) @ Matrix.Translation(-center_xy)
parent.matrix_world = normalization
bpy.context.view_layer.update()
normalized_min, normalized_max = bounds(mesh_objects)
assert abs(normalized_min.z) < 1e-5, 'Normalized model does not touch the ground plane.'
assert abs((normalized_max.z - normalized_min.z) - 2.0) < 1e-5
model_info = []
for obj in mesh_objects:
    assert obj.data.vertices and obj.data.polygons, f'Empty mesh object: {obj.name}'
    assert obj.data.uv_layers, f'Textured generated mesh is missing UV coordinates: {obj.name}'
    model_info.append({'name': obj.name, 'vertices': len(obj.data.vertices), 'polygons': len(obj.data.polygons), 'loop_triangles': sum(len(face.vertices) - 2 for face in obj.data.polygons), 'uv_layers': [layer.name for layer in obj.data.uv_layers], 'materials': [material.name if material else None for material in obj.data.materials]})

studio = bpy.data.collections.new('STUDIO | review cameras and lights')
scene.collection.children.link(studio)


def aim(obj, target):
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat('-Z', 'Y').to_euler()


def light(name, location, energy, size, color):
    data = bpy.data.lights.new(name, 'AREA')
    data.energy = energy
    data.shape = 'DISK'
    data.size = size
    data.color = color
    obj = bpy.data.objects.new(name, data)
    studio.objects.link(obj)
    obj.location = location
    aim(obj, (0, 0, 1))


light('Key | neutral softbox', (-3.0, -4.0, 5.0), 420, 4.0, (1.0, .96, .90))
light('Fill | soft sky', (3.0, -2.0, 3.0), 190, 3.5, (.88, .94, 1.0))
light('Rim | soft', (1.0, 3.0, 4.0), 300, 3.0, (1.0, .95, .88))
world = bpy.data.worlds.new('Studio | neutral environment')
scene.world = world
world.use_nodes = True
world.node_tree.nodes['Background'].inputs['Color'].default_value = (.65, .70, .78, 1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value = .35
extent = normalized_max - normalized_min
framing = max(extent.z, math.hypot(extent.x, extent.y)) * 1.18
center = (normalized_min + normalized_max) / 2
cameras = {}
for name, direction in {'three-quarter': (4.2, -7.0, 1.9), 'front': (0, -8, 1.3), 'side': (8, 0, 1.3), 'back': (0, 8, 1.3)}.items():
    data = bpy.data.cameras.new('Camera | ' + name)
    obj = bpy.data.objects.new(data.name, data)
    studio.objects.link(obj)
    obj.location = center + Vector(direction)
    aim(obj, center)
    data.type = 'ORTHO'
    data.ortho_scale = framing
    data.clip_start = .01
    data.clip_end = 1000
    cameras[name] = obj
scene.camera = cameras['three-quarter']
scene.render.engine = 'CYCLES'
scene.cycles.samples = args.samples
scene.cycles.use_denoising = True
try:
    preferences = bpy.context.preferences.addons['cycles'].preferences
    preferences.compute_device_type = 'OPTIX'
    preferences.get_devices()
    for device in preferences.devices:
        device.use = device.type == 'OPTIX'
    scene.cycles.device = 'GPU' if any(device.use for device in preferences.devices) else 'CPU'
except Exception:
    scene.cycles.device = 'CPU'
scene.render.resolution_x = args.size
scene.render.resolution_y = args.size
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
scene.render.image_settings.color_depth = '8'
scene.render.film_transparent = True
scene.view_settings.view_transform = 'AgX'
scene.view_settings.look = 'AgX - Medium High Contrast'
scene.render.filepath = str(output / 'three-quarter.png')
scene['asset_source'] = 'Meshy image-to-3D generated GLB; original meshes, materials and UVs preserved.'
scene['asset_sha256'] = hashlib.sha256(raw).hexdigest()
scene['normalization'] = 'Parent empty only: uniform scale, ground centering and explicit heading.'
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type == 'VIEW_3D':
            area.spaces.active.region_3d.view_location = center
            area.spaces.active.region_3d.view_distance = framing * 1.5
            area.spaces.active.region_3d.view_rotation = scene.camera.rotation_euler.to_quaternion()
bpy.ops.object.select_all(action='DESELECT')
for obj in mesh_objects:
    obj.select_set(True)
bpy.context.view_layer.objects.active = mesh_objects[0]
bpy.context.preferences.filepaths.save_version = 0
blend = output / 'corgi-meshy.blend'
bpy.ops.wm.save_as_mainfile(filepath=str(blend), compress=True)
report = {
    'status': 'imported and validated',
    'source_file': str(source),
    'source_sha256': hashlib.sha256(raw).hexdigest(),
    'source_bytes': len(raw),
    'source_generator': glb_json.get('asset', {}).get('generator'),
    'blender_version': bpy.app.version_string,
    'blend_file': str(blend),
    'geometry_modified': False,
    'materials_modified': False,
    'normalization': {'parent_empty': parent.name, 'uniform_scale': uniform_scale, 'heading_degrees': args.heading, 'source_bounds': [list(original_min), list(original_max)], 'normalized_bounds': [list(normalized_min), list(normalized_max)], 'matrix': [list(row) for row in normalization]},
    'mesh_objects': len(mesh_objects),
    'vertices': sum(item['vertices'] for item in model_info),
    'polygons': sum(item['polygons'] for item in model_info),
    'meshes': model_info,
    'materials': material_data,
    'textures': images,
    'external_source_dependencies': external_files,
    'armatures': sum(obj.type == 'ARMATURE' for obj in imported),
    'bones': sum(len(obj.data.bones) for obj in imported if obj.type == 'ARMATURE'),
    'actions': len(bpy.data.actions),
    'gltf_animations': len(glb_json.get('animations', [])),
    'render_size': args.size,
    'samples': args.samples,
    'views': list(cameras),
    'renders_completed': [],
}
report_path = output / 'import-report.json'
report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
if not args.no_render:
    for name, camera in cameras.items():
        scene.camera = camera
        scene.render.filepath = str(output / (name + '.png'))
        print('RENDER_IMPORTED_GLB', name, flush=True)
        bpy.ops.render.render(write_still=True)
        report['renders_completed'].append(name)
        report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print('MESHY_GLB_IMPORT_COMPLETE', str(blend), flush=True)
