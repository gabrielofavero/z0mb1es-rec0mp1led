#!/bin/sh
# Personal local build only. Proprietary inputs/outputs remain under private/.
# Usage: sh installer/ios/build-bundled.sh phone.xap zombies-360 time-viking-360
set -eu
repo=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
cd "$repo"
[ "$#" -eq 3 ] || { echo 'Expected: <phone.xap> <zombies-360> <time-viking-360>' >&2; exit 1; }
export PATH="$repo/out/tooling/dotnet:$PATH"
export DEVELOPER_DIR=${DEVELOPER_DIR:-/Applications/Xcode_26.0.1.app/Contents/Developer}
[ "$(dotnet --version)" = 10.0.302 ] || { echo 'Requires .NET SDK 10.0.302' >&2; exit 1; }
case "$(xcodebuild -version)" in
  'Xcode 26.0.1
Build version '*) ;;
  *) echo 'Requires Xcode 26.0.1' >&2; exit 1 ;;
esac
xcodebuild -checkFirstLaunchStatus
dotnet workload list | rg '26\.0\.11017' >/dev/null || { echo 'Requires iOS workload 26.0.11017' >&2; exit 1; }
dotnet build src/Port.Hooks -c Release --source out/deps/cache -p:NuGetAudit=false
python3 installer/ios/stage-game-inputs.py
sh installer/ios/native/build.sh
sh installer/ios/native/build.sh osx-arm64
mkdir -p "$repo/private/ios-bundled"
personal_output=$(mktemp -d "$repo/private/ios-bundled/preparation-XXXXXXXX")
# The preparer requires a new path; retain its parent as a local build record.
personal_payload="$personal_output/game"
dotnet run --project tools/ios-bundle-prepare -c Release -- "$1" out/ios/game "$personal_payload" \
    out/ios/checks/native/libz0audio.dylib "$2" "$3"
dotnet build src/Port.IosBundled -t:Rebuild -c Release -p:NuGetAudit=false \
    "-p:BundledPayloadDirectory=$personal_payload/preinstalled" -bl:private/ios-bundled/build.binlog
personal_ipa="$repo/private/ios-bundled/bin/Release/net10.0-ios26.0/ios-arm64/Z0MB1ES-1.1.0-arm64-ios.ipa"
dotnet run --project tests/IosGame.Checks -c Release -- --bundled-ipa "$personal_ipa"
python3 installer/ios/audit-bundled.py "$personal_ipa" "$personal_payload/preinstalled"
