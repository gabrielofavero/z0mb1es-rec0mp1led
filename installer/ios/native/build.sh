#!/bin/sh
# Build the vendored WMA-to-QOA converter for iOS or the macOS import checks.
set -eu
repo=$(CDPATH= cd -- "$(dirname -- "$0")/../../.." && pwd)
target=${1:-ios-arm64}
case "$target" in
  ios-arm64) sdk=iphoneos; rust_target=aarch64-apple-ios; output="$repo/out/ios/native" ;;
  osx-arm64) sdk=macosx; rust_target=aarch64-apple-darwin; output="$repo/out/ios/checks/native" ;;
  *) echo 'Expected ios-arm64 or osx-arm64' >&2; exit 1 ;;
esac
if [ -x "$repo/out/tooling/rust-1.100.0/bin/cargo" ]; then
  export PATH="$repo/out/tooling/rust-1.100.0/bin:$PATH"
fi
rustc --version | rg '^rustc 1\.100\.0 ' >/dev/null || { echo 'Requires Rust 1.100.0' >&2; exit 1; }
SDKROOT=$(xcrun --sdk "$sdk" --show-sdk-path)
export SDKROOT
export IPHONEOS_DEPLOYMENT_TARGET=15.0
export MACOSX_DEPLOYMENT_TARGET=13.0
export CARGO_TARGET_DIR="$repo/out/ios/audio-converter"
cd "$repo"
# Override cdylib on iOS: the managed host imports the static C ABI from __Internal.
# Cargo tracks source changes even when a previous converter archive exists.
if [ "$target" = ios-arm64 ]; then
  cargo rustc --release --offline --locked --lib --target "$rust_target" \
    --manifest-path tools/audio-converter/Cargo.toml --crate-type staticlib
else
  cargo build --release --offline --locked --lib --target "$rust_target" \
    --manifest-path tools/audio-converter/Cargo.toml
fi
mkdir -p "$output/notices"
if [ "$target" = ios-arm64 ]; then
  cp "$CARGO_TARGET_DIR/$rust_target/release/libport_audio_converter.a" "$output/libz0audio.a"
  library="$output/libz0audio.a"
else
  cp "$CARGO_TARGET_DIR/$rust_target/release/libport_audio_converter.dylib" "$output/libz0audio.dylib"
  rm -f "$output/libz0audio.a"
  library="$output/libz0audio.dylib"
fi
shasum -a 256 "$library" | cut -d ' ' -f 1 > "$output/converter-identity.txt"
# Remove notices left by the retired FFmpeg converter when reusing a build folder.
rm -f "$output/notices/FFmpeg-LGPL-2.1.txt" "$output/notices/libogg.txt" "$output/notices/libvorbis.txt"
cp tools/audio-converter/vendor/gameframe-wma/LICENSE-APACHE "$output/notices/gameframe-wma.txt"
cp tools/audio-converter/vendor/gameframe-wma/THIRD_PARTY.md "$output/notices/gameframe-wma-THIRD-PARTY.txt"
cp tools/audio-converter/vendor/qoaudio/LICENSE "$output/notices/qoaudio.txt"
cp installer/notices/Mono.Cecil-NOTICE.txt "$output/notices/Mono.Cecil.txt"
echo "Built $target WMA-to-QOA converter in $output"
