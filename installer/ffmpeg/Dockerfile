FROM debian:bookworm-slim@sha256:734a72b3d6142a71993b4bc580ca01d8ca687f77a2c422d96a961d3376b179bc

RUN apt-get update && apt-get install -y --no-install-recommends \
    make gcc libc6-dev gcc-mingw-w64-x86-64-win32 binutils-mingw-w64-x86-64 \
    pkgconf xz-utils ca-certificates && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /work
ENTRYPOINT ["/bin/sh", "/work/installer/ffmpeg/build.sh"]
