"""Pack actual GLB animation renders, dog-only hit masks and scene anchors."""
import hashlib
import io
import json
import struct
from pathlib import Path
import numpy as np
from PIL import Image
from verify_corgi_rig_glb import BinaryAsset,SceneEvaluator

ROOT=Path(__file__).resolve().parents[2]
RIG=ROOT/'art/corgi-image-to-3d/rigged'
OUTPUT=ROOT/'assets/corgi-animation.frames'
asset=BinaryAsset(RIG/'corgi-rigged.glb');evaluator=SceneEvaluator(asset)
reports=json.loads((RIG/'previews/render-report.json').read_text())
manifest=json.loads((RIG/'rig-manifest.json').read_text())
names={node.get('name'):index for index,node in enumerate(asset.data['nodes'])}
camera=np.array([3.3,2.0,6.3]);target=np.array([0,.93,.15]);span=2.65
forward=target-camera;forward/=np.linalg.norm(forward)
right=np.cross(forward,[0,1,0]);right/=np.linalg.norm(right)
up=np.cross(right,forward)


def project(point):
    delta=point-target
    return (.5+delta@right/span)*420,(.5-delta@up/span)*420


def string(stream,value):
    data=value.encode('utf-8');length=len(data)
    while length>=128:stream.write(bytes([(length&127)|128]));length>>=7
    stream.write(bytes([length]));stream.write(data)


clips=[];payloads=[]
for entry in manifest['actions']:
    name=entry['name'];duration=entry['duration'];folder=RIG/'previews/frames'/name
    frames=sorted(folder.glob('*.png'));assert len(frames)==reports[name]['frames']
    assert reports[name].get('fps',12)==12
    assert reports[name].get('source_rig_sha256')==hashlib.sha256(asset.raw).hexdigest(), f'{name}: rendered frames are from a different rig; rerender before packing.'
    items=[]
    for index,(path,time) in enumerate(zip(frames,np.linspace(0,duration,len(frames)))):
        image=Image.open(path);assert image.mode=='RGBA' and image.size==(420,420)
        png=path.read_bytes();mask=path.with_suffix('.hit').read_bytes();assert len(mask)==(420*420+7)//8
        bits=np.unpackbits(np.frombuffer(mask,dtype=np.uint8),bitorder='little')[:420*420].reshape(420,420)
        yy,xx=np.where(bits);assert len(xx)>100
        bounds=(int(xx.min()),int(yy.min()),int(xx.max()+1),int(yy.max()+1))
        world,morphs=evaluator.world_matrices(name,float(time))
        mouth=project(world[names['MouthAnchor']][:3,3]);paw=project(world[names['PawAnchor']][:3,3])
        morph=morphs.get(names['CorgiSkin'],np.zeros(5))
        items.append({'time':float(time),'mouth':mouth,'paw':paw,'eye':float(max(morph[:2])),'sleep':float(morph[4]),'bounds':bounds,'png':png,'mask':mask})
    clips.append((name,duration,items))


def header(offsets):
    stream=io.BytesIO();stream.write(struct.pack('<4siii',b'WCAF',1,420,420))
    stream.write(struct.pack('<dd',span,12.0));string(stream,hashlib.sha256(asset.raw).hexdigest())
    stream.write(struct.pack('<i',len(clips)))
    for name,duration,items in clips:
        string(stream,name);stream.write(struct.pack('<di',duration,len(items)))
        for item in items:
            stream.write(struct.pack('<d6f4i',item['time'],*item['mouth'],*item['paw'],item['eye'],item['sleep'],*item['bounds']))
            png_offset,mask_offset=next(offsets)
            stream.write(struct.pack('<qiqi',png_offset,len(item['png']),mask_offset,len(item['mask'])))
    return stream.getvalue()


frame_count=sum(len(items) for _,_,items in clips)
size=len(header(iter([(0,0)]*frame_count)));cursor=size;offsets=[]
for _,_,items in clips:
    for item in items:
        png_offset=cursor;cursor+=len(item['png']);mask_offset=cursor;cursor+=len(item['mask'])
        offsets.append((png_offset,mask_offset));payloads.extend((item['png'],item['mask']))
data=header(iter(offsets))+b''.join(payloads);assert len(data)==cursor
OUTPUT.write_bytes(data)
report={'status':'packed','resource_file':str(OUTPUT),'source_rig_sha256':hashlib.sha256(asset.raw).hexdigest(),'resource_sha256':hashlib.sha256(data).hexdigest(),'bytes':len(data),'frame_count':frame_count,'resolution':[420,420],'fps':12,'clips':{name:{'duration':duration,'frames':len(items)} for name,duration,items in clips},'dog_only_hit_masks':True,'props_baked_into_frames':True,'anchors':'Projected actual GLB MouthAnchor/PawAnchor per frame'}
(RIG/'runtime-export.json').write_text(json.dumps(report,indent=2)+'\n')
print('CORGI_RUNTIME_FRAMES_PACKED',json.dumps(report),flush=True)
