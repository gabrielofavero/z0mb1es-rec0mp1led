"""Audit a local personal IPA against its exact prepared payload, never a release artifact."""
import argparse
import hashlib
import json
import plistlib
import struct
import subprocess
import tempfile
import zipfile
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('ipa', type=Path)
parser.add_argument('payload', type=Path)
args = parser.parse_args()
repo = Path(__file__).resolve().parents[2]
if not args.ipa.resolve().is_relative_to(repo / 'private') or not args.payload.resolve().is_relative_to(repo / 'private'):
    raise SystemExit('Personal audit requires ignored private/ inputs and outputs')
pin = json.loads((repo / 'config/ios-packaging.json').read_text())
receipt = json.loads((args.payload / 'bundled-manifest.json').read_text())
with zipfile.ZipFile(args.ipa) as archive:
    names = archive.namelist()
    info_name = next(n for n in names if n.endswith('.app/Info.plist') and n.count('/') == 2)
    prefix = info_name[:-len('Info.plist')]
    info = plistlib.loads(archive.read(info_name))
    if (info['CFBundleIdentifier'] != 'io.github.z0mb1es.executionprobe'
            or info['CFBundleShortVersionString'] != '1.1.0' or info['CFBundleVersion'] != '13'):
        raise SystemExit('Personal build must retain the original bundle identity, version 1.1.0 and build 13')
    if (info['CFBundleDisplayName'] != 'Z0MB1ES' or info['CFBundleExecutable'] != 'Z0MB1ES'
            or not info.get('UIFileSharingEnabled')):
        raise SystemExit('Missing player identity or Files diagnostics')
    executable = archive.read(prefix + info['CFBundleExecutable'])
    if executable[:4] != b'\xcf\xfa\xed\xfe' or struct.unpack_from('<I', executable, 4)[0] != 0x100000c:
        raise SystemExit('Expected an arm64 executable')
    cursor = 32
    device = False
    for _ in range(struct.unpack_from('<I', executable, 16)[0]):
        command, size = struct.unpack_from('<II', executable, cursor)
        if size < 8 or cursor + size > len(executable):
            raise SystemExit('Invalid load command')
        if command == 0x32:
            device = struct.unpack_from('<I', executable, cursor + 8)[0] == 2
        cursor += size
    if not device:
        raise SystemExit('Expected iOS device platform')
    with tempfile.NamedTemporaryFile() as binary:
        binary.write(executable)
        binary.flush()
        symbols = subprocess.check_output(['/usr/bin/nm', '-gU', binary.name], text=True)
    exported = {row.split()[-1] for row in symbols.splitlines() if row.split()}
    for name in ('_SDL_RunApp', '_FNA3D_CreateDevice', '_FAudioCreate'):
        if name not in exported:
            raise SystemExit('Missing native export: ' + name)
    for name, expected in pin['nativeLibraries'].items():
        if hashlib.sha256((repo / 'out/ios/game/native' / name).read_bytes()).hexdigest() != expected:
            raise SystemExit('Native link input differs: ' + name)
    payload_prefix = prefix + 'preinstalled/'
    wanted = {row['Path'] for row in receipt['Files']} | {'bundled-manifest.json'}
    actual = {n[len(payload_prefix):] for n in names if n.startswith(payload_prefix) and not n.endswith('/')}
    if actual != wanted:
        raise SystemExit('Packaged personal payload inventory differs')
    for name in wanted:
        data = archive.read(payload_prefix + name)
        if data != (args.payload / name).read_bytes():
            raise SystemExit('Packaged game bytes differ: ' + name)
    if not any(n.startswith('content/Content/') for n in actual) or 'game/ZombiesWP7.dll' not in actual:
        raise SystemExit('The prepared game/content is missing')
    for source in ('i-maed-360', 'time-viking-360', 'endless-360'):
        if 'optional-art/' + source + '/current.json' not in actual:
            raise SystemExit('Missing bundled artwork: ' + source)
    for name in names:
        if name.endswith(('.xap', '.mobileprovision')) or '/phone-original/' in name or '/Saves/' in name:
            raise SystemExit('Unexpected source package, signing profile or save payload: ' + name)
    for key in ('CFBundleIcons', 'CFBundleIcons~ipad'):
        if not info.get(key, {}).get('CFBundlePrimaryIcon', {}).get('CFBundleIconFiles'):
            raise SystemExit('Missing black icon declaration')
    for path in (repo / 'out/ios/game/icons').glob('*.png'):
        if archive.read(prefix + path.name) != path.read_bytes():
            raise SystemExit('Black icon changed')
report = {'ipa': str(args.ipa), 'sha256': hashlib.sha256(args.ipa.read_bytes()).hexdigest(),
          'distribution': 'personal local build only', 'build': int(info['CFBundleVersion']), 'payloadFiles': len(actual),
          'bundleIdentityRetained': True, 'deviceGameplay': 'not established by this audit'}
(repo / 'private/ios-bundled/audit.json').write_text(json.dumps(report, indent=2) + '\n')
print(json.dumps(report, indent=2))
