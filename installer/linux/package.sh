#!/bin/sh
# Run on Linux x64 after publishing the managed host/tools and building FFmpeg/art importer.
set -eu
repo=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
cd "$repo"
work=$(mktemp -d /tmp/z0mb1es-package.XXXXXX)
stage="$work/app"
packages="$work/packages"
output=out/linux/packages
publish=private/linux-publish
pin=config/linux-packaging.json
python3 - "$pin" <<'PY'
import hashlib, json, pathlib, sys
p = json.loads(pathlib.Path(sys.argv[1]).read_text())
checks = {'out/deps/cache/fnalibs-2026-11-06.zip': p['nativeArchiveSha256'],
          'private/tooling/ffmpeg-linux': p['ffmpegSha256'],
          'private/tooling/nfpm_2.48.0_Linux_x86_64.tar.gz': p['nfpmArchiveSha256'],
          'installer/out/FFmpeg-8.1.1-source.zip': p['ffmpegBaseSourceBundleSha256'],
          'installer/out/FFmpeg-8.1.1-linux-source.zip': p['ffmpegSourceBundleSha256']}
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
test -d installer/out/ffmpeg-notices
mkdir -p "$stage/tools/abi" "$stage/config" "$stage/notices" "$packages" "$output"
cp -a "$publish/game/." "$stage/"
cp "$publish/inspect/inspect_assembly" "$stage/tools/"
cp -a "$publish/abi/." "$stage/tools/abi/"
cp "$publish/decode/decode_360_xnb" "$stage/tools/"
cp private/linux-art/dist/Port.AssetImporter "$stage/tools/"
cp private/tooling/ffmpeg-linux "$stage/tools/ffmpeg"
cp out/deps/native/linux-x64/* "$stage/"
for library in SDL3 FNA3D FAudio; do
  cp "$stage/lib$library.so.0" "$stage/lib$library.so"
done
cp config/supported-versions.json config/linux-converter.sha256 "$stage/config/"
cp -a installer/out/ffmpeg-notices/. "$stage/notices/"
cp LICENSE "$stage/notices/Port-GPL-2.0.txt"
cp out/deps/fna-26.11/FNA/licenses/LICENSE "$stage/notices/FNA-LICENSE.txt"
cp out/deps/fna-26.11/FNA/lib/FNA3D/LICENSE "$stage/notices/FNA3D-LICENSE.txt"
cp out/deps/fna-26.11/FNA/lib/FAudio/LICENSE "$stage/notices/FAudio-LICENSE.txt"
cp out/deps/fna-26.11/FNA/lib/Theorafile/licenses/LICENSE "$stage/notices/Theorafile-LICENSE.txt"
printf 'Linux ffmpeg SHA-256: %s. Matching source: FFmpeg-8.1.1-linux-source.zip beside each Linux package.\n' \
  7895c3b0687e340f7bee2d2ac5d7049b7a91eec003b042db1263555131c7c562 \
  > "$stage/notices/FFmpeg-SOURCE.txt"
cp installer/linux/launch.sh "$stage/"
find "$stage" -type d -exec chmod 755 {} +
find "$stage" -type f -exec chmod 644 {} +
chmod 755 "$stage/Z0MB1ES" "$stage/launch.sh" "$stage/tools/Port.AssetImporter" \
  "$stage/tools/inspect_assembly" "$stage/tools/decode_360_xnb" "$stage/tools/ffmpeg" \
  "$stage/tools/abi/abi_check" "$stage/createdump"
find "$stage" -name '*.so*' -type f -exec chmod 755 {} +
python3 - "$stage" <<'PY'
import pathlib, sys
root = pathlib.Path(sys.argv[1])
for path in root.rglob('*'):
    if path.is_file() and (path.suffix.lower() in ('.xap', '.xnb', '.wma', '.ogg') or path.name == 'ZombiesWP7.dll'):
        raise SystemExit(f'Proprietary file in package: {path}')
PY
sed "s|out/linux/stage/app/|$stage/|" installer/linux/nfpm.yaml > "$work/nfpm.yaml"
private/tooling/nfpm pkg --config "$work/nfpm.yaml" --packager deb --target "$packages/z0mb1es-rec0mp1led_1.0.0_amd64.deb"
private/tooling/nfpm pkg --config "$work/nfpm.yaml" --packager rpm --target "$packages/z0mb1es-rec0mp1led-1.0.0-1.x86_64.rpm"
cp installer/out/FFmpeg-8.1.1-linux-source.zip "$packages/"
rm -f "$output/FFmpeg-8.1.1-source.zip"
cp "$packages"/* "$output/"
sha256sum "$output"/*
