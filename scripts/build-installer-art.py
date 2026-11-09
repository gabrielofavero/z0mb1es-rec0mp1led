"""Render the authored SVGs into Windows icon and Inno Setup artwork.

Developer-only prerequisites: Pillow, NumPy, and local Edge or Chrome.
The generated artwork can be committed; the player installer does not run this script.
"""

from __future__ import annotations

import argparse
import base64
from copy import deepcopy
import html
import shutil
import subprocess
import tempfile
import time
from pathlib import Path
from xml.etree import ElementTree

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
ICON_DIR = ROOT / "assets" / "icon"
COVER_DIR = ROOT / "assets" / "cover"
INSTALLER_DIR = ROOT / "assets" / "installer"
WORK_DIR = ROOT / "out"
ICON_RENDER_SIZE = 768
COVER_RENDER_SIZE = 1200
ICO_SIZES = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
SIDE_SIZES = [(202, 386), (269, 515), (336, 643), (403, 772), (430, 824), (498, 953), (534, 1022)]
SVG_NS = "http://www.w3.org/2000/svg"
XLINK_NS = "http://www.w3.org/1999/xlink"


def browser_path() -> Path:
    candidates = [
        Path(r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"),
        Path(r"C:\Program Files\Microsoft\Edge\Application\msedge.exe"),
        Path(r"C:\Program Files\Google\Chrome\Application\chrome.exe"),
        Path(r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"),
    ]
    for candidate in candidates:
        if candidate.is_file():
            return candidate
    for command in ("msedge", "chrome"):
        found = shutil.which(command)
        if found:
            return Path(found)
    raise RuntimeError("Local Edge or Chrome is required to render the SVGs")


def svg_data_uri(svg: str) -> str:
    encoded = base64.b64encode(svg.encode("utf-8")).decode("ascii")
    return "data:image/svg+xml;base64," + encoded


def screenshot(svg: str, width: int, height: int, name: str, paired: bool) -> Image.Image:
    render_dir = Path(tempfile.mkdtemp(prefix="installer-art-", dir=WORK_DIR))
    data_uri = html.escape(svg_data_uri(svg), quote=True)
    panels = (
        f'<div class="panel black"><img src="{data_uri}"></div>'
        f'<div class="panel white"><img src="{data_uri}"></div>'
        if paired
        else f'<div class="panel black"><img src="{data_uri}"></div>'
    )
    page = render_dir / f"{name}.html"
    page.write_text(
        f'<html><head><style>html,body{{margin:0;padding:0}}body{{display:flex}}'
        f'.panel{{width:{width}px;height:{height}px;flex:none}}'
        f'.black{{background:#000}}.white{{background:#fff}}'
        f'img{{display:block;width:{width}px;height:{height}px}}'
        f'</style></head><body>{panels}</body></html>',
        encoding="utf-8",
    )
    output = render_dir / f"{name}.png"
    viewport_width = width * (2 if paired else 1)
    command = [
        str(browser_path()),
        "--headless=new",
        "--no-sandbox",
        "--disable-gpu",
        "--hide-scrollbars",
        "--no-first-run",
        "--disable-background-networking",
        "--user-data-dir=" + str(render_dir / "profile"),
        "--screenshot=" + str(output),
        f"--window-size={viewport_width},{height}",
        page.as_uri(),
    ]
    process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    deadline = time.monotonic() + 35
    rendered = None
    try:
        while time.monotonic() < deadline:
            if output.exists() and output.stat().st_size > 0:
                try:
                    with Image.open(output) as image:
                        image.load()
                        rendered = image.convert("RGB")
                    break
                except OSError:
                    pass  # The browser may still be writing the screenshot.
            time.sleep(0.1)
    finally:
        if process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=3)
            except subprocess.TimeoutExpired:
                process.kill()
    if rendered is None:
        raise RuntimeError(f"Browser SVG render failed for {name}")
    expected = (viewport_width, height)
    if rendered.size != expected:
        raise RuntimeError(f"Expected {expected} browser screenshot, got {rendered.size}")
    return rendered


def transparent_icon(svg: str, name: str) -> Image.Image:
    size = ICON_RENDER_SIZE
    paired = screenshot(svg, size, size, name, paired=True)
    black = np.asarray(paired.crop((0, 0, size, size)), dtype=np.float32)
    white = np.asarray(paired.crop((size, 0, size * 2, size)), dtype=np.float32)
    alpha = np.clip(255.0 - np.mean(white - black, axis=2), 0, 255)
    color = np.zeros_like(black)
    np.divide(black * 255.0, alpha[..., None], out=color, where=alpha[..., None] > 0.5)
    rgba = np.dstack((np.clip(color, 0, 255), alpha)).round().astype(np.uint8)
    icon = Image.fromarray(rgba, "RGBA")
    if icon.getpixel((0, 0))[3] != 0 or icon.getpixel((size // 2, size // 2))[3] < 250:
        raise RuntimeError("Icon transparency render was not correct")
    return icon


def write_icon(image: Image.Image, stem: str) -> None:
    image.resize((256, 256), Image.Resampling.LANCZOS).save(ICON_DIR / f"{stem}.png")
    image.save(ICON_DIR / f"{stem}.ico", format="ICO", sizes=ICO_SIZES)


def black_icon(svg: str, viewbox: list[float]) -> str:
    """Keep the red icon artwork and fill its transparent canvas with black."""
    x, y, width, height = viewbox
    background = f'<rect x="{x:g}" y="{y:g}" width="{width:g}" height="{height:g}" fill="#000000"/>'
    anchor = '<g id="red-creature">'
    if svg.count(anchor) != 1:
        raise RuntimeError("Red icon group was not found exactly once")
    return svg.replace(anchor, background + "\n" + anchor, 1)


def side_layout(cover_root: ElementTree.Element) -> str:
    """Recompose the original cover groups for the tall panel without clipping art."""
    ElementTree.register_namespace("", SVG_NS)
    ElementTree.register_namespace("xlink", XLINK_NS)
    side = ElementTree.Element(f"{{{SVG_NS}}}svg", {
        "width": "202", "height": "386", "viewBox": "0 0 202 386"
    })
    definitions = cover_root.find(f"{{{SVG_NS}}}defs")
    if definitions is None:
        raise RuntimeError("Cover SVG is missing its definitions")
    side.append(deepcopy(definitions))
    ElementTree.SubElement(side, f"{{{SVG_NS}}}rect", {
        "width": "202", "height": "386", "fill": "#000000"
    })
    layout = [
        ("title", "translate(-103.3 -4.5) scale(0.9)"),
        ("subtitle", "translate(-48.8 40.5) scale(0.66)"),
        ("red-creature", "translate(-183.375 -60.625) scale(1.25)"),
        ("red-reflection", "translate(-148.7 -35.5) scale(1.1)"),
    ]
    for element_id, transform in layout:
        original = cover_root.find(f".//*[@id='{element_id}']")
        if original is None:
            raise RuntimeError(f"Cover SVG is missing {element_id}")
        wrapper = ElementTree.SubElement(side, f"{{{SVG_NS}}}g", {"transform": transform})
        wrapper.append(deepcopy(original))
    return ElementTree.tostring(side, encoding="unicode")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    group = parser.add_mutually_exclusive_group()
    group.add_argument("--icons-only", action="store_true", help="Regenerate the red, gray, and black icons")
    group.add_argument("--side-only", action="store_true", help="Regenerate only the tall wizard side images")
    args = parser.parse_args()
    WORK_DIR.mkdir(exist_ok=True)
    INSTALLER_DIR.mkdir(exist_ok=True)
    red = (ICON_DIR / "icon.svg").read_text(encoding="utf-8-sig")
    cover_svg = (COVER_DIR / "cover.svg").read_text(encoding="utf-8-sig")
    icon_root = ElementTree.fromstring(red)
    cover_root = ElementTree.fromstring(cover_svg)
    icon_viewbox = [float(value) for value in icon_root.attrib.get("viewBox", "").split()]
    if len(icon_viewbox) != 4 or icon_viewbox[2] != icon_viewbox[3]:
        raise RuntimeError("The icon source needs a square viewBox")
    if cover_root.attrib.get("viewBox") != "0 0 450 450":
        raise RuntimeError("The cover source needs its original square 450x450 viewBox")

    if not args.side_only:
        gray = (
            red.replace("#FF0000", "#A0A0A0")
            .replace("#790000", "#555555")
            .replace("#690000", "#414141")
            .replace("red-glow", "gray-glow")
            .replace("red-creature", "gray-creature")
        )
        if gray == red:
            raise RuntimeError("Gray icon palette did not change")
        (ICON_DIR / "icon-gray.svg").write_text(gray, encoding="utf-8")
        write_icon(transparent_icon(red, "icon-red"), "icon")
        write_icon(transparent_icon(gray, "icon-gray"), "icon-gray")
        black = black_icon(red, icon_viewbox)
        (ICON_DIR / "icon-black.svg").write_text(black, encoding="utf-8")
        screenshot(black, 1024, 1024, "icon-black", paired=False).save(ICON_DIR / "icon-black.png")

    if args.icons_only:
        print("Rendered red and gray ICOs and PNGs, plus the 1024px black icon")
        return

    if not args.side_only:
        cover = screenshot(cover_svg, COVER_RENDER_SIZE, COVER_RENDER_SIZE, "cover", paired=False)
        cover.resize((450, 450), Image.Resampling.LANCZOS).save(COVER_DIR / "cover.png")

    side_svg = side_layout(cover_root)
    (INSTALLER_DIR / "wizard-side.svg").write_text(side_svg, encoding="utf-8")
    side_render = screenshot(side_svg, 808, 1544, "wizard-side", paired=False)
    for width, height in SIDE_SIZES:
        side = side_render.resize((width, height), Image.Resampling.LANCZOS)
        side.save(INSTALLER_DIR / f"wizard-large-{width}x{height}.bmp", format="BMP")

    print("Rendered seven tall wizard side BMPs" if args.side_only else
          "Rendered red and gray ICOs, black icon, square cover, and seven tall wizard side BMPs")


if __name__ == "__main__":
    main()
