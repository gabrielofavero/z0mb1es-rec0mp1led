"""Extract only the pinned Linux x64 native libraries from the FNA daily archive."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path
from zipfile import ZipFile

ROOT = Path(__file__).resolve().parents[2]
pin = json.loads((ROOT / "config/linux-packaging.json").read_text(encoding="utf-8"))
archive = ROOT / "out/deps/cache/fnalibs-2026-11-06.zip"
if hashlib.sha256(archive.read_bytes()).hexdigest() != pin["nativeArchiveSha256"]:
    raise SystemExit("FNA daily archive hash mismatch")
target = ROOT / "out/deps/native/linux-x64"
target.mkdir(parents=True, exist_ok=True)
with ZipFile(archive) as bundle:
    for name, expected in pin["nativeFiles"].items():
        data = bundle.read(f"lib64/{name}")
        if hashlib.sha256(data).hexdigest() != expected:
            raise SystemExit(f"Native library hash mismatch: {name}")
        (target / name).write_bytes(data)
        print(f"{name} {expected}")
