"""Build the rec0mp1led cover variant from the existing vector title."""

from pathlib import Path
from xml.etree import ElementTree


ROOT = Path(__file__).parent
source = ElementTree.parse(ROOT / "cover.svg").getroot()
title_path = next(node.attrib["d"] for node in source.iter() if node.attrib.get("id") == "title-letters")

glyphs = {
    "(": ["001", "010", "100", "100", "100", "010", "001"],
    ")": ["100", "010", "001", "001", "001", "010", "100"],
    "r": ["0000", "1010", "1101", "1000", "1000", "1000", "1000"],
    "e": ["0000", "0110", "1001", "1111", "1000", "1001", "0110"],
    "c": ["0000", "0111", "1000", "1000", "1000", "1000", "0111"],
    "0": ["0110", "1001", "1001", "1001", "1001", "1001", "0110"],
    "m": ["00000", "11010", "10101", "10101", "10101", "10101", "10101"],
    "p": ["0000", "1110", "1001", "1001", "1110", "1000", "1000"],
    "1": ["010", "110", "010", "010", "010", "010", "111"],
    "l": ["100", "100", "100", "100", "100", "100", "011"],
    "d": ["0001", "0001", "0111", "1001", "1001", "1001", "0111"],
}

label = "(rec0mp1led)"
unit = 4.2
gap = 1
columns = sum(len(glyphs[ch][0]) for ch in label) + gap * (len(label) - 1)
x0 = (450 - columns * unit) / 2
pixels = []
column = 0
for ch in label:
    for row, bits in enumerate(glyphs[ch]):
        for col, bit in enumerate(bits):
            if bit == "1":
                pixels.append(f"M{x0 + (column + col) * unit:.2f} {173 + row * unit:.2f}h{unit}v{unit}h-{unit}Z")
    column += len(glyphs[ch][0]) + gap
subtitle_path = "".join(pixels)

svg = f'''<svg xmlns="http://www.w3.org/2000/svg" width="450" height="450" viewBox="0 0 450 450" role="img" aria-labelledby="cover-title">
  <title id="cover-title">z0mb1es (rec0mp1led)</title>
  <defs>
    <filter id="title-glow" x="-35%" y="-160%" width="170%" height="420%" color-interpolation-filters="sRGB"><feGaussianBlur stdDeviation="7"/></filter>
    <filter id="subtitle-glow" x="-20%" y="-140%" width="140%" height="380%" color-interpolation-filters="sRGB"><feGaussianBlur stdDeviation="6"/></filter>
    <filter id="red-glow" x="-70%" y="-70%" width="240%" height="240%" color-interpolation-filters="sRGB"><feGaussianBlur stdDeviation="5"/></filter>
    <filter id="reflection-blur" x="-60%" y="-200%" width="220%" height="500%" color-interpolation-filters="sRGB"><feGaussianBlur stdDeviation="11 8"/></filter>
    <linearGradient id="face-gradient" x1="0" y1="233" x2="0" y2="254" gradientUnits="userSpaceOnUse"><stop stop-color="#790000"/><stop offset="1" stop-color="#690000"/></linearGradient>
    <path id="title-letters" d="{title_path}"/>
    <path id="subtitle-letters" d="{subtitle_path}"/>
    <path id="creature-shape" d="M212 217H243V223H248V259H243V264H212V259H206V223H212Z"/>
  </defs>
  <rect width="450" height="450" fill="#000"/>
  <use href="#title-letters" fill="#fff" opacity=".55" filter="url(#title-glow)"/>
  <use href="#title-letters" fill="#fff"/>
  <use href="#subtitle-letters" fill="#aaa" opacity=".45" filter="url(#subtitle-glow)"/>
  <use href="#subtitle-letters" fill="#949494"/>
  <g transform="translate(-183.375 -60.625) scale(1.25)">
    <use href="#creature-shape" fill="#f00" opacity=".65" filter="url(#red-glow)"/>
    <use href="#creature-shape" fill="#f00"/>
    <g fill="url(#face-gradient)"><rect x="217" y="233" width="5" height="5"/><rect x="233" y="233" width="5" height="5"/><rect x="217" y="243" width="21" height="11"/></g>
  </g>
  <g transform="translate(-148.7 -35.5) scale(1.1)"><ellipse cx="227" cy="339" rx="35" ry="8" fill="#d50000" opacity=".8" filter="url(#reflection-blur)"/></g>
</svg>
'''
(ROOT / "cover-rec0mp1led.svg").write_text(svg, encoding="utf-8")
