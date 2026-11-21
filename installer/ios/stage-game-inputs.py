"""Stage reviewed, content-free iOS native and metadata inputs; never reads a XAP."""
import hashlib
import json
import shutil
import subprocess
import zipfile
from pathlib import Path

repo = Path(__file__).resolve().parents[2]
pin = json.loads((repo / 'config/ios-packaging.json').read_text())
output = repo / 'out/ios/game'

def digest(data):
    return hashlib.sha256(data).hexdigest()

archive = repo / pin['nativeArchive']
if digest(archive.read_bytes()) != pin['nativeArchiveSha256']:
    raise SystemExit('iOS native archive differs from the reviewed pin')
with zipfile.ZipFile(archive) as source:
    for name, expected in pin['nativeLibraries'].items():
        data = source.read('iphoneos/' + name)
        if digest(data) != expected:
            raise SystemExit('iOS native library differs: ' + name)
        destination = output / 'native' / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(data)
inputs = {
    'FNA.dll': repo / 'out/deps/managed/FNA.dll',
    **{name: repo / f'src/{Path(name).stem}/bin/Release/net10.0/{name}'
       for name in ('Port.Bridge.dll', 'Port.Hooks.dll')},
}
for name, source in inputs.items():
    if digest(source.read_bytes()) != pin['preparationAssemblies'][name]:
        raise SystemExit('Preparation assembly differs from the reviewed pin: ' + name)
    destination = output / 'preparation' / name
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, destination)
runtime = repo / ('out/tooling/dotnet/packs/Microsoft.NETCore.App.Runtime.Mono.ios-arm64/'
                  + pin['dotnetRuntime'] + '/runtimes/ios-arm64/lib/net10.0')
# Mono ships its CoreLib under native/, outside the lib/net10.0 facades.
files = sorted([*runtime.glob('*.dll'), runtime.parent.parent / 'native/System.Private.CoreLib.dll'],
               key=lambda file: file.name)
rows = ''.join(file.name + ' ' + digest(file.read_bytes()) + '\n' for file in files)
if digest(rows.encode()) != pin['runtimeCatalogSha256']:
    raise SystemExit('Raw iOS runtime catalog differs from the reviewed pin')
destination = output / 'abi-runtime'
destination.mkdir(parents=True, exist_ok=True)
for file in files:
    shutil.copy2(file, destination / file.name)
shutil.copy2(repo / 'config/supported-versions.json', output / 'preparation/supported-versions.json')
shutil.copy2(repo / 'config/ios-packaging.json', output / 'preparation/ios-packaging.json')
print(f'Staged five iOS native libraries, three preparation assemblies and {len(files)} runtime metadata files.')

# Loose app-icon resources avoid actool's simulator-runtime dependency for this
# device-only package. iOS applies the icon mask to these square opaque images.
icons = output / 'icons'
icons.mkdir(parents=True, exist_ok=True)
for name, size in {'AppIcon60x60@2x.png': 120, 'AppIcon60x60@3x.png': 180,
                   'AppIcon76x76.png': 76, 'AppIcon76x76@2x~ipad.png': 152,
                   'AppIcon83.5x83.5@2x~ipad.png': 167}.items():
    subprocess.run(['/usr/bin/sips', '-z', str(size), str(size),
                    str(repo / 'assets/icon/icon-black.png'), '--out', str(icons / name)],
                   check=True, stdout=subprocess.DEVNULL)
