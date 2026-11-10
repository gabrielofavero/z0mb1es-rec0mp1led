"""Audit the small experiment's IPA. This is not a gameplay or rights approval."""
import hashlib
import argparse
import json
import plistlib
import struct
import zipfile
from pathlib import Path

repo = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--ipa', type=Path, help='Explicit experiment IPA (also useful for audit fixtures)')
args = parser.parse_args()
ipas = [args.ipa.resolve()] if args.ipa else list((repo / 'src/Port.IosProbe/bin/Release').rglob('Z0MB1ES-execution-probe.ipa'))
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
    # Source project has no content resources, FNA, game runtime or prepared output.
    for name in names:
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
destination = repo / 'out/ios/probe-audit.json'
destination.write_text(json.dumps(report, indent=2) + '\n')
print(json.dumps(report, indent=2))
