# Prompt 12 feature matrix — supported XAP

This matrix covers the pinned v1.0.0.0 XAP only. The evidence source is the original `ZombiesWP7.dll` and the Windows/FNA runtime. No original phone runtime was available. `IL` means original IL establishes the path; `probe` means an authored diagnostic called the original method on a locally prepared DLL; `manual` means a player used the visible controls. A probe does not certify the click path, visual quality, or gameplay feel.

## Linux x64 Prompt 2 status

| Path | Current result | Remaining check |
| --- | --- | --- |
| Self-contained `.deb` and `.rpm` | Local packages contain the authored launcher, pinned native libraries, converter, import tools, desktop entry, and icon; payload audit found no phone DLL or game asset files. | Install and update through graphical package managers on native Linux. |
| First import and restart | Headless WSL with scripted dialog responses imported the supported phone XAP into app data; the prepared DLL/content manifest verified. The Linux ABI checker resolved all 146 type and 382 member references. | Use the visible native picker/progress UI; confirm cancellation, repair, update, and source deletion on a Linux desktop. |
| Game startup | The Linux executable loaded 177 prepared game types and JIT-prepared 818 methods. After adding unversioned native-library aliases, startup reached SDL initialization. | WSL reports `No available video device`; play all three modes with graphics, audio, controller, saves, and creator photos on native Linux. |

## Mode and navigation paths

| Path | Original evidence | Port result | Remaining check |
| --- | --- | --- | --- |
| Main carousel: normal zombies | `MainMenu.Update` selects destination 0, state 2, `ZombieGame.Init(false)`; `Menu.Update` calls `ZombieGame.Play(false)` into state 3 | Probe reached state 3, ran 300 updates, paused, resumed, ended, and returned to state 1; exit 0. A gameplay frame was inspected. | Click through the visible carousel and play a meaningful session |
| Main carousel: Time Viking | Destination 1, state 4, `VikingGame.Init`; `VikingGame.Play` enters state 5 | Same short cycle passed, exit 0; a gameplay frame was inspected | Visible menu selection, movement/aim, story progression and ending |
| Main carousel: Endless Zombies | Destination 2, state 6, `ZombieGame.Init(true)`; `Play(true)` enters state 7 | Same short cycle passed, exit 0; a gameplay frame was inspected | Visible selection, room traversal, powerups, and longer survival |
| Pause, resume, end-game confirmation | Original `BaseTimeMgr.Pause`, `Pause.Click`, `EndGameYouSure.Click` | Original methods completed in each mode; active pause cleared on resume and end returned to the mode menu | Check Escape/controller Start, menu selection, and audible pause/resume in each mode |
| Return from mode menu | `Menu.needsQuit` causes `Game1.ReMainLoad` | Diagnostic set the flag after the original end-game path; all modes reached state 1 | Test the user-facing Back action from each mode menu |
| Exit game | `Game1.OnExiting` calls `StorageMgr.Write`; main menu has a quit dialog path | Clean diagnostic exits wrote saves | Check the visible quit dialog and window close |

The diagnostic directly invokes original entry and menu methods. It sets only `GameState.state`, the original `Menu.playerState` readiness value, and `Menu.needsQuit` at the same boundaries the original menu uses. It does not patch gameplay or assert that the player-facing selection paths work. Normal, endless, and Viking state values are 2/3, 6/7, and 4/5 respectively. These values come from `GameState` constants in the original DLL.

## Other menu paths

`Menu.Initialize` constructs the following 17 levels. The `--probe-menus` diagnostic activated each level through the original `Menu.SetLevel` method. Levels 0–10 and 12–15 stayed active with alpha 1 and produced captured frames. Levels 11 and 16 redirected to `ThanksUpsell` under the full-game (`trial=False`) policy, matching the original update logic. The probe completed with `menusVerified=True` and exit 0. Direct activation does not establish that every level is reachable through visible controls.

| Levels | Evidence and current status |
| --- | --- |
| High scores, pause, end-game confirmation | Levels 1, 2, and 4 rendered in the menu probe; pause and end confirmation methods also completed in the mode probe. Visible navigation and high-score persistence are deferred. |
| Help and options, help, settings, credits | Levels 3, 5, 13, and 14 rendered. Settings save/reload was checked in Prompt 10. Visible item navigation is deferred. |
| Achievements and leaderboards | Levels 7 and 8 rendered; `--probe-services` verified 20 achievement rows and the offline leaderboard explanation on a fresh restart. Visible clicks are deferred. |
| Customizer and Game Creator | Levels 9 and 10 rendered. Original save calls occur at `CustomMapper.Cancel` and `GameCreator.Cancel`. Setup selects the PNG/JPEG picture folder; its thumbnails feed the original creator. Saved custom mappings are reapplied when pictures are loaded. Live custom sprite restart behavior remains to be checked. |
| Quit confirmation | Level 0 rendered. Visible confirmation and cancellation are deferred. |
| Trial upsell, thanks, achievement upsell, not-signed-in warning | Levels 11 and 16 redirected to level 12 because `trial=False`; levels 12 and 15 rendered. The supported desktop policy is full-game and local non-Live. Visible trial and marketplace paths are deferred. |
| Debug | Level 6 rendered through direct activation; a normal menu entry was not established. |

## Progression, saves, and achievements

| Path | Original evidence | Port result / limit |
| --- | --- | --- |
| Normal song phases and score | `BaseTimeMgr` advances `TimeSlice` phases from media time; `Hero.AddPoints` checks score thresholds | Three-second-plus live cycle passed. Natural phase changes, win, score thresholds, and high-score recording remain untested. |
| Endless rounds, rooms, and powerups | `EndlessUpdate.round` starts at 1; `NextRound` increments it; `PowerUp` and room methods exist | Calling original `NextRound` twice advanced round 1 to 3, set achievement flags 15/16, and survived the rest of the mode cycle. Natural room completion and pickup behavior remain untested. |
| Time Viking phases and tower | `VikingDirector.phase`, `World.ShiftCells`, Moon hit and score paths are present | Short live cycle passed. Tower height, ending, and score achievements remain untested. |
| Difficulty | No separate difficulty selection was found in the inspected main-menu and mode-menu methods; timed phases, round count, and spawning change challenge | Whether other original UI or hidden settings change difficulty is unresolved. No original phone comparison is available. |
| Save points | Original calls at startup read, settings/customizer/Game Creator cancel, and `Game1.OnExiting` write | Isolated diagnostic saves were written on exit. A fresh process loaded the endless save and reported 20 achievements, 2 earned; settings persistence was checked in Prompt 10. Natural high scores and custom sprites still need restart checks. |

The original achievement manager has 20 keys. The following call-site map was read from the original DLL; only IDs 15 and 16 were exercised as new awards in this stage. The bridge does not award them on its own.

| ID | Original trigger/call site | Runtime status |
| --- | --- | --- |
| 0, 1 | Normal `Hero.AddPoints`: 100,000 and 1,000,000 points | IL only |
| 2, 3 | Normal `SpawnMgr.ZombieClick`: win; win without `Hero.diedOnce` | IL only |
| 4, 5, 6 | `Hero.Update`: asteroid phase without space shots; stand still 60 seconds. `Laser.Update`: flag count 20 | IL only |
| 7, 8 | Viking `Character.AddScore`: 100,000 and 1,000,000 points | IL only |
| 9, 11 | Viking `Moon.CheckHit`: win; win without `VikingGame.diedOnce` | IL only |
| 10 | Viking `World.ShiftCells`: tower crosses height 6,000 | IL only |
| 12, 13 | Endless `Hero.AddPoints`: 100,000 and 1,000,000 points | IL only |
| 14 | Endless `UpdateScroll`: first-round one-room condition | IL only |
| 15, 16 | Endless `NextRound`: completing rounds 1 and 2 | Original method exercised; flags survived restart |
| 17, 18 | `PlayerProfile.AddKill`: cumulative kills 1,000 and 10,000 | IL only |
| 19 | `GameCreator.Cancel`: custom sprites enabled and at least one custom image exists | Desktop folder import and sprite creation passed a graphics probe; achievement trigger remains untested |

## Reproduce the diagnostics

Build and stage a prepared DLL and content from the supported user-supplied XAP as described in the [implementation log](implementation-log.md). Use a different ignored `--save-dir` for each mode so the probe cannot change the player's normal save:

```powershell
./scripts/build-offline.ps1
$stage = "private/run/prompt12-repro-$([guid]::NewGuid().ToString('N'))"
./scripts/stage-local-run.ps1 -PreparedDirectory private/prepared/prompt10-ui-contrast -ContentDirectory private/content-prepared/net10-a -OutputDirectory $stage
foreach ($mode in @('normal','endless','viking')) {
    dotnet (Join-Path $stage 'Port.Launcher.dll') --run (Join-Path $stage 'ZombiesWP7.dll') --content $stage --probe-mode $mode --frames 1100 --windowed --save-dir (Join-Path $stage "saves-$mode")
}
```

`MODE_PROBE ... event=main-menu` and `RUN_END ... modeVerified=True` with exit 0 are the short-cycle checks. For endless, `round-check initialRound=1 round=3 oneRound=True twoRounds=True` verifies original round awards. Restart the same save with `--probe-services --frames 300` (without `--probe-mode`) and look for `SAVE_LOADED` plus `RUN_SERVICE_ACHIEVEMENTS count=20 earned=2`.

To exercise all constructed menu levels with an isolated save, use the same stage:

```powershell
dotnet (Join-Path $stage 'Port.Launcher.dll') --run (Join-Path $stage 'ZombiesWP7.dll') --content $stage --probe-menus --frames 900 --windowed --save-dir (Join-Path $stage 'saves-menu')
```

The expected result is `MENU_PROBE` for levels 0–16 followed by `RUN_END ... menusVerified=True` and exit 0. The diagnostic writes ignored `menu-<level>.bmp` captures for the 15 levels that remain active. In the tested build, achievement, leaderboard, customizer, and Game Creator captures contained screen content; this is a render check, not a visual quality or click-path judgment.

Each mode probe writes an ignored `mode-<name>.bmp` gameplay frame beside the staged launcher. The three captured frames from this Windows run were inspected: the normal and endless maps showed the player, terrain, HUD, and touchpad overlays; Time Viking showed its character, terrain, HUD, and overlays. These snapshots confirm rendering on the tested GPU, not animation quality or full level coverage.

The user chose to defer hands-on movement, aiming, menu navigation, natural progression, and the remaining visual/input checks while the game boots and inputs mostly work. Individual bugs can be checked as they arise. The short diagnostic cycles and the user's report that audio works do not close those deferred checks.

The current local stage is `private/run/prompt12-menus-b`; it includes both mode and menu probes. If a later hands-on check is wanted, launch it without a probe flag:

```powershell
dotnet private/run/prompt12-menus-b/Port.Launcher.dll --run private/run/prompt12-menus-b/ZombiesWP7.dll --content private/run/prompt12-menus-b --windowed --save-dir private/run/prompt12-menus-b/manual-saves
```

The [desktop input guide](../runtime/desktop-input.md) lists mouse, keyboard, and controller controls. A later play check can cover natural score and scene progression, high scores after restart, and individual input bugs. The save directory above is isolated from the player's normal AppData save.

## Optional asset swap: installer and Settings integrated

The phone matrix above remains the gameplay baseline. [The 360 evidence ledger](xbox-360-evidence.md), [asset comparison](../runtime/asset-comparison.md), and [Prompt 0 evidence](asset-swap-p0.md) cover source fingerprints and candidate mappings. [Prompt 1 evidence](asset-swap-p1.md) records local preparation. [Prompt 2 evidence](asset-swap-p2.md) records guarded phone renderer routing and playback probes. [Prompt 3 evidence](asset-swap-p3.md) records the offline setup importer, ASSETS Settings page, and install/repair tests. [Prompt 4 evidence](asset-swap-p4.md) records 18 native-to-widescreen/ultrawide mode runs, source failure handling, preference fallback, rebuilt setup, reinstall, and rollback. Natural death/effects/endings, input feel, and the clean-account wizard remain hands-on checks; redistribution rights remediation remains a release gate.

| Visual source | Inventory evidence | Phone-runtime result |
| --- | --- | --- |
| I MAED 360 | Pinned package; eight backgrounds and individual sprite crops mapped with phone fallback. Backgrounds receive the verified phone vignette treatment. | 121 crops prepared; 75 distinct draw routes rendered in the phone mode; play/pause probe passed |
| Steam I MAED | Executable and nine required textures version-pinned; individual backgrounds and sprite crops mapped. Extra PC art and effect excluded. | 121 crops prepared across I MAED and Endless; 75 distinct draw routes rendered in the I MAED mode; 67 distinct draw routes rendered in Steam-only Endless; play/pause probes passed |
| Time Viking 360 | Pinned package; `grass` face icons relocated individually, moon stages cropped, character/map cells reviewed. | 270 crops prepared; 204 distinct draw routes rendered in the phone mode; play/pause and resize probes passed |
| ZOMBIES ENDLESS | No standalone source. The [Endless manifest](asset-swap-mappings/endless-360.json) orders matching I MAED 360 art, Steam I MAED art, Viking 360 art, then phone Original. | Steam alone imports 113 of 230 elements; Steam plus Viking imports 171. Both Endless mode cycles and art catalogs passed with 67 and 125 distinct routes respectively; earlier mixed 360 resize probes passed. |

The phone's original achievements, saves, controls, pause flow, and offline service policy remain the only planned behavior paths.

## Platform expansion Prompt 0

Windows now uses the [shared preparation and host boundaries](../runtime/platform-host.md). The launcher verified and played a prepared game with its executable, content, and saves in separate directories; the offline setup helper prepared and verified a phone-only install. Existing optional-art selection remained available in the separate-path launch. Linux, macOS, iOS, and Android hosts, native libraries, pickers, and device runs remain future prompts.

## Platform expansion Prompt 1 shared import

The [persistent import transaction](../runtime/platform-import.md) stages a phone-only or optional-art import under app data, checks the prepared game and all asset hashes, and activates it with rollback. `TryOpen` verifies on ordinary starts and offers the same flow for repair when invalid. The Settings Assets page includes Reimport; new hosts use `--run-managed` exit code 10 to dispose the game before selection and launch a fresh process after a successful commit. Windows continues to direct the player to Setup. Native Linux selection, visible progress, package tool inclusion, and player UI acceptance remain for Prompt 2.

## Platform expansion Prompt 4 macOS source

The authored macOS launcher and bundle recipe use the shared import/Reimport transaction, AppleScript system pickers, app-owned persistent data, and a child game session. The build is configured for Apple Silicon only and requires reviewed hashes for Mac native libraries, converter, importer, and runtime packs. No `.app` or `.pkg` has been built on a Mac, and no Finder, gameplay, input, audio, creator photo, or update acceptance has been recorded. Treat macOS as source implementation in progress, not a playable release.
