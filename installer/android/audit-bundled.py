"""Audit a personal Android APK against its verified local payload, never a release audit."""
import hashlib
import json
import sys
import zipfile
from pathlib import Path


def audit(apk, payload):
    pin = json.loads((Path(__file__).resolve().parents[2] / 'config/android-packaging.json').read_text())
    manifest_bytes = (payload / 'bundled-manifest.json').read_bytes()
    manifest = json.loads(manifest_bytes)
    assert manifest['Format'] == 1 and len(manifest['Files']) >= 100, 'Incomplete manifest'
    with zipfile.ZipFile(apk) as package:
        names = package.namelist()
        assert len(names) == len(set(names)), 'Duplicate APK entries'
        assert package.read('assets/preinstalled/bundled-manifest.json') == manifest_bytes
        listed = set()
        for row in manifest['Files']:
            path = row['Path']
            assert '\\' not in path and ':' not in path and all(p not in ('', '.', '..') for p in path.split('/'))
            name = 'assets/preinstalled/' + path
            assert name not in listed, 'Duplicate payload entry'
            listed.add(name)
            data = package.read(name)
            assert len(data) == row['Bytes'] and hashlib.sha256(data).hexdigest() == row['Sha256'], path
            assert data == (payload / path).read_bytes(), path
        actual = {name for name in names if name.startswith('assets/preinstalled/') and not name.endswith('/')}
        assert actual == listed | {'assets/preinstalled/bundled-manifest.json'}, 'Unlisted bundled files'
        for native, expected in pin['nativeLibraries'].items():
            data = package.read('lib/arm64-v8a/' + native)
            assert data[:4] == b'\x7fELF' and data[4:6] == b'\x02\x01' and int.from_bytes(data[18:20], 'little') == 183, native
            assert hashlib.sha256(data).hexdigest() == expected, 'Wrong native library: ' + native
        assert not any(name.startswith(('lib/armeabi', 'lib/x86')) for name in names), 'Unexpected ABI'
        assert not any(name.lower().endswith(('.xap', '.keystore', '.pfx', '.wma', '.exe')) for name in names), 'Unexpected input/tool/key in APK'
        assert 'AndroidManifest.xml' in names and 'classes.dex' in names
    print(f'ANDROID_BUNDLED_AUDIT_OK files={len(listed)} bytes={apk.stat().st_size} sha256={hashlib.sha256(apk.read_bytes()).hexdigest()}')


if __name__ == '__main__':
    if len(sys.argv) != 3:
        raise SystemExit('Usage: audit-bundled.py <personal.apk> <preinstalled-directory>')
    audit(Path(sys.argv[1]), Path(sys.argv[2]))
