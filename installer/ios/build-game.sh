#!/bin/sh
# Content-free player IPA. No XAP or optional artwork is a build input.
set -eu
repo=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
cd "$repo"
if [ -x "$repo/out/tooling/dotnet/dotnet" ]; then
  export PATH="$repo/out/tooling/dotnet:$PATH"
fi
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
# A fresh package also prevents this pinned SDK reusing an old compiled Info.plist.
dotnet build src/Port.IosGame -t:Rebuild -c Release -p:NuGetAudit=false -bl:out/ios/game-build.binlog
dotnet run --project tests/IosGame.Checks -c Release -- --game-bundle \
  src/Port.IosGame/bin/Release/net10.0-ios26.0/ios-arm64/Z0MB1ES-1.1.0-arm64-ios.ipa
python3 installer/ios/audit-probe.py --game-host
