# Port artwork

Only artwork created for this port belongs here. Cover art was reconstructed based on original game icon.

`icon/icon.svg` is the square red creature source. `icon/icon-gray.svg` is its gray uninstaller variant. Each has a 256px PNG preview and a seven-size ICO (16, 24, 32, 48, 64, 128, and 256px). Use `icon/icon.ico` for the game executable and setup icon; use `icon/icon-gray.ico` for the uninstaller display icon.

`cover/cover.svg` is the 450×450 source and `cover/cover.png` is its square raster export. `installer/wizard-top.svg` tightens the square view around the cover's visible art, and `installer/wizard-top.png` is its 159×159 header badge. `installer/wizard-side.svg` rearranges the cover's title, subtitle, creature, and reflection for the tall side panel while retaining every visible element. `installer/wizard-large-*.bmp` contains the seven side-image sizes used by the Blitz installer: 202×386, 269×515, 336×643, 403×772, 430×824, 498×953, and 534×1022. Each is a 24-bit BMP on black; the side creature is scaled separately from the title to balance the panel.

Run `python scripts/build-installer-art.py` from the repository root to regenerate the raster files from the SVGs. Use `--icons-only` for icon changes, `--side-only` for the tall side images, or `--wizard-only` for the top and side images together. This developer-only command needs Pillow, NumPy, and local Edge or Chrome. Check in the generated files with the installer artwork so the installer build and the player's machine do not need those tools.
