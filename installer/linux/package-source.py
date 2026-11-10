"""Make the Linux converter's matching source bundle with its build recipe."""

from __future__ import annotations

import hashlib
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile, ZipInfo

ROOT = Path(__file__).resolve().parents[2]
base = ROOT / "installer/out/FFmpeg-8.1.1-source.zip"
output = ROOT / "installer/out/FFmpeg-8.1.1-linux-source.zip"
if hashlib.sha256(base.read_bytes()).hexdigest() != "46a2190c1331786596aac7ec4943fed4099710dea5a5974926697fc4e8765360":
    raise SystemExit("Base FFmpeg source bundle differs from the pinned source")
recipe = (ROOT / "installer/linux/build-ffmpeg.sh").read_bytes()
readme = ("Linux x64 FFmpeg 8.1.1 converter corresponding source.\n"
          "Use installer/linux/build-ffmpeg.sh with the three source archives in source/.\n"
          "Converter SHA-256: 7895c3b0687e340f7bee2d2ac5d7049b7a91eec003b042db1263555131c7c562\n").encode()
with ZipFile(base) as original, ZipFile(output, "w", ZIP_DEFLATED, compresslevel=9) as result:
    entries = [(name, original.read(name)) for name in original.namelist() if name.startswith("source/")]
    entries += [("installer/linux/build-ffmpeg.sh", recipe), ("README-LINUX.txt", readme)]
    for name, data in sorted(entries):
        info = ZipInfo(name, (1980, 1, 1, 0, 0, 0))
        info.compress_type = ZIP_DEFLATED
        info.external_attr = 0o644 << 16
        result.writestr(info, data, compress_type=ZIP_DEFLATED, compresslevel=9)
print(hashlib.sha256(output.read_bytes()).hexdigest(), output)
