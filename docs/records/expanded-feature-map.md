# Expanded source feature map

Inspected 2026-11-06. Source is the sibling `z0mb1es-expanded` project, especially `src/` and its six `docs/` plans. This is a modified decompilation with original game code and additions mixed together, not a patch set against this port. Its Git worktree was clean at inspection (`2f955b0`, initial commit), so Git history cannot isolate new work. Paths below are relative to expanded `src/`. This is a **static source audit**; no expanded build or play test was run. “Wired” means a call chain was found, not that runtime behavior is proven.

## Findings at a glance

| Feature | Source verdict | Qualification |
| --- | --- | --- |
| Keyboard/controller menu navigation | Wired for main and `MenuLevel` menus | Menu input uses controller 1; other pads mainly serve the lobby. Repeat timing needs testing. |
| Multiple display resolutions | Wired | A fixed 1280×720 game target is letterboxed into selectable backbuffers. This does not show more world area. |
| High-resolution replacement assets | Scaffold only | `AssetScaler` initializes, but its mapping methods have no draw or load call sites. |
| Zombies and Endless local multiplayer | Lobby, input routing and hero spawning wired | Up to four slots; gameplay and game-over paths need play tests. |
| Time Viking local multiplayer | Partially wired | Lobby caps at two and two characters can spawn, but both characters read input slot 0. |
| Desktop settings | Mixed | Graphics and live volume controls have consumers; keyboard bindings and controller style do not affect `InputMgr`. |
| Online multiplayer, ultrawide world, cheats, jukebox/band modes | No implementation found | README marks these as future or “Maybe”. |

## Controller navigation and desktop input

`ZombiesWP7/Game1.cs` calls `InputMgr.Update` every frame. `ZombiesWP7/InputMgr.cs` polls touch, mouse, keyboard and `GamePad.GetState` for `PlayerIndex.One` through `Four` (around lines 221–227). Previous states produce A/B/Back/Enter/Escape edges (325–385, 765–868), and D-pad/left-stick/keyboard pressed and held directions (879–937). It tracks the last active source to change cursor and touch overlay behavior. Analog menu threshold is 0.5; gameplay uses a separate dead zone. Escape pauses gameplay and cancels menus; Start handles pause. Global navigation and confirm use the `PlayerIndex.One` alias, although lobby buttons poll all four.

`IMAK3Z0MB1EGAEM.menu/MenuLevel.cs` (94–173, 207–275) keeps `_navIndex`, wraps up/down, calls the selected item's `Click(text)` on non-touch confirm, lets mouse hover select an item, scrolls to the selected item, and offers a pointer back arrow. `Viking_x86.main/MainMenu.cs` (143–185, 261–351) is a separate horizontal carousel using left/right navigation, click and back. `IMAK3Z0MB1EGAEM.menu/Menu.cs` handles the lobby per slot. A future implementation must cover all three menu paths.

`MenuLevel` declares 0.35 s initial and 0.12 s repeat delays, but its pressed branch does not advance `_navRepeatTimer`, and the next held frame chooses 0.12 s because `_navUpPrev/_navDownPrev` was set. The intended first delay appears ineffective. `DesktopControlSettingsMenu.cs` offers four key presets, a keyboard toggle and three controller-style labels. Source search shows `Cfg.kbUp/Down/Left/Right` and `keyboardEnabled` are only used in settings UI/persistence; `InputMgr` hardcodes WASD/arrows. Controller style changes only `_styleIdx` in that menu. Those controls are not functional input settings.

This port translates desktop input into the original phone touch boundary in [DesktopInput.cs](../../src/Port.Hooks/DesktopInput.cs). The first controller enhancement now follows the pinned phone assembly's `MenuLevel.item` positions, highlights focus, and sends A through the original click path; the main carousel uses its original arrow regions. The expanded source design described above has different menu types and cannot be dropped into the current prepared-assembly architecture directly. Physical controller navigation still needs a focused-window play pass.

## Display resolution versus asset resolution

**Display transform is wired.** `ZombiesWP7.menu/Settings.cs` (19–121, 143–238) stores nine presets from 800×600 through 3840×2160 in `settings.ini`, filters them against the current display, loads settings at startup and saves them on leaving settings. `DesktopGraphicsSettingsMenu.cs` defers resolution selection until `Cancel`, while fullscreen, FPS cap and MSAA apply immediately. `Game1.cs` (77–98, 141–179, 320–395) renders to a fixed 1280×720 target and draws it into a centered rectangle using `min(backbufferW/1280, backbufferH/720)` with black bars. `ApplyNewChanges` sets backbuffer, fullscreen, vsync/fixed timestep, multisampling and world-screen sizes. `InputMgr.cs` (230–245) reverses the letterbox transform for mouse coordinates. `Game1.Draw` still has a stale “native 600×360” comment.

**High-resolution asset replacement is not wired.** `Game1.Initialize` loads `Content/png_manifest.json` via `ZombiesWP7/PngManifest.cs`, then initializes `IMAK3Z0MB1EGAEM.math/AssetScaler.cs`. The manifest records actual and reference dimensions; `MapSource`, `MapX/Y/Vec` could scale atlas coordinates. A source-wide call-site search found no use of these mapping methods outside their definitions. `ZombieGame.cs`, `VikingGame.cs` and other loaders still use fixed `Content.Load<Texture2D>` names; draws still use `ScaleTools.ScaledRect` or raw rectangles. The project therefore does not establish arbitrary-resolution texture replacement. `PngManifest` indexes by filename with last entry winning, so duplicate filenames in different folders would be ambiguous if adopted.

The rendered scene stays 16:9 on ultrawide displays. `docs/3-desktop-resolution-config-scaling.md` and `docs/2-proper-asset-scaling-prompts.md` describe target architectures, including unfinished asset call-site replacement; they are plans, not proof of completion.

## Local multiplayer, mode by mode

`IMAK3Z0MB1EGAEM.menu/Menu.cs` (169–525, 596–715) calls one `UpdateMultiPlayerLobbyInput(playAction)` for states 2 (Zombies), 4 (Time Viking) and 6 (Endless). Slots have `Out → In → Ready`; cancel reverses the stages. It supports gamepad A/B, keyboard Enter/Escape, touch player boxes, a button bar, disconnect removal, and starts after all nonempty slots stay Ready for one second. `CharMan.controlMethods[4]` maps `-1` empty, `1` keyboard, `2` gamepad, `3` touch. One touch and one keyboard player are tracked. Time Viking refuses gamepad/keyboard slots 2–3, although it shares the four-column renderer.

`InputMgr.cs` (940–1060) copies unified movement/aim to all slots when at most one source is assigned. With two or more, it writes `playerLVec/RVec[slot]` from that slot's assigned keyboard, gamepad or touch source. `IMAK3Z0MB1EGAEM.character/Hero.cs` (496–510) consumes the per-slot vectors. `ZombieGame.Play(bool endless)` (331–370) spawns a hero for every Ready slot in normal and endless mode; game-over paths record existing heroes' scores. This is a source path for up to four local Zombies/Endless players, subject to play validation.

**Time Viking is incomplete.** `VikingGame.Play()` (319–349) chooses at most two Ready slots into `mainPlayerIdx[2]`; `CharacterMgr.Reset()` creates up to two player characters; camera and HUD loop over two. `Viking_x86.character/CharKeys.cs` can read per-slot vectors, but `Viking_x86.character/Character.cs:229` calls `charKeys.Update(0)` for every controlled character. The second character therefore reads slot 0. The mode also retains several `character[0]` interactions that require a wider audit. Two-player Time Viking is not established by this source.

Device identity and lobby slot are treated as the same index for gamepads. If keyboard claims a slot corresponding to a connected gamepad, that gamepad cannot join through another free slot. In keyboard/mouse multiplayer aim, `InputMgr.cs` (980–1006) calculates from hero/character 0, not the keyboard player's assigned slot. A future port should separate devices from player slots. No network session code was found.

## Other feature areas

| Area | Concrete trail and status |
| --- | --- |
| Desktop graphics | `DesktopGraphicsSettingsMenu.cs`, `Settings.cs`, `Game1.cs`: resolution, fullscreen, cap and MSAA wired. FPS display is read by `IMAK3Z0MB1EGAEM.hud/HUD.cs`; reduced lights is read in menu/world rendering. |
| Sound settings | `DesktopSoundSettingsMenu.cs` cycles 0–100% in 25% steps and sets `MediaPlayer.Volume` / `SoundEffect.MasterVolume` live. `Settings.LoadSettings` reads saved volumes but does not apply them there; startup restoration needs validation. |
| Touch and pointer | `InputMgr.cs` preserves touch thumbsticks and adds mouse aiming/clicking and cursor drawing. `CustomMgr.cs` uses phone media sources when available and falls back to eight bundled test images on `NotImplementedException`; this is not a general filesystem image importer. |
| Local scores | `LeaderboardMgr.Write` calls `HighScores.AddScore`; `HighScores.cs` writes `scores.sav`; `Leaderboards.cs` draws the local table. `Write` ignores mode ID `lIdx`, so the three modes are not separated in storage. No Xbox LIVE leaderboard is wired. |
| Achievements | `AchievementMgr.cs` defines 20 local achievements, awards through `PlayerProfile`, and has toast state. `Game1.cs` loads their textures. This is local behavior, not Xbox LIVE integration. |
| Saves and profile | `StorageMgr.cs`, `PlayerProfile.cs`, `HighScores.cs`, `Store.cs` use local files. `settings.ini`, `savegame.dat` and `scores.sav` live at the working directory; preserve formats and path decisions explicitly if adapting. |
| Modes and creator | `ZombieGame.cs`, `VikingGame.cs`, `Viking_x86.zomb1es.endless/*`, `CustomMgr.cs`, `GameCreator.cs` contain phone-derived modes and customizer flow. Without a pristine phone source diff, presence does not establish an expanded addition. |
| Platform build | `ZombiesWP7.csproj` targets .NET 8 and references local FNA, content and Windows native libraries. README claims Windows/Linux/macOS; this audit found only a Windows native-copy rule and ran no platform builds. |
| Proposed features | README lists cheats and ultrawide as forthcoming; jukebox, song, band and online modes as “Maybe”. No implementation trail was established. |

## Using it for this port

1. Record original assembly behavior at each intended hook boundary. This port executes a prepared original DLL; expanded is a separately compiled game.
2. For navigation, normalize edge/held actions and implement selection in each original menu path. Validate pointer and controller focus, cancel, scrolling and repeat timing.
3. For resolutions, define one logical viewport, letterbox transform and exact inverse for pointer/touch. Validate 4:3, 16:9 and wider displays. Treat high-resolution asset loading as separate work with atlas-specific checks.
4. For multiplayer, separate device IDs from player slots, then implement lobby ownership, spawning and mode-specific controls. Test Zombies, Endless and Viking independently. Do not use expanded Time Viking as evidence of completed two-player control.

The expanded plans provide intent, but code call sites determine the status above. The key corrections to the earlier map are the unconsumed asset scaler, ineffective control settings, Time Viking input defect and lack of runtime verification.
