"""Prepare pinned, player-owned optional artwork into ignored private storage.

Usage: python tools/prepare_optional_art.py prepare --kind i-maed-steam --source <Steam directory>
       python tools/prepare_optional_art.py prepare --kind i-maed-360 --source <LIVE file>
       python tools/prepare_optional_art.py prepare --kind time-viking-360 --source <LIVE file>
       python tools/prepare_optional_art.py probe [--kind <source id>]

Requires Pillow, numpy, the sibling xnb_parse checkout, .NET 10 and the local FNA
build for compressed 360 XNBs. No source executable is copied or launched.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import uuid

import numpy as np
from PIL import Image

from inspect_stfs import inspect as inspect_stfs

ROOT = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parent.parent))
PRIVATE = Path(os.environ.get("PORT_ASSET_PRIVATE", ROOT / "private"))
DEFAULT_STORE = PRIVATE / "asset-swap/prepared"
PARSER = ROOT.parent / "xna-to-fna-asset-extractor/xnb_parse"
KINDS = ("i-maed-steam", "i-maed-360", "time-viking-360")
PAD = 2


def load(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def file_digest(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def canonical(value: dict) -> bytes:
    return (json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False) + "\n").encode("utf-8")


def safe_relative(value: str) -> Path:
    parts = value.split("/")
    if not parts or any(p in ("", ".", "..") or "\\" in p or ":" in p for p in parts):
        raise ValueError(f"unsafe asset path: {value}")
    return Path(*parts)


def checked_file(path: Path, expected: dict, label: str) -> bytes:
    if not path.is_file() or path.is_symlink():
        raise ValueError(f"{label}: required file missing or linked: {path}")
    data = path.read_bytes()
    if len(data) != expected["bytes"] or digest(data) != expected["sha256"]:
        raise ValueError(f"{label}: unsupported file/version: {path.name}")
    return data


def inside(rect: list[int], size: tuple[int, int] | list[int]) -> bool:
    if not isinstance(rect, list) or len(rect) != 4 or any(type(v) is not int for v in rect):
        return False
    x, y, w, h = rect
    return x >= 0 and y >= 0 and w > 0 and h > 0 and x + w <= size[0] and y + h <= size[1]


def validate_phone(mapping: dict, phone_root: Path, inventory: dict) -> dict:
    label = "supported phone v1.0.0.0"
    dll = phone_root / "ZombiesWP7.dll"
    if not dll.is_file() or file_digest(dll) != mapping["phoneDllSha256"]:
        raise ValueError(f"{label}: ZombiesWP7.dll fingerprint mismatch")
    known = {t["path"]: t for t in inventory["textures"] if t["source"] == "phone"}
    needed = {a["path"]: a["sha256"] for e in mapping["elements"] for a in e["fallback"]["alternatives"]}
    result = {}
    for name, sha in sorted(needed.items()):
        meta = known[name]
        if sha != meta["sha256"]:
            raise ValueError(f"{label}: fallback pin mismatch: {name}")
        checked_file(phone_root / "Content" / safe_relative(name), meta, label)
        result[name] = {"sha256": sha, "size": [meta["width"], meta["height"]]}
    for element in mapping["elements"]:
        fb = element["fallback"]
        if not fb["alternatives"] and fb.get("runtimeTexture") != "generated/text-render-target":
            raise ValueError(f"{label}: no verified Original fallback for {element['id']}")
        for alt in fb["alternatives"]:
            if fb["rectangle"] is not None and not inside(fb["rectangle"], result[alt["path"]]["size"]):
                raise ValueError(f"{label}: fallback crop outside texture: {element['id']}")
    return result


def read_texture(path: Path, meta: dict, label: str) -> Image.Image:
    if not getattr(sys, "frozen", False):
        sys.path.insert(0, str(PARSER))
    from xnb_parse.xnb_reader import XNBReader
    from xnb_parse.xna_types.xna_graphics import SURFACE_FORMAT, SURFACE_FORMAT4

    checked_file(path, meta, label)
    content = XNBReader.load(filename=str(path)).content
    if type(content).__name__ != "Texture2D":
        raise ValueError(f"{label}: expected Texture2D: {path.name}")
    formats = SURFACE_FORMAT4 if type(content.surface_format).__name__ == "SurfaceFormat4" else SURFACE_FORMAT
    actual = (content.width, content.height, formats[content.surface_format.value][0], len(content.mip_levels))
    expected = (meta["width"], meta["height"], meta["surfaceFormat"], meta["mipCount"])
    if actual != expected:
        raise ValueError(f"{label}: decoded texture type/dimensions mismatch: {path.name}: {actual} != {expected}")
    return Image.frombytes("RGBA", (content.width, content.height), content.full_data())


def source_files(kind: str, source: Path, mapping: dict, inventory: dict, stage: Path) -> tuple[dict, dict]:
    label = {"i-maed-steam": "Steam I MAED 1.0.0.0", "i-maed-360": "I MAED 360 pinned LIVE", "time-viking-360": "Time Viking 360 pinned LIVE"}[kind]
    known = {t["path"]: t for t in inventory["textures"] if t["source"] == kind}
    required = {e["source"]["path"] for e in mapping["elements"] if e["status"] == "candidate"}
    if kind == "i-maed-steam":
        pin = load(ROOT / "config/supported-steam-versions.json")
        checked_file(source / safe_relative(pin["executable"]["path"]), pin["executable"], label)
        required |= {t["path"] for t in pin["requiredTextures"]}
        root = source / "Content"
        for name in required:
            checked_file(root / safe_relative(name), known[name], label)
        identity = {"sourceId": kind, "version": pin["version"], "executableSha256": pin["executable"]["sha256"]}
    else:
        pin = next(t["package"] for t in load(ROOT / "config/supported-360-versions.json")["titles"] if t["sourceId"] == ("zombies-360" if kind == "i-maed-360" else "viking-360"))
        checked_file(source, pin, label)
        paths = {known[name]["packageEntry"]["path"] for name in required}
        report = inspect_stfs(source, None)
        pinned_report = load(ROOT / "docs/records" / ("zombies-360-inventory.json" if kind == "i-maed-360" else "viking-360-inventory.json"))
        if report["files"] != pinned_report["files"] or report["gameInfo"] != pinned_report["gameInfo"] or report["package"] != pinned_report["package"]:
            raise ValueError(f"{label}: unsupported title, version or STFS file inventory")
        if int(report["gameInfo"]["TitleID"]) != int(pin["gameInfoTitleId"], 16) or report["gameInfo"]["VirtualTitleID"] != pin["gameInfoVirtualTitleId"]:
            raise ValueError(f"{label}: TitleID mismatch")
        raw = stage / "raw"
        inspect_stfs(source, raw, paths)
        bundled = os.environ.get("PORT_ASSET_DECODER")
        decoder = Path(bundled) if bundled else ROOT / "tools/decode_360_xnb/bin/Release/net10.0/decode_360_xnb.dll"
        fna = Path(os.environ.get("PORT_ASSET_FNA", str(ROOT / "out/deps/managed/FNA.dll")))
        if not decoder.is_file() or not fna.is_file():
            raise ValueError(f"{label}: build tools/decode_360_xnb and out/deps/managed/FNA.dll first")
        decoded = stage / "decoded"
        command = [str(decoder), str(fna), str(raw), str(decoded)] if bundled else ["dotnet", str(decoder), str(fna), str(raw), str(decoded)]
        subprocess.run(command, cwd=PRIVATE.parent if bundled else ROOT, check=True, capture_output=True, text=True)
        root = decoded / "584E07D1/Content"
        identity = {"sourceId": kind, "format": "LIVE/STFS", "packageSha256": pin["sha256"], "gameInfoTitleId": pin["gameInfoTitleId"]}
    images = {}
    inputs = {}
    for name in sorted(required):
        meta = known[name]
        path = root / safe_relative(name)
        images[name] = read_texture(path, meta, label)
        inputs[name] = {"sha256": meta["sha256"], "size": [meta["width"], meta["height"]], "type": meta["type"], "surfaceFormat": meta["surfaceFormat"], "mipCount": meta["mipCount"]}
        if kind.endswith("360"):
            inputs[name]["packageEntry"] = meta["packageEntry"]
    return identity | {"textures": inputs}, images


def derive_vignette(mapping: dict, images: dict, phone_root: Path, inventory: dict) -> tuple[np.ndarray, str] | None:
    backgrounds = [e for e in mapping["elements"] if e["id"].startswith("background/i-maed/") and e["status"] == "candidate"]
    if not backgrounds:
        return None
    if len(backgrounds) != 8:
        raise ValueError("I MAED background treatment requires all eight pinned images")
    phone_meta = {t["path"]: t for t in inventory["textures"] if t["source"] == "phone"}
    numerator = np.zeros((360, 640), dtype=np.float64)
    denominator = np.zeros((360, 640), dtype=np.float64)
    for element in backgrounds:
        source = images[element["source"]["path"]]
        fallback_path = element["fallback"]["alternatives"][0]["path"]
        original = read_texture(phone_root / "Content" / safe_relative(fallback_path), phone_meta[fallback_path], "phone vignette")
        if source.size != (1280, 720) or original.size != (640, 360):
            raise ValueError("I MAED background vignette dimensions changed")
        small = source.resize(original.size, Image.Resampling.BOX)
        a = np.asarray(original, dtype=np.float64)[:, :, :3]
        b = np.asarray(small, dtype=np.float64)[:, :, :3]
        numerator += (a * b).sum(axis=2)
        denominator += (b * b).sum(axis=2)
    low = np.rint(np.clip(numerator / np.maximum(denominator, 1), 0, 1) * 255).astype(np.uint8)
    high = np.asarray(Image.fromarray(low, "L").resize((1280, 720), Image.Resampling.BILINEAR), dtype=np.uint8)
    return high, digest(low.tobytes())


def crop_png(image: Image.Image, rect: list[int], vignette: np.ndarray | None = None) -> bytes:
    x, y, w, h = rect
    crop = image.crop((x, y, x + w, y + h))
    pixels = np.asarray(crop).copy()
    if vignette is not None:
        if vignette.shape != (image.height, image.width):
            raise ValueError("I MAED vignette and source art dimensions differ")
        factor = vignette[y:y+h, x:x+w, None].astype(np.float32) / 255
        pixels[:, :, :3] = np.rint(pixels[:, :, :3].astype(np.float32) * factor).astype(np.uint8)
    padded = np.pad(pixels, ((PAD, PAD), (PAD, PAD), (0, 0)), mode="edge")
    output = Image.fromarray(padded, "RGBA")
    stream = io.BytesIO()
    output.save(stream, format="PNG", optimize=False, compress_level=9)
    return stream.getvalue()


def prepare_rows(kind: str, mapping: dict, inputs: dict, images: dict, phone: dict, art_dir: Path,
                 vignette: tuple[np.ndarray, str] | None) -> list[dict]:
    rows = []
    art_dir.mkdir(parents=True)
    for element in mapping["elements"]:
        row = {"id": element["id"], "mode": element.get("mode"), "fallback": element["fallback"], "selection": "phone"}
        if element["status"] == "candidate":
            src = element["source"]
            name = src["path"]
            rect = src["rectangle"]
            if src["decodedSha256"] != inputs[name]["sha256"] or not inside(rect, images[name].size):
                raise ValueError(f"{kind}: source rectangle/hash mismatch: {element['id']}")
            logical = element["expectedOutput"]["logicalSize"]
            if rect[2:] != element["expectedOutput"]["physicalSize"] or any(v <= 0 for v in logical):
                raise ValueError(f"{kind}: output geometry mismatch: {element['id']}")
            phone_rect = element["fallback"]["rectangle"]
            if phone_rect is not None and logical != phone_rect[2:]:
                raise ValueError(f"{kind}: phone logical geometry mismatch: {element['id']}")
            background = element["id"].startswith("background/i-maed/")
            png = crop_png(images[name], rect, vignette[0] if background and vignette else None)
            sha = digest(png)
            target = art_dir / f"{sha}.png"
            if not target.exists():
                target.write_bytes(png)
            row.update({"selection": kind, "sourcePath": name, "sourceRect": rect,
                        "sourceSha256": inputs[name]["sha256"], "output": {"path": f"art/{sha}.png", "sha256": sha,
                        "size": [rect[2] + PAD * 2, rect[3] + PAD * 2], "contentRect": [PAD, PAD, rect[2], rect[3]],
                        "logicalSize": logical, "sampler": "clamp", "padding": "edge-extended"}})
            if background:
                if vignette is None:
                    raise ValueError(f"{kind}: missing verified I MAED vignette")
                row["treatment"] = {"id": "phone-vignette-v1", "maskSha256": vignette[1]}
        rows.append(row)
    return rows


def probe_manifest(folder: Path, phone: dict | None = None) -> dict:
    manifest = load(folder / "manifest.json")
    if manifest["schemaVersion"] != 1:
        raise ValueError("unsupported prepared-art manifest version")
    kind = manifest["sourceId"]
    if kind not in KINDS:
        raise ValueError("unsupported prepared-art source")
    mapping_path = ROOT / "docs/records/asset-swap-mappings" / f"{kind}.json"
    mapping = load(mapping_path)
    if manifest["mappingSha256"] != file_digest(mapping_path) or manifest["phoneDllSha256"] != mapping["phoneDllSha256"]:
        raise ValueError(f"{kind}: mapping or phone version mismatch")
    if [r["id"] for r in manifest["elements"]] != [e["id"] for e in mapping["elements"]]:
        raise ValueError(f"{kind}: incomplete or reordered element coverage")
    seen = set()
    files = manifest["inputs"]["textures"]
    inventory = load(ROOT / "docs/records/asset-swap-inventory.json")
    known = {t["path"]: t for t in inventory["textures"] if t["source"] == kind}
    needed = {e["source"]["path"] for e in mapping["elements"] if e["status"] == "candidate"}
    if kind == "i-maed-steam":
        pin = load(ROOT / "config/supported-steam-versions.json")
        needed |= {t["path"] for t in pin["requiredTextures"]}
        if manifest["inputs"]["executableSha256"] != pin["executable"]["sha256"] or manifest["inputs"]["version"] != pin["version"]:
            raise ValueError("Steam I MAED identity mismatch")
    else:
        pin = next(t["package"] for t in load(ROOT / "config/supported-360-versions.json")["titles"] if t["sourceId"] == ("zombies-360" if kind == "i-maed-360" else "viking-360"))
        if manifest["inputs"]["packageSha256"] != pin["sha256"] or manifest["inputs"]["gameInfoTitleId"] != pin["gameInfoTitleId"]:
            raise ValueError(f"{kind}: LIVE identity mismatch")
    if set(files) != needed:
        raise ValueError(f"{kind}: incomplete source texture inventory")
    for name, item in files.items():
        expected = known[name]
        fields = {"sha256": expected["sha256"], "size": [expected["width"], expected["height"]],
                  "type": expected["type"], "surfaceFormat": expected["surfaceFormat"], "mipCount": expected["mipCount"]}
        if kind.endswith("360"):
            fields["packageEntry"] = expected["packageEntry"]
        if item != fields:
            raise ValueError(f"{kind}: source texture fingerprint mismatch: {name}")
    for row, element in zip(manifest["elements"], mapping["elements"]):
        if row["id"] in seen:
            raise ValueError(f"duplicate prepared element: {row['id']}")
        seen.add(row["id"])
        fb = row["fallback"]
        if fb != element["fallback"]:
            raise ValueError(f"changed Original fallback: {row['id']}")
        if not fb["alternatives"] and fb.get("runtimeTexture") != "generated/text-render-target":
            raise ValueError(f"missing Original fallback: {row['id']}")
        if phone is not None:
            for alt in fb["alternatives"]:
                if alt["sha256"] != phone[alt["path"]]["sha256"] or (fb["rectangle"] is not None and not inside(fb["rectangle"], phone[alt["path"]]["size"])):
                    raise ValueError(f"bad Original fallback: {row['id']}")
        expected_selection = kind if element["status"] == "candidate" else "phone"
        if row["selection"] != expected_selection:
            raise ValueError(f"changed source choice: {row['id']}")
        if row["selection"] == "phone":
            continue
        if row["sourcePath"] != element["source"]["path"] or row["sourceRect"] != element["source"]["rectangle"]:
            raise ValueError(f"changed source rectangle: {row['id']}")
        src = files[row["sourcePath"]]
        if row["sourceSha256"] != src["sha256"] or src["sha256"] != element["source"]["decodedSha256"] or not inside(row["sourceRect"], src["size"]):
            raise ValueError(f"bad source rectangle: {row['id']}")
        out = row["output"]
        if out["path"] != f"art/{out['sha256']}.png" or out["size"] != [row["sourceRect"][2] + PAD * 2, row["sourceRect"][3] + PAD * 2]:
            raise ValueError(f"prepared art output layout mismatch: {row['id']}")
        path = folder / safe_relative(out["path"])
        data = path.read_bytes()
        if digest(data) != out["sha256"]:
            raise ValueError(f"prepared art hash mismatch: {row['id']}")
        with Image.open(io.BytesIO(data)) as image:
            image.load()
            if image.format != "PNG" or image.mode != "RGBA" or list(image.size) != out["size"] or not inside(out["contentRect"], image.size):
                raise ValueError(f"prepared art geometry/format mismatch: {row['id']}")
        if out["contentRect"][2:] != row["sourceRect"][2:]:
            raise ValueError(f"prepared art crop mismatch: {row['id']}")
        if out["logicalSize"] != element["expectedOutput"]["logicalSize"] or out["contentRect"][:2] != [PAD, PAD] or out["padding"] != "edge-extended" or out["sampler"] != "clamp":
            raise ValueError(f"prepared art logical geometry mismatch: {row['id']}")
        if element["id"].startswith("background/i-maed/"):
            treatment = row.get("treatment", {})
            mask = treatment.get("maskSha256", "")
            if treatment.get("id") != "phone-vignette-v1" or len(mask) != 64:
                raise ValueError(f"missing verified phone vignette treatment: {row['id']}")
    return manifest


def current(store: Path, kind: str) -> Path | None:
    pointer = store / kind / "current.json"
    if not pointer.is_file():
        return None
    value = load(pointer)
    generation = value["generation"]
    if len(generation) != 64 or any(c not in "0123456789abcdef" for c in generation):
        raise ValueError(f"invalid current generation for {kind}")
    return store / kind / generation


def commit(store: Path, kind: str, staged: Path, manifest: dict) -> Path:
    sha = digest(canonical(manifest))
    destination = store / kind / sha
    destination.parent.mkdir(parents=True, exist_ok=True)
    if destination.exists():
        probe_manifest(destination)
        if file_digest(destination / "manifest.json") != sha:
            raise ValueError(f"prepared generation collision or corruption: {kind}")
    else:
        staged.rename(destination)
    pointer = destination.parent / f"current.{uuid.uuid4().hex}.tmp"
    pointer.write_bytes(canonical({"schemaVersion": 1, "generation": sha}))
    os.replace(pointer, destination.parent / "current.json")
    return destination


def compose_endless(store: Path, phone_root: Path, inventory: dict) -> Path:
    mapping = load(ROOT / "docs/records/asset-swap-mappings/endless-360.json")
    phone = validate_phone(mapping, phone_root, inventory)
    sources = {}
    for kind in ("i-maed-360", "i-maed-steam", "time-viking-360"):
        folder = current(store, kind)
        if folder:
            source_mapping = load(ROOT / "docs/records/asset-swap-mappings" / f"{kind}.json")
            source_phone = validate_phone(source_mapping, phone_root, inventory)
            source_manifest = probe_manifest(folder, source_phone)
            sources[kind] = (folder, {row["id"]: row for row in source_manifest["elements"]})
    if not sources:
        raise ValueError("Endless art requires a verified I MAED or Time Viking import")
    rows = []
    for element in mapping["elements"]:
        row = {"id": element["id"], "fallback": element["fallback"], "selection": "phone"}
        for variant in element["variants"]:
            kind = variant["sourceId"]
            if kind in sources and element["id"] in sources[kind][1]:
                candidate = sources[kind][1][element["id"]]
                if candidate["selection"] == kind and candidate["sourcePath"] == variant["source"]["path"] and candidate["sourceRect"] == variant["source"]["rectangle"]:
                    row.update({"selection": kind, "generation": sources[kind][0].name, "output": candidate["output"], "sourcePath": candidate["sourcePath"], "sourceRect": candidate["sourceRect"]})
                    break
        rows.append(row)
    result = {"schemaVersion": 1, "game": "endless", "phoneDllSha256": mapping["phoneDllSha256"],
              "priority": mapping["sourcePriority"], "elements": rows}
    folder = store / "endless-360"
    folder.mkdir(parents=True, exist_ok=True)
    sha = digest(canonical(result))
    target = folder / f"{sha}.json"
    if not target.exists():
        target.write_bytes(canonical(result))
    pointer = folder / f"current.{uuid.uuid4().hex}.tmp"
    pointer.write_bytes(canonical({"schemaVersion": 1, "generation": sha}))
    os.replace(pointer, folder / "current.json")
    return target


def probe_endless(store: Path, phone_root: Path, inventory: dict) -> dict | None:
    pointer = store / "endless-360/current.json"
    if not pointer.is_file():
        return None
    generation = load(pointer)["generation"]
    if len(generation) != 64 or any(c not in "0123456789abcdef" for c in generation):
        raise ValueError("invalid Endless generation pointer")
    target = store / "endless-360" / f"{generation}.json"
    data = target.read_bytes()
    if digest(data) != generation:
        raise ValueError("Endless manifest hash mismatch")
    result = json.loads(data)
    mapping = load(ROOT / "docs/records/asset-swap-mappings/endless-360.json")
    phone = validate_phone(mapping, phone_root, inventory)
    if result["schemaVersion"] != 1 or result["priority"] != mapping["sourcePriority"] or result["phoneDllSha256"] != mapping["phoneDllSha256"]:
        raise ValueError("Endless manifest schema or source priority mismatch")
    if [r["id"] for r in result["elements"]] != [e["id"] for e in mapping["elements"]]:
        raise ValueError("Endless element coverage mismatch")
    for row, element in zip(result["elements"], mapping["elements"]):
        if row["fallback"] != element["fallback"]:
            raise ValueError(f"Endless Original fallback mismatch: {row['id']}")
        for alt in row["fallback"]["alternatives"]:
            if alt["sha256"] != phone[alt["path"]]["sha256"]:
                raise ValueError(f"Endless Original fallback hash mismatch: {row['id']}")
            rect = row["fallback"]["rectangle"]
            if rect is not None and not inside(rect, phone[alt["path"]]["size"]):
                raise ValueError(f"Endless Original fallback crop mismatch: {row['id']}")
        expected = "phone"
        for variant in element["variants"]:
            if current(store, variant["sourceId"]):
                expected = variant["sourceId"]
                break
        if row["selection"] != expected:
            raise ValueError(f"Endless priority mismatch: {row['id']}")
        if expected != "phone":
            folder = current(store, expected)
            assert folder is not None
            source_rows = {r["id"]: r for r in load(folder / "manifest.json")["elements"]}
            source = source_rows[row["id"]]
            if row["generation"] != folder.name or row["output"] != source["output"] or row["sourcePath"] != source["sourcePath"] or row["sourceRect"] != source["sourceRect"]:
                raise ValueError(f"Endless prepared route mismatch: {row['id']}")
            path = folder / safe_relative(row["output"]["path"])
            if file_digest(path) != row["output"]["sha256"]:
                raise ValueError(f"Endless prepared art hash mismatch: {row['id']}")
    return result


def run_prepare(kind: str, source: Path, store: Path, phone_root: Path) -> None:
    mapping = load(ROOT / "docs/records/asset-swap-mappings" / f"{kind}.json")
    inventory = load(ROOT / "docs/records/asset-swap-inventory.json")
    phone = validate_phone(mapping, phone_root, inventory)
    if kind == "i-maed-steam":
        pin = load(ROOT / "config/supported-steam-versions.json")
        checked_file(source / pin["executable"]["path"], pin["executable"], "Steam I MAED 1.0.0.0")
    else:
        pin = next(t["package"] for t in load(ROOT / "config/supported-360-versions.json")["titles"] if t["sourceId"] == ("zombies-360" if kind == "i-maed-360" else "viking-360"))
        label = "I MAED 360 pinned LIVE" if kind == "i-maed-360" else "Time Viking 360 pinned LIVE"
        checked_file(source, pin, label)
    stage = store / ".staging" / uuid.uuid4().hex
    stage.mkdir(parents=True)
    try:
        inputs, images = source_files(kind, source, mapping, inventory, stage)
        prepared = stage / "prepared"
        vignette = derive_vignette(mapping, images, phone_root, inventory)
        rows = prepare_rows(kind, mapping, inputs["textures"], images, phone, prepared / "art", vignette)
        manifest = {"schemaVersion": 1, "sourceId": kind, "mappingSha256": file_digest(ROOT / "docs/records/asset-swap-mappings" / f"{kind}.json"),
                    "phoneDllSha256": mapping["phoneDllSha256"], "inputs": inputs, "elements": rows}
        (prepared / "manifest.json").write_bytes(canonical(manifest))
        probe_manifest(prepared, phone)
        destination = commit(store, kind, prepared, manifest)
        print(f"PREPARE_OK {kind} generation={destination.name} elements={len(rows)} imported={sum(r['selection'] == kind for r in rows)}")
        endless = compose_endless(store, phone_root, inventory)
        probe_endless(store, phone_root, inventory)
        print(f"ENDLESS_OK manifest={endless.name}")
    finally:
        shutil.rmtree(stage, ignore_errors=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("prepare", "probe", "compose-endless"))
    parser.add_argument("--kind", choices=KINDS)
    parser.add_argument("--source", type=Path)
    parser.add_argument("--store", type=Path, default=DEFAULT_STORE)
    parser.add_argument("--phone-root", type=Path, default=PRIVATE / "original-xap")
    args = parser.parse_args()
    try:
        store = args.store.resolve()
        if not store.is_relative_to(PRIVATE.resolve()) or store == PRIVATE.resolve():
            raise ValueError("prepared art must stay under ignored private/")
        inventory = load(ROOT / "docs/records/asset-swap-inventory.json")
        if args.action == "prepare":
            if not args.kind or not args.source:
                parser.error("prepare requires --kind and --source")
            run_prepare(args.kind, args.source.resolve(), store, args.phone_root.resolve())
        elif args.action == "compose-endless":
            path = compose_endless(store, args.phone_root.resolve(), inventory)
            probe_endless(store, args.phone_root.resolve(), inventory)
            print(f"ENDLESS_OK manifest={path.name}")
        else:
            for kind in ([args.kind] if args.kind else KINDS):
                folder = current(store, kind)
                if not folder:
                    continue
                mapping = load(ROOT / "docs/records/asset-swap-mappings" / f"{kind}.json")
                phone = validate_phone(mapping, args.phone_root.resolve(), inventory)
                manifest = probe_manifest(folder, phone)
                if file_digest(folder / "manifest.json") != folder.name:
                    raise ValueError(f"{kind}: manifest generation hash mismatch")
                if manifest["mappingSha256"] != file_digest(ROOT / "docs/records/asset-swap-mappings" / f"{kind}.json"):
                    raise ValueError(f"{kind}: mapping version mismatch")
                print(f"PROBE_OK {kind} elements={len(manifest['elements'])} generation={folder.name}")
            if not args.kind:
                endless = probe_endless(store, args.phone_root.resolve(), inventory)
                if endless:
                    print(f"PROBE_OK endless elements={len(endless['elements'])} imported={sum(r['selection'] != 'phone' for r in endless['elements'])}")
    except (OSError, ValueError, KeyError, IndexError, subprocess.CalledProcessError) as error:
        print(f"PREPARE_FAIL {args.kind or args.action}: {error}", file=sys.stderr)
        raise SystemExit(1) from None


if __name__ == "__main__":
    main()
