#!/bin/sh
# Build only WMA v2 -> Vorbis/Ogg, using the desktop converter's reviewed sources.
set -eu
repo=$(CDPATH= cd -- "$(dirname -- "$0")/../../.." && pwd)
target=${1:-ios-arm64}
case "$target" in
  ios-arm64) sdk=iphoneos; minimum=15.0; triple=arm64-apple-ios$minimum; platform=ios ;;
  osx-arm64) sdk=macosx; minimum=13.0; triple=arm64-apple-macos$minimum; platform=darwin ;;
  *) echo 'Expected ios-arm64 or osx-arm64' >&2; exit 1 ;;
esac
sdkroot=$(xcrun --sdk "$sdk" --show-sdk-path)
work="$repo/out/ios/audio-build-$target"
prefix="$work/prefix"
output="$repo/out/ios/native"
[ "$target" = ios-arm64 ] || output="$repo/out/ios/checks/native"
sources=${IOS_AUDIO_SOURCES:-"$repo/private/ffmpeg-macos/source"}
mkdir -p "$work" "$output"
printf '%s  %s\n' \
  83e6704730683d004d20e21b8f7f55dcb3383cdf84c0daedf30bde175f774638 "$sources/libogg-1.3.6.tar.gz" \
  b33cc4934322bcbf6efcbacf49e3ca01aadbea4114ec9589d1b1e9d20f72954b "$sources/libvorbis-1.3.7.tar.xz" \
  11b3259addbc18cfb5cf4b198a746e8bd34828419f4284a3d00599abaadd0244 "$sources/ffmpeg-239f2c733d.tar.gz" | shasum -a 256 -c -
export PATH="$repo/out/tooling/pkg-config/bin:$PATH"
export CC="$(xcrun --sdk "$sdk" --find clang) -target $triple -isysroot $sdkroot"
export CFLAGS='-O2 -fPIC'
export LDFLAGS="-target $triple -isysroot $sdkroot"
export PKG_CONFIG_LIBDIR="$prefix/lib/pkgconfig"
export PKG_CONFIG_PATH="$prefix/lib/pkgconfig"
export GIT_CEILING_DIRECTORIES="$work"
cd "$work"
for archive in libogg-1.3.6.tar.gz libvorbis-1.3.7.tar.xz ffmpeg-239f2c733d.tar.gz; do
  tar -xf "$sources/$archive"
done
cd libogg-1.3.6
./configure --host=aarch64-apple-darwin --prefix="$prefix" --disable-shared --enable-static
make -j4
make install
cd ../libvorbis-1.3.7
./configure --host=aarch64-apple-darwin --prefix="$prefix" --disable-shared --enable-static --disable-oggtest --disable-vorbistest
make -j4
make install
cd ../FFmpeg-239f2c733d
./configure --cc="$CC" --arch=aarch64 --target-os="$platform" --enable-cross-compile \
  --sysroot="$sdkroot" --disable-autodetect --disable-everything --disable-programs \
  --disable-doc --disable-debug --disable-asm --disable-shared --enable-static \
  --disable-gpl --disable-nonfree --disable-network --disable-avdevice --disable-avfilter \
  --disable-swscale --disable-swresample --enable-decoder=wmav2 --enable-demuxer=asf \
  --enable-encoder=libvorbis --enable-libvorbis --enable-muxer=ogg --enable-protocol=file \
  --pkg-config-flags=--static --extra-cflags="-O2 -fPIC -I$prefix/include" --extra-ldflags="-L$prefix/lib"
make -j4
# Deliberate word splitting: CC contains compiler flags, not a shell program.
$CC -O2 -fPIC -Wall -Wextra -Werror -I. -c "$repo/installer/ios/native/audio.c" -o "$work/audio.o"
xcrun --sdk "$sdk" libtool -static -o "$output/libz0audio.a" "$work/audio.o" \
  libavformat/libavformat.a libavcodec/libavcodec.a libavutil/libavutil.a \
  "$prefix/lib/libvorbisenc.a" "$prefix/lib/libvorbis.a" "$prefix/lib/libogg.a"
shasum -a 256 "$output/libz0audio.a" | cut -d ' ' -f 1 > "$output/converter-identity.txt"
mkdir -p "$output/notices"
cp COPYING.LGPLv2.1 "$output/notices/FFmpeg-LGPL-2.1.txt"
cp "$work/libogg-1.3.6/COPYING" "$output/notices/libogg.txt"
cp "$work/libvorbis-1.3.7/COPYING" "$output/notices/libvorbis.txt"
cp "$repo/installer/notices/Mono.Cecil-NOTICE.txt" "$output/notices/Mono.Cecil.txt"
if [ "$target" = osx-arm64 ]; then
  $CC -dynamiclib "$output/libz0audio.a" -Wl,-u,_z0_audio_convert -lz -liconv \
    -o "$output/libz0audio.dylib"
fi
echo "Built $target in $output; no device acceptance is implied."
