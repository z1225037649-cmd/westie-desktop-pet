from pathlib import Path
import csv, struct
import numpy as np
root = Path(__file__).resolve().parents[1]
blob = (root / 'assets/westie-generated.mesh').read_bytes()
offset = 12 + struct.unpack_from('<i', blob, 8)[0] * 12
anchors = struct.unpack_from('<i', blob, offset)[0]
offset += 4 + anchors * 12 + 4
material, count, indices = struct.unpack_from('<iii', blob, offset)
offset += 12
dtype = np.dtype([('p','<f4',3),('n','<f4',3),('uv','<f4',2),('b0','u1'),('w0','<f4'),('b1','u1'),('w1','<f4'),('b2','u1'),('w2','<f4'),('b3','u1'),('w3','<f4')])
vertices = np.frombuffer(blob, dtype=dtype, count=count, offset=offset)
triangles = np.frombuffer(blob, dtype='<i4', count=indices, offset=offset + count * dtype.itemsize).reshape(-1, 3)
rows = list(csv.DictReader((root / 'qa/generated/sleep-head-neck-new-pairs.csv').open()))
if not rows:
    print('No new head/neck intersections to inspect.')
    raise SystemExit(0)
for kind in ['headTriangle', 'neckTriangle']:
    ids = sorted(set(int(row[kind]) for row in rows))
    ids = np.unique(triangles[ids].reshape(-1))
    points = vertices['p'][ids]
    print(kind, 'vertices', len(points), 'min/median/max', np.round(points.min(0),3), np.round(np.median(points,axis=0),3), np.round(points.max(0),3))
    for index in ids[:12]:
        print(index, np.round(vertices['p'][index],3).tolist(), [(int(vertices['b'+str(k)][index]), round(float(vertices['w'+str(k)][index]),3)) for k in range(4)])
