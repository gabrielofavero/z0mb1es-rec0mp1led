# Asset swap Prompt 0: visual inventory and mapping

**Result (2026-11-07):** The pinned phone DLL, both pinned `LIVE` packages, and the local Steam I MAED 1.0.0.0 build have metadata-only texture inventories and conservative per-element mappings. These mappings are evidence for the importer and renderer prompts; no optional art is installed or selectable yet. All unresolved, uncertain, unrelated, and dynamically selected elements retain the phone Original.

## Exact sources and method

| Source | Identity verified in this pass | Decoding |
| --- | --- | --- |
| Phone XAP | `ZombiesWP7.dll` SHA-256 `7b819cb562826ae4d2592ff741877e76da0e57bfbe48192aabb79319fdb36823`; supported XAP in [supported versions](../../config/supported-versions.json) | ILSpy CLI 11.1.0.9782 for private C# inspection; `xnb_parse.XNBReader` for texture pixels |
| I MAED 360 | `game/zombies-360` SHA-256 `27caf29d55a1d14089d6ec7f63d3838ae3a4b360f175c4ac93c88e3ab68947d4`; title and entry hashes in the [package inventory](zombies-360-inventory.json) | STFS block-verified extraction, authored FNA LZX decoder, then `xnb_parse` |
| Time Viking 360 | `game/time-vicking-360` SHA-256 `a0cf9187acca62058466654c53e57eb94148ad31ee2def83a8a6a37b1bf445c4`; title and entry hashes in the [package inventory](viking-360-inventory.json) | Same path; `maps` and `maps2` decode to 2048×2048, without a 2× size change |
| Steam I MAED | Steam app ID `1800730`, `Z0MB1ES.exe` file version 1.0.0.0, 148,480 bytes, SHA-256 `08d43293af258d4b30ecee748f9ee984aca097dcc3c3200b4096fa6decbe58dd`; all nine required art file sizes, hashes, types, and dimensions are in [supported Steam versions](../../config/supported-steam-versions.json) | Uncompressed PC `XNBw` `Texture2D Color` through `xnb_parse`; `fx/thresh.xnb` has no XNB header and is excluded |

The two package hashes and the ten Steam file hashes were recomputed from the local inputs and matched the pinned metadata. The authored decoder rebuilt with zero warnings or errors. It decoded 11 Zombies and 23 Viking XNB files into ignored storage; SHA-256 of all 34 freshly decoded files matched the earlier decoded copies byte for byte. A negative check targeting `generated/` failed before creating that directory, as required by the decoder's private-output guard. The package entry SHA-256 in each mapping refers to the **compressed original entry**; `decodedSha256` refers to the LZX-expanded XNB. The Steam mapping uses the raw Steam file hash for both fields.

Reproduction from the repository root, using fresh ignored output directories:

```powershell
python tools/inspect_stfs.py game/zombies-360 --extract-private private/asset-swap/repro-zombies --output generated/asset-swap-zombies-inventory.json
python tools/inspect_stfs.py game/time-vicking-360 --extract-private private/asset-swap/repro-viking --output generated/asset-swap-viking-inventory.json
dotnet build tools/decode_360_xnb/decode_360_xnb.csproj -c Release --source out/deps/cache -p:NuGetAudit=false -v:q
dotnet tools/decode_360_xnb/bin/Release/net10.0/decode_360_xnb.dll out/deps/managed/FNA.dll private/asset-swap/repro-zombies/584E07D1/Content private/asset-swap/redecoded-zombies
dotnet tools/decode_360_xnb/bin/Release/net10.0/decode_360_xnb.dll out/deps/managed/FNA.dll private/asset-swap/repro-viking/584E07D1/Content private/asset-swap/redecoded-viking
private/inspect-360/tools/ilspycmd.exe -p -o private/asset-swap/phone-source private/original-xap/ZombiesWP7.dll
python tools/build_asset_swap_inventory.py
python tools/build_asset_swap_mapping.py
python tools/check_asset_swap_mapping.py
```

The actual run reused the previously block-verified extraction in `private/asset-comparison/` and used fresh `private/asset-swap/redecoded-*` destinations. The decoder's output directory must not exist before it runs. The mapping scripts use the ignored phone extraction, the local Steam installation, and the sibling `xnb_parse` library. Their contact sheets are written only under ignored `private/asset-swap/contact/`.

## Phone draw ownership and coverage

The [inventory](asset-swap-inventory.json) records 68 `Texture2D`/`XTexture` load sites, 170 direct `SpriteBatch.Draw` call sites, 126 `.zsx` sprite cells, and 113 bounded possible frames from twelve dynamic zombie sprite formulas. Of the draw calls, 97 have a literal phone rectangle and 73 retain the exact runtime rectangle expression. The call-site record includes its decompiled source location, texture expression, mode, logical ID, and Original fallback. The source tree and any pixels stay in ignored `private/`.

`ZombieGame.SharedLoad` loads `gfx_2/zombie/sprites` in both I MAED and Endless. Its normal branch loads the eight zombie backgrounds; the Endless branch does not. Endless additionally calls `MapMan.ReadMap`, loading phone `gfx/maps` and `gfx/maps2`. Therefore the same zombie sprite name can be shared, but the backgrounds are **I MAED only** and the map cells are **Endless only**. `MapEdit.map.Map.Draw` selects those map cells from the `.zsx` definitions. The shared phone menu, achievement, custom-image, touch, scene, font, and generated render-target draws remain Original.

Stable IDs use `draw/<decompiled type>/<call ordinal>`, `cell/<sheet>/<cell index>`, `frame/<draw ID>/<possible frame>`, and named `background/`, `grass/`, and `moon/` elements. They are stable for the pinned DLL and ILSpy version; a changed phone DLL requires a new inventory version. A dynamic draw retains its formula and Original fallback even when its bounded frames have separate candidates. Bounded possible frames do not assert that every index is reachable in gameplay.

## Mapping decisions

The version 1 [mapping manifests](asset-swap-mappings/) contain source and phone hashes, exact rectangles where known, expected physical output size and logical phone size, comparison measures, and an explicit Original fallback. `candidate` means a decoded crop passed the conservative static comparison and can be prepared in Prompt 1; it does **not** mean runtime compatibility has been established. `uncertain` and `original` remain phone-only. The independent [mapping checker](../../tools/check_asset_swap_mapping.py) validated fallback coverage, source hashes, rectangle bounds, Steam pins, and Endless priority: `MAPPING_CHECK_OK drawSites=170 spriteCells=126 expandedFrames=113 sourceRectangles=557 endlessElements=230`.

| Element group | Source rectangle and treatment | Decision |
| --- | --- | --- |
| I MAED backgrounds | Each of the eight phone 640×360 whole images corresponds to its 360/Steam 1280×720 image. Keep logical 640×360 geometry. | Candidate metadata for each source. The phone vignette is baked in; keep Original until the renderer has a verified edge treatment. |
| I MAED sprites | Static draw crops and all 113 bounded possible dynamic frames were compared individually against each source at 2× coordinates. | 88 dynamic frames are candidates for each I MAED source; 25 static draw crops for 360 and 24 for Steam are candidates. Eight dynamic and three static crops are uncertain, and 17 possible frames are empty phone cells. The full atlas is never selected as one element. |
| Time Viking `robot`, `scrap`, `viking` | 67 phone `.zsx` cells use half-size phone coordinates; the 360 source uses their raw `.zsx` rectangles. | 54 individual cells are candidates; 13 remain Original because alpha or visible structure differs. Origins and flags are recorded for later repacking. |
| Time Viking `grass` | Grass strips and the tree use 2× source coordinates. The four face icons instead move from phone `[448,128+64i,64,64]` to 360 `[495,272+64i,64,64]`, `i=0..3`; their physical size stays 64×64. | All four icons are mapped separately. Each has alpha overlap 1.0 and visible RGB mean error below 1/255 after the shifted crop. The 360 extra raptor face is unrelated. |
| Time Viking moon | Phone stages 1 and 2 map to 360 `[512,6,1024,384]` and `[510,714,1024,384]`. Phone stage 3 is drawn in two halves, mapping to `[504,1416,512,384]` and `[1016,1416,512,384]`. | Four crop candidates; correlation with phone crops is 0.943–0.988. The 2048×704 source rows are never substituted whole. Preserve the game's red hit flash, alpha, rotation, and origins. |
| Endless `maps`/`maps2` | Phone and Viking 360 sheets are both 2048×2048. The 59 `.zsx` cell rectangles are used at the same physical coordinates. | 58 cells are candidates; one phone cell is empty and stays Original. This is a same-size quality source, not higher resolution. |
| Endless zombie sprites | [Endless mapping](asset-swap-mappings/endless-360.json) tries matching I MAED 360 crop first, then a matching Viking 360 `gfx/zombie/sprites` crop, then phone Original. | 230 recorded draw/cell/frame elements; 171 have at least one candidate. The Steam source alone never enables Endless 360. Uncertain crops do not enter the priority list. |

The contact sheets include side-by-side phone/source crops for every candidate and uncertain static cell, plus a full phone/360/Steam zombie sprite grid. Visual inspection found the I MAED atlas composition mostly aligned, with different Steam monster art in some crops. The raw comparison numbers are stored per element. Alpha intersection-over-union below 0.8, or luminance correlation below 0.75 with visible mean error at least 1, marks a crop uncertain. These thresholds are triage, not proof of game rendering. Blank phone cells stay Original. The `grass` icons were additionally searched within the 360 sheet and aligned by alpha silhouette; similarly named whole-sheet substitution would have selected the wrong region.

The Viking 360 `raptor`, `controller`, `covers`, and the extra raptor face do not replace phone gameplay elements. Steam `crowdcontrol_logo`, `omni_logo`, `ska_logo`, `spritesPC`, and its non-XNB effect have no verified phone target. I MAED 360 has no `maps`/`maps2`; the Viking sheets supply only matching Endless map cells. Phone scenes, touch controls, achievements, fonts, music, sound, and data are outside this visual mapping.

## Effects and unresolved work

The phone `MapMan.DrawJustMap` draws backgrounds through `BlendState.Opaque`, brightness tinting, and a viewport-dependent source rectangle; `DrawMapTrans` uses `BlendState.NonPremultiplied` for crossfades. Space/grid overlays and many particles use additive blending. No separate phone vignette pass appears in those draw paths, consistent with the baked edge darkness observed in the phone images. The mapping records the plain source crop and Original fallback; it does not bake a guessed mask or alter game effects. Prompt 2 must preserve those blend/tint behaviors and establish a tested vignette treatment before offering imported backgrounds.

Other dynamic rectangles, character origins, atlas filtering, and frame timing are recorded as expressions or `.zsx` metadata, not asserted to be renderer-compatible. Prompt 1 must prepare and hash actual outputs from user-owned inputs; Prompt 2 must prove rendering and input alignment at the phone's 600×360 logical viewport; Prompt 3 must add optional setup and Settings choices. No proprietary texture, source executable, extracted package, contact sheet, or prepared atlas is tracked.
