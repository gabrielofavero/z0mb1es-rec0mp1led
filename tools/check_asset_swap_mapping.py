"""Validate P0 mapping coverage, fingerprints, and rectangle bounds from metadata."""

from __future__ import annotations

import json
import pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
RECORDS = ROOT / "docs/records"


def read(path: pathlib.Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def inside(rect: list[int], width: int, height: int) -> bool:
    x, y, w, h = rect
    return x >= 0 and y >= 0 and w > 0 and h > 0 and x + w <= width and y + h <= height


def check() -> None:
    inventory = read(RECORDS / "asset-swap-inventory.json")
    files = {(t["source"], t["path"]): t for t in inventory["textures"]}
    steam = read(ROOT / "config/supported-steam-versions.json")
    assert steam["executable"] == inventory["steamExecutable"]
    assert len(steam["requiredTextures"]) == 9
    for item in steam["requiredTextures"]:
        assert item == {key: files[("i-maed-steam", item["path"])][key] for key in item}

    rows = {r["id"]: r for r in inventory["drawSites"] + inventory["spriteCells"] + inventory["expandedFrames"]}
    assert len(rows) == len(inventory["drawSites"]) + len(inventory["spriteCells"]) + len(inventory["expandedFrames"])
    checked = 0
    for source in ("i-maed-360", "i-maed-steam", "time-viking-360"):
        manifest = read(RECORDS / "asset-swap-mappings" / f"{source}.json")
        assert manifest["schemaVersion"] == 1
        assert manifest["sourceId"] == source
        assert manifest["phoneDllSha256"] == inventory["phoneDllSha256"]
        seen = set()
        for element in manifest["elements"]:
            name = element["id"]
            assert name not in seen, name
            seen.add(name)
            fallback = element["fallback"]
            assert fallback["alternatives"] or fallback.get("runtimeTexture"), name
            for alt in fallback["alternatives"]:
                file = files[("phone", alt["path"])]
                assert alt["sha256"] == file["sha256"]
                if fallback["rectangle"]:
                    assert inside(fallback["rectangle"], file["width"], file["height"]), name
            if element["status"] not in ("candidate", "uncertain"):
                continue
            src = element["source"]
            file = files[(source, src["path"])]
            assert src["decodedSha256"] == file["sha256"], name
            if source.endswith("360"):
                assert src["sha256"] == file["packageEntry"]["sha256"], name
                assert src["packageEntryPath"] == file["packageEntry"]["path"], name
            else:
                assert src["sha256"] == file["sha256"], name
            assert inside(src["rectangle"], file["width"], file["height"]), name
            assert element["expectedOutput"]["physicalSize"] == src["rectangle"][2:], name
            checked += 1
        expected = {name for name, row in rows.items() if row["mode"] != "phone-shared"
                    and (source == "time-viking-360"
                         or (source == "i-maed-steam" and row["mode"] in ("zombie-shared", "endless"))
                         or (source == "i-maed-360" and row["mode"] in ("zombie-shared", "endless")))}
        extras = ({f"background/i-maed/{name}" for name in
                   ("concrete", "fire", "grass", "grid", "neko", "psychoneko", "ska", "space")}
                  if source.startswith("i-maed") else
                  {"moon/stage1", "moon/stage2", "moon/stage3-left", "moon/stage3-right",
                   "grass/face0", "grass/face1", "grass/face2", "grass/face3"})
        assert seen == expected | extras, (source, len(expected - seen), len(seen - (expected | extras)))
        if source == "time-viking-360":
            by_id = {e["id"]: e for e in manifest["elements"]}
            for i in range(4):
                face = by_id[f"grass/face{i}"]
                assert face["status"] == "candidate"
                assert face["source"]["rectangle"] == [495, 272 + i * 64, 64, 64]
                assert face["comparison"]["alphaIoU"] == 1.0
            for name in ("moon/stage1", "moon/stage2", "moon/stage3-left", "moon/stage3-right"):
                assert by_id[name]["status"] == "candidate"
                assert by_id[name]["source"]["path"] == "gfx/moon.xnb"
        if source == "i-maed-360":
            by_id = {e["id"]: e for e in manifest["elements"]}
            shadowed = {f"draw/IMAK3Z0MB1EGAEM.character/Monster/{i:03d}" for i in (8, 9, 10)}
            shadowed.update(f"frame/IMAK3Z0MB1EGAEM.character/Hero/001/{i:03d}" for i in range(5))
            for name in shadowed:
                assert by_id[name]["status"] == "candidate", name
                assert by_id[name]["source"]["path"] == "gfx/sprites.xnb", name

    endless = read(RECORDS / "asset-swap-mappings/endless-360.json")
    assert endless["sourcePriority"] == ["i-maed-360", "i-maed-steam", "time-viking-360", "phone"]
    assert endless["steamEligible"] is True
    source_elements = {
        source: {e["id"]: e for e in read(RECORDS / "asset-swap-mappings" / f"{source}.json")["elements"]}
        for source in endless["sourcePriority"][:-1]
    }
    assert {e["id"] for e in endless["elements"]} == {
        name for name, row in rows.items() if row["mode"] in ("zombie-shared", "endless")}
    for element in endless["elements"]:
        assert [v["sourceId"] for v in element["variants"]] == [
            source for source in endless["sourcePriority"][:-1]
            if source_elements[source][element["id"]]["status"] == "candidate"]
        assert element["fallback"]["alternatives"] or element["fallback"].get("runtimeTexture")
    print(f"MAPPING_CHECK_OK drawSites={len(inventory['drawSites'])} "
          f"spriteCells={len(inventory['spriteCells'])} expandedFrames={len(inventory['expandedFrames'])} sourceRectangles={checked} "
          f"endlessElements={len(endless['elements'])}")


if __name__ == "__main__":
    check()
