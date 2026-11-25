"""Author a real skinned, facial-morph, multi-action GLB without modifying its source.

The generated sitting mesh gets a 24-joint rig, split lip topology, an inner
mouth, and curved skin eyelids. All original texture images remain embedded.
"""
import hashlib
import json
import math
import io
from pathlib import Path
import numpy as np
from PIL import Image, ImageFilter

from corgi_glb_io import Builder, accessor, read_glb
from corgi_rig_math import AXIS, BONES, DURATIONS, INDEX, PARENTS, PIVOTS, SHAPES, SLEEP_STEPS, SLEEP_CURVATURE, hierarchy, matrix_quaternion, pose, skin, smooth

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'art/corgi-image-to-3d/rigged'
OUT.mkdir(parents=True, exist_ok=True)
previous_manifest = json.loads((OUT / 'rig-manifest.json').read_text(encoding='utf-8')) if (OUT / 'rig-manifest.json').exists() else {}
SOURCE = ROOT / 'art/corgi-image-to-3d/source/corgi-meshy.glb'
doc, binary = read_glb(SOURCE)
original = doc['meshes'][0]['primitives'][0]
raw = accessor(doc, binary, original['attributes']['POSITION']).astype(float)
raw_n = accessor(doc, binary, original['attributes']['NORMAL']).astype(float)
uv = accessor(doc, binary, original['attributes']['TEXCOORD_0']).astype(float)
tri = accessor(doc, binary, original['indices']).reshape((-1, 3))
scale = 2 / np.ptp(raw[:, 1])
origin = (raw.max(axis=0) + raw.min(axis=0)) / 2
origin[1] = raw[:, 1].min()
points = ((raw - origin) * scale) @ AXIS
normals = raw_n @ AXIS
source_points = points.copy()
source_uv = uv.copy()


def lip_height(x):
    return 1.208 + 4.75 * np.minimum(np.abs(x), .18) ** 2


def split_lips(p, n, texture_uv, faces):
    """Split triangles exactly along the lip curve; coincident rest boundaries.

    This creates an opening when the lower lip moves, instead of stretching a
    sealed polygon over the inside of the mouth. UV interpolation retains the
    rest texture. Seam duplicates are independently weighted to head and jaw.
    """
    positions, directions, texcoords = list(p), list(n), list(texture_uv)
    new_faces = []
    lower_boundary, upper_boundary = set(), set()
    value = p[:, 2] - lip_height(p[:, 0])
    edge_cache = {}
    cut_count = 0
    for face in faces:
        center = p[face].mean(axis=0)
        if not (abs(center[0]) < .166 and center[1] < -.68 + 4.8 * center[0] ** 2 and abs(center[2] - lip_height(center[0])) < .025) or not (value[face].min() < 0 < value[face].max()):
            new_faces.append(tuple(face))
            continue
        cut_count += 1
        for lower in (False, True):
            clipped = []
            for index in range(3):
                a, b = int(face[index]), int(face[(index + 1) % 3])
                va, vb = value[a], value[b]
                inside_a = va <= 0 if lower else va >= 0
                inside_b = vb <= 0 if lower else vb >= 0
                if inside_a:
                    clipped.append(a)
                if inside_a != inside_b:
                    key = (min(a, b), max(a, b), lower)
                    if key not in edge_cache:
                        factor = va / (va - vb)
                        positions.append(p[a] * (1 - factor) + p[b] * factor)
                        normal = n[a] * (1 - factor) + n[b] * factor
                        directions.append(normal / np.linalg.norm(normal))
                        texcoords.append(texture_uv[a] * (1 - factor) + texture_uv[b] * factor)
                        edge_cache[key] = len(positions) - 1
                    result = edge_cache[key]
                    (lower_boundary if lower else upper_boundary).add(result)
                    clipped.append(result)
            for i in range(1, len(clipped) - 1):
                new_faces.append((clipped[0], clipped[i], clipped[i + 1]))
    assert cut_count > 10, 'Lip region was not located.'
    return np.array(positions), np.array(directions), np.array(texcoords), np.array(new_faces), lower_boundary, upper_boundary, cut_count


points, normals, uv, tri, lower_lip, upper_lip, split_count = split_lips(points, normals, uv, tri)


def weight_surface(p, lower=(), upper=()):
    x, y, z = p.T
    w = np.zeros((len(p), len(BONES)))
    head = smooth(1.02, 1.17, z)
    chest = smooth(.43, .92, z) * smooth(.22, -.16, y) * (1 - head)
    w[:, INDEX['Pelvis']] = (1 - chest - head) * (1 - smooth(.30, .62, z))
    w[:, INDEX['Spine']] = 1 - chest - head - w[:, INDEX['Pelvis']]
    w[:, INDEX['Chest']] = chest
    w[:, INDEX['Head']] = head
    # Separate ears and muzzle from the rigid skull, with soft root transitions.
    ear = smooth(1.47, 1.65, z) * smooth(.20, .30, abs(x))
    for side, sign in [('L', 1), ('R', -1)]:
        mask = ear * smooth(-.015, .015, sign * x)
        w[:, INDEX['Head']] -= mask * head
        w[:, INDEX['Ear.' + side]] += mask * head
    jaw = smooth(-.50, -.58, y) * (z < lip_height(x)) * smooth(1.11, 1.19, z) * (1 - smooth(.13, .166, abs(x)))
    if lower:
        indices = np.array(sorted(lower))
        jaw[indices] = smooth(-.50, -.58, y[indices]) * (1 - smooth(.13, .166, abs(x[indices])))
    if upper:
        jaw[np.array(sorted(upper))] = 0
    w[:, INDEX['Head']] *= 1 - jaw
    w[:, INDEX['Jaw']] = jaw * head
    for side, sign in [('L', 1), ('R', -1)]:
        # Spatial envelopes keep the chest and the opposite leg out of the limb.
        front = np.exp(-((x - sign * .17) / .10) ** 4) * (1 - smooth(.32, .58, z)) * smooth(-.16, -.28, y) * (1 - smooth(-.51, -.62, y))
        hind = np.exp(-((x - sign * .25) / .14) ** 4) * (1 - smooth(.27, .47, z)) * smooth(.03, .23, y) * (1 - smooth(.55, .66, y))
        for mask, names, cuts in [(front, ['Paw.' + side, 'Forearm.' + side, 'Shoulder.' + side], (.12, .24, .31, .48)), (hind, ['HindPaw.' + side, 'Hock.' + side, 'Hip.' + side], (.07, .14, .17, .28))]:
            w *= (1 - mask[:, None])
            bottom = 1 - smooth(cuts[0], cuts[1], z)
            top = smooth(cuts[2], cuts[3], z)
            w[:, INDEX[names[0]]] += mask * bottom
            w[:, INDEX[names[1]]] += mask * (1 - bottom - top)
            w[:, INDEX[names[2]]] += mask * top
    # The generated curled tip is fused to the back. Keep the fused upper
    # surface on the torso; tail joints act only on the free lower curl.
    tail = smooth(.51, .67, y) * (1 - smooth(.50, .73, z))
    w *= 1 - tail[:, None]
    top = smooth(.49, .67, z)
    middle = smooth(.30, .47, z) * (1 - top)
    w[:, INDEX['TailRoot']] += tail * (1 - middle - top)
    w[:, INDEX['TailMid']] += tail * middle
    w[:, INDEX['TailTip']] += tail * top
    w = np.maximum(w, 0)
    choices = np.argsort(w, axis=1)[:, -4:][:, ::-1]
    values = np.take_along_axis(w, choices, axis=1)
    values /= values.sum(axis=1)[:, None]
    return choices.astype(np.uint16), values


joints, weights = weight_surface(points, lower_lip, upper_lip)
targets = np.zeros((len(SHAPES), len(points), 3), dtype=float)
x, y, z = points.T
lower_mask = smooth(-.50, -.58, y) * smooth(1.11, 1.19, z) * (1 - smooth(.13, .166, abs(x))) * (z < lip_height(x))
lower_mask[np.array(sorted(lower_lip))] = smooth(-.50, -.58, y[np.array(sorted(lower_lip))]) * (1 - smooth(.13, .166, abs(x[np.array(sorted(lower_lip))])))
lower_mask[np.array(sorted(upper_lip))] = 0
targets[2, :, 2] = -.052 * lower_mask
targets[2, :, 1] = .008 * lower_mask
smile = np.exp(-((abs(x) - .15) / .055) ** 2 - ((y + .61) / .13) ** 2 - ((z - 1.31) / .08) ** 2)
targets[3, :, 2] = .010 * smile
targets[3, :, 0] = np.sign(x) * .004 * smile
lie_mask = smooth(.16, .45, y) * smooth(.30, .61, z) * (1 - smooth(.92, 1.10, z))
targets[4, :, 1] = -.07 * lie_mask
targets[4, :, 2] = -.025 * lie_mask
# The source has a continuous breast surface between the forelegs. Bending
# that surface with independent limb envelopes folds it through itself.
# Bend transverse body sections around a circular centerline; each section
# retains its width and depth. Unlike a height squash, this leaves a rounded
# breast and belly even when the head settles forward. Invert the full sleep
# skinning to store the target in a bind-space morph.
sleep_r, sleep_o, sleep_m = pose('Sleep', DURATIONS['Sleep'])
sleep_world = hierarchy(sleep_r, sleep_o)
sleep_skin = sleep_world.copy()
sleep_skin[:, :3, 3] -= np.einsum('nij,nj->ni', sleep_skin[:, :3, :3], PIVOTS)
linear = np.sum(sleep_skin[joints, :3, :3] * weights[:, :, None, None], axis=1)
translation = np.sum(sleep_skin[joints, :3, 3] * weights[:, :, None], axis=1)
posed = skin(points, joints, weights, sleep_world, targets, sleep_m)
ordered = points.copy()
start, finish, curvature = .10, 1.05, SLEEP_CURVATURE
height = np.clip(z - start, 0, finish - start)
angle = curvature * height
ordered[:, 1] = y * np.cos(angle) - (1 - np.cos(angle)) / curvature
ordered[:, 2] = start + np.sin(angle) / curvature + y * np.sin(angle)
above = np.maximum(0, z - finish)
ordered[:, 1] -= above * np.sin(angle)
ordered[:, 2] += above * np.cos(angle)
below = z < start
ordered[below] = points[below]
# Match the full head to this rigid continuation rather than compressing it
# into a separately lowered torso surface.
head = smooth(1.01, 1.17, z)
head_matrix = sleep_skin[INDEX['Head']]
head_target = points @ head_matrix[:3, :3].T + head_matrix[:3, 3]
desired = ordered * (1 - head[:, None]) + head_target * head[:, None]
front = np.ones(len(points))
targets[4] = np.linalg.solve(linear, (desired - translation)[:, :, None])[:, :, 0] - points
# In-between correctives cancel the independent limb rotation throughout
# descent. A final-pose morph alone still rolls the ankle through the shin.
# The same progress-keyed shapes are played backwards when waking.
front_transition = front * (1 - smooth(.70, 1.17, z))
for shape, amount in enumerate(SLEEP_STEPS, 5):
    lo, hi = 0., 1.
    for _ in range(40):
        middle = (lo + hi) / 2
        if smooth(0, 1, middle) < amount: lo = middle
        else: hi = middle
    sample_r, sample_o, sample_m = pose('Sleep', .4 + 3.2 * (lo + hi) / 2)
    sample_world = hierarchy(sample_r, sample_o)
    sample_linear = np.sum(sample_world[joints, :3, :3] * weights[:, :, None, None], axis=1)
    sample_posed = skin(points, joints, weights, sample_world, targets, sample_m)
    sample_target = (1 - amount) * points + amount * desired
    targets[shape] = np.linalg.solve(sample_linear, ((sample_target - sample_posed) * front_transition[:, None])[:, :, None])[:, :, 0]

builder = Builder(doc, binary)
doc['nodes'] = []
doc['skins'] = []
doc['animations'] = []
doc['meshes'] = []
doc['asset']['generator'] = 'WestiePet corgi rig authoring; Meshy source textures and surface preserved, lip seam split and facial surfaces added'
doc['asset']['extras'] = {'source_sha256': hashlib.sha256(SOURCE.read_bytes()).hexdigest(), 'coordinate_system': 'glTF +Y up, original character faces +Z; height 2 units'}
doc['nodes'].append({'name': 'CorgiSkin', 'mesh': 0, 'skin': 0, 'weights': [0.0] * len(SHAPES)})
for index, (name, _, _) in enumerate(BONES):
    parent = PARENTS[index]
    translation = AXIS @ (PIVOTS[index] - (PIVOTS[parent] if parent >= 0 else 0))
    doc['nodes'].append({'name': name, 'translation': translation.tolist(), 'rotation': [0, 0, 0, 1], 'children': [], 'extras': {'joint': True}})
    if parent >= 0:
        doc['nodes'][parent + 1]['children'].append(index + 1)
ibm = np.repeat(np.eye(4)[None, :, :], len(BONES), axis=0)
ibm[:, :3, 3] = -(PIVOTS @ AXIS.T)
doc['skins'].append({'name': 'Corgi_24Joint_Skin', 'inverseBindMatrices': builder.add(ibm.transpose(0, 2, 1).reshape((-1, 16)), 'MAT4'), 'skeleton': 1, 'joints': list(range(1, len(BONES) + 1))})


def primitive(p, n, texture_uv, faces, j, w, morphs, material, role):
    attrs = {'POSITION': builder.add(p @ AXIS.T, 'VEC3', target=34962, bounds=True), 'NORMAL': builder.add(n @ AXIS.T, 'VEC3', target=34962), 'JOINTS_0': builder.add(j, 'VEC4', 5123, target=34962), 'WEIGHTS_0': builder.add(w, 'VEC4', target=34962)}
    if texture_uv is not None:
        attrs['TEXCOORD_0'] = builder.add(texture_uv, 'VEC2', target=34962)
    return {'attributes': attrs, 'indices': builder.add(np.asarray(faces).reshape(-1), 'SCALAR', 5125, target=34963), 'material': material, 'mode': 4, 'targets': [{'POSITION': builder.add(delta @ AXIS.T, 'VEC3', target=34962)} for delta in morphs], 'extras': {'deformationRole': role}}


primitives = [primitive(points, normals, uv, tri, joints, weights, targets, original['material'], 'body')]
preview_parts = [(points, normals, uv, tri, joints, weights, targets, original['material'], 'body')]


def grid_faces(cols, rows):
    faces = []
    for row in range(rows - 1):
        for col in range(cols - 1):
            a = row * cols + col
            faces.extend([(a, a + cols, a + 1), (a + 1, a + cols, a + cols + 1)])
    return np.array(faces, dtype=np.int32)


def closest_face(xpos, zpos):
    candidates = np.where((abs(source_points[:, 0] - xpos) < .032) & (abs(source_points[:, 2] - zpos) < .03) & (source_points[:, 1] < -.50))[0]
    assert len(candidates)
    score = ((source_points[candidates, 0] - xpos) / .022) ** 2 + ((source_points[candidates, 2] - zpos) / .018) ** 2
    chosen = candidates[np.argmin(score)]
    return source_points[chosen], source_uv[chosen]


# Build a small atlas from the actual neighboring gold and cream fur pixels.
# Atlas-island seams and original eye pixels are excluded from these patches.
source_texture = Image.open(ROOT / 'art/corgi-image-to-3d/rig-analysis/base-color.png').convert('RGB')
atlas = Image.new('RGB', (256,128))
for index, center_uv, tone in [(0,(.46385,.861446),(161,84,16)),(1,(.492059,.853959),(252,238,211))]:
    px, py = int(center_uv[0]*source_texture.width), int(center_uv[1]*source_texture.height)
    crop = np.array(source_texture.crop((px-24,py-24,px+24,py+24))).copy()
    if index == 0:
        valid = (crop[:,:,0]>95)&(crop[:,:,0]<225)&(crop[:,:,1]>45)&(crop[:,:,1]<160)&(crop[:,:,2]<120)
    else:
        valid = (crop[:,:,0]>218)&(crop[:,:,1]>198)&(crop[:,:,2]>155)
    median = np.median(crop[valid],axis=0) if valid.any() else np.array(tone)
    crop[~valid] = median.astype(np.uint8)
    crop = np.clip(median + (crop.astype(float)-median)*(.35 if index==0 else .18),0,255).astype(np.uint8)
    patch = Image.fromarray(crop).filter(ImageFilter.GaussianBlur(.45)).resize((128,128),Image.Resampling.BICUBIC)
    atlas.paste(patch,(index*128,0))
atlas.save(OUT/'eyelid-fur-atlas.png')
encoded = io.BytesIO();atlas.save(encoded,format='PNG')
while len(builder.binary)%4: builder.binary.append(0)
doc['bufferViews'].append({'buffer':0,'byteOffset':len(builder.binary),'byteLength':len(encoded.getvalue())})
builder.binary.extend(encoded.getvalue())
doc['images'].append({'name':'Eyelids | sampled neighboring fur','mimeType':'image/png','bufferView':len(doc['bufferViews'])-1})
doc.setdefault('samplers',[]).append({'magFilter':9729,'minFilter':9729,'wrapS':33071,'wrapT':33071})
doc['textures'].append({'source':len(doc['images'])-1,'sampler':len(doc['samplers'])-1})
lid_texture = len(doc['textures'])-1
# Upper and lower curved skin eyelids meet at a narrow closed-eye crease.
upper_lid_material = len(doc['materials'])
doc['materials'].append({'name': 'Upper eyelid | sampled neighboring gold fur', 'doubleSided': True, 'pbrMetallicRoughness': {'baseColorTexture':{'index':lid_texture},'metallicFactor': 0, 'roughnessFactor': .85}})
lower_lid_material = len(doc['materials'])
doc['materials'].append({'name': 'Lower eyelid | sampled neighboring cream fur', 'doubleSided': True, 'pbrMetallicRoughness': {'baseColorTexture':{'index':lid_texture},'metallicFactor': 0, 'roughnessFactor': .85}})
crease_material = len(doc['materials'])
doc['materials'].append({'name': 'Closed eyelid crease', 'doubleSided': True, 'pbrMetallicRoughness': {'baseColorFactor': [.30,.155,.063,1], 'metallicFactor': 0, 'roughnessFactor': .85}})
lid_report = []
for side, cx, cz in [('L', .119, 1.487), ('R', -.113, 1.480)]:
    cols, rows = 25, 11
    eye_candidates = (abs(source_points[:, 0] - cx) < .075) & (abs(source_points[:, 2] - cz) < .070) & (source_points[:, 1] < -.56)
    eye = source_points[eye_candidates]
    dx, dz = eye[:, 0] - cx, eye[:, 2] - cz
    surface_fit = np.linalg.lstsq(np.column_stack((np.ones(len(eye)), dx, dz, dx*dx, dz*dz, dx*dz)), eye[:, 1], rcond=None)[0]

    def lid_surface_y(xx, zz):
        a, b = xx - cx, zz - cz
        return np.array([1, a, b, a*a, b*b, a*b]) @ surface_fit
    base, closed, texture_uv = [], [], []
    for lid_kind, material in [('upper', upper_lid_material), ('lower', lower_lid_material), ('crease', crease_material)]:
        base, closed, eyelid_uv = [], [], []
        lid_rows = 2 if lid_kind == 'crease' else rows
        for row in range(lid_rows):
            v = row / (lid_rows - 1)
            for col in range(cols):
                u = col / (cols - 1)
                xx = cx + (u - .5) * .138
                arc = max(.09, math.sin(math.pi * u) ** .65)
                top, bottom, meeting = cz + .024 * arc, cz - .055 * arc, cz - .012 * arc
                edge = top if lid_kind != 'lower' else bottom
                if lid_kind == 'upper':
                    zz_closed = top * (1-v) + meeting * v
                elif lid_kind == 'lower':
                    zz_closed = bottom * (1-v) + meeting * v
                else:
                    zz_closed = meeting + (v-.5) * .0018 * arc
                zz = edge + .0005 * v
                base.append((xx,lid_surface_y(xx,zz)+.015,zz))
                depth = .005 + .011 * math.sin(math.pi*u) * math.sin(math.pi*v)
                if lid_kind=='crease': depth=.006+.012*math.sin(math.pi*u)
                closed.append((xx,lid_surface_y(xx,zz_closed)-depth,zz_closed))
                eyelid_uv.append((.52+.46*u if lid_kind=='lower' else .02+.46*u,.04+.92*v))
        p = np.array(base)
        normal = np.tile([0., -1., 0.], (len(p), 1))
        j = np.zeros((len(p),4),dtype=np.uint16);j[:,0]=INDEX['Head']
        w = np.zeros((len(p),4));w[:,0]=1
        morphs = np.zeros((len(SHAPES),len(p),3));morphs[SHAPES.index('Blink.'+side)]=np.array(closed)-p
        faces = grid_faces(cols,lid_rows)
        if lid_kind!='upper': faces=faces[:,[0,2,1]]
        args = (p,normal,np.array(eyelid_uv) if lid_kind!='crease' else None,faces,j,w,morphs,material,'eyelid_cover')
        primitives.append(primitive(*args));preview_parts.append(args)
    lid_report.append({'side':side,'center_blender':[cx,-.595,cz],'upper_lower_lids':True,'closed_crease':True})


def plain_material(name, color, roughness=.65):
    doc['materials'].append({'name': name, 'doubleSided': True, 'pbrMetallicRoughness': {'baseColorFactor': color, 'metallicFactor': 0, 'roughnessFactor': roughness}})
    return len(doc['materials']) - 1


oral_material = plain_material('Inner mouth | dark warm tissue', [.08, .021, .024, 1])
tongue_material = plain_material('Tongue | soft rose', [.57, .19, .24, 1])
tooth_material = plain_material('Teeth | warm ivory', [.89, .85, .72, 1])
cols, rows = 25, 5
oral_points, oral_open = [], []
for row in range(rows):
    v = row / (rows - 1)
    for col in range(cols):
        u = col / (cols - 1)
        xx = (u - .5) * .34
        arc = math.sin(math.pi * u)
        top = lip_height(xx)
        yy = -.723 + 5.9 * xx * xx + .006 + .012 * math.sin(math.pi * v)
        oral_points.append((xx, yy, top - .001 * v))
        oral_open.append((xx, yy + .004 * v, top - .078 * arc * v))
oral_points = np.array(oral_points)
oral_targets = np.zeros((len(SHAPES), len(oral_points), 3))
oral_targets[2] = np.array(oral_open) - oral_points
j = np.zeros((len(oral_points), 4), dtype=np.uint16); j[:, 0] = INDEX['Head']
w = np.zeros((len(oral_points), 4)); w[:, 0] = 1
args = (oral_points, np.tile([0, -1, 0], (len(oral_points), 1)), None, grid_faces(cols, rows), j, w, oral_targets, oral_material, 'oral_cavity')
primitives.append(primitive(*args)); preview_parts.append(args)


def sphere_geometry(center, radii, cols=20, rows=12):
    p, n = [], []
    for row in range(rows + 1):
        theta = math.pi * (.001 + (1 - .002) * row / rows)
        for col in range(cols + 1):
            phi = 2 * math.pi * col / cols
            direction = np.array([math.sin(theta) * math.cos(phi), math.sin(theta) * math.sin(phi), math.cos(theta)])
            p.append(np.array(center) + np.array(radii) * direction)
            normal = direction / np.array(radii)
            n.append(normal / np.linalg.norm(normal))
    return np.array(p), np.array(n), grid_faces(cols + 1, rows + 1)


# Tongue and two small teeth are tucked behind the lip in rest pose.
for center, radii, material, role in [((0, -.628, 1.198), (.060, .025, .008), tongue_material, 'tongue'), ((.070, -.656, 1.234), (.008, .011, .014), tooth_material, 'tooth'), ((-.070, -.656, 1.234), (.008, .011, .014), tooth_material, 'tooth')]:
    p, n, faces = sphere_geometry(center, radii, 12, 8)
    j = np.zeros((len(p), 4), dtype=np.uint16); j[:, 0] = INDEX['Jaw'] if role == 'tongue' else INDEX['Head']
    w = np.zeros((len(p), 4)); w[:, 0] = 1
    morphs = np.zeros((len(SHAPES), len(p), 3))
    if role == 'tongue':
        morphs[2, :, 2] = -.035
        morphs[2, :, 1] = -.060
    args = (p, n, None, faces, j, w, morphs, material, role)
    primitives.append(primitive(*args)); preview_parts.append(args)

doc['meshes'].append({'name': 'Corgi | skinned surface, split lips, eyelids and oral anatomy', 'primitives': primitives, 'weights': [0.] * len(SHAPES), 'extras': {'targetNames': SHAPES, 'source_triangle_count': int(len(accessor(doc, binary, original['indices'])) // 3)}})
anchors = {}
for name, parent, desired in [('MouthAnchor', 'Head', (0, -.723, 1.209)), ('PawAnchor', 'Paw.L', (.17, -.458, .075))]:
    if name == 'PawAnchor':
        mask = weights[:, 0] > .90
        mask &= joints[:, 0] == INDEX['Paw.L']
        selected = np.where(mask)[0]
        location = points[selected[np.argmin(np.linalg.norm(points[selected] - desired, axis=1))]]
    else:
        location = np.array(desired)
    node = len(doc['nodes'])
    doc['nodes'].append({'name': name, 'translation': (AXIS @ (location - PIVOTS[INDEX[parent]])).tolist(), 'extras': {'surface_anchor': True}})
    doc['nodes'][INDEX[parent] + 1]['children'].append(node)
    anchors[name] = (node, INDEX[parent], location)

prop_specs = [('Ball', .105, [.18, .48, .83, 1]), ('Treat', .035, [.73, .40, .15, 1])]
prop_nodes = {}
for name, radius, color in prop_specs:
    material = plain_material(name + ' | preview prop', color, .45)
    p, n, faces = sphere_geometry((0, 0, 0), (radius, radius, radius), 24, 12)
    doc['meshes'].append({'name': name, 'primitives': [{'attributes': {'POSITION': builder.add(p @ AXIS.T, 'VEC3', bounds=True), 'NORMAL': builder.add(n @ AXIS.T, 'VEC3')}, 'indices': builder.add(faces.reshape(-1), 'SCALAR', 5125), 'material': material, 'mode': 4}]})
    node = len(doc['nodes'])
    doc['nodes'].append({'name': name, 'mesh': len(doc['meshes']) - 1, 'translation': [0, 0, 0], 'scale': [.001] * 3, 'extras': {'interaction_prop': True, 'radius': radius}})
    prop_nodes[name] = node

doc['scenes'] = [{'name': 'Corgi rig and interaction props', 'nodes': [0, 1] + list(prop_nodes.values())}]
doc['scene'] = 0
manifest_actions = []
all_pose_data = {}
contact_rot, _, _ = pose('PlayBall', 1.5)
contact_world = hierarchy(contact_rot)
_, paw_parent, paw_location = anchors['PawAnchor']
paw_contact = contact_world[paw_parent] @ np.r_[paw_location - PIVOTS[paw_parent], 1]
ball_vertical_gap = paw_contact[2] - .105
assert abs(ball_vertical_gap)<.105,'Paw cannot reach the grounded ball.'
ball_contact = paw_contact[:3] + np.array([0,-math.sqrt(.105**2-ball_vertical_gap**2),-ball_vertical_gap])


def props(action, time, matrices):
    ball = ball_contact.copy()
    ball_scale = .001
    treat = np.array([0, -.9, 1.2])
    treat_scale = .001
    if action == 'PlayBall':
        ball_scale = float(smooth(0, .35, time)) * (1 - float(smooth(3.4, 4, time)))
        # No ball displacement before physical paw contact.
        distance = .65 * float(smooth(1.5, 3.4, time))
        ball[0] += .10 * distance
        ball[1] -= distance
        ball[2] = ball_contact[2] * (1 - float(smooth(1.5, 2.0, time))) + .105 * float(smooth(1.5, 2.0, time))
    if action == 'Eat':
        _, parent, location = anchors['MouthAnchor']
        mouth = (matrices[parent] @ np.r_[location - PIVOTS[parent], 1])[:3]
        offered = mouth + np.array([0, -.10, -.035])
        amount = float(smooth(.35, 1.6, time))
        treat = offered * (1 - amount) + (mouth + [0, -.026, 0]) * amount
        treat_scale = float(smooth(0, .3, time)) * (1 - float(smooth(2.6, 3.6, time)))
    return {'Ball': (ball, max(ball_scale, .001)), 'Treat': (treat, max(treat_scale, .001))}


for action, duration in DURATIONS.items():
    times = np.linspace(0, duration, round(duration * 24) + 1)
    exact_events = {'Idle':[2.4],'Blink':[.6],'PlayBall':[1.5],'Eat':[1.6,1.7]}.get(action,[])
    times = np.unique(np.r_[times,exact_events])
    rotations, translations, morphs, prop_translations, prop_scales = [], [], [], {name: [] for name in prop_nodes}, {name: [] for name in prop_nodes}
    for time in times:
        r, offsets, morph = pose(action, float(time))
        matrices = hierarchy(r, offsets)
        local_q, local_t = [], []
        for index, (name, _, _) in enumerate(BONES):
            parent = PARENTS[index]
            local_q.append(matrix_quaternion(AXIS @ r.get(name, np.eye(3)) @ AXIS.T))
            local_t.append(AXIS @ (PIVOTS[index] - (PIVOTS[parent] if parent >= 0 else 0) + offsets.get(name, 0)))
        rotations.append(local_q); translations.append(local_t); morphs.append(morph)
        for name, (position, size) in props(action, float(time), matrices).items():
            prop_translations[name].append(AXIS @ position)
            prop_scales[name].append([size] * 3)
    rotations, translations, morphs = np.array(rotations), np.array(translations), np.array(morphs)
    entry = {'name': action, 'samplers': [], 'channels': []}
    input_accessor = builder.add(times, 'SCALAR', bounds=True)

    def channel(node, path, values, kind):
        entry['samplers'].append({'input': input_accessor, 'output': builder.add(values, kind), 'interpolation': 'LINEAR'})
        entry['channels'].append({'sampler': len(entry['samplers']) - 1, 'target': {'node': node, 'path': path}})

    for index in range(len(BONES)):
        channel(index + 1, 'rotation', rotations[:, index], 'VEC4')
        channel(index + 1, 'translation', translations[:, index], 'VEC3')
    channel(0, 'weights', morphs.reshape(-1), 'SCALAR')
    for name, node in prop_nodes.items():
        channel(node, 'translation', prop_translations[name], 'VEC3')
        channel(node, 'scale', prop_scales[name], 'VEC3')
    doc['animations'].append(entry)
    item = {'name': action, 'duration': duration, 'sample_times': sorted(set([0, duration * .25, duration * .5, duration * .75, duration])), 'returns_to_rest': action in ('Idle', 'Blink', 'Pet', 'Eat', 'PlayBall', 'Wake')}
    if action == 'Sleep':
        item['holds_sleep_pose'] = True
    if action == 'Wake':
        item['starts_from'] = 'Sleep'
    if action in ('Eat', 'PlayBall'):
        item.update({'contact_time': 1.6 if action == 'Eat' else 1.5, 'contact_kind': 'mouth_food' if action == 'Eat' else 'paw_ball', 'prop_node': 'Treat' if action == 'Eat' else 'Ball', 'prop_radius': .035 if action == 'Eat' else .105, 'mouth_anchor': 'MouthAnchor', 'paw_anchor': 'PawAnchor'})
        item['sample_times'] = sorted(set(item['sample_times'] + [item['contact_time']]))
    manifest_actions.append(item)
    all_pose_data[action] = {'times': times, 'morphs': morphs}

builder.save(OUT / 'corgi-rigged.glb')
manifest = {'status': 'authored, pending independent verification and preview review', 'source_file': str(SOURCE), 'source_sha256': hashlib.sha256(SOURCE.read_bytes()).hexdigest(), 'rigged_file': str(OUT / 'corgi-rigged.glb'), 'armature_object': 'Root', 'skin_objects': ['CorgiSkin'], 'required_shape_keys': SHAPES, 'joint_count': len(BONES), 'coordinate_system': 'glTF Y up, ground Y=0, height 2 units', 'up_axis': 1, 'ground_tolerance': .055, 'max_edge_growth': .16, 'actions': manifest_actions, 'split_lip_triangles': split_count, 'eyelid_patches': lid_report, 'surface_notes': 'Original texture/UV retained; lip triangles split with interpolated UV, curved eyelid cover primitives and inner mouth/tongue/teeth added.', 'blender_export_status': 'GLB authored without launching Blender; native blend save currently blocked by automatic approval service error.'}
manifest['blender_export_status'] = 'GLB updated; native blend must be refreshed and reopened'
manifest['rigged_sha256'] = hashlib.sha256((OUT / 'corgi-rigged.glb').read_bytes()).hexdigest()
previous_manifest.update(manifest)
manifest = previous_manifest
(OUT / 'rig-manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
# Preview consumes the exact same positions, weights, joints and facial deltas.
arrays = {}
part_manifest = []
for index, part in enumerate(preview_parts):
    p, n, texture_uv, faces, j, w, morphs, material, role = part
    for name, value in [('positions', p), ('normals', n), ('triangles', faces), ('joints', j), ('weights', w), ('targets', morphs)]:
        arrays[f'{index}_{name}'] = value
    if texture_uv is not None:
        arrays[f'{index}_uv'] = texture_uv
    part_manifest.append({'index': index, 'material': material, 'role': role, 'has_texture': texture_uv is not None})
np.savez_compressed(OUT / 'preview-geometry.npz', **arrays)
(OUT / 'preview-parts.json').write_text(json.dumps({'parts': part_manifest, 'materials': doc['materials'], 'ball_contact': ball_contact.tolist(), 'anchors': {name: {'parent': parent, 'location': location.tolist()} for name, (_, parent, location) in anchors.items()}}, indent=2))
print('CORGI_RIG_AUTHORED', len(BONES), len(SHAPES), list(DURATIONS), 'source vertices', len(raw), 'rigged vertices', len(points), 'split faces', split_count, flush=True)
