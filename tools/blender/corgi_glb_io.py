"""Small binary glTF reader/writer for this asset's embedded data."""
import json
import struct
from pathlib import Path
import numpy as np

DTYPES = {5120: '<i1', 5121: '<u1', 5122: '<i2', 5123: '<u2', 5125: '<u4', 5126: '<f4'}
SIZES = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}


def read_glb(path):
    raw = Path(path).read_bytes()
    assert struct.unpack_from('<4sII', raw) == (b'glTF', 2, len(raw))
    length, kind = struct.unpack_from('<II', raw, 12)
    assert kind == 0x4e4f534a
    document = json.loads(raw[20:20 + length])
    offset = 20 + length
    size, kind = struct.unpack_from('<II', raw, offset)
    assert kind == 0x004e4942
    return document, raw[offset + 8:offset + 8 + size]


def accessor(document, binary, index):
    item = document['accessors'][index]
    view = document['bufferViews'][item['bufferView']]
    size = SIZES[item['type']]
    dtype = np.dtype(DTYPES[item['componentType']])
    offset = view.get('byteOffset', 0) + item.get('byteOffset', 0)
    data = np.ndarray((item['count'], size), dtype=dtype, buffer=binary, offset=offset, strides=(view.get('byteStride', size * dtype.itemsize), dtype.itemsize)).copy()
    return data[:, 0] if size == 1 else data


class Builder:
    def __init__(self, document, binary):
        self.document = document
        self.binary = bytearray(binary)

    def add(self, values, kind, component=5126, target=None, bounds=False):
        values = np.asarray(values, dtype=DTYPES[component])
        while len(self.binary) % 4:
            self.binary.append(0)
        view = {'buffer': 0, 'byteOffset': len(self.binary), 'byteLength': values.nbytes}
        if target:
            view['target'] = target
        self.document['bufferViews'].append(view)
        self.binary.extend(values.tobytes())
        item = {'bufferView': len(self.document['bufferViews']) - 1, 'byteOffset': 0, 'componentType': component, 'count': len(values), 'type': kind}
        if bounds:
            array = values.reshape((len(values), -1))
            item['min'] = array.min(axis=0).astype(float).tolist()
            item['max'] = array.max(axis=0).astype(float).tolist()
        self.document['accessors'].append(item)
        return len(self.document['accessors']) - 1

    def save(self, path):
        self.document['buffers'] = [{'byteLength': len(self.binary)}]
        text = json.dumps(self.document, ensure_ascii=False, separators=(',', ':')).encode('utf-8')
        text += b' ' * (-len(text) % 4)
        data = bytes(self.binary) + b'\0' * (-len(self.binary) % 4)
        total = 12 + 8 + len(text) + 8 + len(data)
        Path(path).write_bytes(struct.pack('<4sII', b'glTF', 2, total) + struct.pack('<II', len(text), 0x4e4f534a) + text + struct.pack('<II', len(data), 0x004e4942) + data)


if __name__ == '__main__':
    from PIL import Image
    import io
    root = Path(__file__).resolve().parents[2]
    out = root / 'art/corgi-image-to-3d/rig-analysis'
    out.mkdir(parents=True, exist_ok=True)
    doc, data = read_glb(root / 'art/corgi-image-to-3d/source/corgi-meshy.glb')
    primitive = doc['meshes'][0]['primitives'][0]
    p = accessor(doc, data, primitive['attributes']['POSITION'])
    n = accessor(doc, data, primitive['attributes']['NORMAL'])
    uv = accessor(doc, data, primitive['attributes']['TEXCOORD_0'])
    scale = 2 / (p[:, 1].max() - p[:, 1].min())
    center = (p.min(axis=0) + p.max(axis=0)) / 2
    center[1] = p[:, 1].min()
    q = (p - center) * scale
    # Match the prior Blender preview coordinate system.
    world = q[:, [0, 2, 1]] * [1, -1, 1]
    normals = n[:, [0, 2, 1]] * [1, -1, 1]
    np.savez_compressed(out / 'geometry.npz', positions=world, normals=normals, uv=uv, triangles=accessor(doc, data, primitive['indices']).reshape((-1, 3)))
    view = doc['bufferViews'][doc['images'][0]['bufferView']]
    texture = Image.open(io.BytesIO(data[view.get('byteOffset', 0):view.get('byteOffset', 0) + view['byteLength']]))
    texture.save(out / 'base-color.png')
    (out / 'geometry.json').write_text(json.dumps({'coordinate_system': 'Normalized Blender coordinates: X sides, -Y front, Z up, floor=0, height=2', 'bounds': [world.min(axis=0).tolist(), world.max(axis=0).tolist()]}, indent=2))
    print('CORGI_INPUT_EXTRACTED', len(p), flush=True)
