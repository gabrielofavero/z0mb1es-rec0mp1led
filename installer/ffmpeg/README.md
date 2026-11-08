# Redistributable FFmpeg converter

The installer uses an independent `ffmpeg.exe` to convert the player's WMA music to Ogg Vorbis during local preparation. The previous third-party static build enabled GPL components and lacked a verifiable complete source package. This recipe builds FFmpeg 8.1.1 at revision `239f2c733d` with only the libogg 1.3.6 and libvorbis 1.3.7 external libraries. GPL and nonfree components are disabled. The resulting converter reports LGPL 2.1 or later; the port's own license does not have to change.

Download these archives to ignored `private/ffmpeg-compliance/`:

| File | Source | SHA-256 |
| --- | --- | --- |
| `ffmpeg-239f2c733d.tar.gz` | [FFmpeg revision 239f2c733d](https://github.com/FFmpeg/FFmpeg/archive/239f2c733d.tar.gz) | `11b3259addbc18cfb5cf4b198a746e8bd34828419f4284a3d00599abaadd0244` |
| `libogg-1.3.6.tar.gz` | [Xiph libogg 1.3.6](https://downloads.xiph.org/releases/ogg/libogg-1.3.6.tar.gz) | `83e6704730683d004d20e21b8f7f55dcb3383cdf84c0daedf30bde175f774638` |
| `libvorbis-1.3.7.tar.xz` | [Xiph libvorbis 1.3.7](https://downloads.xiph.org/releases/vorbis/libvorbis-1.3.7.tar.xz) | `b33cc4934322bcbf6efcbacf49e3ca01aadbea4114ec9589d1b1e9d20f72954b` |

Build from the repository root with Docker Desktop in Linux mode:

```powershell
docker build -t zombies-ffmpeg-build:1 installer/ffmpeg
docker run --rm -v "${PWD}:/work" zombies-ffmpeg-build:1
python installer/ffmpeg/package-source.py
```

The build writes `private/tooling/ffmpeg.exe`. The packaging script verifies the pinned binary and source hashes, creates `installer/out/FFmpeg-8.1.1-source.zip`, and extracts full license texts to `installer/out/ffmpeg-notices/`. Ship the source ZIP beside `Z0MB1ES-0.1.0.exe` at the same release location. The source ZIP contains all three complete source archives and the build scripts. Include both files in every mirror and retain both for as long as the setup is offered. The setup itself includes the license and source-location notices in the installed `current/notices/` directory.

This is a separate executable. The project license can remain as it is. Any future FFmpeg binary replacement needs its own matching-source package and notice review.
