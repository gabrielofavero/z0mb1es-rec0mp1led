# Asset swap Prompt 4 — visual and release validation

## Tested inputs and build

The only supported phone input is the v1.0.0.0 XAP with SHA-256 `6b88c7fbd95444d2560b4b11e6f35d40a6c0f7abc30706155dcbccd18bc333fe`. Optional inputs were the Steam I MAED app `1800730`, version `1.0.0.0`, `Z0MB1ES.exe` SHA-256 `08d43293af258d4b30ecee748f9ee984aca097dcc3c3200b4096fa6decbe58dd`; I MAED LIVE/STFS package SHA-256 `27caf29d55a1d14089d6ec7f63d3838ae3a4b360f175c4ac93c88e3ab68947d4`; and Time Viking LIVE/STFS package SHA-256 `a0cf9187acca62058466654c53e57eb94148ad31ee2def83a8a6a37b1bf445c4`. Both 360 assemblies report version `1.0.0.0`. The full Steam texture and package fingerprints are in [the source pins](../../config/README.md). All source files and generated captures remained in ignored local storage.

The rebuilt setup used:

```powershell
./installer/build.ps1 -FfmpegPath private/tooling/ffmpeg.exe -RuntimePackSource out/deps/cache
python installer/audit-release.py --xap 'game/Z0MB1ES (on teh ph0ne) v1.0.0.0.xap'
```

The final audit reported `source_files=178 payload_files=227`, `NEGATIVE_CONTROLS passed=5`, and `AUDIT_CLEAN`. The resulting `installer/out/ZombiesPort-Setup.exe` SHA-256 is `1b05b9f1d00f280254454f61f77b84f224e0e0fb6a44f1f3d54235b3360a89b8`. The audit checks the setup payload and prohibited game material; it is not a redistribution license determination.

## Visual matrix

The local matrix runner `python private/asset-p4/run_matrix.py` used the P3 one-file install, isolated saves, and prepared source stores. It drove the phone DLL through mode menu, 300 gameplay updates, pause, resume, End Game confirmation, and return to the main menu. Every case began at the native 600×360 backbuffer and then resized during gameplay to **1280×720** and **2560×1080**, for 18 runs. Each run exited 0 with `modeVerified=True`, captured six nonblank images at their expected dimensions, and rendered both original and (where selected) imported elements. The result manifest and captures are under ignored `private/asset-p4/matrix/`.

| Phone mode | Choice | Imported draws at 1280 / 2560 | Distinct imported routes | Original fallback draws at 1280 / 2560 |
| --- | --- | ---: | ---: | ---: |
| I MAED | Original | 0 / 0 | 0 | 27,132 / 23,736 |
| I MAED | Steam | 4,142 / 5,273 | 75 | 17,603 / 18,803 |
| I MAED | 360 | 4,115 / 6,641 | 75 | 17,858 / 18,666 |
| Time Viking | Original | 0 / 0 | 0 | 38,537 / 39,217 |
| Time Viking | 360 | 22,628 / 22,580 | 204 | 15,882 / 15,850 |
| Endless | Original | 0 / 0 | 0 | 101,581 / 105,269 |
| Endless | I MAED 360 only | 17,064 / 18,885 | 67 | 90,879 / 89,853 |
| Endless | Time Viking 360 only | 66,118 / 68,867 | 123 | 33,546 / 34,072 |
| Endless | Both 360 sources | 67,619 / 68,131 | 125 | 33,575 / 33,716 |

The deterministic 600×360 art catalog captures differ from Original by 212,485 pixels for Steam I MAED, 215,309 for I MAED 360, 29,864 for Viking 360, 3,306 for I MAED-only Endless, 29,287 for Viking-only Endless, and 25,953 for mixed Endless. This confirms visible substitution and sparse fallback, while the per-source mapping and manifest probes establish element priority and bounds. Representative ultrawide gameplay images were inspected: the original and 360 Viking frames retain the same character and HUD placement; the mixed Endless capture retains the phone HUD and frame with imported world art. Frame timing and random gameplay differ between runs, so these are layout inspections, not pixel-exact game comparisons.

The catalog uploads and draws mapped sprite rectangles, including animation frames and uncommon atlas regions. The short mode probe captures a gameplay frame and pause state; it does not trigger every natural effect, death, score outcome, or Viking ending. Those visual and input checks still require a focused hands-on session.

## Failure and persistence checks

`Port.SetupHelper validate-art` rejected a copied Steam tree missing `gfx/sprites.xnb`, a one-byte-modified I MAED 360 LIVE package, and the Time Viking package supplied as I MAED. An I MAED 360 generation with a valid rehashed manifest but an out-of-bounds `contentRect` failed the preparation probe with `prepared art geometry/format mismatch`. The installed launcher treated that generation as `normal=UNAVAILABLE`, rendered 0 imported draws and 25,278 original draws, and completed the normal mode cycle. P3 separately exercised an incomplete installed atlas, repair, removal, and keeping the unaffected Viking import.

Three isolated preference fixtures began with the same existing phone `savegame`: no `assets.json`, unsupported `Format: 0`, and malformed JSON. All three launched the menu, selected the available installed 360 art by default, and left the phone save SHA-256 unchanged. The unsupported and malformed files logged `ART_PREFERENCES_UNAVAILABLE`. P3 verified a valid choice persists across restarts and a damaged import shows the repair instruction. The asset preference is stored in its own `assets.json`; art switching does not write the phone save.

The first forced post-commit failure exposed a rollback bug: Inno's `DelTree` left generated art whose absolute path reached 260 characters, then failed to rename `current.previous` back. The one-file setup now delegates rollback and successful backup removal to the shipped .NET helper, which verifies the target before deleting or restoring it. The direct helper restored the old install manifest SHA-256 `7873f66f6dac15d5e68a2a1af1874a15b5fbf46c631399198d353128a3fdefa3`. The rebuilt setup was run against a fresh fixture with a deliberately denied per-user uninstall registry write: exit code 4, Inno `Rolling back changes`, then `INSTALL_ROLLED_BACK`; the original manifest hash was restored exactly and `current.previous` was gone. This failure was induced by the restricted test environment, not by corrupting a player's registry.

The final helper also removes a newly committed install when no earlier `current` exists. A direct test deleted a copied verified install. The final one-file setup repeated the denied-registry test in a fresh phone-only destination: exit code 4, `INSTALL_ROLLED_BACK`, and neither `current` nor `current.previous` remained.

The rebuilt one-file setup installed a clean **phone-only** copy from only the pinned XAP: exit 0, shipped helper `INSTALL_VERIFIED`, no `optional-art` directory, and Inno `INSTALL_FINALIZED`. A second one-file run used `/ARTNORMAL=360` and `/ARTVIKING=360` with the two pinned packages on the same installation: exit 0, `INSTALL_VERIFIED`, Endless generation present, `INSTALL_FINALIZED`, and no remaining `current.previous`. The isolated phone `savegame` hash was unchanged. The reinstalled launcher started with `normal=360 viking=360 endless=360`, rendered the menu for 120 frames, and again left that save hash unchanged. These successful installs used the immediately preceding rebuild, whose only subsequent change was the first-install rollback edge case. The P3 one-file phone-only play run and the P4 Original mode matrix cover play without optional imports.

## Release rights and remaining gates

The package audit found no source game executable, XAP, extracted content, or prepared proprietary art in setup. Optional art is generated locally from the player's supplied sources. The dependency notices include FNA/native components, Python/PyInstaller, NumPy, and an FFmpeg notice. The bundled pinned FFmpeg 8.1.1 static executable reports `--enable-gpl --enable-version3`; its current `FFmpeg-LICENSE.txt` is only the short `ffmpeg -L` notice and does not contain the full GPLv3 text or a clear corresponding-source delivery path. [FFmpeg's legal guidance](https://ffmpeg.org/legal.html) calls for the appropriate license and corresponding source when distributing a GPL build. [PyInstaller's license documentation](https://www.pyinstaller.org/en/stable/license.html) describes its bootloader exception; [NumPy's license document](https://github.com/numpy/numpy/blob/main/doc/source/license.rst) describes bundled third-party components. The legal and dependency notice review is therefore **open before publication**, with FFmpeg source/license delivery the concrete blocker to resolve. No publication was attempted.

The automated matrix cannot establish natural death/end screens, every effect or animation in context, controller/mouse feel, or the interactive wizard on a clean Windows account without developer tools. These are release gates, not silent claims of validation. The phone-only install and scripted Original runs do establish that optional Steam/360 art is unnecessary to start and play the supported phone modes.

The focused manual pass should use the final setup hash above on a clean Windows account: select the XAP in the wizard, observe clear validation for a wrong source, and install first with no optional art. Play I MAED, Viking, and Endless through visible menu input, pause/resume, a natural death or end state, and representative effects/animation; then import Steam I MAED, I MAED 360 (as a replacement), and Viking 360, switching each eligible ASSETS row and checking the same paths at native and scaled window sizes. Inspect sprite edges, grass/moon frames, background vignette, HUD alignment, controls, sound, and save/achievement continuity. Repeat Endless with each single 360 source and both together. Sign off only after the captures and behavior are satisfactory and the dependency redistribution package is resolved.

The FFmpeg finding above records the P4 package as built at that time. The [redistribution follow-up](ffmpeg-redistribution.md) replaces that converter and setup executable with an LGPL build and matching source archive; its new hashes supersede the P4 setup hash for future checks.
