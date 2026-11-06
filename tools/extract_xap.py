#!/usr/bin/env python3
"""List or safely extract a user-supplied Windows Phone XAP (ZIP) package."""

import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import stat
import sys
import zipfile

WINDOWS_DEVICE_NAMES = {"CON", "PRN", "AUX", "NUL"}
WINDOWS_DEVICE_NAMES.update(f"COM{i}" for i in range(1, 10))
WINDOWS_DEVICE_NAMES.update(f"LPT{i}" for i in range(1, 10))


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def safe_name(info: zipfile.ZipInfo) -> PurePosixPath:
    raw = info.filename
    name = PurePosixPath(raw)
    if (
        not raw
        or "\\" in raw
        or raw.startswith("/")
        or name.is_absolute()
        or any(part in ("", ".", "..") for part in name.parts)
        or any(part.endswith((" ", ".")) for part in name.parts)
        or any(":" in part for part in name.parts)
        or any(part.rstrip(" .").split(".")[0].upper() in WINDOWS_DEVICE_NAMES for part in name.parts)
    ):
        raise ValueError(f"unsafe XAP entry path: {raw!r}")
    mode = info.external_attr >> 16
    if stat.S_IFMT(mode) == stat.S_IFLNK:
        raise ValueError(f"symbolic link in XAP: {raw!r}")
    return name


def inspect(archive: Path) -> tuple[list[zipfile.ZipInfo], int]:
    with zipfile.ZipFile(archive) as package:
        entries = package.infolist()
        seen = set()
        total = 0
        for info in entries:
            name = safe_name(info)
            key = str(name).casefold()
            if key in seen:
                raise ValueError(f"duplicate XAP entry: {info.filename!r}")
            seen.add(key)
            if not info.is_dir():
                total += info.file_size
        return entries, total


def extract(archive: Path, destination: Path, entries: list[zipfile.ZipInfo]) -> None:
    if destination.is_symlink():
        raise ValueError(f"destination is a symbolic link: {destination}")
    if destination.exists() and any(destination.iterdir()):
        raise ValueError(f"destination is not empty: {destination}")
    destination.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(archive) as package:
        for info in entries:
            path = destination.joinpath(*safe_name(info).parts)
            if info.is_dir():
                path.mkdir(parents=True, exist_ok=True)
                continue
            path.parent.mkdir(parents=True, exist_ok=True)
            with package.open(info) as source, path.open("xb") as target:
                while chunk := source.read(1024 * 1024):
                    target.write(chunk)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=Path, help="user-supplied .xap file")
    parser.add_argument("--list", action="store_true", help="list entries without extracting")
    parser.add_argument("--output", type=Path, default=Path("private/original-xap"))
    args = parser.parse_args()
    archive = args.archive.resolve(strict=True)
    if not zipfile.is_zipfile(archive):
        parser.error("input is not a ZIP/XAP package")
    try:
        entries, total = inspect(archive)
        if args.list:
            for info in entries:
                print(f"{info.file_size:>10}  {info.filename}")
        else:
            extract(archive, args.output, entries)
        print(json.dumps({"archive_sha256": sha256(archive), "entries": len(entries), "uncompressed_bytes": total, "output": None if args.list else str(args.output)}, indent=2))
    except (OSError, ValueError, zipfile.BadZipFile) as error:
        print(f"error: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
