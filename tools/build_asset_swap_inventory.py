"""Build metadata-only visual inventory for the pinned phone and optional sources.

Run after decompiling the player-owned XAP with the pinned local ILSpy CLI and
decoding the player-owned 360 XNBs into ignored private storage. No pixel data
or decompiled game code is written to the repository.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import pathlib
import re
import struct
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
SOURCE = ROOT / "private/asset-swap/phone-source"
PHONE = ROOT / "private/original-xap"
XBOX_Z = ROOT / "private/asset-comparison/xnb-unpacked-zombies"
XBOX_V = ROOT / "private/asset-comparison/xnb-unpacked-viking"
STEAM = pathlib.Path("D:/Program Files (x86)/Steam/steamapps/common/IMAGWZII")
PARSER = ROOT.parent / "xna-to-fna-asset-extractor/xnb_parse"


def sha256(path: pathlib.Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def split_args(s: str) -> list[str]:
    result, start, depth, quote, escape = [], 0, 0, None, False
    for i, c in enumerate(s):
        if quote:
            if escape:
                escape = False
            elif c == "\\":
                escape = True
            elif c == quote:
                quote = None
        elif c in ('"', "'"):
            quote = c
        elif c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
        elif c == "," and depth == 0:
            result.append(s[start:i].strip())
            start = i + 1
    result.append(s[start:].strip())
    return result


def call_body(text: str, start: int) -> str:
    depth, quote, escape = 1, None, False
    for i in range(start, len(text)):
        c = text[i]
        if quote:
            if escape:
                escape = False
            elif c == "\\":
                escape = True
            elif c == quote:
                quote = None
        elif c in ('"', "'"):
            quote = c
        elif c == "(":
            depth += 1
        elif c == ")":
            depth -= 1
            if depth == 0:
                return text[start:i]
    raise ValueError("unterminated Draw call")


def literal_rect(expr: str) -> list[int] | None:
    m = re.fullmatch(r"new Rectangle\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)", expr)
    if m:
        return [int(x) for x in m.groups()]
    m = re.fullmatch(r"ScaleTools.ScaledRect\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)", expr)
    return [int(x) // 2 for x in m.groups()] if m else None


def mode_of(path: pathlib.Path) -> str:
    name = str(path.relative_to(SOURCE)).replace("\\", "/")
    if name.startswith("MapEdit"):
        return "endless"
    if name.startswith(("IMAK3Z0MB1EGAEM", "Viking_x86.zomb1es.endless")):
        return "zombie-shared"  # call graph decides I MAED versus Endless
    if name.startswith("Viking_x86") or name.startswith("SheetEdit"):
        return "time-viking"
    return "phone-shared"


def texture_of(expr: str) -> str | None:
    if "moonTex" in expr:
        return "gfx_2/moon1|moon2|moon3"
    patterns = [
        (r"ZombieGame\.(\w+)Tex", lambda m: "gfx_2/zombie/" + {"psychoNeko": "psychoneko"}.get(m[1], m[1].lower())),
        (r"Game1\.vgame\.(\w+)Tex", lambda m: "gfx_2/" + {"heart": "hearts"}.get(m[1], m[1].lower())),
    ]
    for pattern, value in patterns:
        m = re.search(pattern, expr)
        if m:
            return value(m)
    if "VikingGame.textures[" in expr or "textures[" in expr:
        m = re.search(r'\["(robot|viking|scrap)"\]', expr)
        return "gfx_2/" + m[1] if m else None
    for token, path in {
        "Game1.nullTex": "gfx/1x1", "nullTex": "gfx/1x1",
        "Game1.thumclickTex": "gfx_2/thumbclick", "Game1.thumpadTex": "gfx_2/thumbpad|thumbpad2",
        "Game1.glowTex": "gfx/glow", "skalogoTex": "gfx/skalogo",
        "mslogoTex": "gfx/mslogo", "Game1.achieveTextures": "gfx/achievements/*",
    }.items():
        if token in expr:
            return path
    return None


def draw_sites() -> list[dict]:
    rows = []
    pattern = re.compile(r"\b(?:sprite|spriteBatch)\.Draw\(")
    for path in sorted(SOURCE.rglob("*.cs")):
        data = path.read_text(encoding="utf-8-sig")
        n = 0
        for m in pattern.finditer(data):
            n += 1
            args = split_args(call_body(data, m.end()))
            if not args:
                continue
            if args[0].startswith("origin:"):
                args = [next(a.partition(":")[2].strip() for a in args if a.startswith("texture:")),
                        next(a.partition(":")[2].strip() for a in args if a.startswith("position:")),
                        next(a.partition(":")[2].strip() for a in args if a.startswith("sourceRectangle:"))]
            line = data.count("\n", 0, m.start()) + 1
            rel = str(path.relative_to(SOURCE)).replace("\\", "/")
            texture = texture_of(args[0])
            if texture is None:
                if rel.startswith("IMAK3Z0MB1EGAEM.map/MapMan") and args[0] in ("tex", "tex1", "tex2"):
                    texture = "gfx_2/zombie/concrete|gfx_2/zombie/fire|gfx_2/zombie/grass|gfx_2/zombie/grid|gfx_2/zombie/neko|gfx_2/zombie/psychoneko|gfx_2/zombie/ska|gfx_2/zombie/space"
                elif rel.startswith("MapEdit"):
                    texture = "gfx/maps|gfx/maps2" if "map/" in rel else "gfx_2/zombie/sprites"
                elif rel.startswith("Viking_x86.character/BodySec"):
                    texture = "gfx_2/robot|gfx_2/scrap|gfx_2/viking"
                elif rel.startswith("SceneEdit.scene"):
                    texture = "gfx_2/scenes/*"
                elif "custom" in rel.lower() or "CustomMapper" in rel or args[0] == "customImg.texture":
                    texture = "gfx/custom/*|generated/custom"
                elif args[0] == "Game1.textTarg":
                    texture = "generated/text-render-target"
            source = args[2] if len(args) > 2 and not args[2].startswith(("Color.", "new Color(")) else "full texture"
            rect = literal_rect(source)
            rows.append({
                "id": f"draw/{rel[:-3]}/{n:03d}", "mode": mode_of(path),
                "sourceCode": f"{rel}:{line}", "textureExpression": args[0],
                "phoneTexture": texture, "sourceRectangleExpression": source,
                "phoneRectangle": rect, "coverage": "original",
            })
    return rows


def texture_loads() -> list[dict]:
    rows = []
    for path in sorted(SOURCE.rglob("*.cs")):
        data = path.read_text(encoding="utf-8-sig")
        rel = str(path.relative_to(SOURCE)).replace("\\", "/")
        for m in re.finditer(r'Content\.Load<Texture2D>\(([^)]*)\)|new XTexture\(Content,\s*"([^"]+)"\)', data):
            expression = (m.group(1) or '"' + m.group(2) + '"').strip()
            rows.append({"sourceCode": f"{rel}:{data.count(chr(10), 0, m.start()) + 1}",
                         "mode": mode_of(path), "pathExpression": expression})
    return rows


def zombie_sprite_frames(draws: list[dict]) -> list[dict]:
    """Enumerate bounded sprite cells selected by the pinned DLL's 12 formulas.

    These are all in-bounds *possible* cells. Reachability of every index is not
    asserted; the original expression and call site remain in drawSites.
    """
    recipes = {
        "character/Hero/001": ([i * 256 for i in range(5)], [0], 256, 128),
        "character/Monster/001": ([1152], [192 + i * 128 for i in range(8)], 128, 128),
        "hud/HUD/002": ([i * 128 for i in range(10)], [1152], 128, 128),
        "particles.particles/Blood/001": ([i * 128 for i in range(10)], [128], 128, 128),
        "particles.particles/Explode/001": ([1024], [i * 64 for i in range(20)], 64, 64),
        "particles.particles/FaceDie/001": ([i * 128 for i in range(6)], [1024], 128, 128),
        "particles.particles/Flame/001": ([1024], [i * 64 for i in range(20)], 64, 64),
        "particles.particles/MuzzleFlash/001": ([i * 128 for i in range(10)], [320], 128, 128),
        "particles.particles/Neuchunk/001": ([i * 128 for i in range(2)], [320], 128, 128),
        "particles.particles/Neutron/001": ([i * 128 for i in range(2)], [320], 128, 128),
        "particles.particles/PowerUp/001": ([i * 128 for i in range(10)], [1152], 128, 128),
        "particles.particles/PowerUp/002": ([i * 128 for i in range(10)], [1152], 128, 128),
    }
    result = []
    found = set()
    for draw in draws:
        if draw["phoneTexture"] != "gfx_2/zombie/sprites" or draw["phoneRectangle"] is not None:
            continue
        key = draw["id"].removeprefix("draw/IMAK3Z0MB1EGAEM.")
        if key not in recipes:
            raise ValueError(f"unexpanded zombie sprite formula: {draw['id']}")
        found.add(key)
        xs, ys, width, height = recipes[key]
        for index, (x, y) in enumerate((x, y) for y in ys for x in xs):
            raw = [x, y, width, height]
            result.append({"id": f"frame/{draw['id'][5:]}/{index:03d}",
                           "drawSiteId": draw["id"], "mode": "zombie-shared",
                           "phoneTexture": "gfx_2/zombie/sprites",
                           "phoneRectangle": [v // 2 for v in raw],
                           "rawRectangle": raw, "coverage": "original",
                           "reachability": "bounded possible atlas cell; game-state index range not asserted"})
    if found != set(recipes):
        raise ValueError(f"missing formula sites: {set(recipes) - found}")
    return result


def read_7bit(data: bytes, pos: int) -> tuple[int, int]:
    value, shift = 0, 0
    while True:
        byte = data[pos]
        pos += 1
        value |= (byte & 127) << shift
        if byte < 128:
            return value, pos
        shift += 7


def sprite_cells() -> list[dict]:
    result = []
    for name in ("robot", "scrap", "viking", "maps", "maps2"):
        path = PHONE / ("gfx" if name.startswith("maps") else "gfx_2") / f"{name}.zsx"
        data, pos = path.read_bytes(), 0
        for index in range(128):
            enabled = data[pos]
            pos += 1
            if not enabled:
                continue
            length, pos = read_7bit(data, pos)
            label = data[pos:pos + length].decode("utf-8", errors="replace")
            pos += length
            raw = list(struct.unpack_from("<4i", data, pos))
            pos += 16
            origin = list(struct.unpack_from("<2f", data, pos))
            pos += 8
            flags = struct.unpack_from("<i", data, pos)[0]
            pos += 4
            result.append({
                "id": f"cell/{name}/{index:03d}", "mode": "endless" if name.startswith("maps") else "time-viking",
                "phoneTexture": f"{'gfx' if name.startswith('maps') else 'gfx_2'}/{name}",
                "phoneRectangle": raw if name.startswith("maps") else [v // 2 for v in raw],
                "zsxRectangle": raw, "name": label, "origin": origin,
                "flags": flags, "coverage": "original",
            })
        if pos != len(data):
            raise ValueError(f"trailing data in {path}: {len(data) - pos}")
    return result


def texture_files() -> list[dict]:
    sys.path.insert(0, str(PARSER))
    from xnb_parse.xnb_reader import XNBReader
    from xnb_parse.xna_types.xna_graphics import SURFACE_FORMAT, SURFACE_FORMAT4

    roots = {
        "phone": PHONE / "Content", "i-maed-360": XBOX_Z,
        "time-viking-360": XBOX_V, "i-maed-steam": STEAM / "Content",
    }
    original_entries = {}
    for source, report in (("i-maed-360", "zombies"), ("time-viking-360", "viking")):
        report_data = json.loads((ROOT / f"docs/records/{report}-360-inventory.json").read_text(encoding="utf-8"))
        for entry in report_data["files"]:
            original_entries[(source, entry["path"].removeprefix("584E07D1/Content/"))] = entry
    rows = []
    for source, root in roots.items():
        for path in sorted(root.rglob("*.xnb")):
            rel = path.relative_to(root).as_posix()
            data = path.read_bytes()
            if data[:3] != b"XNB":
                rows.append({"source": source, "path": rel, "bytes": len(data),
                             "sha256": sha256(path), "type": "not-xnb"})
                continue
            try:
                content = XNBReader.load(filename=str(path)).content
                if not all(hasattr(content, x) for x in ("width", "height", "full_data")):
                    continue
                formats = SURFACE_FORMAT4 if type(content.surface_format).__name__ == "SurfaceFormat4" else SURFACE_FORMAT
                row = {"source": source, "path": rel, "bytes": len(data),
                       "sha256": sha256(path), "width": content.width,
                       "height": content.height, "type": "Texture2D",
                       "surfaceFormat": formats[content.surface_format.value][0],
                       "mipCount": len(content.mip_levels)}
                if source in ("i-maed-360", "time-viking-360"):
                    entry = original_entries.get((source, rel))
                    if entry is None:
                        raise ValueError(f"360 decoded file has no pinned package entry: {source}:{rel}")
                    row["packageEntry"] = {"path": entry["path"], "bytes": entry["bytes"],
                                           "sha256": entry["sha256"]}
                rows.append(row)
            except Exception as error:
                raise ValueError(f"cannot decode {source}:{rel}: {error}") from error
    return rows


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=pathlib.Path,
                        default=ROOT / "docs/records/asset-swap-inventory.json")
    args = parser.parse_args()
    if not SOURCE.exists():
        raise SystemExit(f"decompile the pinned phone DLL into {SOURCE} first")
    draws = draw_sites()
    inventory = {
        "schemaVersion": 1,
        "phoneDllSha256": sha256(PHONE / "ZombiesWP7.dll"),
        "decompiler": "ILSpy CLI 11.1.0.9782",
        "steamExecutable": {"path": "Z0MB1ES.exe", "bytes": (STEAM / "Z0MB1ES.exe").stat().st_size,
                            "sha256": sha256(STEAM / "Z0MB1ES.exe")},
        "textures": texture_files(), "textureLoads": texture_loads(),
        "drawSites": draws, "spriteCells": sprite_cells(),
        "expandedFrames": zombie_sprite_frames(draws),
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(inventory, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"INVENTORY_OK textures={len(inventory['textures'])} loads={len(inventory['textureLoads'])} drawSites={len(inventory['drawSites'])} "
          f"spriteCells={len(inventory['spriteCells'])} expandedFrames={len(inventory['expandedFrames'])} output={args.output}")


if __name__ == "__main__":
    main()
