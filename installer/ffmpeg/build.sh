#!/bin/sh
set -eu

src=/work/private/ffmpeg-compliance
out=/tmp/ffmpeg-build
mkdir -p "$out" /work/private/tooling
cd "$out"

printf '%s  %s\n' \
    83e6704730683d004d20e21b8f7f55dcb3383cdf84c0daedf30bde175f774638 "$src/libogg-1.3.6.tar.gz" \
    b33cc4934322bcbf6efcbacf49e3ca01aadbea4114ec9589d1b1e9d20f72954b "$src/libvorbis-1.3.7.tar.xz" \
    11b3259addbc18cfb5cf4b198a746e8bd34828419f4284a3d00599abaadd0244 "$src/ffmpeg-239f2c733d.tar.gz" | sha256sum -c -

[ -d "$out/libogg-1.3.6" ] || tar -xf "$src/libogg-1.3.6.tar.gz"
[ -d "$out/libvorbis-1.3.7" ] || tar -xf "$src/libvorbis-1.3.7.tar.xz"
[ -d "$out/FFmpeg-239f2c733d" ] || tar -xf "$src/ffmpeg-239f2c733d.tar.gz"

export CC=x86_64-w64-mingw32-gcc
export AR=x86_64-w64-mingw32-ar
export RANLIB=x86_64-w64-mingw32-ranlib
export PKG_CONFIG_LIBDIR=/opt/ffmpeg/lib/pkgconfig

cd "$out/libogg-1.3.6"
./configure --host=x86_64-w64-mingw32 --prefix=/opt/ffmpeg --disable-shared --enable-static
make -j4
make install

cd "$out/libvorbis-1.3.7"
./configure --host=x86_64-w64-mingw32 --prefix=/opt/ffmpeg --disable-shared --enable-static
make -j4
make install

cd "$out/FFmpeg-239f2c733d"
./configure \
    --target-os=mingw32 --arch=x86_64 --cross-prefix=x86_64-w64-mingw32- \
    --enable-cross-compile --disable-autodetect --disable-doc --disable-debug \
    --disable-ffplay --disable-ffprobe --disable-avdevice --disable-network \
    --disable-asm --disable-pthreads --disable-shared --enable-static --disable-gpl --disable-nonfree \
    --enable-libvorbis --pkg-config=pkgconf --pkg-config-flags=--static \
    --extra-cflags=-I/opt/ffmpeg/include --extra-ldflags=-L/opt/ffmpeg/lib
make -j4
if x86_64-w64-mingw32-objdump -p ffmpeg.exe | grep -qi 'DLL Name: libwinpthread-1.dll'; then
    echo 'Unexpected libwinpthread runtime dependency' >&2
    exit 1
fi
cp ffmpeg.exe /work/private/tooling/ffmpeg.exe
