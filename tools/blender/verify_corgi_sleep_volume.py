"""Measure retained skin area, local edge lengths and chest section thickness.

Evaluates the actual GLB. Sections use bind-space vertex neighborhoods and
their covariance eigenvalues, so a mere rigid rotation cannot pass as squash.
"""
import argparse,hashlib,json
from pathlib import Path
import numpy as np
from verify_corgi_rig_glb import BinaryAsset,SceneEvaluator
root=Path(__file__).resolve().parents[2]
parser=argparse.ArgumentParser();parser.add_argument('--input',default=str(root/'art/corgi-image-to-3d/rigged/corgi-rigged.glb'))
parser.add_argument('--report',default=str(root/'art/corgi-image-to-3d/rigged/sleep-volume-checks.json'))
args=parser.parse_args();asset=BinaryAsset(args.input);e=SceneEvaluator(asset);s=e.specs[0];p=s['positions'];f=s['triangles']
cent=p[f].mean(1);region=(cent[:,1]>.30)&(cent[:,1]<1.15)&(cent[:,2]>.08)
faces=f[region];edge=np.unique(np.sort(np.concatenate([faces[:,[0,1]],faces[:,[1,2]],faces[:,[2,0]]]),axis=1),axis=0)
length=np.linalg.norm(p[edge[:,1]]-p[edge[:,0]],axis=1);edge=edge[length>.003];length=length[length>.003]
def area(points):
    t=points[faces];return np.linalg.norm(np.cross(t[:,1]-t[:,0],t[:,2]-t[:,0]),axis=1)*.5
rest_area=area(p);records=[]
for action,duration in [('Sleep',5),('Wake',4)]:
    for time in np.linspace(0,duration,round(duration*12)+1):
        q=e.evaluate(action,float(time))[0][0]
        ratio=np.linalg.norm(q[edge[:,1]]-q[edge[:,0]],axis=1)/length
        sections=[]
        for height in [.45,.65,.85,1.05]:
            mask=(np.abs(p[:,1]-height)<.055)&(p[:,2]>.08)
            r=np.linalg.eigvalsh(np.cov(p[mask].T));v=np.linalg.eigvalsh(np.cov(q[mask].T))
            sections.append({'bind_height':height,'width_ratio':float(np.sqrt(v[2]/r[2])),'depth_ratio':float(np.sqrt(v[1]/r[1])),'section_area_ratio':float(np.sqrt(v[1]*v[2]/(r[1]*r[2])))})
        records.append({'action':action,'time':float(time),'edge_p05':float(np.quantile(ratio,.05)),'edge_p95':float(np.quantile(ratio,.95)),
                        'collapsed_edge_fraction':float(np.mean(ratio<.35)),'stretched_edge_fraction':float(np.mean(ratio>2)),
                        'skin_area_ratio':float(area(q).sum()/rest_area.sum()),'sections':sections})
worst={'minimum_edge_p05':min(r['edge_p05'] for r in records),'maximum_edge_p95':max(r['edge_p95'] for r in records),
       'maximum_collapsed_edge_fraction':max(r['collapsed_edge_fraction'] for r in records),'maximum_stretched_edge_fraction':max(r['stretched_edge_fraction'] for r in records),
       'minimum_skin_area_ratio':min(r['skin_area_ratio'] for r in records),'minimum_section_area_ratio':min(s['section_area_ratio'] for r in records for s in r['sections']),
       'minimum_section_depth_ratio':min(s['depth_ratio'] for r in records for s in r['sections'])}
passed=worst['minimum_edge_p05']>.55 and worst['maximum_edge_p95']<1.6 and worst['maximum_collapsed_edge_fraction']<.002 and worst['maximum_stretched_edge_fraction']<.002 and worst['minimum_skin_area_ratio']>.70 and worst['minimum_section_area_ratio']>.72 and worst['minimum_section_depth_ratio']>.72
report={'status':'passed' if passed else 'failed','rig_sha256':hashlib.sha256(asset.raw).hexdigest(),'sampled_frames':len(records),'worst':worst,'frames':records}
Path(args.report).write_text(json.dumps(report,indent=2)+'\n');print(json.dumps({k:v for k,v in report.items() if k!='frames'}),flush=True)
assert passed,'Chest volume or skin stretch collapsed; inspect the report and rendered views.'
