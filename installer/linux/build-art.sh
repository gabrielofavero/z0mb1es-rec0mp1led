#!/bin/sh
# Run on Linux x64 with Python 3.8, numpy 1.24.4, Pillow 10.4.0,
# PyInstaller 6.10.0 and the sibling xnb_parse checkout available.
set -eu
repo=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
parser="$repo/../xna-to-fna-asset-extractor/xnb_parse"
[ -d "$parser/xnb_parse" ] || { echo 'Missing sibling xnb_parse checkout' >&2; exit 1; }
cd "$repo"
"${PYTHON:-python3}" -m PyInstaller --noconfirm --clean --onefile --name Port.AssetImporter \
  --distpath "$repo/private/linux-art/dist" --workpath "$repo/private/linux-art/build" \
  --specpath "$repo/private/linux-art" \
  --paths "$parser" --collect-submodules xnb_parse \
  --add-data "$repo/config/supported-steam-versions.json:config" \
  --add-data "$repo/config/supported-360-versions.json:config" \
  --add-data "$repo/docs/records/asset-swap-inventory.json:docs/records" \
  --add-data "$repo/docs/records/zombies-360-inventory.json:docs/records" \
  --add-data "$repo/docs/records/viking-360-inventory.json:docs/records" \
  --add-data "$repo/docs/records/asset-swap-mappings:docs/records/asset-swap-mappings" \
  "$repo/tools/prepare_optional_art.py"
