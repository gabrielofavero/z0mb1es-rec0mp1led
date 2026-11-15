#!/bin/sh
set -eu
repo=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
cd "$repo"
[ "$(dotnet --version)" = 10.0.302 ] || { echo 'Requires .NET SDK 10.0.302' >&2; exit 1; }
# This baseline matches the iOS manifest shipped in the pinned SDK. Do not silently
# accept a newer workload/Xcode pairing; review and update the experiment together.
xcode=$(xcodebuild -version)
case "$xcode" in
  'Xcode 26.0.1
Build version '*) ;;
  *) echo 'Probe baseline requires full Xcode 26.0.1 on macOS 15.6+; Command Line Tools are insufficient.' >&2; exit 1 ;;
esac
dotnet workload list | rg '26\.0\.11017' >/dev/null || {
  echo 'Requires iOS workload manifest 26.0.11017; install with --skip-manifest-update using the pinned SDK.' >&2; exit 1;
}
test -f out/ios/native/libz0audio.a || sh installer/ios/native/build.sh
test -f out/ios/native/converter-identity.txt
project=src/Port.IosProbe/Port.IosProbe.csproj
dotnet restore "$project"
dotnet build "$project" -c Release --no-restore \
  -p:EnableCodeSigning=false -p:BuildIpa=true \
  -p:UseInterpreter=true -p:PublishAot=false -p:TrimMode=copy \
  -bl:out/ios/probe-build.binlog
dotnet run --project tests/IosProbe.Checks -c Release -- --bundle \
  src/Port.IosProbe/bin/Release/net10.0-ios26.0/ios-arm64/Z0MB1ES-execution-probe.ipa
python3 installer/ios/audit-probe.py
