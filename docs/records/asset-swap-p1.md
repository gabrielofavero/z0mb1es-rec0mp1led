# Asset swap Prompt 1 — local preparation

`tools/prepare_optional_art.py` prepares player-owned art into ignored `private/asset-swap/prepared/`. It accepts the pinned Steam I MAED installation or either pinned 360 `LIVE` package. It requires Python with Pillow and NumPy, the sibling `xna-to-fna-asset-extractor/xnb_parse` checkout, and, for 360 sources, the built `tools/decode_360_xnb` helper plus `out/deps/managed/FNA.dll`. This is a local preparation tool; setup remains Prompt 3 work, and the phone renderer is covered by [Prompt 2](asset-swap-p2.md).

```powershell
python tools/prepare_optional_art.py prepare --kind i-maed-steam --source 'D:/Program Files (x86)/Steam/steamapps/common/IMAGWZII'
python tools/prepare_optional_art.py prepare --kind i-maed-360 --source game/zombies-360
python tools/prepare_optional_art.py prepare --kind time-viking-360 --source game/time-vicking-360
python tools/prepare_optional_art.py probe
```

`--phone-root` defaults to the ignored `private/original-xap` extraction, with `ZombiesWP7.dll` and `Content/`. `--store` can select another path under ignored `private/`. The phone DLL and every referenced Original texture must match the P0 hashes. The 360 importer checks the complete package SHA-256, pinned title IDs, STFS block hashes and file inventory, then extracts **only mapped XNB art** into temporary private storage. Steam checks the pinned executable fingerprint and all nine required art files; it never runs or prepares the executable. Decoded XNB type, size, format, mip count and hash are checked against P0 inventory.

For 360 preparation, build the local decoder with `dotnet build tools/decode_360_xnb/decode_360_xnb.csproj -c Release --source out/deps/cache -p:NuGetAudit=false`; the existing local `out/deps/managed/FNA.dll` must also be present.

Each source has an immutable, content-addressed generation and a small atomically replaced `current.json` pointer. Failed validation never changes the pointer. The versioned generation manifest contains source fingerprints, every logical element's Original fallback, imported source rectangle, logical phone size, output rectangle, PNG hash and sampling instructions. Candidate crops are losslessly written as RGBA PNG with two pixels of edge extension and an explicit interior rectangle; uncertain/missing artwork routes to Original. No proprietary pixels are tracked or included in setup. `endless-360/current.json` selects each logical element from I MAED 360, then Time Viking 360, then Original. Steam alone cannot enable Endless 360 art.

Local results on the pinned sources:

| Source | Mapped elements | Prepared candidate crops | Reproducible generation SHA-256 |
| --- | ---: | ---: | --- |
| Steam I MAED | 175 | 120 | `1abfc14233852da06c312fea9945de97959d73e6c4322c8af99911ddec81f9ec` |
| I MAED 360 | 238 | 121 | `74b3b76ef151cfc69fb331876c226c0ac6ae8b42dafca881bf41c1832b2192af` |
| Time Viking 360 | 408 | 270 | `42d8abbfd8ee4b60f6b9a829cde483401170a17b7ca59f3c9b69fd8cd9d05dbf` |

`probe` decoded every prepared output, checked its hash and content rectangle, checked each source rectangle against the decoded texture dimensions, and verified every pinned Original fallback. The combined Endless route covered 230 elements and selected imported art for 171. Repeating Steam produced the same generation hash. Independent I MAED 360 and Time Viking 360 preparations produced the same respective generation hashes; each produced a valid Endless manifest with the other 360 source absent. An incomplete Steam tree with the correct executable and the two wrong-title 360 inputs each failed with exit 1. All three existing `current.json` hashes were unchanged after those failures. The source files, phone content and working install were not modified.

Prompt 2 revised both I MAED generations to bake a measured phone vignette into the eight imported backgrounds. Their manifests record `phone-vignette-v1` and the mask hash; the candidate counts are unchanged. [The P2 evidence](asset-swap-p2.md) records the comparison and runtime results. The local tool depends on developer-side Python and the sibling parser; Prompt 3 must package or port this preparation capability for the offline installer without shipping original assets.
