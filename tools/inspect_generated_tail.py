from pathlib import Path
import struct
import numpy as np
root = Path(__file__).resolve().parents[1]
blob = (root / 'assets/westie-generated.mesh').read_bytes()
offset = 12 + struct.unpack_from('<i', blob, 8)[0] * 12
offset += 4 + struct.unpack_from('<i', blob, offset)[0] * 12 + 4
_, count, _ = struct.unpack_from('<iii', blob, offset)
dtype = np.dtype([('p','<f4',3),('n','<f4',3),('uv','<f4',2),('b0','u1'),('w0','<f4'),('b1','u1'),('w1','<f4'),('b2','u1'),('w2','<f4'),('b3','u1'),('w3','<f4')])
v = np.frombuffer(blob, dtype=dtype, count=count, offset=offset+12)
p = v['p']
for z in np.arange(-1.50, -.85, .05):
    q = p[(p[:,2]>=z)&(p[:,2]<z+.05)&(p[:,1]>.85)&(np.abs(p[:,0])<.30)]
    if len(q): print(round(z,2),len(q),'Y',np.round(np.quantile(q[:,1],[0,.25,.5,.75,1]),3),'X',np.round([q[:,0].min(),q[:,0].max()],3))
