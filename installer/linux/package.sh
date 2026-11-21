#!/bin/sh
# Run on Linux x64 after publishing the managed host/tools and building the art importer.
set -eu
repo=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
cd "$repo"
test "$(uname -m)" = x86_64 || { echo 'Linux x64 build host is required' >&2; exit 1; }
work=$(mktemp -d /tmp/z0mb1es-package.XXXXXX)
trap 'rm -rf "$work"' EXIT
stage="$work/app"
packages="$work/packages"
output=out/linux/packages
publish=private/linux-publish
pin=config/linux-packaging.json
python3 - "$pin" <<'PY'
import hashlib, json, pathlib, sys
p = json.loads(pathlib.Path(sys.argv[1]).read_text())
checks = {'out/deps/cache/fnalibs-2026-11-06.zip': p['nativeArchiveSha256'],
          'private/tooling/nfpm_2.48.0_Linux_x86_64.tar.gz': p['nfpmArchiveSha256']}
checks.update({f'out/deps/native/linux-x64/{name}': sha for name, sha in p['nativeFiles'].items()})
for name, expected in checks.items():
    digest = hashlib.sha256(pathlib.Path(name).read_bytes()).hexdigest()
    if digest != expected: raise SystemExit(f'Hash mismatch: {name}')
PY
test -x private/tooling/nfpm
test -x private/linux-art/dist/Port.AssetImporter
test -f "$publish/game/Z0MB1ES"
test -f "$publish/inspect/inspect_assembly"
test -f "$publish/abi/abi_check"
test -f "$publish/decode/decode_360_xnb"
CARGO_TARGET_DIR=out/audio-converter cargo build --release --offline --manifest-path tools/audio-converter/Cargo.toml
test -x out/audio-converter/release/port-audio-converter
mkdir -p "$stage/tools/abi" "$stage/config" "$stage/notices" "$packages" "$output"
cp -a "$publish/game/." "$stage/"
cp "$publish/inspect/inspect_assembly" "$stage/tools/"
cp -a "$publish/abi/." "$stage/tools/abi/"
cp "$publish/decode/decode_360_xnb" "$stage/tools/"
cp private/linux-art/dist/Port.AssetImporter "$stage/tools/"
cp out/audio-converter/release/port-audio-converter "$stage/tools/port-audio-converter"
cp out/deps/native/linux-x64/* "$stage/"
for library in SDL3 FNA3D FAudio; do
  cp "$stage/lib$library.so.0" "$stage/lib$library.so"
done
cp config/supported-versions.json "$stage/config/"
(cd "$stage/tools" && sha256sum port-audio-converter) > "$stage/config/linux-converter.sha256"
cp tools/audio-converter/vendor/gameframe-wma/LICENSE-APACHE "$stage/notices/gameframe-wma-LICENSE-APACHE"
cp tools/audio-converter/vendor/qoaudio/LICENSE "$stage/notices/qoaudio-LICENSE"
cp LICENSE "$stage/notices/Port-GPL-2.0.txt"
cp out/deps/fna-26.11/FNA/licenses/LICENSE "$stage/notices/FNA-LICENSE.txt"
cp out/deps/fna-26.11/FNA/lib/FNA3D/LICENSE "$stage/notices/FNA3D-LICENSE.txt"
cp out/deps/fna-26.11/FNA/lib/FAudio/LICENSE "$stage/notices/FAudio-LICENSE.txt"
cp out/deps/fna-26.11/FNA/lib/Theorafile/licenses/LICENSE "$stage/notices/Theorafile-LICENSE.txt"
cp installer/linux/launch.sh "$stage/"
find "$stage" -type d -exec chmod 755 {} +
find "$stage" -type f -exec chmod 644 {} +
chmod 755 "$stage/Z0MB1ES" "$stage/launch.sh" "$stage/tools/Port.AssetImporter" \
  "$stage/tools/inspect_assembly" "$stage/tools/decode_360_xnb" "$stage/tools/port-audio-converter" \
  "$stage/tools/abi/abi_check" "$stage/createdump"
find "$stage" -name '*.so*' -type f -exec chmod 755 {} +
python3 - "$stage" <<'PY'
import pathlib, sys
root = pathlib.Path(sys.argv[1])
for path in root.rglob('*'):
    if path.is_file() and (path.suffix.lower() in ('.xap', '.xnb', '.wma', '.ogg', '.qoa') or path.name == 'ZombiesWP7.dll'):
        raise SystemExit(f'Proprietary file in package: {path}')
PY
sed "s|out/linux/stage/app/|$stage/|" installer/linux/nfpm.yaml > "$work/nfpm.yaml"
private/tooling/nfpm pkg --config "$work/nfpm.yaml" --packager deb --target "$packages/Z0MB1ES-1.1.0-x64-linux.deb"
private/tooling/nfpm pkg --config "$work/nfpm.yaml" --packager rpm --target "$packages/Z0MB1ES-1.1.0-x64-linux.rpm"
rm -f "$output/FFmpeg-8.1.1-source.zip" "$output/FFmpeg-8.1.1-linux-source.zip"
cp "$packages"/* "$output/"
sha256sum "$output"/*
