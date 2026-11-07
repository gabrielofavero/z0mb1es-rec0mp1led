# Asset swap Prompt 2 — phone renderer and visual probes

## Renderer

The guarded phone patch replaces 63 `ContentManager.Load<Texture2D>` calls and 170 `SpriteBatch.Draw` calls in the pinned phone DLL with `Port.Hooks.AssetDraw` wrappers. The patch checks the original method bodies, call signatures, exact call counts, and postconditions. The original `ZombiesWP7.Game1` remains the running game. The wrappers identify each phone texture by its content path and each artwork element by its phone source rectangle. They load only the selected mode's prepared crops, verify generation manifests and every imported PNG SHA-256 before use, and leave unmapped elements on their original phone texture. A missing or corrupt selected generation logs `ART_UNAVAILABLE` and renders that mode with phone art.

Prepared crops may be 2×, the same size, or smaller atlas pieces. The wrappers convert source rectangles, scale and origins so the destination remains in the original 600×360 logical coordinates. Destination rectangles, rotation, color, depth, and sprite effects remain the phone game's values. A dynamic I MAED background subrectangle is converted within its mapped full background; an out-of-bounds subrectangle stays on the phone texture. The prepared PNGs have two pixels of extended edge color, retain alpha, and are sampled through the game's existing SpriteBatch state. PNG uploads have one texture level, so no mipmap neighbor colors enter the crops. The hook does not change input, game state, collision geometry, timing, or saves.

Time Viking's four `grass` face icons route individually from their shifted 360 rectangles without resizing. `moon1` and `moon2` route from the 360 atlas center, and `moon3` routes as left and right halves. Map cells and animation frames also route individually. Endless uses its P1 priority manifest for each element, so I MAED 360 art wins over Viking 360 where both map and unresolved elements remain Original.

## Background treatment

For the eight I MAED backgrounds, `tools/prepare_optional_art.py` fits one 640×360 luminance mask from all pinned phone/source pairs and applies it to the imported 1280×720 RGB before cropping; alpha is retained. Each prepared background records `phone-vignette-v1` and the mask SHA-256. Downsampling the eight treated backgrounds to phone size yielded mean RGB MAE **1.335** for Steam and **1.723** for I MAED 360; the largest sampled grid MAE was **3.654** and **3.822**, respectively. This restores the phone's darkened edges while retaining the source detail. The treatment changed the Steam and I MAED 360 generation hashes; see [P1](asset-swap-p1.md). Source artwork and generated pixels remain ignored.

## Reproduction and results

```powershell
python tools/check_asset_swap_mapping.py
python tools/prepare_optional_art.py probe
./scripts/build-offline.ps1 -Configuration Release
./scripts/build-offline.ps1 -Configuration Debug
dotnet tools/prepare-cli/bin/Release/net10.0/prepare.dll hook --xap 'game/Z0MB1ES (on teh ph0ne) v1.0.0.0.xap' --dll private/original-xap/ZombiesWP7.dll --prepared private/prepared/prompt8-fresh-base/ZombiesWP7.dll --hooks src/Port.Hooks/bin/Release/net10.0/Port.Hooks.dll --output private/prepared/asset-p2-release
dotnet tools/prepare-cli/bin/Debug/net10.0/prepare.dll hook --xap 'game/Z0MB1ES (on teh ph0ne) v1.0.0.0.xap' --dll private/original-xap/ZombiesWP7.dll --prepared private/prepared/prompt8-fresh-base/ZombiesWP7.dll --hooks src/Port.Hooks/bin/Debug/net10.0/Port.Hooks.dll --output private/prepared/asset-p2-debug-final
./scripts/stage-local-run.ps1 -PreparedDirectory private/prepared/asset-p2-release -ContentDirectory private/content-prepared/audio-portability-a -OutputDirectory private/run/asset-p2-release
dotnet private/run/asset-p2-release/Port.Launcher.dll --run private/run/asset-p2-release/ZombiesWP7.dll --content private/run/asset-p2-release --windowed --save-dir private/run/asset-p2-release/probe-saves --probe-mode normal --art-store private/asset-swap/prepared --art-normal steam
```

The same launcher flags select `--art-normal 360`, `--art-viking 360`, and `--art-endless 360`; omit the art flags for Original. `--probe-mode normal|viking|endless` scripts menu entry, gameplay, pause, resume, end confirmation, and return to the main menu with an isolated save. `--probe-resize 1280x720` or `1280x540` changes the backbuffer during gameplay. The art catalog diagnostic uploads and draws every distinct mapped phone rectangle, including frames that a short play run may not naturally reach. All generated images, logs, and saves stay under ignored `private/`.

| Selected art | Distinct GPU catalog routes | Focused crop probe | Mode cycle | Resize capture |
| --- | ---: | --- | --- | --- |
| Steam I MAED | 75 | Background subregion + sprite imported | Passed | 1280×720 passed |
| I MAED 360 | 75 | Background subregion + sprite imported | Passed | Not separately repeated |
| Time Viking 360 | 204 | Four grass faces + four moon regions imported | Passed | 1280×540 passed |
| Endless mixed 360 | 125 | I MAED sprite + Viking map imported; monster Original | Passed | 1280×540 passed |
| Original, all three modes | 0 | All corresponding probe items Original | Passed | Endless 1280×540 passed |

Each catalog asserted that all its mapped routes were actually drawn. At 600×360, focused imported-versus-Original PNGs had RGB MAE 3.189 for Steam normal, 3.553 for I MAED 360 normal, 2.268 for Viking, and 0.189 for mixed Endless. The images and native/scaled gameplay captures were inspected for shifted positions, clipped frames, alpha fringes, and atlas neighbors. None were observed in the inspected samples. The black world boundary visible at the left of an Endless 1280×540 capture also appeared with Original art. Missing and deliberately corrupted prepared art each logged `ART_UNAVAILABLE`, completed the normal mode cycle, and reported zero imported draws. The final guarded Debug and Release hooks both produced phone DLL SHA-256 `1895480b74feab01c9bdb058f26508a895bb98a2628924a3e2f999757ca43c80`.

These are scripted render and mode probes. They confirm the phone display path still resizes and that the asset hook leaves input code untouched; a hands-on pointer alignment pass across display modes remains in Prompt 4. The installer and in-game ASSETS Settings page remain Prompt 3 work.
