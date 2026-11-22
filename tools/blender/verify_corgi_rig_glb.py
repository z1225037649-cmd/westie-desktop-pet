"""Independently decode, animate and skin the deliverable GLB using NumPy.

This verifier shares no animation or matrix code with the authoring/rendering
pipeline. It checks the actual binary deliverable, including hierarchy, bind
matrices, morph data, weights and animated evaluated surface coordinates.
"""
import argparse
import base64
import hashlib
import io
import json
import math
import struct
from collections import Counter
from pathlib import Path

import numpy as np
from PIL import Image


COMPONENT = {5120: np.dtype('i1'), 5121: np.dtype('u1'), 5122: np.dtype('<i2'),
             5123: np.dtype('<u2'), 5125: np.dtype('<u4'), 5126: np.dtype('<f4')}
WIDTH = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT2': 4, 'MAT3': 9, 'MAT4': 16}


class BinaryAsset:
    def __init__(self, path):
        self.path = Path(path)
        self.raw = self.path.read_bytes()
        assert len(self.raw) >= 20, 'Truncated GLB.'
        magic, version, length = struct.unpack_from('<4sII', self.raw)
        assert magic == b'glTF' and version == 2 and length == len(self.raw), 'Invalid GLB header.'
        chunks, cursor = [], 12
        while cursor < length:
            assert cursor + 8 <= length, 'Truncated chunk header.'
            size, kind = struct.unpack_from('<II', self.raw, cursor)
            assert size % 4 == 0 and cursor + 8 + size <= length, 'Invalid chunk bounds/alignment.'
            chunks.append((kind, self.raw[cursor + 8:cursor + 8 + size]))
            cursor += size + 8
        assert chunks[0][0] == 0x4E4F534A and sum(kind == 0x4E4F534A for kind, _ in chunks) == 1
        self.data = json.loads(chunks[0][1].decode('utf-8').rstrip('\x00 \t\r\n'))
        assert self.data.get('asset', {}).get('version') == '2.0'
        binary = [data for kind, data in chunks if kind == 0x004E4942]
        assert len(binary) <= 1
        self.buffers = []
        for index, buffer in enumerate(self.data.get('buffers', [])):
            uri = buffer.get('uri')
            if uri:
                assert uri.startswith('data:') and ';base64,' in uri, 'External buffer dependency.'
                payload = base64.b64decode(uri.split(',', 1)[1], validate=True)
            else:
                assert index == 0 and binary, 'Missing GLB BIN buffer.'
                payload = binary[0]
            assert buffer['byteLength'] <= len(payload)
            self.buffers.append(payload[:buffer['byteLength']])
        for view in self.data.get('bufferViews', []):
            assert 0 <= view['buffer'] < len(self.buffers)
            assert view.get('byteOffset', 0) >= 0 and view['byteLength'] >= 0
            assert view.get('byteOffset', 0) + view['byteLength'] <= len(self.buffers[view['buffer']])
        self.cache = {}
        for index in range(len(self.data.get('accessors', []))):
            array = self.accessor(index)
            assert np.isfinite(array).all(), f'Non-finite accessor {index}.'

    def view_array(self, view_index, offset, count, dtype, width, stride=None):
        view = self.data['bufferViews'][view_index]
        item = dtype.itemsize * width
        stride = stride or item
        assert stride >= item and stride % dtype.itemsize == 0
        assert offset >= 0 and offset % dtype.itemsize == 0
        size = (count - 1) * stride + item if count else 0
        assert offset + size <= view['byteLength'], 'Accessor exceeds its buffer view.'
        buffer = self.buffers[view['buffer']]
        start = view.get('byteOffset', 0) + offset
        assert start % dtype.itemsize == 0
        return np.ndarray((count, width), dtype=dtype, buffer=buffer, offset=start,
                          strides=(stride, dtype.itemsize)).copy()

    def accessor(self, index):
        if index in self.cache:
            return self.cache[index]
        assert 0 <= index < len(self.data['accessors'])
        item = self.data['accessors'][index]
        assert item['componentType'] in COMPONENT and item['type'] in WIDTH and item['count'] >= 0
        dtype, width = COMPONENT[item['componentType']], WIDTH[item['type']]
        if 'bufferView' in item:
            view = self.data['bufferViews'][item['bufferView']]
            result = self.view_array(item['bufferView'], item.get('byteOffset', 0), item['count'],
                                     dtype, width, view.get('byteStride'))
        else:
            result = np.zeros((item['count'], width), dtype=dtype)
        if 'sparse' in item:
            sparse = item['sparse']
            assert sparse['count'] <= item['count']
            inds, vals = sparse['indices'], sparse['values']
            assert inds['componentType'] in (5121, 5123, 5125)
            idx = self.view_array(inds['bufferView'], inds.get('byteOffset', 0), sparse['count'],
                                  COMPONENT[inds['componentType']], 1).ravel()
            assert len(idx) == 0 or (idx[-1] < item['count'] and np.all(np.diff(idx.astype(np.int64)) > 0))
            result[idx] = self.view_array(vals['bufferView'], vals.get('byteOffset', 0), sparse['count'], dtype, width)
        if item.get('normalized'):
            assert dtype.kind in 'iu'
            limit = np.iinfo(dtype)
            result = result.astype(np.float64) / limit.max
            if dtype.kind == 'i':
                result = np.maximum(result, -1)
        self.cache[index] = result
        return result

    def image_bytes(self, image):
        if 'bufferView' in image:
            view = self.data['bufferViews'][image['bufferView']]
            start = view.get('byteOffset', 0)
            return self.buffers[view['buffer']][start:start + view['byteLength']]
        uri = image['uri']
        assert uri.startswith('data:') and ';base64,' in uri, 'External image dependency.'
        return base64.b64decode(uri.split(',', 1)[1], validate=True)


def quaternion_matrix(q):
    q = np.asarray(q, dtype=float)
    assert q.shape == (4,) and np.isfinite(q).all()
    norm = np.linalg.norm(q)
    assert norm > 1e-10
    x, y, z, w = q / norm
    return np.array([[1 - 2*y*y - 2*z*z, 2*x*y - 2*z*w, 2*x*z + 2*y*w],
                     [2*x*y + 2*z*w, 1 - 2*x*x - 2*z*z, 2*y*z - 2*x*w],
                     [2*x*z - 2*y*w, 2*y*z + 2*x*w, 1 - 2*x*x - 2*y*y]])


def spherical(a, b, amount):
    a, b = np.asarray(a, float), np.asarray(b, float)
    a /= np.linalg.norm(a)
    b /= np.linalg.norm(b)
    dot = float(np.dot(a, b))
    if dot < 0:
        b, dot = -b, -dot
    dot = np.clip(dot, -1, 1)
    if dot > .9995:
        value = a + amount * (b - a)
    else:
        angle = math.acos(dot)
        value = (math.sin((1 - amount) * angle) * a + math.sin(amount * angle) * b) / math.sin(angle)
    return value / np.linalg.norm(value)


class SceneEvaluator:
    def __init__(self, asset):
        self.asset, self.data = asset, asset.data
        self.nodes = self.data['nodes']
        self.parents = [-1] * len(self.nodes)
        for parent, node in enumerate(self.nodes):
            for child in node.get('children', []):
                assert 0 <= child < len(self.nodes) and self.parents[child] == -1
                self.parents[child] = parent
        self.specs = []
        for node_index, node in enumerate(self.nodes):
            if 'mesh' not in node or 'skin' not in node:
                continue
            skin = self.data['skins'][node['skin']]
            joints = np.asarray(skin['joints'], dtype=np.int64)
            assert len(joints) > 0 and len(np.unique(joints)) == len(joints)
            assert joints.min() >= 0 and joints.max() < len(self.nodes)
            inverse_bind = asset.accessor(skin['inverseBindMatrices']).astype(float).reshape(-1, 4, 4).transpose(0, 2, 1)
            assert len(inverse_bind) == len(joints)
            mesh = self.data['meshes'][node['mesh']]
            for primitive_index, primitive in enumerate(mesh['primitives']):
                assert primitive.get('mode', 4) == 4, 'Expected a triangle skinned surface.'
                attrs = primitive['attributes']
                positions = asset.accessor(attrs['POSITION']).astype(float)
                indices = asset.accessor(primitive['indices']).ravel().astype(np.int64)
                assert len(indices) % 3 == 0 and len(indices) > 0
                assert indices.min() >= 0 and indices.max() < len(positions)
                joint_arrays, weight_arrays = [], []
                for suffix in range(2):
                    if f'JOINTS_{suffix}' not in attrs:
                        break
                    joint_arrays.append(asset.accessor(attrs[f'JOINTS_{suffix}']).astype(np.int64))
                    weight_arrays.append(asset.accessor(attrs[f'WEIGHTS_{suffix}']).astype(float))
                assert joint_arrays, 'No real JOINTS/WEIGHTS skin attributes.'
                influences, weights = np.concatenate(joint_arrays, axis=1), np.concatenate(weight_arrays, axis=1)
                assert influences.shape == weights.shape and len(weights) == len(positions)
                assert influences.min() >= 0 and influences.max() < len(joints)
                assert weights.min() >= -1e-7 and weights.max() <= 1 + 1e-7
                assert np.max(np.abs(weights.sum(axis=1) - 1)) < 2e-4, 'Skin weights are not normalized.'
                morphs = [asset.accessor(target['POSITION']).astype(float) for target in primitive.get('targets', [])]
                assert all(target.shape == positions.shape for target in morphs)
                names = mesh.get('extras', {}).get('targetNames', [])
                assert len(names) == len(morphs), 'Morph target names missing or inconsistent.'
                triangles = indices.reshape(-1, 3)
                edges = np.unique(np.sort(np.concatenate((triangles[:, [0, 1]], triangles[:, [1, 2]], triangles[:, [2, 0]])), axis=1), axis=0)
                self.specs.append({'node': node_index, 'primitive': primitive_index, 'mesh': mesh,
                                   'positions': positions, 'joints': joints, 'ibm': inverse_bind,
                                   'influences': influences, 'weights': weights, 'morphs': morphs,
                                   'morph_names': names, 'edges': edges, 'triangles': triangles,
                                   'attributes': attrs,
                                   'deformation_role': primitive.get('extras', {}).get('deformationRole', 'surface')})
        assert self.specs, 'No skinned mesh in the exported asset.'
        self.animations = {animation['name']: animation for animation in self.data.get('animations', [])}
        assert len(self.animations) == len(self.data.get('animations', [])), 'Duplicate animation names.'
        self.track_data = {}
        for name, animation in self.animations.items():
            paths, tracks = set(), []
            for channel in animation['channels']:
                target = channel['target']
                node, path = target['node'], target['path']
                assert 0 <= node < len(self.nodes) and path in ('translation', 'rotation', 'scale', 'weights')
                assert (node, path) not in paths and 'matrix' not in self.nodes[node]
                paths.add((node, path))
                sampler = animation['samplers'][channel['sampler']]
                times = asset.accessor(sampler['input']).ravel().astype(float)
                assert len(times) > 1 and np.all(np.diff(times) > 0) and times[0] >= 0
                interpolation = sampler.get('interpolation', 'LINEAR')
                assert interpolation in ('LINEAR', 'STEP', 'CUBICSPLINE')
                width = {'translation': 3, 'rotation': 4, 'scale': 3}.get(path)
                if width is None:
                    mesh = self.data['meshes'][self.nodes[node]['mesh']]
                    width = len(mesh['primitives'][0].get('targets', []))
                    assert width > 0
                factor = 3 if interpolation == 'CUBICSPLINE' else 1
                values = asset.accessor(sampler['output']).astype(float)
                assert values.size == len(times) * width * factor, f'Invalid animation output width: {name}/{path}.'
                values = values.reshape(len(times), factor, width) if factor == 3 else values.reshape(len(times), width)
                if path == 'rotation':
                    quaternions = values[:, 1] if factor == 3 else values
                    assert np.max(np.abs(np.linalg.norm(quaternions, axis=1) - 1)) < 1e-3
                tracks.append((node, path, times, values, interpolation))
            assert tracks, f'Empty animation {name}.'
            self.track_data[name] = tracks

    @staticmethod
    def sample_track(track, time):
        node, path, times, values, interpolation = track
        if time <= times[0]:
            return values[0, 1].copy() if interpolation == 'CUBICSPLINE' else values[0].copy()
        if time >= times[-1]:
            return values[-1, 1].copy() if interpolation == 'CUBICSPLINE' else values[-1].copy()
        left = int(np.searchsorted(times, time, side='right') - 1)
        duration = times[left + 1] - times[left]
        u = (time - times[left]) / duration
        if interpolation == 'STEP':
            return values[left].copy()
        if interpolation == 'CUBICSPLINE':
            value = ((2*u**3 - 3*u**2 + 1)*values[left, 1] + (u**3 - 2*u**2 + u)*duration*values[left, 2]
                     + (-2*u**3 + 3*u**2)*values[left + 1, 1] + (u**3 - u**2)*duration*values[left + 1, 0])
            return value / np.linalg.norm(value) if path == 'rotation' else value
        if path == 'rotation':
            return spherical(values[left].copy(), values[left + 1].copy(), u)
        return values[left] * (1 - u) + values[left + 1] * u

    def world_matrices(self, animation=None, time=0):
        overrides, morph_weights = {}, {}
        for track in self.track_data.get(animation, []):
            node, path = track[:2]
            value = self.sample_track(track, time)
            if path == 'weights':
                morph_weights[node] = value
            else:
                overrides.setdefault(node, {})[path] = value
        local = []
        for index, node in enumerate(self.nodes):
            if 'matrix' in node:
                matrix = np.asarray(node['matrix'], float).reshape(4, 4).T
            else:
                pose = overrides.get(index, {})
                translation = np.asarray(pose.get('translation', node.get('translation', [0, 0, 0])), float)
                rotation = pose.get('rotation', node.get('rotation', [0, 0, 0, 1]))
                scale = np.asarray(pose.get('scale', node.get('scale', [1, 1, 1])), float)
                # glTF permits a zero scale to hide an unskinned prop. Skinned
                # joint transforms are checked separately before evaluation.
                assert np.isfinite(translation).all() and np.isfinite(scale).all()
                matrix = np.eye(4)
                matrix[:3, :3] = quaternion_matrix(rotation) @ np.diag(scale)
                matrix[:3, 3] = translation
            assert np.isfinite(matrix).all()
            local.append(matrix)
        world, visiting = [None] * len(local), set()

        def resolve(index):
            if world[index] is not None:
                return world[index]
            assert index not in visiting, 'Cyclic node hierarchy.'
            visiting.add(index)
            parent = self.parents[index]
            world[index] = local[index] if parent == -1 else resolve(parent) @ local[index]
            visiting.remove(index)
            return world[index]

        for index in range(len(local)):
            resolve(index)
        return np.asarray(world), morph_weights

    def evaluate(self, animation=None, time=0, forced_morph=None):
        world, animated_weights = self.world_matrices(animation, time)
        surfaces = []
        for spec in self.specs:
            node_index, mesh = spec['node'], spec['mesh']
            weights = np.asarray(animated_weights.get(node_index, self.nodes[node_index].get('weights', mesh.get('weights', [0] * len(spec['morphs'])))))
            if forced_morph is not None:
                weights = np.zeros(len(spec['morphs']))
                if forced_morph in spec['morph_names']:
                    weights[spec['morph_names'].index(forced_morph)] = 1
            assert len(weights) == len(spec['morphs']) and np.isfinite(weights).all()
            positions = spec['positions'].copy()
            for amount, delta in zip(weights, spec['morphs']):
                positions += amount * delta
            homogeneous = np.column_stack((positions, np.ones(len(positions))))
            assert np.min(np.abs(np.linalg.det(world[spec['joints'], :3, :3]))) > 1e-8, 'Singular skinned joint transform.'
            palette = world[spec['joints']] @ spec['ibm']
            selected = palette[spec['influences']]
            result = np.einsum('nvij,nj,nv->ni', selected, homogeneous, spec['weights'], optimize=True)
            assert np.isfinite(result).all() and np.max(np.abs(result[:, 3] - 1)) < 3e-4
            surfaces.append(result[:, :3])
        return surfaces, world


def verify(asset, manifest):
    evaluator = SceneEvaluator(asset)
    rest, worlds = evaluator.evaluate()
    joined = np.concatenate(rest)
    bounds = [joined.min(axis=0), joined.max(axis=0)]
    ground_axis = manifest.get('ground_axis', 'Y').upper()
    axis = {'X': 0, 'Y': 1, 'Z': 2}[ground_axis]
    height = float(bounds[1][axis] - bounds[0][axis])
    assert height > 0
    floor = float(manifest.get('ground_height', 0))
    tolerance = float(manifest.get('ground_tolerance', height * .015))
    checks = {'status': 'passed', 'independent_numpy_skinning': True,
              'rig_sha256': hashlib.sha256(asset.raw).hexdigest(),
              'ground_axis': ground_axis, 'rest_bounds': [bound.tolist() for bound in bounds],
              'skinned_primitives': len(evaluator.specs), 'skin_checks': [], 'morph_checks': {}, 'animations': {}, 'contacts': {}}
    for spec in evaluator.specs:
        mesh_world = worlds[spec['node']]
        bind_error = float(np.max(np.abs(worlds[spec['joints']] @ spec['ibm'] - mesh_world)))
        assert bind_error < 2e-4, f'Inverse bind matrices inconsistent with bind pose: {bind_error}.'
        checks['skin_checks'].append({'node': evaluator.nodes[spec['node']].get('name', ''),
                                     'vertices': len(spec['positions']), 'triangles': len(spec['triangles']),
                                     'bones': len(spec['joints']), 'weight_sum_max_error': float(np.max(np.abs(spec['weights'].sum(axis=1) - 1))),
                                     'inverse_bind_max_error': bind_error, 'deformation_role': spec['deformation_role']})
    morph_names = set(name for spec in evaluator.specs for name in spec['morph_names'])
    required_morphs = manifest.get('required_shape_keys', ['Blink.L', 'Blink.R', 'JawOpen', 'Smile'])
    assert set(required_morphs).issubset(morph_names)
    for name in required_morphs:
        morphed, _ = evaluator.evaluate(forced_morph=name)
        displacement = np.concatenate([np.linalg.norm(actual - original, axis=1) for actual, original in zip(morphed, rest)])
        maximum, affected = float(displacement.max()), int(np.count_nonzero(displacement > height * 1e-7))
        assert maximum > height * 1e-5 and affected >= 3, f'Morph {name} is empty or has no real evaluated effect.'
        checks['morph_checks'][name] = {'affected_vertices': affected, 'max_evaluated_displacement': maximum}
    required = manifest.get('required_animations', ['Idle', 'Pet', 'Eat', 'PlayBall', 'Sleep', 'Wake'])
    assert set(required).issubset(evaluator.animations), 'Required interaction animation missing.'
    base_edges = [np.linalg.norm(points[spec['edges'][:, 0]] - points[spec['edges'][:, 1]], axis=1)
                  for spec, points in zip(evaluator.specs, rest)]
    sample_overrides = {entry['name']: entry for entry in manifest.get('actions', [])}
    endpoint_surfaces = {}
    tested_names = list(dict.fromkeys(required + list(sample_overrides)))
    assert set(tested_names).issubset(evaluator.animations), 'Manifest lists an animation absent from GLB.'
    for name in tested_names:
        tracks = evaluator.track_data[name]
        start, end = min(track[2][0] for track in tracks), max(track[2][-1] for track in tracks)
        assert end > start
        entry = sample_overrides.get(name, {})
        times = sorted(set(float(time) for time in np.linspace(start, end, 19)) | set(entry.get('sample_times', [])))
        maximum, minimum_ground, edge_growth = 0., float('inf'), 0.
        first, last = None, None
        for time in times:
            assert start <= time <= end
            surfaces, _ = evaluator.evaluate(name, time)
            if first is None:
                first = surfaces
            last = surfaces
            for spec, points, original, lengths in zip(evaluator.specs, surfaces, rest, base_edges):
                maximum = max(maximum, float(np.max(np.linalg.norm(points - original, axis=1))))
                minimum_ground = min(minimum_ground, float(points[:, axis].min()))
                animated = np.linalg.norm(points[spec['edges'][:, 0]] - points[spec['edges'][:, 1]], axis=1)
                if spec['deformation_role'] != 'eyelid_cover':
                    edge_growth = max(edge_growth, float(np.max(animated - lengths)))
        assert maximum > height * .002, f'{name} contains tracks but produces no visible surface movement.'
        assert minimum_ground >= floor - tolerance, f'{name} penetrates ground by {floor - minimum_ground:.6f}.'
        allowed_growth = float(entry.get('max_edge_growth', manifest.get('max_edge_growth', .15 * height)))
        assert edge_growth < allowed_growth, f'{name} tears/stretches a mesh edge by {edge_growth:.6f}.'
        return_error = max(float(np.max(np.linalg.norm(end_points - start_points, axis=1))) for start_points, end_points in zip(first, last))
        if entry.get('loop', name in ('Idle', 'Pet', 'Eat', 'PlayBall')):
            assert return_error < height * .02, f'{name} ends without recovering its initial pose.'
        if entry.get('returns_to_rest'):
            rest_return = max(float(np.max(np.linalg.norm(end_points - original, axis=1))) for end_points, original in zip(last, rest))
            assert rest_return < height * .02, f'{name} does not end at the shared rest pose.'
        checks['animations'][name] = {'duration_seconds': float(end - start), 'sample_count': len(times),
                                      'max_surface_motion': maximum, 'min_ground_coordinate': minimum_ground,
                                      'max_edge_growth': edge_growth, 'endpoint_distance': return_error}
        endpoint_surfaces[name] = (first, last)
        if name in ('Eat', 'PlayBall'):
            assert 'contact_time' in entry, f'{name} lacks an auditable contact time.'
        if 'contact_time' in entry:
            contact_time = float(entry['contact_time'])
            assert start <= contact_time < end
            is_mouth_contact = entry.get('contact_kind') == 'mouth_food' or name == 'Eat'
            anchor_name = entry.get('mouth_anchor') if is_mouth_contact else entry.get('paw_anchor')
            prop_name = entry['prop_node']
            named = {node.get('name'): index for index, node in enumerate(evaluator.nodes)}
            assert anchor_name in named and prop_name in named, f'Missing contact anchor/prop: {name}.'
            points, contact_world = evaluator.evaluate(name, contact_time)
            anchor = contact_world[named[anchor_name]][:3, 3]
            prop_world = contact_world[named[prop_name]]
            prop_center = prop_world[:3, 3]
            anchor_surface_distance = min(float(np.linalg.norm(surface - anchor, axis=1).min()) for surface in points)
            assert anchor_surface_distance < height * .05, f'{name} anchor floats away from the evaluated skin.'
            prop_node = evaluator.nodes[named[prop_name]]
            if 'mesh' in prop_node:
                prop_vertices = []
                for primitive in asset.data['meshes'][prop_node['mesh']]['primitives']:
                    positions = asset.accessor(primitive['attributes']['POSITION']).astype(float)
                    prop_vertices.append((prop_world @ np.column_stack((positions, np.ones(len(positions)))).T).T[:, :3])
                prop_vertices = np.concatenate(prop_vertices)
                radius = float(np.linalg.norm(prop_vertices - prop_center, axis=1).max())
            else:
                radius = float(entry['prop_radius'])
            distance = float(np.linalg.norm(anchor - prop_center))
            contact_gap = abs(distance - radius)
            contact_tolerance = float(entry.get('contact_tolerance', max(height * .025, radius * .45)))
            assert contact_gap <= contact_tolerance, f'{name} misses prop contact: gap {contact_gap:.6f}.'
            contact_result = {'time_seconds': contact_time, 'anchor': anchor_name, 'prop': prop_name,
                              'anchor_skin_distance': anchor_surface_distance, 'prop_radius': radius,
                              'center_distance': distance, 'contact_surface_gap': contact_gap}
            if name == 'PlayBall' or entry.get('contact_kind') == 'paw_ball':
                before_world, _ = evaluator.world_matrices(name, max(start, contact_time - .04))
                start_world, _ = evaluator.world_matrices(name, start)
                after_world, _ = evaluator.world_matrices(name, min(end, contact_time + max(.1, (end - contact_time) * .3)))
                pre_motion = float(np.linalg.norm(before_world[named[prop_name]][:3, 3] - start_world[named[prop_name]][:3, 3]))
                post_motion = float(np.linalg.norm(after_world[named[prop_name]][:3, 3] - prop_center))
                if not entry.get('before_contact_motion', False):
                    assert pre_motion < height * .001, 'Ball moves before paw contact.'
                assert post_motion > height * .015, 'Ball does not move after contact.'
                contact_result.update({'pre_contact_motion': pre_motion, 'post_contact_motion': post_motion})
            checks['contacts'][name] = contact_result
    if 'Sleep' in endpoint_surfaces and 'Wake' in endpoint_surfaces:
        continuity = max(float(np.max(np.linalg.norm(a - b, axis=1))) for a, b in zip(endpoint_surfaces['Sleep'][1], endpoint_surfaces['Wake'][0]))
        assert continuity < height * .02, 'Sleep endpoint and Wake initial pose are discontinuous.'
        checks['sleep_wake_continuity_error'] = continuity
    checks['packed_images'] = []
    for image in asset.data.get('images', []):
        raw = asset.image_bytes(image)
        decoded = Image.open(io.BytesIO(raw))
        decoded.load()
        assert min(decoded.size) > 0
        checks['packed_images'].append({'size': list(decoded.size), 'sha256': hashlib.sha256(raw).hexdigest()})
    assert len(checks['packed_images']) >= 3, 'Expected the original embedded PBR textures.'
    original_path = asset.path.parent.parent / 'source/corgi-meshy.glb'
    assert original_path.is_file(), 'Original GLB provenance file is missing.'
    original = BinaryAsset(original_path)
    original_sha = hashlib.sha256(original.raw).hexdigest()
    import_report_path = asset.path.parent.parent / 'blender/import-report.json'
    original_report = json.loads(import_report_path.read_text(encoding='utf-8'))
    assert original_sha == original_report['source_sha256'], 'Original source GLB was changed.'
    original_image_hashes = Counter(hashlib.sha256(original.image_bytes(image)).hexdigest() for image in original.data.get('images', []))
    rig_image_hashes = Counter(image['sha256'] for image in checks['packed_images'])
    assert not original_image_hashes - rig_image_hashes, 'Original embedded texture bytes were changed or dropped.'
    original_primitive = original.data['meshes'][0]['primitives'][0]
    original_uv = original.accessor(original_primitive['attributes']['TEXCOORD_0'])
    matching_uv = [spec for spec in evaluator.specs if len(spec['positions']) >= len(original_uv) and 'TEXCOORD_0' in spec['attributes']]
    assert matching_uv, 'The original textured surface could not be identified.'
    assert any(np.array_equal(asset.accessor(spec['attributes']['TEXCOORD_0'])[:len(original_uv)], original_uv) for spec in matching_uv), 'Original surface UV coordinates changed.'
    checks.update({'original_source_sha256': original_sha, 'original_texture_bytes_preserved': True, 'original_surface_uv_preserved': True})
    return checks


def main():
    root = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser()
    parser.add_argument('--input', default=str(root / 'art/corgi-image-to-3d/rigged/corgi-rigged.glb'))
    parser.add_argument('--manifest')
    args = parser.parse_args()
    path = Path(args.input).resolve()
    manifest_path = Path(args.manifest) if args.manifest else path.parent / 'rig-manifest.json'
    manifest = json.loads(manifest_path.read_text(encoding='utf-8')) if manifest_path.is_file() else {}
    output = path.parent / 'rig-checks.json'
    try:
        checks = verify(BinaryAsset(path), manifest)
    except Exception as error:
        output.write_text(json.dumps({'status': 'failed', 'error': str(error)}, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
        raise
    output.write_text(json.dumps(checks, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print('CORGI_RIG_GLB_VERIFIED', json.dumps(checks, ensure_ascii=False))


if __name__ == '__main__':
    main()
