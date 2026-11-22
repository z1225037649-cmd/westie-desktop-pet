"""Check actual rendered frames, including depth-buffer eyelid occlusion."""
import json
from pathlib import Path
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'art/corgi-image-to-3d/rigged'
PREVIEW = OUT / 'previews'
report = json.loads((PREVIEW/'render-report.json').read_text())
checks = {'status':'passed','actions':{},'closed_iris_occlusion':[]}
for action, entry in report.items():
    frames = sorted((PREVIEW/'frames'/action).glob('*.png'))
    assert len(frames)==entry['frames'] and len(frames)>1
    margin = 10000
    for file in frames:
        image = Image.open(file)
        assert image.mode=='RGBA'
        alpha = image.getchannel('A')
        assert alpha.getextrema()==(0,255)
        box = alpha.getbbox()
        assert box
        margin=min(margin,box[0],box[1],image.width-box[2],image.height-box[3])
    assert margin>=8,(action,'cropped geometry',margin)
    gif=Image.open(PREVIEW/(action+'.gif'))
    assert gif.n_frames>=4,(action,'animation collapsed')
    gif_duration=0
    for index in range(gif.n_frames):
        gif.seek(index);gif_duration+=gif.info.get('duration',0)
    assert abs(gif_duration-len(frames)*1000/12)<25,(action,'GIF timing changed',gif_duration)
    checks['actions'][action]={'transparent_frames':len(frames),'minimum_margin':margin,'gif_frames':gif.n_frames,'gif_duration_ms':gif_duration}
opened=np.array(Image.open(PREVIEW/'face-open.png'))
parts=np.fromfile(PREVIEW/'face-closed-parts.bin',dtype='<u2').reshape(opened.shape[:2])
yy,xx=np.indices(opened.shape[:2])
size=opened.shape[0]
for center in [(133/420,212/420),(289/420,212/420)]:
    mask=((xx-center[0]*size)/(52/420*size))**2+((yy-center[1]*size)/(24/420*size))**2<1
    dark=mask&(opened[:,:,0]<65)&(opened[:,:,3]>0)
    assert dark.sum()>30,'Original iris not visible in open-eye control.'
    visible_lid=(parts>=1)&(parts<=6)
    coverage=float(visible_lid[dark].mean())
    assert coverage>=.995,('Iris still visible during full closure',coverage)
    dy,dx=np.where(dark)
    iris_box=np.zeros_like(mask);iris_box[dy.min():dy.max()+1,dx.min():dx.max()+1]=True
    highlights=iris_box&(opened[:,:,0]>210)&(opened[:,:,1]>190)&(opened[:,:,2]>150)
    highlight_coverage=float(visible_lid[highlights].mean()) if highlights.any() else 1.
    assert highlight_coverage>=.995,('Original eye highlight leaks through',highlight_coverage)
    checks['closed_iris_occlusion'].append({'original_dark_iris_pixels':int(dark.sum()),'covered_by_actual_eyelid_geometry':coverage,'highlight_coverage':highlight_coverage})
(PREVIEW/'preview-checks.json').write_text(json.dumps(checks,indent=2)+'\n')
print('CORGI_ANIMATION_PREVIEWS_VERIFIED',json.dumps(checks),flush=True)
