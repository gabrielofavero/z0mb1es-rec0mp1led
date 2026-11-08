"""Build conservative, metadata-only asset-swap mappings and private crop sheets.

Candidate mappings are evidence for the next importer prompt. A candidate is not
runtime-enabled until its treatment and draw geometry have been reviewed.
"""

from __future__ import annotations

import collections
import json
import pathlib
import sys

import numpy as np
from PIL import Image, ImageDraw

ROOT = pathlib.Path(__file__).resolve().parent.parent
INVENTORY = ROOT / "docs/records/asset-swap-inventory.json"
OUT = ROOT / "docs/records/asset-swap-mappings"
PRIVATE = ROOT / "private/asset-swap/contact"
PARSER = ROOT.parent / "xna-to-fna-asset-extractor/xnb_parse"
SOURCE_ROOTS = {
    "phone": ROOT / "private/original-xap/Content",
    "i-maed-360": ROOT / "private/asset-comparison/xnb-unpacked-zombies",
    "time-viking-360": ROOT / "private/asset-comparison/xnb-unpacked-viking",
    "i-maed-steam": pathlib.Path("D:/Program Files (x86)/Steam/steamapps/common/IMAGWZII/Content"),
}

# The phone atlas bakes a wide shadow into these character cells. Its shadow
# reaches the cell boundary, while the matching 360 character art has clean
# alpha. The low alpha IoU is therefore expected for these reviewed crops.
SHADOW_BAKED_PHONE_FRAMES = {
    "draw/IMAK3Z0MB1EGAEM.character/Monster/008",
    "draw/IMAK3Z0MB1EGAEM.character/Monster/009",
    "draw/IMAK3Z0MB1EGAEM.character/Monster/010",
    *(f"frame/IMAK3Z0MB1EGAEM.character/Hero/001/{i:03d}" for i in range(5)),
}


def visible(array: np.ndarray) -> np.ndarray:
    return array[:, :, :3].astype(np.float32) * array[:, :, 3:4].astype(np.float32) / 255


def metrics(phone: Image.Image, source: Image.Image) -> dict:
    source = source.resize(phone.size, Image.Resampling.BOX)
    a, b = np.asarray(phone), np.asarray(source)
    ap, bp = a[:, :, 3] > 16, b[:, :, 3] > 16
    union = int(np.logical_or(ap, bp).sum())
    av, bv = visible(a), visible(b)
    ag, bg = av.mean(axis=2).ravel(), bv.mean(axis=2).ravel()
    corr = float(np.corrcoef(ag, bg)[0, 1]) if ag.std() and bg.std() else 1.0
    return {"phoneVisiblePixels": int(ap.sum()), "sourceVisiblePixels": int(bp.sum()),
            "alphaIoU": round(float(np.logical_and(ap, bp).sum() / union) if union else 1.0, 4),
            "visibleMae": round(float(np.abs(av - bv).mean()), 3),
            "luminanceCorrelation": round(corr, 4)}


def crop(image: Image.Image, rect: list[int]) -> Image.Image:
    x, y, w, h = rect
    if x < 0 or y < 0 or w <= 0 or h <= 0 or x + w > image.width or y + h > image.height:
        raise ValueError(f"out-of-bounds rectangle {rect} in {image.size}")
    return image.crop((x, y, x + w, y + h))


def main() -> None:
    sys.path.insert(0, str(PARSER))
    from xnb_parse.xnb_reader import XNBReader

    inventory = json.loads(INVENTORY.read_text(encoding="utf-8"))
    package_versions = json.loads((ROOT / "config/supported-360-versions.json").read_text(encoding="utf-8"))
    packages = {item["sourceId"]: item["package"] for item in package_versions["titles"]}
    files = {(t["source"], t["path"]): t for t in inventory["textures"]}
    images: dict[tuple[str, str], Image.Image] = {}

    def image(source: str, path: str) -> Image.Image:
        key = source, path
        if key not in images:
            content = XNBReader.load(filename=str(SOURCE_ROOTS[source] / path)).content
            images[key] = Image.frombytes("RGBA", (content.width, content.height), content.full_data())
        return images[key]

    def fallback(row: dict) -> dict:
        texture = row["phoneTexture"]
        paths = []
        for option in (texture or "").split("|"):
            if option.endswith("/*"):
                prefix = option[:-1]
                paths.extend(t for (s, t) in files if s == "phone" and t.startswith(prefix) and t.endswith(".xnb"))
            elif option and not option.startswith("generated/"):
                paths.append(option + ".xnb")
        alternatives = [{"path": p, "sha256": files[("phone", p)]["sha256"]}
                        for p in sorted(set(paths)) if ("phone", p) in files]
        return {"alternatives": alternatives, "rectangle": row["phoneRectangle"],
                "expression": row.get("sourceRectangleExpression"),
                "runtimeTexture": texture if not alternatives else None,
                "resolution": "exact" if len(alternatives) == 1 and row["phoneRectangle"] else "runtime-expression"}

    def proposed(row: dict, source: str) -> tuple[str, list[int] | None, str]:
        texture = row["phoneTexture"] or ""
        rect = row["phoneRectangle"]
        if source in ("i-maed-360", "i-maed-steam"):
            if not texture.startswith("gfx_2/zombie/"):
                return "", None, "unrelated phone texture"
            name = texture.rsplit("/", 1)[-1]
            if source == "i-maed-steam" and name == "1x1":
                return "", None, "phone-only pixel"
            path = f"gfx/{name}.xnb"
            if not rect:
                return path, None, "dynamic or whole-texture rectangle needs frame enumeration"
            return path, [v * 2 for v in rect], "background vignette treatment pending" if name != "sprites" else "sprite frame visual review pending"
        if source == "time-viking-360":
            if texture.startswith("gfx_2/zombie/") and texture.endswith("/sprites"):
                if not rect:
                    return "gfx/zombie/sprites.xnb", None, "dynamic zombie rectangle needs frame enumeration"
                return "gfx/zombie/sprites.xnb", [v * 2 for v in rect], "Endless secondary candidate; I MAED 360 has priority"
            if texture.startswith("gfx_2/") and "|" not in texture:
                name = texture.rsplit("/", 1)[-1]
                if name in ("thumbclick", "thumbpad", "thumbpad2"):
                    return "", None, "phone touch control"
                path = f"gfx/{name}.xnb"
                if not rect:
                    return path, None, "dynamic rectangle needs frame enumeration"
                if name == "grass" and rect[0] >= 448:
                    return path, None, "grass face icons moved and are smaller in 360 atlas"
                raw = row.get("zsxRectangle")
                return path, raw if raw else [v * 2 for v in rect], "per-frame visual review pending"
            if texture in ("gfx/maps", "gfx/maps2"):
                path = texture + ".xnb"
                return path, rect, "same-size atlas; inspect cell and game effect"
        return "", None, "no matching source or phone-only artwork"

    rows = inventory["drawSites"] + inventory["spriteCells"] + inventory["expandedFrames"]
    outputs = {
        "i-maed-360": ("i-maed", "endless"),
        "i-maed-steam": ("i-maed", "endless"),
        "time-viking-360": ("time-viking", "endless"),
    }
    OUT.mkdir(parents=True, exist_ok=True)
    PRIVATE.mkdir(parents=True, exist_ok=True)
    written: dict[str, dict] = {}
    for source, games in outputs.items():
        elements = []
        previews = collections.defaultdict(list)
        for row in rows:
            if row["mode"] == "phone-shared":
                continue
            if source == "i-maed-steam" and row["mode"] not in ("zombie-shared", "endless"):
                continue
            if source == "i-maed-360" and row["mode"] not in ("zombie-shared", "endless"):
                continue
            if source == "time-viking-360" and row["mode"] not in ("time-viking", "endless", "zombie-shared"):
                continue
            path, rect, reason = proposed(row, source)
            item = {"id": row["id"], "mode": row["mode"], "fallback": fallback(row),
                    "status": "original", "reason": reason}
            if path and rect and (source, path) in files and len(item["fallback"]["alternatives"]) == 1:
                src = files[(source, path)]
                try:
                    a = crop(image("phone", item["fallback"]["alternatives"][0]["path"]), row["phoneRectangle"])
                    b = crop(image(source, path), rect)
                except ValueError as error:
                    item["reason"] = str(error)
                else:
                    scores = metrics(a, b)
                    item.update({"status": "candidate", "source": {"path": path,
                                 "sha256": src.get("packageEntry", {}).get("sha256", src["sha256"]),
                                 "decodedSha256": src["sha256"],
                                 "packageEntryPath": src.get("packageEntry", {}).get("path"),
                                 "rectangle": rect},
                                 "expectedOutput": {"physicalSize": rect[2:],
                                                    "logicalSize": row["phoneRectangle"][2:]},
                                 "comparison": scores})
                    if "visual review pending" in reason:
                        item["reason"] = "individual decoded crop compared; runtime geometry and effects pending"
                    if scores["phoneVisiblePixels"] == 0:
                        item["status"] = "original"
                        item["reason"] = "empty phone atlas cell; no imported artwork selected"
                    elif scores["alphaIoU"] < 0.8 or (scores["luminanceCorrelation"] < 0.75 and scores["visibleMae"] >= 1):
                        if (source == "i-maed-360" and row["id"] in SHADOW_BAKED_PHONE_FRAMES
                                and scores["luminanceCorrelation"] >= 0.98):
                            item["reason"] = "reviewed 360 character crop; phone baked shadow reaches atlas cell edge"
                        else:
                            item["status"] = "uncertain"
                            item["reason"] = "crop differs materially; keep Original until manually remapped"
                    previews[path].append((row["id"], a, b, scores))
            elements.append(item)
        if source == "time-viking-360":
            moon_parts = [
                ("moon/stage1", "gfx_2/moon1.xnb", [0, 0, 512, 192], [512, 6, 1024, 384]),
                ("moon/stage2", "gfx_2/moon2.xnb", [0, 0, 512, 192], [510, 714, 1024, 384]),
                ("moon/stage3-left", "gfx_2/moon3.xnb", [0, 0, 256, 192], [504, 1416, 512, 384]),
                ("moon/stage3-right", "gfx_2/moon3.xnb", [256, 0, 256, 192], [1016, 1416, 512, 384]),
            ]
            moon_source = files[(source, "gfx/moon.xnb")]
            for label, phone_path, phone_rect, source_rect in moon_parts:
                scores = metrics(crop(image("phone", phone_path), phone_rect),
                                 crop(image(source, "gfx/moon.xnb"), source_rect))
                elements.append({
                    "id": label, "mode": "time-viking", "status": "candidate",
                    "fallback": {"alternatives": [{"path": phone_path,
                                  "sha256": files[("phone", phone_path)]["sha256"]}],
                                  "rectangle": phone_rect, "resolution": "exact"},
                    "source": {"path": "gfx/moon.xnb",
                               "sha256": moon_source["packageEntry"]["sha256"],
                               "decodedSha256": moon_source["sha256"],
                               "packageEntryPath": moon_source["packageEntry"]["path"],
                               "rectangle": source_rect},
                    "expectedOutput": {"physicalSize": source_rect[2:], "logicalSize": phone_rect[2:]},
                    "comparison": scores,
                    "reason": "center crop from matching 360 moon atlas row; no full-row substitution",
                })
                previews["gfx/moon.xnb"].append((label, crop(image("phone", phone_path), phone_rect),
                                                 crop(image(source, "gfx/moon.xnb"), source_rect), scores))
            grass_source = files[(source, "gfx/grass.xnb")]
            grass_phone = files[("phone", "gfx_2/grass.xnb")]
            for index in range(4):
                phone_rect = [448, 128 + index * 64, 64, 64]
                source_rect = [495, 272 + index * 64, 64, 64]
                a = crop(image("phone", "gfx_2/grass.xnb"), phone_rect)
                b = crop(image(source, "gfx/grass.xnb"), source_rect)
                scores = metrics(a, b)
                elements.append({
                    "id": f"grass/face{index}", "mode": "time-viking", "status": "candidate",
                    "fallback": {"alternatives": [{"path": "gfx_2/grass.xnb",
                                  "sha256": grass_phone["sha256"]}],
                                  "rectangle": phone_rect, "resolution": "exact"},
                    "source": {"path": "gfx/grass.xnb",
                               "sha256": grass_source["packageEntry"]["sha256"],
                               "decodedSha256": grass_source["sha256"],
                               "packageEntryPath": grass_source["packageEntry"]["path"],
                               "rectangle": source_rect},
                    "expectedOutput": {"physicalSize": [64, 64], "logicalSize": [64, 64]},
                    "comparison": scores,
                    "reason": "relocated same-size icon; exact alpha silhouette and visible MAE below 1",
                })
                previews["gfx/grass.xnb"].append((f"grass/face{index}", a, b, scores))
        if source in ("i-maed-360", "i-maed-steam"):
            for name in ("concrete", "fire", "grass", "grid", "neko", "psychoneko", "ska", "space"):
                phone_path, source_path = f"gfx_2/zombie/{name}.xnb", f"gfx/{name}.xnb"
                phone_file, source_file = files[("phone", phone_path)], files[(source, source_path)]
                phone_rect = [0, 0, phone_file["width"], phone_file["height"]]
                source_rect = [0, 0, source_file["width"], source_file["height"]]
                scores = metrics(image("phone", phone_path), image(source, source_path))
                elements.append({
                    "id": f"background/i-maed/{name}", "mode": "i-maed", "status": "candidate",
                    "fallback": {"alternatives": [{"path": phone_path, "sha256": phone_file["sha256"]}],
                                  "rectangle": phone_rect, "resolution": "exact"},
                    "source": {"path": source_path,
                               "sha256": source_file.get("packageEntry", {}).get("sha256", source_file["sha256"]),
                               "decodedSha256": source_file["sha256"],
                               "packageEntryPath": source_file.get("packageEntry", {}).get("path"),
                               "rectangle": source_rect},
                    "expectedOutput": {"physicalSize": source_rect[2:], "logicalSize": phone_rect[2:]},
                    "comparison": scores,
                    "reason": "single background image; phone vignette is baked and requires guarded treatment review",
                })
                previews[source_path].append((f"background/{name}", image("phone", phone_path),
                                              image(source, source_path), scores))
        manifest = {"schemaVersion": 1, "sourceId": source, "games": games,
                    "phoneDllSha256": inventory["phoneDllSha256"],
                    "sourceIdentity": inventory["steamExecutable"] if source == "i-maed-steam" else packages["zombies-360" if source == "i-maed-360" else "viking-360"],
                    "candidatePolicy": "metadata evidence only; importer and renderer not implemented",
                    "elements": elements}
        dest = OUT / f"{source}.json"
        dest.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        written[source] = manifest
        for path, samples in previews.items():
            # Every frame appears once, side by side; sheets stay ignored/private.
            for offset in range(0, len(samples), 40):
                subset = samples[offset:offset + 40]
                sheet = Image.new("RGB", (720, len(subset) * 112), (36, 36, 36))
                draw = ImageDraw.Draw(sheet)
                for i, (label, a, b, scores) in enumerate(subset):
                    top = i * 112
                    draw.text((4, top + 2), f"{label} IoU={scores['alphaIoU']} corr={scores['luminanceCorrelation']}", fill="white")
                    for image_value, x in ((a, 4), (b, 360)):
                        thumb = image_value.copy()
                        thumb.thumbnail((350, 88))
                        sheet.paste(thumb, (x, top + 22), thumb)
                name = path.replace("/", "-").replace(".xnb", "")
                sheet.save(PRIVATE / f"{source}-{name}-{offset // 40}.png")
        print(f"MAPPING_OK {source} elements={len(elements)} candidates={sum(e['status'] == 'candidate' for e in elements)}")

    by_source = {source: {e["id"]: e for e in manifest["elements"]}
                 for source, manifest in written.items()}
    endless = []
    for row in rows:
        if row["mode"] not in ("zombie-shared", "endless"):
            continue
        first = by_source["i-maed-360"][row["id"]]
        variants = []
        for source in ("i-maed-360", "i-maed-steam", "time-viking-360"):
            item = by_source[source][row["id"]]
            if item["status"] == "candidate":
                variants.append({"sourceId": source, "source": item["source"],
                                 "expectedOutput": item["expectedOutput"],
                                 "comparison": item["comparison"]})
        endless.append({"id": row["id"], "fallback": first["fallback"],
                        "variants": variants, "status": "candidate" if variants else "original",
                        "reason": "source priority is I MAED 360, Steam I MAED, Time Viking 360, then phone Original"})
    endless_manifest = {
        "schemaVersion": 1, "game": "endless", "phoneDllSha256": inventory["phoneDllSha256"],
        "sourcePriority": ["i-maed-360", "i-maed-steam", "time-viking-360", "phone"],
        "steamEligible": True,
        "sourceIdentity": {"i-maed-360": packages["zombies-360"],
                           "i-maed-steam": inventory["steamExecutable"],
                           "time-viking-360": packages["viking-360"]},
        "candidatePolicy": "metadata evidence only; importer and renderer not implemented",
        "elements": endless,
    }
    (OUT / "endless-360.json").write_text(json.dumps(endless_manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"MAPPING_OK endless-360 elements={len(endless)} candidates={sum(e['status'] == 'candidate' for e in endless)}")

    grid = Image.new("RGB", (1920, 670), (45, 45, 45))
    draw = ImageDraw.Draw(grid)
    for i, (source, path, label) in enumerate((
        ("phone", "gfx_2/zombie/sprites.xnb", "Phone"),
        ("i-maed-360", "gfx/sprites.xnb", "360 downsampled"),
        ("i-maed-steam", "gfx/sprites.xnb", "Steam downsampled"),
    )):
        atlas = image(source, path).resize((640, 640), Image.Resampling.BOX)
        grid.paste(atlas, (640 * i, 28), atlas)
        draw.text((640 * i + 10, 5), label, fill="white")
        for n in range(0, 641, 32):
            draw.line((640 * i + n, 28, 640 * i + n, 668), fill=(90, 90, 90))
            draw.line((640 * i, 28 + n, 640 * i + 640, 28 + n), fill=(90, 90, 90))
    grid.save(PRIVATE.parent / "zombie-sprites-grid.png")


if __name__ == "__main__":
    main()
