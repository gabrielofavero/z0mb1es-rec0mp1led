#!/usr/bin/env python3
"""Assemble an unsigned Apple Silicon app and installer from pinned local inputs."""
import hashlib
import json
import pathlib
import shutil
import subprocess
import tempfile
import zipfile

repo = pathlib.Path(__file__).resolve().parents[2]
pin = json.loads((repo / 'config/macos-packaging.json').read_text())
if pin['runtime'] != 'osx-arm64':
    raise SystemExit('Only osx-arm64 is configured')
inputs = repo / 'private/macos-inputs'
publish = repo / 'private/macos-publish'
output = repo / 'out/macos/packages'
output.mkdir(parents=True, exist_ok=True)
source_dir = repo / 'private/ffmpeg-macos/source'
source_hashes = {
    'libogg-1.3.6.tar.gz': '83e6704730683d004d20e21b8f7f55dcb3383cdf84c0daedf30bde175f774638',
    'libvorbis-1.3.7.tar.xz': 'b33cc4934322bcbf6efcbacf49e3ca01aadbea4114ec9589d1b1e9d20f72954b',
    'ffmpeg-239f2c733d.tar.gz': '11b3259addbc18cfb5cf4b198a746e8bd34828419f4284a3d00599abaadd0244',
}
for name, expected in source_hashes.items():
    if hashlib.sha256((source_dir / name).read_bytes()).hexdigest() != expected:
        raise SystemExit(f'FFmpeg source archive hash mismatch: {name}')
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
    shutil.copy2(inputs / 'ffmpeg', tools / 'ffmpeg')
    for name in pin['requiredNativeFiles']:
        shutil.copy2(repo / 'out/deps/native/osx-arm64' / name, macos / name)
    for name in ('SDL3', 'FNA3D', 'FAudio'):
        shutil.copy2(macos / f'lib{name}.0.dylib', macos / f'lib{name}.dylib')
    config = macos / 'config'
    config.mkdir()
    shutil.copy2(repo / 'config/supported-versions.json', config)
    converter_hash = hashlib.sha256((tools / 'ffmpeg').read_bytes()).hexdigest()
    (config / 'macos-converter.sha256').write_text(converter_hash + '  ffmpeg\n')
    shutil.copy2(repo / 'installer/macos/dialogs.applescript', macos)
    shutil.copy2(repo / 'installer/macos/launch.sh', macos)
    shutil.copy2(repo / 'installer/macos/Info.plist', app / 'Contents/Info.plist')
    iconset = pathlib.Path(temporary) / 'Z0MB1ES.iconset'
    iconset.mkdir()
    icon = repo / 'assets/icon/icon.png'
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
    for path in app.rglob('*'):
        if path.is_file() and (path.suffix.lower() in ('.xap', '.xnb', '.wma', '.ogg')
                               or path.name == 'ZombiesWP7.dll'):
            raise SystemExit(f'Proprietary file in package: {path}')
    for path in (macos / 'Z0MB1ES', macos / 'launch.sh', tools / 'ffmpeg', tools / 'Port.AssetImporter',
                 tools / 'inspect_assembly', tools / 'decode_360_xnb', tools / 'abi/abi_check'):
        path.chmod(0o755)
    app_destination = output / 'Z0MB1ES-1.0.0-osx-arm64.app'
    if app_destination.exists():
        raise SystemExit(f'Remove or archive the existing app before repackaging: {app_destination}')
    shutil.copytree(app, app_destination)
    destination = output / 'Z0MB1ES-1.0.0-osx-arm64.pkg'
    subprocess.run(['pkgbuild', '--root', str(stage), '--install-location', '/Applications',
                    '--identifier', 'io.github.z0mb1esrec0mp1led.game', '--version', '1.0.0',
                    str(destination)], check=True)
    print(f'{hashlib.sha256(destination.read_bytes()).hexdigest()}  {destination}')
    source = output / 'FFmpeg-8.1.1-macos-source.zip'
    with zipfile.ZipFile(source, 'w', compression=zipfile.ZIP_DEFLATED) as archive:
        for name in source_hashes:
            archive.write(source_dir / name, name)
        archive.write(repo / 'installer/macos/build-ffmpeg.sh', 'build-ffmpeg.sh')
    print(f'{hashlib.sha256(source.read_bytes()).hexdigest()}  {source}')
