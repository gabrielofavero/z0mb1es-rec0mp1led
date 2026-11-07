# Game hot swap: optional Xbox 360 sources

**Status:** Planned. The only supported input today is the Windows Phone XAP. No Xbox 360 package has been validated or decompiled for this port, so the 360 preparation path and runtime behavior below are implementation targets, not completed features.

## Player contract

- The existing phone XAP remains the base install and supplies the main menu and ZOMBIES ENDLESS. The installer also accepts the player's own Xbox 360 copies of **I MAED A GAM3 W1TH Z0MB1ES 1N IT!!!1** and **TIME VIKING**, independently and optionally. A player can import either, both, or neither. Setup shows which game and version it recognized before preparing files locally.
- Add **GAMES** under **HELP AND OPTIONS → SETTINGS**, alongside the existing Display and Touch pages. Show separate `Z0MB1ES: MOBILE / 360` and `TIME VIKING: MOBILE / 360` choices. Offer 360 only when that game's prepared install verifies; otherwise show it as unavailable and tell the player to rerun the installer to add or repair it. Both default to Mobile, including upgrades from existing installs. ZOMBIES ENDLESS always uses Mobile.
- Selecting Z0MB1ES or TIME VIKING from the existing carousel and lobby starts the chosen source for that game. A 360 choice runs the imported 360 game instead of the phone implementation. Returning or quitting goes back to the same phone main menu. Keep the player's other source choices unchanged.
- Both sources use the same visible pause menu and actions as the mobile port: pause, resume, end game confirmation, and return to the menu. Escape, controller Start/Back behavior, and the touch pause button must work consistently. Mouse, keyboard, controller, direct touch, and on-screen touchpads follow the existing desktop input settings; Show Touchpads and Relative Controls apply to either source. Both sources render to the user's selected display mode, resolution, and VSync setting. Keep aspect ratios and pointer/touch coordinates correct when their internal scene sizes differ.
- Support local achievements for both 360 games wherever their original triggers can be identified and reached in this port. Show earned state for the selected source and persist it across restarts. Keep the phone game's existing 20 achievement flags intact. Do not claim Xbox Live unlocks or invent awards for unverified conditions.

## Shared rules and prompt order

Run the prompts below in order, one at a time. Each has a separate acceptance gate. Read the [initial implementation rules](../done/2026-11-06-initial-implementation/implementation-prompts.md), [architecture](../../foundation/architecture.md), [installer contract](../../../installer/README.md), and relevant runtime notes before changing code. Record exact versions, commands, results, and unresolved behavior in `docs/records/implementation-log.md` and update the feature matrix. If the 360 package format or original behavior changes the design, revise the plan from evidence before continuing.

Keep original packages, extracted files, decompiled code, and prepared assemblies/assets in ignored local storage. Do not commit or bundle them. Guard every title-specific patch by supported version and exact code anchor; reject unsupported inputs clearly. Preserve phone-only installation, ZOMBIES ENDLESS, saves, and the current offline service policy throughout.

0. Prompt 0 — Inventory and decompile the 360 titles.
1. Prompt 1 — Prepare and boot each 360 title locally.
2. Prompt 2 — Import optional titles through the installer.
3. Prompt 3 — Add source settings and game dispatch.
4. Prompt 4 — Match pause, controls, display, and saves.
5. Prompt 5 — Wire supported 360 achievements.
6. Prompt 6 — Validate combinations and audit the release.

### Prompt 0 — Inventory and decompile the 360 titles

1. Obtain locally owned copies of each 360 title for development. Record package identity, file inventory, executable/content formats, version, sizes, and hashes in a metadata-only evidence ledger. Determine what can actually be recovered as managed code and content; do not assume the phone assembly or its patches apply to either title.
2. Decompile and inspect each supported 360 build locally. Map its entry point, game loop, menu, pause, input, rendering, content loader, audio, saves, multiplayer, achievement metadata, and every achievement award call site to the phone counterpart. Label observed, IL-derived, and inferred behavior separately. Identify which existing FNA bridge and content conversions can be shared and which need title-specific adapters. Classify achievement triggers as locally reachable, dependent on unavailable services, or unknown.
3. Define exact supported 360 version fingerprints and guarded rewrite plans for each title. If a selected package cannot be prepared with the available tooling, reject it with a specific unsupported-format/version message; keep the phone game usable. Keep original packages, extracted files, decompiled code, and locally prepared outputs ignored and out of distributable artifacts.

**Acceptance:** Both titles have a reproducible inventory, a documented decompilation result and feasible preparation path, and an achievement map that distinguishes reachable triggers, unavailable services, and unknown conditions. No proprietary output is tracked or shipped.

### Prompt 1 — Prepare and boot each 360 title locally

1. Extend `Port.Prepare` and the content pipeline with per-title validation, extraction, dependency/ABI checks, guarded code adaptation, content conversion, and prepared-output manifests. Preserve original bytes in local private storage and record the source package hash and preparation version. Validate each title's entry assembly and all required assets before reporting success.
2. Keep prepared title files in separate, versioned runtime directories rather than mixing assemblies or `Content/` trees with the phone runtime. Choose a process or assembly isolation boundary after inspecting the 360 entry points; account for the current launcher's assumption that one `ZombiesWP7.dll` and its content live beside it. Add a local launch/check command for each title.

**Acceptance:** Each supported 360 title independently prepares, verifies, loads, renders, starts play, and exits in a local developer run. Wrong versions and changed patch anchors fail clearly. The phone-only build and all three phone modes still run.

### Prompt 2 — Import optional titles through the installer

1. Extend `Port.SetupHelper` and the Inno wizard with optional, independent 360 import selectors and per-title progress/errors. Show which title and version was detected before preparation. Never require a 360 source to install or update the phone game.
2. Stage each import and commit only after full verification; a bad or cancelled optional import must not damage the working phone install or the other 360 title. Support later add, replace, repair, and removal of one optional title without deleting saves or requiring the other title's package again.
3. Record installed sources in a manifest; verify hashes at startup and before selection. Do not retain source packages by default. Update release payload allow-lists and audits so no original or prepared 360 content enters setup or Git.

**Acceptance:** Install and update matrices pass for phone only, phone plus each title, and phone plus both; invalid/cancelled imports leave the prior install playable. A player needs only setup and their own supported packages, with no developer tools or network.

### Prompt 3 — Add source settings and game dispatch

1. Add a small source registry with stable IDs for `zombies-mobile`, `zombies-360`, `viking-mobile`, and `viking-360`. Store the two choices in a versioned preference file beside existing desktop settings. Migrate missing/old preferences to Mobile. Verify availability before showing or booting a 360 choice; if its files are missing or corrupt, tell the player to rerun the installer to add or repair it. The player can select Mobile in Settings; do not silently launch another source.
2. Add the GAMES page through the existing guarded Settings menu hooks, with the original font, layout, navigation, and Back behavior. Keep source choice independent of display, touch, and game save settings. Extend the menu probe to exercise both rows, persistence, and unavailable states.
3. Intercept only the two game-start transitions after the player's carousel/lobby choice is known. Launch the selected verified runtime while preserving the host's menu and return path. A failed 360 boot reports an actionable error and returns to the menu without losing the selected source or save data.

**Acceptance:** Each menu choice boots the requested source, survives restart, and returns to the same main menu. ZOMBIES ENDLESS still boots the phone path. Unavailable and corrupt 360 files never start the wrong source.

### Prompt 4 — Match pause, controls, display, and saves

1. Reuse the existing desktop input mapping as a common semantic layer: movement, aim, select, cancel, pause, and touchpad contacts. Add title-specific adapters only where 360 control semantics differ. Test mouse and keyboard, gamepad, direct touch, and the virtual touchpads in each imported title. Honor existing Touch settings and multiplayer slot ownership where supported by the source game.
2. Make the visible pause flow match the mobile port. Reuse the phone pause UI or an equivalent shared host UI backed by the same assets and actions, and adapt each 360 game so pause stops simulation/audio appropriately and resume/end-game returns cleanly. Do not present a different 360 pause screen to the player.
3. Pass saved Display preferences to the selected runtime before its graphics device starts. Use the exact chosen output resolution and mode, preserve VSync, and map the 360 game's logical viewport to that output without stretched art or broken hit testing. Handle live display changes, resize, fullscreen/borderless transitions, and returning to the main menu.
4. Keep per-source saves and progress isolated unless a format has been proven compatible. Preserve all save directories through reinstall, repair, source changes, and uninstall according to the existing save policy.

**Acceptance:** For both imported games, a focused-window session can start, move and aim, pause and resume, end and return, save and reload, and switch back to Mobile at the selected resolution with every supported input type. The pause screen and actions match Mobile.

### Prompt 5 — Wire supported 360 achievements

1. Use Prompt 0's map to enumerate every original achievement ID/key, metadata source, award condition, and call site for each 360 title. Classify each entry as implementable from reachable local gameplay, blocked by an unavailable platform service, or still unverified. Record a reason for every entry not wired. Do not assume a phone achievement with a similar name has the same condition or ID.
2. Route every implementable original trigger through a title-specific local achievement provider. Prefer the original award path; add narrow, version-guarded hooks only where that path cannot run on desktop. Make awards idempotent and persist them atomically in a per-source profile. Keep the phone's 20 flags and both 360 titles' progress separate. Do not claim Xbox Live synchronization or Gamerscore changes.
3. Make the local achievements screen source-aware, using recovered original metadata where available and clear local-only labeling. Show the correct list and earned state for the selected source. Verify that source switching and reinstall preserve earned state without awarding anything merely because a game was imported or selected.

**Acceptance:** Every trigger classified implementable has a controlled test showing no early award, one award at its original condition, no duplicate, persistence after restart, and isolation from other sources. The achievements screen shows the correct source's earned state. The evidence ledger lists unsupported and unknown achievements with reasons.

### Prompt 6 — Validate combinations and audit the release

1. Add checks for package fingerprint rejection, guarded patch anchors, prepared-output hashes, title isolation, optional-import rollback, source preference migration, unavailable/corrupt imports, boot dispatch, and local achievement persistence. Keep per-title diagnostics that identify which source and preparation version ran without logging proprietary code or assets.
2. Run the complete phone-only regression suite, including ZOMBIES ENDLESS, then the same menu/input/pause/display/save/achievement probes for each 360 source at native, common widescreen, and ultrawide resolutions. Perform a focused-window hands-on pass for mouse, keyboard, controller, and touch or touch emulation; automated state changes alone do not establish input parity.
3. Rebuild and audit the one-file installer. Test on a clean Windows account without developer tools using independently supplied supported packages. Record the exact versions, commands, results, remaining achievement gaps, and package hash in the implementation log and feature matrix. Complete the existing release rights review before publishing.

**Acceptance:** A player can install either or both supported 360 titles, choose Mobile or 360 separately for Z0MB1ES and TIME VIKING, play through the common menu, pause, controls, and display settings, and earn every verified locally supportable 360 achievement without losing phone-only features or saves. The release audit is clean and no original or prepared game material enters the distributable artifact.
