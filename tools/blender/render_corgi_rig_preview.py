"""Render the delivered GLB's evaluated skeleton/morph geometry into RGBA frames."""
import argparse
import json
import io
import subprocess
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFont

from verify_corgi_rig_glb import BinaryAsset, SceneEvaluator

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'art/corgi-image-to-3d/rigged'
parser = argparse.ArgumentParser()
parser.add_argument('--node', required=True)
parser.add_argument('--size', type=int, default=420)
parser.add_argument('--fps', type=int, default=12)
parser.add_argument('--actions', default='Idle,Blink,Pet,Eat,PlayBall,Sleep,Wake')
parser.add_argument('--stills-only', action='store_true')
parser.add_argument('--runtime-masks', action='store_true')
args = parser.parse_args()
asset = BinaryAsset(OUT / 'corgi-rigged.glb')
evaluator = SceneEvaluator(asset)
preview = OUT / 'previews'
preview.mkdir(exist_ok=True)
temp = preview / 'render-work'
temp.mkdir(exist_ok=True)
texture_specs = {}
for index, texture in enumerate(asset.data['textures']):
    image = Image.open(io.BytesIO(asset.image_bytes(asset.data['images'][texture['source']]))).convert('RGBA')
    path = temp/f'texture-{index}.rgba';path.write_bytes(image.tobytes())
    texture_specs[index]={'path':str(path),'width':image.width,'height':image.height}
parts, geometry = [], []
base = 0
for spec in evaluator.specs:
    primitive = spec['mesh']['primitives'][spec['primitive']]
    material = asset.data['materials'][primitive['material']]
    color = material.get('pbrMetallicRoughness', {}).get('baseColorFactor', [1,1,1,1])[:3]
    geometry.append({'spec': spec, 'uv': asset.accessor(spec['attributes']['TEXCOORD_0']) if 'TEXCOORD_0' in spec['attributes'] else np.zeros((len(spec['positions']),2)), 'material': color, 'textured': 'TEXCOORD_0' in spec['attributes']})
    faces = spec['triangles'].astype(np.uint32) + base
    base += len(spec['positions'])
    parts.append((faces, color, material.get('pbrMetallicRoughness',{}).get('baseColorTexture',{}).get('index')))
props = []
for node_index, node in enumerate(asset.data['nodes']):
    if 'mesh' not in node or 'skin' in node:
        continue
    for primitive in asset.data['meshes'][node['mesh']]['primitives']:
        p = asset.accessor(primitive['attributes']['POSITION']).astype(float)
        n = asset.accessor(primitive['attributes']['NORMAL']).astype(float)
        faces = asset.accessor(primitive['indices']).reshape(-1,3).astype(np.uint32) + base
        base += len(p)
        material = asset.data['materials'][primitive['material']]['pbrMetallicRoughness']['baseColorFactor'][:3]
        parts.append((faces, material, None))
        props.append((node_index, p, n))
combined_indices = np.concatenate([part[0].ravel() for part in parts])
(temp / 'indices.bin').write_bytes(combined_indices.tobytes())
part_specs, offset = [], 0
for faces, color, texture_index in parts:
    part_specs.append({'start': offset, 'end': offset+faces.size, 'color': color, 'textured': texture_index is not None, 'texture':texture_index})
    offset += faces.size
job = {'vertices': str(temp/'vertices.bin'), 'indices': str(temp/'indices.bin'), 'textures':texture_specs,'output': str(temp/'frame.rgba'), 'size': args.size, 'camera': [3.3,2.0,6.3], 'target': [0,.93,.15], 'span': 2.65, 'parts': part_specs}


def render(action, time, target=None, span=None, camera=None):
    surfaces, world = evaluator.evaluate(action, time)
    verts = []
    for points, item in zip(surfaces, geometry):
        spec = item['spec']
        # Recompute surface normals from the actual evaluated triangles.
        faces = spec['triangles']
        face_normal = np.cross(points[faces[:,1]]-points[faces[:,0]],points[faces[:,2]]-points[faces[:,0]])
        normal = np.zeros_like(points)
        for slot in range(3):
            np.add.at(normal, faces[:,slot], face_normal)
        length = np.linalg.norm(normal,axis=1)
        normal /= np.maximum(length[:,None],1e-12)
        verts.append(np.column_stack((points,normal,item['uv'])))
    for node, p, n in props:
        matrix = world[node]
        points = p @ matrix[:3,:3].T + matrix[:3,3]
        normal = n @ matrix[:3,:3].T
        normal /= np.maximum(np.linalg.norm(normal,axis=1)[:,None],1e-12)
        verts.append(np.column_stack((points,normal,np.zeros((len(p),2)))))
    (temp/'vertices.bin').write_bytes(np.concatenate(verts).astype('<f4').tobytes())
    current = dict(job)
    if target is not None: current['target'] = target
    if span is not None: current['span'] = span
    if camera is not None: current['camera'] = camera
    (temp/'job.json').write_text(json.dumps(current))
    subprocess.run([args.node,str(ROOT/'tools/blender/raster_corgi_preview.js'),str(temp/'job.json')],check=True,capture_output=True)
    return Image.frombytes('RGBA',(args.size,args.size),(temp/'frame.rgba').read_bytes())


manifest = json.loads((OUT/'rig-manifest.json').read_text())
action_manifest = {item['name']:item for item in manifest['actions']}
labels = {'Idle':'待机','Blink':'闭眼','Pet':'摸摸','Eat':'咬食','PlayBall':'拨球','Sleep':'趴睡','Wake':'起身'}
font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',19)
records = json.loads((preview/'render-report.json').read_text()) if (preview/'render-report.json').exists() else {}
for action in args.actions.split(','):
    entry = action_manifest[action]
    duration = entry['duration']
    peak = {'Idle':1.0,'Blink':.6,'Pet':2.0,'Eat':1.7,'PlayBall':1.5,'Sleep':5.0,'Wake':2.0}[action]
    still = render(action,peak)
    still.save(preview/(action+'-peak.png'))
    frames=[]
    if not args.stills_only:
        directory = preview/'frames'/action
        directory.mkdir(parents=True,exist_ok=True)
        for index,time in enumerate(np.linspace(0,duration,round(duration*args.fps)+1)):
            frame = render(action,float(time))
            frame.save(directory/f'{index:04d}.png')
            if args.runtime_masks:
                visible=np.fromfile(temp/'frame.rgba.parts.bin',dtype='<u2').reshape(args.size,args.size)
                hit=(visible<len(evaluator.specs))&(np.asarray(frame)[:,:,3]>=24)
                (directory/f'{index:04d}.hit').write_bytes(np.packbits(hit.reshape(-1),bitorder='little').tobytes())
            bg=Image.new('RGB',frame.size,(239,242,245));bg.paste(frame,(0,0),frame)
            draw=ImageDraw.Draw(bg);draw.text((12,10),labels[action],font=font,fill=(50,58,67))
            frames.append(bg)
        frames[0].save(preview/(action+'.gif'),save_all=True,append_images=frames[1:],duration=round(1000/args.fps),loop=0,disposal=2)
    records[action]={'peak_time':peak,'frames':len(frames),'duration':duration,'fps':args.fps,'preview_render':'Actual delivered GLB, evaluated joints and morphs, CPU texture/depth-buffer rendering'}
    print('CORGI_ACTION_RENDERED',action,len(frames),flush=True)

# Isolated closeups expose missed eyelid coverage and stretched lip membranes.
if 'Blink' in records:
    for name,time in [('face-open',0),('face-closed',.6)]:
        render('Blink',time,target=[0,1.47,.60],span=.62,camera=[.25,1.51,4]).save(preview/(name+'.png'))
        (preview/(name+'-parts.bin')).write_bytes((temp/'frame.rgba.parts.bin').read_bytes())
if 'Eat' in records:
    render('Eat',1.7,target=[0,1.29,.70],span=.65,camera=[.35,1.5,4]).save(preview/'mouth-open.png')
(preview/'render-report.json').write_text(json.dumps(records,ensure_ascii=False,indent=2)+'\n')
