"""Create the source archive and installed notices for the pinned FFmpeg build."""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import subprocess
import tarfile
import zipfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "private/ffmpeg-compliance"
OUTPUT = ROOT / "installer/out"
ARCHIVES = {
    "ffmpeg-239f2c733d.tar.gz": "11b3259addbc18cfb5cf4b198a746e8bd34828419f4284a3d00599abaadd0244",
    "libogg-1.3.6.tar.gz": "83e6704730683d004d20e21b8f7f55dcb3383cdf84c0daedf30bde175f774638",
    "libvorbis-1.3.7.tar.xz": "b33cc4934322bcbf6efcbacf49e3ca01aadbea4114ec9589d1b1e9d20f72954b",
}
LICENSES = {
    "FFmpeg-COPYING.LGPLv2.1": ("ffmpeg-239f2c733d.tar.gz", "FFmpeg-239f2c733d/COPYING.LGPLv2.1"),
    "FFmpeg-LICENSE.md": ("ffmpeg-239f2c733d.tar.gz", "FFmpeg-239f2c733d/LICENSE.md"),
    "FFmpeg-libogg-COPYING": ("libogg-1.3.6.tar.gz", "libogg-1.3.6/COPYING"),
    "FFmpeg-libvorbis-COPYING": ("libvorbis-1.3.7.tar.xz", "libvorbis-1.3.7/COPYING"),
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ffmpeg-path", type=Path, default=ROOT / "private/tooling/ffmpeg.exe")
    args = parser.parse_args()
    for name, expected in ARCHIVES.items():
        actual = sha256(SOURCE / name)
        if actual != expected:
            raise SystemExit(f"Wrong source archive hash: {name}: {actual}")

    executable = args.ffmpeg_path.resolve()
    binary_hash = sha256(executable)
    pin = json.loads((ROOT / "config/content-tools.json").read_text())["ffmpeg"]["executableSha256"]
    if binary_hash != pin:
        raise SystemExit(f"Built FFmpeg differs from pin: {binary_hash}")
    version = subprocess.check_output([str(executable), "-version"], text=True, stderr=subprocess.DEVNULL)
    license_text = subprocess.check_output([str(executable), "-L"], text=True, stderr=subprocess.DEVNULL)
    if "--enable-libvorbis" not in version or "--disable-gpl" not in version:
        raise SystemExit("FFmpeg is missing the expected LGPL/Vorbis build flags")
    if "--enable-gpl" in version or "--enable-nonfree" in version:
        raise SystemExit("FFmpeg has GPL or nonfree build flags")
    if "GNU Lesser General Public License" not in " ".join(license_text.split()):
        raise SystemExit("FFmpeg does not report the LGPL")

    notices = OUTPUT / "ffmpeg-notices"
    notices.mkdir(parents=True, exist_ok=True)
    for output_name, (archive, member) in LICENSES.items():
        with tarfile.open(SOURCE / archive) as source:
            file = source.extractfile(member)
            if file is None:
                raise SystemExit(f"Missing source license: {member}")
            (notices / output_name).write_bytes(file.read())

    readme = (
        "FFmpeg converter corresponding source for Z0MB1ES-0.1.0.exe\n\n"
        f"Windows ffmpeg.exe SHA-256: {binary_hash}\n"
        "FFmpeg revision: 239f2c733d (8.1.1)\n"
        "libogg: 1.3.6; libvorbis: 1.3.7\n\n"
        "This ZIP contains the complete source archives used for the bundled static converter,\n"
        "plus Dockerfile and build.sh, the scripts that control its compilation and linking.\n"
        "Place the archives under private/ffmpeg-compliance in a checkout of this project,\n"
        "then run: docker build -t zombies-ffmpeg-build:1 installer/ffmpeg\n"
        "and: docker run --rm -v <absolute-repository-path>:/work zombies-ffmpeg-build:1\n"
        "The result is private/tooling/ffmpeg.exe. The converter is LGPL; the port's license\n"
        "does not change because this independent executable is shipped beside it.\n\n"
        "Source archive SHA-256:\n"
        + "".join(f"{name}: {digest}\n" for name, digest in ARCHIVES.items())
    )
    (notices / "FFmpeg-SOURCE.txt").write_text(
        f"Bundled ffmpeg.exe SHA-256: {binary_hash}. "
        "Matching source for the bundled ffmpeg.exe is the companion download "
        "FFmpeg-8.1.1-source.zip. Distribute that ZIP alongside Z0MB1ES-0.1.0.exe "
        "at the same release location. It contains FFmpeg, libogg, libvorbis, and "
        "the build scripts.\n",
        encoding="utf-8",
    )
    destination = OUTPUT / "FFmpeg-8.1.1-source.zip"
    def add(bundle: zipfile.ZipFile, name: str, path: Path | None = None, data: str | None = None) -> None:
        info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
        info.compress_type = zipfile.ZIP_STORED
        with bundle.open(info, "w") as target:
            if path is not None:
                with path.open("rb") as source:
                    shutil.copyfileobj(source, target)
            else:
                target.write((data or "").encode("utf-8"))

    with zipfile.ZipFile(destination, "w", compression=zipfile.ZIP_STORED) as bundle:
        add(bundle, "README.txt", data=readme)
        for name in ARCHIVES:
            add(bundle, "source/" + name, path=SOURCE / name)
        for name in ("Dockerfile", "build.sh"):
            add(bundle, "installer/ffmpeg/" + name, path=ROOT / "installer/ffmpeg" / name)
    print(f"SOURCE_BUNDLE {destination} SHA-256 {sha256(destination)}")


if __name__ == "__main__":
    main()
