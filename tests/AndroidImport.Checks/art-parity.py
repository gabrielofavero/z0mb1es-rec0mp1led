"""Compare active artwork generations by mapped pixels, ignoring old cached generations."""
import json
import sys
from pathlib import Path
from PIL import Image


def active(root, kind):
    generation = json.loads((root / kind / 'current.json').read_text())['generation']
    folder = root / kind / generation
    path = root / kind / (generation + '.json') if kind == 'endless-360' else folder / 'manifest.json'
    return folder, json.loads(path.read_text())


actual, expected = map(Path, sys.argv[1:])
checked = 0
for kind in ('i-maed-360', 'time-viking-360'):
    folder, manifest = active(actual, kind)
    reference, desktop = active(expected, kind)
    rows = {row['id']: row for row in desktop['elements']}
    for row in manifest['elements']:
        if row['selection'] != kind:
            continue
        other = rows[row['id']]
        assert other['selection'] == kind, row['id']
        for key in ('size', 'contentRect', 'logicalSize', 'sampler', 'padding'):
            assert row['output'][key] == other['output'][key], (row['id'], key)
        with Image.open(folder / row['output']['path']) as a, Image.open(reference / other['output']['path']) as b:
            assert a.size == b.size and a.convert('RGBA').tobytes() == b.convert('RGBA').tobytes(), row['id']
        checked += 1
assert checked > 200, 'Incomplete enhanced artwork coverage'
_, endless = active(actual, 'endless-360')
enhanced = [row for row in endless['elements'] if row['selection'] != 'phone']
assert enhanced and all(row['selection'] in ('i-maed-360', 'time-viking-360') for row in enhanced)
print(f'ANDROID_ART_PIXEL_PARITY_OK outputs={checked} endless={len(enhanced)}')
