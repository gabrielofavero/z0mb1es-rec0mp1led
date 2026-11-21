"""Compare --art-decode output with the independent desktop XNB/Pillow path.

Run using the desktop optional-art Python environment. Arguments are the private
phone-original directory and the private --art-decode output directory.
"""
import hashlib
import json
from pathlib import Path
import sys

repo = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(repo / 'tools'))
import prepare_optional_art as desktop

if len(sys.argv) != 3:
    raise SystemExit('Expected <phone-original> <art-decode-output>')
phone, output = map(Path, sys.argv[1:])
hashes = json.loads((output / 'pixel-hashes.json').read_text())
checked = set()
for meta in desktop.load(repo / 'docs/records/asset-swap-inventory.json')['textures']:
    if meta['source'] != 'phone':
        continue
    name = meta['path']
    image = desktop.read_texture(phone / 'Content' / name, meta, 'pixel parity')
    if hashlib.sha256(image.tobytes()).hexdigest() != hashes[name]:
        raise AssertionError('Decoded pixels differ: ' + name)
    with desktop.Image.open(output / (name + '.png')) as png:
        padded = desktop.np.pad(desktop.np.asarray(image), ((2, 2), (2, 2), (0, 0)), mode='edge')
        if png.mode != 'RGBA' or png.size != (image.width + 4, image.height + 4) or png.tobytes() != padded.tobytes():
            raise AssertionError('Padded PNG differs: ' + name)
    checked.add(name)
if checked != set(hashes):
    raise AssertionError('Incomplete phone texture comparison')
print(f'INDEPENDENT_PIXEL_PARITY_OK count={len(checked)}')
