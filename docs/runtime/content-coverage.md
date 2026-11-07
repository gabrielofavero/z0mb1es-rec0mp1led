# Prompt 7 content coverage

This report covers the exact supported XAP only. The original DLL's inventory has 66 `ContentManager.Load` calls in 12 methods and 155 candidate asset strings. The path scan found 74 distinct path-like literals. Original-runtime behavior has not been observed; dynamic names are identified from IL and package structure.

| Original package input | Direct FNA result | Prepared result |
| --- | --- | --- |
| 68 texture XNBs (`XNBm` v5) | All loaded with original dimensions | All 68 original files copied byte for byte |
| 19 sound-effect XNBs (`XNBm` v5) | All loaded | All 19 copied byte for byte |
| `Content/Arial.xnb` SpriteFont | Loaded, 96 glyphs and line spacing 22 | Copied byte for byte and loaded |
| Three Song XNBs and three WMA companions | Song objects loaded, but playback duration became **0 seconds** for all three WMA files | XNBs unchanged; three same-name Ogg companions decoded and played with nonzero duration |
| Nine files in `chardef/`, `gfx/`, `gfx_2/`, `maps/data/`, and `scene/data/` | Read and hashed by inventory; these are not XNBs | Copied byte for byte |

The prepared content adds five byte-identical XNB aliases under `Content/gfx/scenes/` for `diamond`, `face1`, `face2`, `heart`, and `viking`. `SceneMgr.ReadTextures` builds `gfx/scenes/<keyframe texture>`, and the package stores those five matching textures under `Content/gfx_2/scenes/`. The scene data contains these names. This alias is supported by IL and package evidence; whether that scene path is reached in play remains for Prompt 8. No texture is rescaled or converted.

After preparation, the probe loaded all **96 XNBs** (91 original plus five aliases), including the font, and reported zero failures. It hashed the three Ogg companions and nine custom data files. The 100 original non-WMA files in the prepared tree have the same hashes and texture dimensions as the source. The original WMA files stay in the untouched extraction.

| Music companion | Source WMA duration from ffprobe | Prepared Ogg duration from ffprobe | FNA duration after playback starts |
| --- | ---: | ---: | ---: |
| `endless` | 302.045 s | 302.036 s | 302.091 s |
| `epicopus` | 817.945 s | 817.949 s | 817.992 s |
| `timeviking` | 907.667 s | 907.671 s | 907.713 s |

The Ogg durations differ from source container durations by less than 0.01 seconds. FNA reports roughly 0.04–0.05 seconds more after starting playback, consistent with codec padding; gameplay timing still needs a running-game check. The original DLL queries `MediaPlayer.State` and `PlayPosition`, but the static call inventory has no `Song.Duration` getter call. The Song XNB metadata is preserved despite its misleading pre-play duration.

Of the 74 path-like literals, 72 resolve in prepared content. `gfx_2/moon` is a dynamic stem with packaged `moon1`, `moon2`, and `moon3` XNBs. The remaining `data/` literal is used by character write paths and is not a packaged content directory; save-path behavior belongs to Prompt 10. The `sfx/fx/` prefix has 19 packaged effects, and the `gfx/scenes/` prefix resolves through the five guarded aliases. Full path and per-file statuses are in ignored `private/content-probe/final.json`.

## Reproduce locally

Run from the repository root after the offline build and verified XAP extraction:

```powershell
./scripts/build-offline.ps1
dotnet tools/content-probe/bin/Release/net10.0/content-probe.dll --root private/preparation-net10/original-xap --report private/content-probe/direct.json --evidence generated/evidence-from-xap.json
$ffmpeg = '<absolute path to pinned ffmpeg.exe>'
dotnet tools/content-prepare/bin/Release/net10.0/content-prepare.dll --xap 'game/Z0MB1ES (on teh ph0ne) v1.0.0.0.xap' --preparation private/preparation-net10 --ffmpeg $ffmpeg --output private/content-prepared/new-run
dotnet tools/content-probe/bin/Release/net10.0/content-probe.dll --root private/content-prepared/new-run --report private/content-probe/prepared.json --evidence generated/evidence-from-xap.json
```

The direct probe exits 1 because all three WMA playback attempts yield zero decoded duration. The prepared probe exits 0. `content-prepare` verifies the supported XAP, extraction provenance, every source content hash, and the FFmpeg executable hash before creating output. It writes a new staged directory, a deterministic manifest, and no source files in the tracked tree. A fresh preparation with the current LGPL converter produced manifest SHA-256 `f5d9d1c0ca0d91991d5f900441a435c35dd2049105268ca4a11183118f02a1e9`; FNA loaded all 96 prepared XNBs with zero failures and music durations of 302.091, 817.992, and 907.713 seconds. An earlier two-run determinism check with the previous converter yielded manifest SHA-256 `239e04ae6cadf64b937bb1e7bc2cd5eaa511a7998096fa7d6dde33c25c60ab77` in both runs. A wrong FFmpeg executable fails before output is created.

The Windows conversion dependency is the project-built FFmpeg 8.1.1 executable pinned in [`config/content-tools.json`](../../config/content-tools.json) by SHA-256. Build it from the exact FFmpeg, libogg, and libvorbis sources using the [redistribution recipe](../../installer/ffmpeg/README.md), then point `$ffmpeg` at `private/tooling/ffmpeg.exe`. The SHA-256 check rejects another Windows build. The converter reports LGPL 2.1 or later and is not checked in; the installer ships its full license notices and is paired with a matching-source ZIP. A non-Windows development host may supply a local converter with `--ffmpeg-sha256`; no Linux or macOS release converter is pinned yet. The [audio portability audit](audio-portability.md) records the format checks and platform limits.
