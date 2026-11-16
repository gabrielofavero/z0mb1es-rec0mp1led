"""Audit the small experiment's IPA. This is not a gameplay or rights approval."""
import hashlib
import argparse
import json
import plistlib
import struct
import subprocess
import tempfile
import zipfile
from pathlib import Path

repo = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--ipa', type=Path, help='Explicit experiment IPA (also useful for audit fixtures)')
parser.add_argument('--game-host', action='store_true', help='Audit the FNA/original-game host experiment')
args = parser.parse_args()
project, package = ('Port.IosGame', 'Z0MB1ES.ipa') if args.game_host else ('Port.IosProbe', 'Z0MB1ES-execution-probe.ipa')
ipas = [args.ipa.resolve()] if args.ipa else list((repo / f'src/{project}/bin/Release').rglob(package))
if len(ipas) != 1:
    raise SystemExit(f'Expected one fresh probe IPA, found {len(ipas)}')
ipa = ipas[0]
with zipfile.ZipFile(ipa) as archive:
    names = archive.namelist()
    infos = [n for n in names if n.endswith('.app/Info.plist') and n.count('/') == 2]
    if len(infos) != 1:
        raise SystemExit('Expected one top-level app')
    prefix = infos[0][:-len('Info.plist')]
    info = plistlib.loads(archive.read(infos[0]))
    if info['CFBundleIdentifier'] != 'io.github.z0mb1es.executionprobe':
        raise SystemExit('Unexpected bundle identity')
    executable = archive.read(prefix + info['CFBundleExecutable'])
    if executable[:4] != b'\xcf\xfa\xed\xfe' or struct.unpack_from('<I', executable, 4)[0] != 0x100000c:
        raise SystemExit('Expected a thin arm64 Mach-O')
    cursor = 32
    device_platform = False
    for _ in range(struct.unpack_from('<I', executable, 16)[0]):
        command, size = struct.unpack_from('<II', executable, cursor)
        if size < 8 or cursor + size > len(executable):
            raise SystemExit('Invalid Mach-O load command')
        if command == 0x32 and size >= 24:
            device_platform = struct.unpack_from('<I', executable, cursor + 8)[0] == 2
        cursor += size
    if not device_platform:
        raise SystemExit('Expected an iOS device platform load command, not macOS or simulator')
    converter = archive.read(prefix + 'converter-identity.txt').decode().strip()
    native = repo / 'out/ios/native/libz0audio.a'
    if hashlib.sha256(native.read_bytes()).hexdigest() != converter:
        raise SystemExit('Converter identity differs from native input')
    if args.game_host:
        if info['CFBundleVersion'] != '18' or info['CFBundleDisplayName'] != 'Z0MB1ES':
            raise SystemExit('Unexpected original-game host build number')
        if not info.get('UIFileSharingEnabled') or not info.get('LSSupportsOpeningDocumentsInPlace'):
            raise SystemExit('Game logs must be accessible from Files')
        scenes = info.get('UIApplicationSceneManifest', {})
        configs = scenes.get('UISceneConfigurations', {}).get('UIWindowSceneSessionRoleApplication', [])
        if scenes.get('UIApplicationSupportsMultipleScenes') is not False or not any(
            c.get('UISceneDelegateClassName') == 'SDLUIKitSceneDelegate' for c in configs
        ):
            raise SystemExit('Files setup must use the SDL scene lifecycle')
        for key in ('CFBundleIcons', 'CFBundleIcons~ipad'):
            if not info.get(key, {}).get('CFBundlePrimaryIcon', {}).get('CFBundleIconFiles'):
                raise SystemExit('Missing black app icon declaration')
        pin = json.loads((repo / 'config/ios-packaging.json').read_text())
        for name, expected in pin['nativeLibraries'].items():
            if hashlib.sha256((repo / 'out/ios/game/native' / name).read_bytes()).hexdigest() != expected:
                raise SystemExit('Native link input differs: ' + name)
        for name, expected in pin['preparationAssemblies'].items():
            if hashlib.sha256(archive.read(prefix + 'preparation/' + name)).hexdigest() != expected:
                raise SystemExit('Unexpected preparation metadata: ' + name)
        runtime_names = sorted(name for name in names if name.startswith(prefix + 'abi-runtime/') and name.endswith('.dll'))
        rows = ''.join(Path(name).name + ' ' + hashlib.sha256(archive.read(name)).hexdigest() + '\n'
                       for name in runtime_names)
        if hashlib.sha256(rows.encode()).hexdigest() != pin['runtimeCatalogSha256']:
            raise SystemExit('Unexpected iOS runtime metadata catalog')
        with tempfile.NamedTemporaryFile() as binary:
            binary.write(executable)
            binary.flush()
            symbols = subprocess.check_output(['/usr/bin/nm', '-gU', binary.name], text=True)
        exported = {line.split()[-1] for line in symbols.splitlines() if line.split()}
        for name in ('SDL_RunApp', 'FNA3D_CreateDevice', 'FAudioCreate', 'z0_audio_convert'):
            if '_' + name not in exported:
                raise SystemExit('Missing linked native symbol: ' + name)
        for name in ('SDLUIKitSceneDelegate',):
            if '_OBJC_CLASS_$_' + name not in exported:
                raise SystemExit('Missing registered native UIKit delegate: ' + name)
    icon_names = {prefix + path.name for path in (repo / 'out/ios/game/icons').glob('*.png')} if args.game_host else set()
    if args.game_host:
        if len(icon_names) != 5 or not icon_names.issubset(names):
            raise SystemExit('Missing generated black app icons')
        for name in icon_names:
            if archive.read(name) != (repo / 'out/ios/game/icons' / Path(name).name).read_bytes():
                raise SystemExit('Packaged icon differs from staged black icon')
    # Authored libraries and target metadata are allowed; imported game material is not.
    for name in names:
        if name in icon_names:
            continue
        lower = name.lower()
        if lower.endswith(('.xap', '.xnb', '.wma', '.ogg', '.png', '.jpg', '.jpeg')) or any(
            token in lower for token in ('zombieswp7', 'content-manifest', 'patch-manifest', 'game-metadata', 'phone-original', 'optional-art', 'embedded.mobileprovision')
        ):
            raise SystemExit(f'Unexpected probe payload: {name}')
    # Also compare all members to the supplied XAP's entries when available locally.
    source_hashes = set()
    for xap in (repo / 'game').glob('*.xap'):
        with zipfile.ZipFile(xap) as original:
            source_hashes.update(hashlib.sha256(original.read(n)).digest()
                                 for n in original.namelist() if not n.endswith('/'))
    for name in names:
        if not name.endswith('/') and hashlib.sha256(archive.read(name)).digest() in source_hashes:
            raise SystemExit(f'Original XAP bytes in probe: {name}')
report = {
    'ipa': str(ipa.relative_to(repo)) if ipa.is_relative_to(repo) else ipa.name,
    'sha256': hashlib.sha256(ipa.read_bytes()).hexdigest(),
    'converterArchiveSha256': converter,
    'localXapComparison': bool(source_hashes),
    'deviceAcceptance': 'not established by this audit',
}
if args.game_host:
    report['nativeLibraries'] = pin['nativeLibraries']
    report['runtimeCatalogSha256'] = pin['runtimeCatalogSha256']
destination = repo / ('out/ios/game-audit.json' if args.game_host else 'out/ios/probe-audit.json')
destination.write_text(json.dumps(report, indent=2) + '\n')
print(json.dumps(report, indent=2))
