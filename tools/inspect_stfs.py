"""Read-only, metadata-only inventory of the two locally supplied LIVE/STFS games.

This deliberately supports the read-only, one-file-table-block STFS layout found
in the supplied packages. It verifies every data block against its STFS SHA-1
entry but does not verify the package signature. Extraction is optional and is
confined to the ignored private/ tree for local decompilation.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import sys
import xml.etree.ElementTree as ET


BLOCK = 0x1000
REPO = Path(__file__).resolve().parent.parent


def integer(data: bytes, offset: int, size: int, byteorder: str = "big") -> int:
    return int.from_bytes(data[offset : offset + size], byteorder)


def data_offset(block: int, header_size: int) -> int:
    # Xenia's BlockToOffsetSTFS for read-only packages below 28,900 blocks.
    extra = (block + 170) // 170
    if block >= 170:
        extra += 1
    return (header_size + BLOCK - 1) // BLOCK * BLOCK + (block + extra) * BLOCK


def hash_offset(block: int, header_size: int) -> int:
    # Xenia's level-0 table placement for the same restricted layout.
    table_block = 0 if block < 170 else (block // 170) * 171 + 1
    return (header_size + BLOCK - 1) // BLOCK * BLOCK + table_block * BLOCK + (block % 170) * 0x18


def read_file(data: bytes, header_size: int, start: int, blocks: int, length: int) -> bytes:
    content = bytearray()
    for block in range(start, start + blocks):
        offset = data_offset(block, header_size)
        chunk = data[offset : offset + BLOCK]
        expected = data[hash_offset(block, header_size) : hash_offset(block, header_size) + 20]
        if len(chunk) != BLOCK or hashlib.sha1(chunk).digest() != expected:
            raise ValueError(f"STFS data block {block} fails its SHA-1 entry")
        content.extend(chunk)
    if length > len(content):
        raise ValueError("file length exceeds allocated blocks")
    return bytes(content[:length])


def safe_name(name: str) -> str:
    if name in ("", ".", "..") or any(c in name for c in "/\\:"):
        raise ValueError(f"unsafe STFS name {name!r}")
    return name


def inspect(package: Path, extract_dir: Path | None, only_paths: set[str] | None = None) -> dict:
    data = package.read_bytes()
    if len(data) < 0xC000 or data[:4] != b"LIVE":
        raise ValueError("expected a LIVE package with an STFS header")
    header_size = integer(data, 0x340, 4)
    descriptor = data[0x379:0x39D]
    table_count = integer(descriptor, 3, 2, "little")
    table_block = integer(descriptor, 5, 3, "little")
    total_blocks = integer(descriptor, 0x1C, 4)
    if descriptor[0] != 0x24 or descriptor[2] & 1 == 0 or table_count != 1 or table_block != 0:
        raise ValueError("unsupported STFS volume layout")
    if not 1 <= total_blocks < 28900 or data_offset(total_blocks - 1, header_size) + BLOCK > len(data):
        raise ValueError("invalid or unsupported STFS block count")
    table = read_file(data, header_size, 0, 1, BLOCK)
    entries = []
    for index in range(64):
        row = table[index * 64 : (index + 1) * 64]
        if row[0] == 0:
            break
        flags = row[40]
        if not flags & 0x40:
            raise ValueError("noncontiguous STFS entry is unsupported")
        entry = {
            "name": safe_name(row[: flags & 0x3F].decode("ascii")),
            "directory": bool(flags & 0x80),
            "blocks": integer(row, 44, 3, "little"),
            "start": integer(row, 47, 3, "little"),
            "parent": integer(row, 50, 2),
            "bytes": integer(row, 52, 4),
        }
        entries.append(entry)
    used = {0}
    files = []
    extracted = []
    game_info = None
    for index, entry in enumerate(entries):
        parent = entry["parent"]
        names = [entry["name"]]
        visited = {index}
        while parent != 0xFFFF:
            if parent >= len(entries) or parent in visited or not entries[parent]["directory"]:
                raise ValueError("invalid STFS directory parent")
            visited.add(parent)
            names.insert(0, entries[parent]["name"])
            parent = entries[parent]["parent"]
        if entry["directory"]:
            continue
        start, blocks, length = entry["start"], entry["blocks"], entry["bytes"]
        if blocks != (length + BLOCK - 1) // BLOCK or start < 1 or start + blocks > total_blocks:
            raise ValueError("invalid STFS file block range")
        for block in range(start, start + blocks):
            if block in used:
                raise ValueError("overlapping STFS file blocks")
            used.add(block)
        payload = read_file(data, header_size, start, blocks, length)
        relative = "/".join(names)
        suffix = Path(entry["name"]).suffix.lower()
        row = {"path": relative, "bytes": length, "sha256": hashlib.sha256(payload).hexdigest()}
        if suffix == ".xnb":
            if not payload.startswith(b"XNB") or len(payload) < 10:
                raise ValueError(f"invalid XNB header: {relative}")
            row["xnbPlatform"] = chr(payload[3])
            row["xnbVersion"] = payload[4]
        files.append(row)
        if relative == "GameInfo.xml":
            title = ET.fromstring(payload).find("TitleInfo")
            if title is None:
                raise ValueError("GameInfo.xml has no TitleInfo")
            game_info = {key: title.attrib[key] for key in ("ID", "Name", "TitleID", "VirtualTitleID")}
        if extract_dir is not None and (only_paths is None or relative in only_paths):
            extracted.append((Path(*names), payload))
    if used != set(range(total_blocks)):
        raise ValueError("STFS file table does not account for all data blocks")
    if game_info is None:
        raise ValueError("GameInfo.xml is missing")
    if only_paths is not None and only_paths - {row["path"] for row in files}:
        raise ValueError(f"required STFS artwork missing: {sorted(only_paths - {row['path'] for row in files})}")
    if extract_dir is not None:
        private_root = Path(os.environ.get("PORT_ASSET_PRIVATE", str(REPO / "private"))).resolve()
        target_root = extract_dir.resolve()
        if not target_root.is_relative_to(private_root) or target_root == private_root:
            raise ValueError("extraction must target a new subdirectory under private/")
        if target_root.exists():
            raise ValueError("extraction target already exists")
        for relative, payload in extracted:
            target = target_root / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(payload)
    return {
        "schemaVersion": 1,
        "input": {"name": package.name, "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()},
        "package": {
            "magic": "LIVE", "headerSize": header_size,
            "contentType": f"0x{integer(data, 0x344, 4):08x}",
            "metadataVersion": integer(data, 0x348, 4),
            "containerTitleId": f"0x{integer(data, 0x360, 4):08x}",
            "volume": "STFS read-only", "dataBlocksVerified": total_blocks,
            "displayName": data[0x411:0x511].decode("utf-16-be").split("\0", 1)[0],
        },
        "gameInfo": game_info,
        "counts": {"entries": len(entries), "directories": sum(e["directory"] for e in entries), "files": len(files)},
        "files": files,
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package", type=Path)
    parser.add_argument("--output", type=Path, help="metadata-only JSON output")
    parser.add_argument("--extract-private", type=Path, help="new ignored directory under private/")
    args = parser.parse_args()
    try:
        report = inspect(args.package, args.extract_private)
    except (OSError, ValueError, ET.ParseError, UnicodeError) as error:
        print(f"INVENTORY_FAIL {args.package.name}: {error}", file=sys.stderr)
        raise SystemExit(1) from None
    result = json.dumps(report, indent=2, ensure_ascii=False) + "\n"
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(result, encoding="utf-8")
        print(f"INVENTORY_OK {args.package.name} files={report['counts']['files']} blocks={report['package']['dataBlocksVerified']}")
    else:
        print(result, end="")


if __name__ == "__main__":
    main()
