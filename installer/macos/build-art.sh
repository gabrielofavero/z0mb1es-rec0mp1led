#!/bin/sh
# Build the optional art importer with the pinned Python requirements on macOS.
set -eu
repo=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
parser="$repo/../xna-to-fna-asset-extractor/xnb_parse"
test -d "$parser/xnb_parse" || { echo 'Missing sibling xnb_parse checkout' >&2; exit 1; }
cd "$repo"
"${PYTHON:-python3}" -m PyInstaller --noconfirm --clean --onefile --name Port.AssetImporter \
  --distpath "$repo/private/macos-inputs" --workpath "$repo/private/macos-art/build" \
  --specpath "$repo/private/macos-art" \
  --paths "$parser" --collect-submodules xnb_parse \
  --add-data "$repo/config/supported-steam-versions.json:config" \
  --add-data "$repo/config/supported-360-versions.json:config" \
  --add-data "$repo/docs/records/asset-swap-inventory.json:docs/records" \
  --add-data "$repo/docs/records/zombies-360-inventory.json:docs/records" \
  --add-data "$repo/docs/records/viking-360-inventory.json:docs/records" \
  --add-data "$repo/docs/records/asset-swap-mappings:docs/records/asset-swap-mappings" \
  "$repo/tools/prepare_optional_art.py"
shasum -a 256 "$repo/private/macos-inputs/Port.AssetImporter"
