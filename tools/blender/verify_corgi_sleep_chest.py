"""Check actual GLB chest/foreleg triangles across every shipped sleep/wake frame.

This uses the independent GLB evaluator, not the corrective authoring code.
Shared indices and coincident UV seams are excluded; actual crossings are not.
"""
import sys,json,argparse,hashlib
from pathlib import Path
import numpy as np
root=Path(__file__).resolve().parents[2]
from verify_corgi_rig_glb import BinaryAsset,SceneEvaluator
parser=argparse.ArgumentParser()
parser.add_argument('--input',default=str(root/'art/corgi-image-to-3d/rigged/corgi-rigged.glb'))
parser.add_argument('--times',nargs='*',type=float)
args=parser.parse_args()
asset=BinaryAsset(args.input)
e=SceneEvaluator(asset);spec=e.specs[0];p=spec['positions'];faces=spec['triangles']
cent=p[faces].mean(1)
selected=np.where((cent[:,1]<1.2)&(cent[:,2]>.16))[0];f=faces[selected]

def hits(q):
    t=q[f];c=t.mean(1);r=np.linalg.norm(t-c[:,None,:],axis=2).max(1)
    cells={}
    for i,(lo,hi) in enumerate(zip(np.floor(t.min(1)/.015).astype(int),np.floor(t.max(1)/.015).astype(int))):
        for x in range(lo[0],hi[0]+1):
            for y in range(lo[1],hi[1]+1):
                for z in range(lo[2],hi[2]+1):cells.setdefault((x,y,z),[]).append(i)
    pairs=np.array(list({(a,b) for items in cells.values() for k,a in enumerate(items) for b in items[k+1:]}),dtype=int)
    pairs=pairs[np.linalg.norm(c[pairs[:,0]]-c[pairs[:,1]],axis=1)<r[pairs].sum(1)+1e-8]
    a,b=t[pairs[:,0]],t[pairs[:,1]]
    keep=np.all(np.maximum(a.min(1),b.min(1))<np.minimum(a.max(1),b.max(1))+1e-9,axis=1)
    pairs=pairs[keep]
    adj=np.any(f[pairs[:,0],:,None]==f[pairs[:,1],None,:],axis=(1,2))
    pairs=pairs[~adj]
    a,b=t[pairs[:,0]],t[pairs[:,1]]
    # Exclude coincident seam vertices as well as shared indices.
    close=np.min(np.linalg.norm(a[:,:,None,:]-b[:,None,:,:],axis=3),axis=(1,2))<1e-6
    pairs=pairs[~close];a,b=t[pairs[:,0]],t[pairs[:,1]]
    result=np.zeros(len(pairs),dtype=bool)
    def edge(s,end,tri):
        d=end-s;e1=tri[:,1]-tri[:,0];e2=tri[:,2]-tri[:,0]
        h=np.cross(d,e2);det=np.einsum('ij,ij->i',e1,h)
        safe=np.abs(det)>1e-12;inv=np.divide(1.,det,out=np.zeros_like(det),where=safe)
        delta=s-tri[:,0];u=inv*np.einsum('ij,ij->i',delta,h)
        k=np.cross(delta,e1);v=inv*np.einsum('ij,ij->i',d,k)
        z=inv*np.einsum('ij,ij->i',e2,k)
        return safe&(u>1e-6)&(v>1e-6)&(u+v<1-1e-6)&(z>1e-6)&(z<1-1e-6)
    for k in range(3):result|=edge(a[:,k],a[:,(k+1)%3],b)|edge(b[:,k],b[:,(k+1)%3],a)
    return {tuple(v) for v in selected[pairs[result]]}
base=hits(e.evaluate('Sleep',0)[0][0])
assert not base, 'Bind chest unexpectedly intersects itself.'
records=[];cache={}
for action,duration in [('Sleep',5),('Wake',4)]:
    times=args.times if args.times is not None else np.linspace(0,duration,round(duration*12)+1)
    for time in times:
        q=e.evaluate(action,float(time))[0][0]
        fingerprint=hashlib.sha256(np.round(q[np.unique(f)],7).tobytes()).digest()
        if fingerprint not in cache:cache[fingerprint]=hits(q)-base
        found=cache[fingerprint]
        records.append({'action':action,'time':float(time),'new_intersections':len(found)})
        print(action,round(float(time),4),len(found),flush=True)
        if found:
            report={'status':'failed','rig_sha256':hashlib.sha256(asset.raw).hexdigest(),'frames':records,'pairs':[[int(i) for i in v] for v in sorted(found)]}
            (root/'art/corgi-image-to-3d/rigged/sleep-chest-checks.json').write_text(json.dumps(report,indent=2)+'\n')
            raise AssertionError(f'{action} at {time}: {len(found)} new chest/foreleg intersections')
report={'status':'passed','rig_sha256':hashlib.sha256(asset.raw).hexdigest(),'triangle_count':len(f),'sampled_frames':len(records),'unique_surfaces':len(cache),'frames':records}
(root/'art/corgi-image-to-3d/rigged/sleep-chest-checks.json').write_text(json.dumps(report,indent=2)+'\n')
print('CORGI_SLEEP_CHEST_VERIFIED',json.dumps({k:v for k,v in report.items() if k!='frames'}),flush=True)
