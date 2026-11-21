"""Audit the content-free Android player and its exact preparation metadata."""
import hashlib
import json
import sys
import zipfile
from pathlib import Path

repo = Path(__file__).resolve().parents[2]


def audit(apk):
    pin = json.loads((repo / 'config/android-packaging.json').read_text())
    inputs = repo / 'out/android/import-inputs'
    manifest = (inputs / 'inputs-manifest.json').read_bytes()
    entries = json.loads(manifest)
    expected = {'assets/preparation/inputs-manifest.json'}
    with zipfile.ZipFile(apk) as package:
        names = package.namelist()
        assert len(names) == len(set(names)), 'Duplicate APK entries'
        assert package.read('assets/preparation/inputs-manifest.json') == manifest
        for entry in entries:
            relative = entry['Path']
            assert '\\' not in relative and ':' not in relative and all(p not in ('', '.', '..') for p in relative.split('/'))
            name = 'assets/preparation/' + relative
            assert name not in expected, 'Duplicate preparation input'
            expected.add(name)
            data = package.read(name)
            assert data == (inputs / relative).read_bytes()
            assert len(data) == entry['Bytes'] and hashlib.sha256(data).hexdigest() == entry['Sha256']
        actual = {name for name in names if name.startswith('assets/preparation/') and not name.endswith('/')}
        assert actual == expected, 'Unlisted preparation metadata'
        for name in names:
            lower = name.lower()
            assert not lower.startswith('assets/preinstalled/'), 'Bundled game payload'
            assert not lower.endswith(('.xap', '.xnb', '.wma', '.qoa', '.ogg', '.keystore', '.pfx', '.exe')), 'Game content/tool/key: ' + name
            assert 'zombieswp7.dll' not in lower and not lower.endswith(('asset-manifest.json', 'patch-manifest.json', 'content-manifest.json', 'bundled-manifest.json')), 'Prepared game: ' + name
            assert not lower.startswith(('lib/armeabi', 'lib/x86')), 'Unexpected ABI'
        native = dict(pin['nativeLibraries'])
        converter = (repo / 'out/android/native/libport_audio_converter.so').read_bytes()
        converter_hash = hashlib.sha256(converter).hexdigest()
        assert package.read('assets/converter-identity.txt').decode().strip() == converter_hash
        native['libport_audio_converter.so'] = converter_hash
        for name, expected_hash in native.items():
            data = package.read('lib/arm64-v8a/' + name)
            assert data[:4] == b'\x7fELF' and int.from_bytes(data[18:20], 'little') == 183, name
            assert hashlib.sha256(data).hexdigest() == expected_hash, name
            offset = int.from_bytes(data[32:40], 'little')
            size = int.from_bytes(data[54:56], 'little')
            count = int.from_bytes(data[56:58], 'little')
            for index in range(count):
                row = data[offset + index * size:offset + (index + 1) * size]
                if int.from_bytes(row[:4], 'little') == 1:
                    assert int.from_bytes(row[48:56], 'little') >= 16384, 'ELF alignment: ' + name
        assert package.read('assets/notices/audio/gameframe-wma.txt')
        assert package.read('assets/notices/audio/qoaudio.txt')
        assert package.read('assets/notices/audio/gameframe-wma-THIRD-PARTY.txt')
        assert 'AndroidManifest.xml' in names and 'classes.dex' in names
    print(f'ANDROID_IMPORTER_AUDIT_OK inputs={len(entries)} bytes={apk.stat().st_size} sha256={hashlib.sha256(apk.read_bytes()).hexdigest()}')


if __name__ == '__main__':
    if len(sys.argv) != 2:
        raise SystemExit('Usage: audit-importer.py <player.apk>')
    audit(Path(sys.argv[1]))
