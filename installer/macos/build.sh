#!/bin/sh
# Run on an Apple Silicon Mac after filling artifactHashes in macos-packaging.json.
set -eu
repo=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
cd "$repo"
test "$(uname -s)" = Darwin || { echo 'macOS is required for .app/.pkg assembly' >&2; exit 1; }
test "$(uname -m)" = arm64 || { echo 'Only Apple Silicon is configured; Intel is not promised' >&2; exit 1; }
python3 - "$repo" <<'PY'
import hashlib, json, pathlib, subprocess, sys
repo = pathlib.Path(sys.argv[1])
pin = json.loads((repo / 'config/macos-packaging.json').read_text())
sdk = subprocess.check_output(['dotnet', '--version'], text=True).strip()
if sdk != pin['dotnetSdk']: raise SystemExit(f'Expected .NET SDK {pin["dotnetSdk"]}; found {sdk}')
xcode = subprocess.check_output(['xcodebuild', '-version'], text=True).splitlines()[0].removeprefix('Xcode ')
if xcode != pin['xcodeVersion']: raise SystemExit(f'Expected Xcode {pin["xcodeVersion"]}; found {xcode}')
checks = pin['artifactHashes']
required = [f'out/deps/native/osx-arm64/{name}' for name in pin['requiredNativeFiles']]
required += ['private/macos-inputs/ffmpeg', 'private/macos-inputs/Port.AssetImporter']
required += [f'out/deps/cache/microsoft.netcore.app.{kind}.osx-arm64.{pin["dotnetRuntime"]}.nupkg'
             for kind in ('runtime', 'host')]
for name in required:
    expected = checks.get(name)
    if not expected or len(expected) != 64: raise SystemExit(f'Missing reviewed SHA-256 pin: {name}')
    actual = hashlib.sha256((repo / name).read_bytes()).hexdigest()
    if actual != expected: raise SystemExit(f'Hash mismatch: {name}')
PY
mkdir -p private/macos-publish/game private/macos-publish/inspect \
  private/macos-publish/abi private/macos-publish/decode out/macos/packages
for spec in 'src/Port.Launcher/Port.Launcher.csproj game' \
            'tools/inspect_assembly/inspect_assembly.csproj inspect' \
            'tools/abi_check/abi_check.csproj abi' \
            'tools/decode_360_xnb/decode_360_xnb.csproj decode'; do
  project=${spec% *}
  name=${spec#* }
  dotnet restore "$project" -r osx-arm64 --source out/deps/cache --source "${NUGET_PACKAGES:-$HOME/.nuget/packages}"
  single_file=false
  if [ "$name" = inspect ] || [ "$name" = decode ]; then single_file=true; fi
  dotnet publish "$project" -c Release -r osx-arm64 --self-contained true --no-restore \
    -p:PublishTrimmed=false -p:PublishSingleFile="$single_file" \
    -o "private/macos-publish/$name" -v:q
done
for binary in private/macos-publish/game/Z0MB1ES private/macos-publish/inspect/inspect_assembly \
              private/macos-publish/abi/abi_check private/macos-publish/decode/decode_360_xnb \
              private/macos-inputs/ffmpeg private/macos-inputs/Port.AssetImporter \
              out/deps/native/osx-arm64/*.dylib; do
  lipo -archs "$binary" | grep -qw arm64 || { echo "Missing arm64 slice: $binary" >&2; exit 1; }
  if otool -L "$binary" | grep -Eq '/opt/homebrew|/usr/local'; then
    echo "Nonbundled native dependency: $binary" >&2; exit 1
  fi
done
python3 installer/macos/package.py
