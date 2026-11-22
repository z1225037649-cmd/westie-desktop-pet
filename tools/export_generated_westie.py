"""Export only project-local GLB data with numpy/Pillow, without external apps.

Adaptive vertex clustering keeps facial detail and separates UV discontinuities.
Original GLB remains byte-identical. Fine fur shares analytic skin weights.
"""
from pathlib import Path
import json, struct, hashlib, io
import numpy as np
from PIL import Image
ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'assets/blender/westie-generated/dog_model.glb'
data=SOURCE.read_bytes();size=struct.unpack_from('<I',data,12)[0]
doc=json.loads(data[20:20+size]);blob=data[28+size:]
def view(i):
    v=doc['bufferViews'][i];return blob[v.get('byteOffset',0):v.get('byteOffset',0)+v['byteLength']]
def accessor(i):
    a=doc['accessors'][i];n={'SCALAR':1,'VEC2':2,'VEC3':3}[a['type']]
    typ={5126:'<f4',5125:'<u4',5123:'<u2'}[a['componentType']]
    return np.frombuffer(view(a['bufferView']),dtype=typ,offset=a.get('byteOffset',0),count=a['count']*n).reshape(-1,n).copy()
prim=doc['meshes'][0]['primitives'][0]
p=accessor(prim['attributes']['POSITION']).astype(float)
n=accessor(prim['attributes']['NORMAL']).astype(float)
uv=accessor(prim['attributes']['TEXCOORD_0']).astype(float)
tri=accessor(prim['indices']).reshape(-1,3)
scale=2.2*.85/.8392670154571533
p=np.column_stack((p[:,0]*scale,-p[:,2]*scale+.02975,(p[:,1]+.00832799077)*scale))
n=np.column_stack((n[:,0],-n[:,2],n[:,1]))
assert np.isfinite(p).all() and tri.max()<len(p)
image_id=doc['textures'][doc['materials'][0]['pbrMetallicRoughness']['baseColorTexture']['index']]['source']
png=view(doc['images'][image_id]['bufferView'])
(ROOT/'assets/westie-generated.png').write_bytes(png)
img=np.array(Image.open(io.BytesIO(png)).convert('RGB'));ih,iw=img.shape[:2]
def colors(u):
    xy=np.clip(u,0,.999999)*[iw,ih]
    return img[xy[:,1].astype(int),xy[:,0].astype(int)]/255.
col=colors(uv)
# Fine facial cells keep eyelids/nose readable. Material classes prevent mixing
# dark eyes, blue collar and pink ears into the white coat during clustering.
face=(p[:,1]>1.05)&(p[:,2]>.65)
cell=np.where(face,.0035,.022)
kind=np.where(col.max(1)<.35,1,np.where((col[:,2]>col[:,0]*1.13)&(col[:,0]<.7),2,
               np.where(col[:,0]>col[:,2]*1.18,3,0)))
geo_keys=np.column_stack((np.floor(p/cell[:,None]),face)).astype(np.int32)
_,geo_group=np.unique(geo_keys,axis=0,return_inverse=True)
geo_count=np.bincount(geo_group)
geo_pos=np.column_stack([np.bincount(geo_group,weights=p[:,k])/geo_count for k in range(3)])
# UV islands remain separate vertices, but share exactly the same reduced
# geometric position. Moving each island independently would open cracks.
keys=np.column_stack((geo_group,kind,np.floor(uv*12))).astype(np.int32)
_,group=np.unique(keys,axis=0,return_inverse=True);count=np.bincount(group)
def average(a):return np.column_stack([np.bincount(group,weights=a[:,k])/count for k in range(a.shape[1])])
cp,cn,cu=average(geo_pos[geo_group]),average(n),average(uv)
cn/=np.maximum(np.linalg.norm(cn,axis=1)[:,None],1e-9)
ct=group[tri]
ct=ct[(ct[:,0]!=ct[:,1])&(ct[:,0]!=ct[:,2])&(ct[:,1]!=ct[:,2])]
_,unique=np.unique(np.sort(ct,axis=1),axis=0,return_index=True);ct=ct[np.sort(unique)]
area=np.linalg.norm(np.cross(cp[ct[:,1]]-cp[ct[:,0]],cp[ct[:,2]]-cp[ct[:,0]]),axis=1)
ct=ct[area>1e-10]
used=np.unique(ct);remap=np.full(len(cp),-1);remap[used]=np.arange(len(used));ct=remap[ct]
cp,cn,cu=cp[used],cn[used],cu[used]
def smooth(a,b,x):
    t=np.clip((x-a)/(b-a),0,1);return t*t*(3-2*t)
def weights(pos):
    x,y,z=pos.T
    # Bind the rear skull/ears as well as the front muzzle to the head. The
    # earlier front-only mask left parts of the skull anchored to the torso.
    head=np.maximum(smooth(1.02,1.20,y)*smooth(.12,.35,z),
                    smooth(.85,1.0,y)*smooth(.64,.80,z))
    tail=smooth(.99,1.17,y)*(1-smooth(-.91,-.78,z))
    jaw=head*(1-smooth(1.17,1.28,y))*smooth(.82,.95,z)
    leg=1-smooth(.48,.79,y);front=smooth(-.03,.10,z);back=1-smooth(-.56,-.42,z)
    right=smooth(-.09,.09,x)
    w=np.column_stack((np.maximum(0,1-head-tail)*(1-leg*np.maximum(front,back)),head-jaw,
        leg*front*right,leg*front*(1-right),leg*back*right,leg*back*(1-right),tail,jaw))
    bones=np.argsort(-w,axis=1)[:,:4];w=np.take_along_axis(w,bones,axis=1)
    w/=w.sum(1)[:,None];return bones,w

# Area-weighted fine tapered fur from original high-resolution surface.
rng=np.random.default_rng(20261122)
ar=np.linalg.norm(np.cross(p[tri[:,1]]-p[tri[:,0]],p[tri[:,2]]-p[tri[:,0]]),axis=1)
ids=tri[rng.choice(len(tri),size=28000,p=ar/ar.sum())]
s=np.sqrt(rng.random(len(ids)));t=rng.random(len(ids));b=np.column_stack((1-s,s*(1-t),s*t))
fp=(geo_pos[geo_group][ids]*b[:,:,None]).sum(1);fn=(n[ids]*b[:,:,None]).sum(1);fu=(uv[ids]*b[:,:,None]).sum(1)
fc=colors(fu)
clear=(fc.min(1)>.70)&(fp[:,1]>.075)
clear&=~((fp[:,2]>.66)&(fp[:,1]>1.27)&(fp[:,1]<1.58)&(np.abs(np.abs(fp[:,0])-.137)<.085))
clear&=~((fp[:,2]>.90)&(fp[:,1]<1.34))
fp,fn,fu=fp[clear][:6500],fn[clear][:6500],fu[clear][:6500];fn/=np.maximum(np.linalg.norm(fn,axis=1)[:,None],1e-9)
flow=np.column_stack((fp[:,0]*.18,np.full(len(fp),-.85),np.full(len(fp),-.13)))
flow[fp[:,1]>1.65,1]=.55;flow[(fp[:,2]<-.85)&(fp[:,1]>1),1]=.7
flow-=fn*(flow*fn).sum(1)[:,None];flow/=np.maximum(np.linalg.norm(flow,axis=1)[:,None],1e-9)
dire=flow*.88+fn*.48;dire/=np.linalg.norm(dire,axis=1)[:,None]
tangent=np.cross(fn,dire);tangent/=np.maximum(np.linalg.norm(tangent,axis=1)[:,None],1e-9)
length=rng.uniform(.012,.033,len(fp))*np.where(fp[:,1]>1.25,1.25,1)
width=rng.uniform(.0008,.0017,len(fp))
fur=np.stack((fp-tangent*width[:,None]+fn*.0008,fp+tangent*width[:,None]+fn*.0008,fp+dire*length[:,None]),axis=1).reshape(-1,3)
fur_n=np.repeat(fn,3,axis=0);fur_uv=np.repeat(fu,3,axis=0);fur_tri=np.arange(len(fur)).reshape(-1,3)
fb,fw=weights(fp);fb=np.repeat(fb,3,axis=0);fw=np.repeat(fw,3,axis=0)
pivots=[(0,.68,-.25),(0,1.06,.54),(.255,.61,.27),(-.255,.61,.27),(.27,.61,-.77),(-.27,.61,-.77),(0,1.01,-.91),(0,1.20,.82)]
candidates=cp[(cp[:,0]>.17)&(cp[:,0]<.35)&(cp[:,1]>.075)&(cp[:,1]<.17)&(cp[:,2]>.38)]
paw=candidates[candidates[:,2].argmax()]
anchors=[(0,1.075,1.003),paw.tolist(),(.135,1.424,.748),(-.135,1.424,.748),(0,1.26,1.04),(0,1.82,.62)]
dtype=np.dtype([('p','<f4',3),('n','<f4',3),('uv','<f4',2),('b0','u1'),('w0','<f4'),('b1','u1'),('w1','<f4'),('b2','u1'),('w2','<f4'),('b3','u1'),('w3','<f4')])
with (ROOT/'assets/westie-generated.mesh').open('wb') as f:
    f.write(struct.pack('<iii',0x31505747,1,8));f.write(np.array(pivots,dtype='<f4').tobytes())
    f.write(struct.pack('<i',6));f.write(np.array(anchors,dtype='<f4').tobytes());f.write(struct.pack('<i',2))
    for mat,pos,norm,tex,faces,bones,ww in [(0,cp,cn,cu,ct,*weights(cp)),(1,fur,fur_n,fur_uv,fur_tri,fb,fw)]:
        assert len(faces)+len(ct)<200000
        a=np.zeros(len(pos),dtype=dtype);a['p']=pos;a['n']=norm;a['uv']=tex
        for k in range(4):a['b'+str(k)]=bones[:,k];a['w'+str(k)]=ww[:,k]
        f.write(struct.pack('<iii',mat,len(a),faces.size));f.write(a.tobytes());f.write(faces.astype('<i4').tobytes())
report={'source_sha256':hashlib.sha256(data).hexdigest(),'skin_vertices':len(cp),'skin_triangles':len(ct),
        'fur_vertices':len(fur),'fur_triangles':len(fur_tri),'anchors':anchors,'pivots':pivots,
        'source_vertices':len(p),'source_triangles':len(tri),'texture_size':[iw,ih],
        'max_cluster_displacement':float(np.linalg.norm(p-geo_pos[geo_group],axis=1).max()),
        'technique':'face/material/UV-aware vertex clustering and root-weighted fine tapered geometry; eight analytic bones',
        'runtime_axes':'Y up, Z forward; source display scale times .85'}
(SOURCE.parent/'runtime-export.json').write_text(json.dumps(report,indent=2),encoding='utf8')
print('RUNTIME_EXPORT_COMPLETE',json.dumps(report))
