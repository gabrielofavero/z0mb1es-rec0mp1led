#!/usr/bin/env python3
"""Assemble an unsigned Apple Silicon app and installer from pinned local inputs."""
import argparse
import hashlib
import json
import pathlib
import shutil
import subprocess
import tempfile

repo = pathlib.Path(__file__).resolve().parents[2]
pin = json.loads((repo / 'config/macos-packaging.json').read_text())
if pin['runtime'] != 'osx-arm64':
    raise SystemExit('Only osx-arm64 is configured')
inputs = repo / 'private/macos-inputs'
publish = repo / 'private/macos-publish'
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--output', type=pathlib.Path, default=repo / 'out/macos/packages',
                    help='Destination directory for the app and installer')
output = parser.parse_args().output.resolve()
output.mkdir(parents=True, exist_ok=True)
obsolete_source = output / 'FFmpeg-8.1.1-macos-source.zip'
if obsolete_source.is_file():
    obsolete_source.unlink()
with tempfile.TemporaryDirectory(prefix='z0mb1es-macos-') as temporary:
    stage = pathlib.Path(temporary) / 'stage'
    app = stage / 'Z0MB1ES.app'
    macos = app / 'Contents/MacOS'
    resources = app / 'Contents/Resources'
    macos.mkdir(parents=True)
    resources.mkdir()
    shutil.copytree(publish / 'game', macos, dirs_exist_ok=True)
    tools = macos / 'tools'
    (tools / 'abi').mkdir(parents=True)
    for name in ('inspect', 'decode'):
        executable = 'inspect_assembly' if name == 'inspect' else 'decode_360_xnb'
        shutil.copy2(publish / name / executable, tools / executable)
    shutil.copytree(publish / 'abi', tools / 'abi', dirs_exist_ok=True)
    shutil.copy2(inputs / 'Port.AssetImporter', tools / 'Port.AssetImporter')
    shutil.copy2(repo / 'out/audio-converter/release/port-audio-converter', tools / 'port-audio-converter')
    for name in pin['requiredNativeFiles']:
        shutil.copy2(repo / 'out/deps/native/osx-arm64' / name, macos / name)
    for name in ('SDL3', 'FNA3D', 'FAudio'):
        shutil.copy2(macos / f'lib{name}.0.dylib', macos / f'lib{name}.dylib')
    config = macos / 'config'
    config.mkdir()
    shutil.copy2(repo / 'config/supported-versions.json', config)
    converter_hash = hashlib.sha256((tools / 'port-audio-converter').read_bytes()).hexdigest()
    (config / 'macos-converter.sha256').write_text(converter_hash + '  port-audio-converter\n')
    shutil.copy2(repo / 'installer/macos/dialogs.applescript', macos)
    shutil.copy2(repo / 'installer/macos/launch.sh', macos)
    shutil.copy2(repo / 'installer/macos/Info.plist', app / 'Contents/Info.plist')
    iconset = pathlib.Path(temporary) / 'Z0MB1ES.iconset'
    iconset.mkdir()
    icon = repo / 'assets/icon/icon-macos.png'
    for size in (16, 32, 128, 256, 512):
        for scale in (1, 2):
            target = iconset / f'icon_{size}x{size}{"@2x" if scale == 2 else ""}.png'
            subprocess.run(['sips', '-z', str(size * scale), str(size * scale),
                            str(icon), '--out', str(target)], check=True, capture_output=True)
    subprocess.run(['iconutil', '-c', 'icns', str(iconset), '-o', str(resources / 'Z0MB1ES.icns')], check=True)
    notices = resources / 'Notices'
    notices.mkdir()
    for source, target in (
        ('LICENSE', 'Port-GPL-2.0.txt'),
        ('out/deps/fna-26.11/FNA/licenses/LICENSE', 'FNA-LICENSE.txt'),
        ('out/deps/fna-26.11/FNA/lib/FNA3D/LICENSE', 'FNA3D-LICENSE.txt'),
        ('out/deps/fna-26.11/FNA/lib/FAudio/LICENSE', 'FAudio-LICENSE.txt'),
        ('out/deps/fna-26.11/FNA/lib/Theorafile/licenses/LICENSE', 'Theorafile-LICENSE.txt')):
        shutil.copy2(repo / source, notices / target)
    shutil.copy2(repo / 'tools/audio-converter/vendor/gameframe-wma/LICENSE-APACHE', notices / 'gameframe-wma-LICENSE-APACHE')
    shutil.copy2(repo / 'tools/audio-converter/vendor/qoaudio/LICENSE', notices / 'qoaudio-LICENSE')
    for path in app.rglob('*'):
        if path.is_file() and (path.suffix.lower() in ('.xap', '.xnb', '.wma', '.ogg', '.qoa')
                               or path.name == 'ZombiesWP7.dll'):
            raise SystemExit(f'Proprietary file in package: {path}')
    for path in (macos / 'Z0MB1ES', macos / 'launch.sh', tools / 'port-audio-converter', tools / 'Port.AssetImporter',
                 tools / 'inspect_assembly', tools / 'decode_360_xnb', tools / 'abi/abi_check'):
        path.chmod(0o755)
    app_destination = output / 'Z0MB1ES-1.1.0-osx-arm64.app'
    if app_destination.exists():
        raise SystemExit(f'Remove or archive the existing app before repackaging: {app_destination}')
    shutil.copytree(app, app_destination)
    destination = output / 'Z0MB1ES-1.1.0-osx-arm64.pkg'
    subprocess.run(['pkgbuild', '--root', str(stage), '--install-location', '/Applications',
                    '--component-plist', str(repo / 'installer/macos/components.plist'),
                    '--identifier', 'io.github.z0mb1esrec0mp1led.game', '--version', '1.1.0',
                    str(destination)], check=True)
    print(f'{hashlib.sha256(destination.read_bytes()).hexdigest()}  {destination}')
