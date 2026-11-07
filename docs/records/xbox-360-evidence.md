# Xbox 360 package research: cancelled hot swap Prompt 0

**Status:** Static inventory and managed-code decompilation completed for the two local packages on 2026-11-07. The [game hot swap plan](../plans/cancelled/2026-11-07-game-hot-swap.md) was subsequently cancelled. Neither title has been booted by this port. Package and texture observations remain inputs to the [asset swap plan](../plans/in-progress/2026-11-07-asset-swap.md); the code rewrite, runtime, and achievement proposals below are archived reasoning, not current work. The fingerprint file is metadata evidence; no current importer consumes it.

Evidence labels in this record: **package** means bytes read from the supplied package; **IL** means the original managed assembly or its decompiled view; **inference** means a proposed desktop adaptation that has not run. The decompiler output is a navigation aid; method IL and hashes are the authoritative static evidence. No original Xbox 360 runtime observation was available.

## Reproduce the private inventory

The input files are ignored `game/zombies-360` and `game/time-vicking-360`. The authored inspector is read-only by default. It supports the read-only, contiguous, one-file-table-block STFS layout of these inputs, verifies every allocated data block against its STFS SHA-1 entry, and rejects a different layout. It does **not** authenticate the package signature. Metadata-only snapshots with every file path, byte count, SHA-256, and XNB header are tracked as [Zombies inventory](zombies-360-inventory.json) and [Viking inventory](viking-360-inventory.json); no extracted game bytes are tracked. The same reports can be regenerated under ignored `generated/`.

```powershell
python tools/inspect_stfs.py game/zombies-360 --output generated/zombies-360-inventory.json
python tools/inspect_stfs.py game/time-vicking-360 --output generated/viking-360-inventory.json
python tools/inspect_stfs.py game/zombies-360 --extract-private private/inspect-360/zombies-360 --output generated/zombies-360-inventory.json
python tools/inspect_stfs.py game/time-vicking-360 --extract-private private/inspect-360/viking-360 --output generated/viking-360-inventory.json
dotnet build tools/inspect_360_assembly/inspect_360_assembly.csproj -c Release --source out/deps/cache -p:NuGetAudit=false -v:q
dotnet tools/inspect_360_assembly/bin/Release/net10.0/inspect_360_assembly.dll private/inspect-360/zombies-360/584E07D1/IMAK3Z0MB1EGAEM.exe --output generated/zombies-360-assembly.json
dotnet tools/inspect_360_assembly/bin/Release/net10.0/inspect_360_assembly.dll private/inspect-360/viking-360/584E07D1/Viking_x86.exe --output generated/viking-360-assembly.json
private/inspect-360/tools/ilspycmd.exe -p -o private/inspect-360/zombies-source private/inspect-360/zombies-360/584E07D1/IMAK3Z0MB1EGAEM.exe
private/inspect-360/tools/ilspycmd.exe -p -o private/inspect-360/viking-source private/inspect-360/viking-360/584E07D1/Viking_x86.exe
```

The private extraction/decompilation commands require fresh destination directories. The inspected assemblies were decompiled with ILSpy CLI 11.1.0.9782. Zombies decompilation emitted some unresolved XNA 3 result-type comments; use IL for exact signatures and behavior before writing a patch. The 360 executable and source trees remain under ignored `private/`; the JSON reports remain under ignored `generated/`.

## Package and assembly identity

| Field | I MAED A GAM3 W1TH Z0MB1ES!!!1 | TIME VIKING!!!!!ANDSPACERAPTOR |
| --- | --- | --- |
| Input and SHA-256 | `zombies-360`, 13,324,288 bytes; `27caf29d55a1d14089d6ec7f63d3838ae3a4b360f175c4ac93c88e3ab68947d4` | `time-vicking-360`, 39,981,056 bytes; `a0cf9187acca62058466654c53e57eb94148ad31ee2def83a8a6a37b1bf445c4` |
| Package | `LIVE` STFS, metadata version 2, content type `0x2`, 3,222 verified data blocks | Same layout, 9,691 verified data blocks |
| File table | 26 entries: 5 directories, 21 files | 47 entries: 8 directories, 39 files |
| `GameInfo.xml` title ID | `0x585502a6`; virtual title ID `f030e2bf95a04d548ef88c2263b78f0c` | `0x58550e08`; virtual title ID `c07223621f1b4663a33960bd13ce8363` |
| Managed executable | `584E07D1/IMAK3Z0MB1EGAEM.exe`, 85,504 bytes; SHA-256 `7ee601db5529b84a5ec3102036e93778973f9c27f748c50b2cc7260c614205d9` | `584E07D1/Viking_x86.exe`, 198,144 bytes; SHA-256 `9c79105aec24cad5f8a962cf7ba8d75efea4e23fc4ddef2b8c348bfcdae0d1d2` |
| Assembly | `IMAK3Z0MB1EGAEM` 1.0.0.0, MVID `c2aed8a6-5191-4d1c-89e6-3f20414a5456`, 64 types/228 methods | `Viking_x86` 1.0.0.0, MVID `44105f14-caa2-42dc-802b-f6a385d2adca`, 146 types/644 methods |
| Referenced runtime | `mscorlib` 3.5.0.0; XNA Game/Framework 3.0.0.0 | `mscorlib` 2.0.5.0; XNA Game/Framework/Graphics/Storage/Xact/GamerServices 4.0.0.0 |
| `Content/` | 11 `XNBx` v3, 2 XWB, 2 XSB, 1 XGS | 23 `XNBx` v5, 1 WMA, 1 XWB, 1 XSB, 1 XGS; seven `.zsx` files outside `Content/` |

Both package headers report execution/base version zero, and both assemblies report 1.0.0.0. Neither is a sufficient version key. Their common outer STFS container title ID `0x584e07d2` also does not identify the game. Validate the full package hash, title ID, executable hash, MVID, and any method being rewritten. Reject unknown packages with a specific unsupported title/version/format result before altering an installation.

## Original call paths and desktop preparation map

| Concern | Zombies 360: original evidence | Viking 360: original evidence | Preparation implication (inference) |
| --- | --- | --- | --- |
| Entry and game loop | `Program.Main` runs `Game1`. `Game1.Initialize` creates managers at 1280×720; `Update` dispatches Menu/Playing; `Draw` uses `SpriteBatch`, map, HUD, and effect. **IL** | `Program.Main` runs `Game1`. `LoadContent` calls `ThreadedMainLoad`, which creates `VikingGame`; `Update` dispatches Loading/VikingMenu/VikingPlaying and `Draw` calls `VikingGame.Draw`, initially at 1280×720. **IL** | Each needs its own assembly ABI rewrite and isolated content/runtime directory. The phone's `ZombiesWP7.dll` patch signatures do not apply. |
| Menu and game selection | `menu.Menu.Update` accepts gamepad input from up to four player slots. **IL** | `VikingGame.Update` hosts its Viking menu and play states; only `VikingGame` is constructed by this entry point. **IL** | The phone menu should remain the host; dispatch at the selected game start boundary. Do not treat the extra zombie/endless types embedded in `Viking_x86.exe` as available modes. |
| Pause | `Hero.Update` uses controller Start to invoke `TimeMgr.Pause`; paused `Game1.Update` updates only the pause menu. `TimeMgr.Pause` pauses music and tracks pause ticks; pause menu has Resume/Quit. **IL** | `CharKeys.Update` reads Start; paused `VikingGame.Update` updates its pause menu. `TimeMgr` and `Pause.Accept` resume or quit. **IL** | A shared phone-style pause surface needs title-specific pause/resume/end adapters and audio validation. The standalone 360 pause UIs are different from the requested phone UX. |
| Input and players | `Hero.Update` reads left/right sticks, triggers, buttons by `PlayerIndex`; up to four local player slots. **IL** | `CharKeys.Update` reads per-slot gamepad movement/aim/buttons; Viking character manager has two player slots and shared camera. **IL** | Reuse the port's semantic mouse/keyboard/controller/touch mapping; adapt each title's gamepad reads and slot ownership separately. Touchpads are a desktop overlay, not native 360 input. |
| Rendering/content | `Content.Load` uses `gfx/*`, `fx/thresh`; `SpriteBatch` and camera use a 1280×720 scene. 11 platform-`x` XNB v3 files. **IL/package** | `Content.Load` uses `gfx/*`, `sfx/music/timeviking`, `.zsx` data; camera uses `VScroll`. 23 platform-`x` XNB v5 files. **IL/package** | Reuse FNA content conversion concepts, but add version-specific XNB/LZX and effect handling, per-title path maps, viewport and touch coordinate transforms. [Asset comparison](../runtime/asset-comparison.md) documents actual atlas differences. |
| Audio | XACT engine/project `sfxproj.xgs`, effects bank `fx.xwb`/`fx.xsb`, song bank `music.xwb`/`music.xsb`. **IL/package** | XACT `sfxproj4.xgs`, `fx.xwb`/`fx.xsb`; `MediaPlayer` loads a Song descriptor and WMA companion. **IL/package** | Phone WMA-to-Ogg conversion may be reused for Viking after format checks. Zombies needs XACT cue/wave-bank extraction or an equivalent validated local audio adapter; the phone sound-effect files do not cover its bank. |
| Saves | `Store` uses XNA `StorageDevice`/`StorageContainer`, container `zomgam`, files `settings.sav` and `game.sav`; high scores are persisted. **IL** | `Store` uses container `zp2k5`, file `scores.zdx` for high scores. **IL** | Implement storage bridge or guarded calls for each, isolate per-source data, and test round trips before enabling import. Do not reuse phone save bytes without format proof. |
| Progression | `Hero.AddPoints` increases score; `SpawnMgr`'s end phase applies a survivor bonus and closes the session. **IL** | `Character` kill/score paths, `World.ShiftCells` height increment, and `Moon.CheckHit` damage/defeat are present. **IL** | These are possible local event hooks, not achievement awards by themselves. Verify exact conditions with controlled gameplay before wiring phone-derived achievement rules. |

There is a feasible **static** preparation path: extract each supported STFS package, rewrite its managed assembly against an appropriate FNA/bridge ABI, convert only its own content and audio to loadable local formats, then stage each in an isolated runtime. Prompt 1 must prove load, render, play, and exit. The current FNA bridge covers some XNA concepts, but it has not been checked against either 360 assembly's complete ABI. XNA 3 and 4 references differ, so their adapters may differ. An unsupported layout or version must stop before preparation while leaving the phone runtime usable.

## Achievement evidence and candidate rules

**Observed/IL:** Neither standalone package contains an achievement manifest, achievement metadata, native achievement IDs, an achievement manager, or an award call site. The assemblies have 11 (Zombies) and 9 (Viking) GamerServices/Guide member references, all for sign-in, names, or storage UI; zero achievement-related member references. A case-insensitive scan of both decompiled trees for `achievement` and `award` found no code or XML hits. The `GameInfo.xml` files have no achievement entries. No Xbox Live unlock path is available from these packages. This is a negative finding for these exact versions, not a claim about every release.

The **phone** XAP has 20 local achievement flags, mapped in the [phone feature matrix](feature-matrix.md). To satisfy the requested local achievement support, Prompt 5 can reuse a phone achievement's wording and condition for a 360 source only after the equivalent 360 event and predicate are verified. Its local identifier must be source-qualified; it is not a recovered 360 ID or Gamerscore award. The table is a work queue, not an award list.

| Phone flag(s) | Candidate 360 event | Current classification |
| --- | --- | --- |
| 0, 1 | Zombies `Hero.AddPoints` has a local score counter; inspect threshold crossing for 100,000/1,000,000. | **Reachable local signal; predicate unverified.** No award exists in 360 IL. |
| 2, 3 | Zombies end phase and per-hero survival are visible in `SpawnMgr`; phone requires win and no death. | **Unknown equivalence.** Confirm a win boundary and a death-history flag before wiring. |
| 4, 5, 6 | Zombies asteroid phase, no shots, stationary 60 seconds, and laser flag count are phone rules. | **Unknown.** Trace the 360 phase and counters; no matching award predicate was found. |
| 7, 8 | Viking `Character` score is local; inspect 100,000/1,000,000 threshold crossing. | **Reachable local signal; predicate unverified.** No award exists in 360 IL. |
| 9, 11 | Viking `Moon.CheckHit` has moon defeat logic; phone also checks a no-death condition. | **Reachable local moon event; full condition unknown.** Verify defeat and per-run death history. |
| 10 | Viking `World.ShiftCells` increments tower height by 16; phone threshold is 6,000. | **Reachable local signal; predicate unverified.** Confirm units and crossing semantics. |
| 12–16 | Phone ZOMBIES ENDLESS-only rules. | **Unavailable in either standalone 360 game.** Viking's assembly contains some endless types, but its entry point does not launch them and its package lacks the mode's required content. Keep phone ownership. |
| 17, 18 | Phone cumulative profile kills at 1,000/10,000. | **Unknown.** 360 kills occur in gameplay, but neither package has the same cumulative profile/award path. Define persistence only after event equivalence is demonstrated. |
| 19 | Phone custom image/sprite rule. | **Unavailable as a 360 title rule.** No equivalent imported-custom-sprite workflow was established. Keep phone ownership. |

No native 360 award can be classified as locally reachable because none is present. Gameplay events listed above are candidates for **new local parity awards** only; preserve the phone's 20 flags and keep each source's earned state separate. The unimplemented GamerServices sign-in/storage functions need desktop adapters for gameplay and saves, but none gates an identified native award call.

## Guarded rewrite boundary for Prompt 1

The [fingerprint file](../../config/supported-360-versions.json) contains package and executable hashes plus representative method tokens and raw IL SHA-256 anchors: Zombies `Game1.Initialize`, `Game1.Update`, `TimeMgr.Pause`, `Menu.Update`, `Hero.AddPoints`; Viking `CharKeys.Update`, `Game1.Initialize`, `VikingGame.Update`, `World.ShiftCells`, `Moon.CheckHit`. Every actual rewrite must additionally assert the full method signature and exact operand pattern, document the reason, and fail if the anchor differs. The file is evidence for a future guard; it is not yet read by the installer or preparer.

Keep originals, extracted game files, decompiled source, prepared game assemblies/assets, and detailed inventories in ignored `game/`, `private/`, `generated/`, or `out/`. The tracked files from Prompt 0 contain metadata and authored inspection code only. No source package or proprietary output belongs in setup payloads.
