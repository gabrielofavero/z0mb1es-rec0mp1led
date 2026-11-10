#!/bin/sh
# Build the pinned LGPL converter on Apple Silicon before reviewing its binary hash.
set -eu
repo=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
source_dir="$repo/private/ffmpeg-macos/source"
work="$repo/private/ffmpeg-macos/build"
output="$repo/private/macos-inputs/ffmpeg"
test "$(uname -s)" = Darwin && test "$(uname -m)" = arm64
mkdir -p "$work" "$(dirname "$output")"
printf '%s  %s\n' \
  83e6704730683d004d20e21b8f7f55dcb3383cdf84c0daedf30bde175f774638 "$source_dir/libogg-1.3.6.tar.gz" \
  b33cc4934322bcbf6efcbacf49e3ca01aadbea4114ec9589d1b1e9d20f72954b "$source_dir/libvorbis-1.3.7.tar.xz" \
  11b3259addbc18cfb5cf4b198a746e8bd34828419f4284a3d00599abaadd0244 "$source_dir/ffmpeg-239f2c733d.tar.gz" | shasum -a 256 -c -
cd "$work"
for archive in "$source_dir"/*; do tar -xf "$archive"; done
prefix="$work/prefix"
cd libogg-1.3.6
./configure --prefix="$prefix" --disable-shared --enable-static
make -j4 && make install
cd ../libvorbis-1.3.7
PKG_CONFIG_PATH="$prefix/lib/pkgconfig" ./configure --prefix="$prefix" --disable-shared --enable-static
make -j4 && make install
cd ../FFmpeg-239f2c733d
PKG_CONFIG_PATH="$prefix/lib/pkgconfig" ./configure \
  --disable-autodetect --disable-doc --disable-debug --disable-ffplay --disable-ffprobe \
  --disable-avdevice --disable-network --disable-asm --disable-shared --enable-static \
  --disable-gpl --disable-nonfree --enable-libvorbis --pkg-config-flags=--static \
  --extra-cflags="-I$prefix/include" --extra-ldflags="-L$prefix/lib"
make -j4
cp ffmpeg "$output"
chmod 755 "$output"
shasum -a 256 "$output"
