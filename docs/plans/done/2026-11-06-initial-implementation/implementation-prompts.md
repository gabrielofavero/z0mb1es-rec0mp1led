# Implementation prompts for the XAP-based FNA port

Run these prompts **in order**, one at a time, in this new workspace. Each prompt asks for code, a testable result, and a short update to `docs/records/implementation-log.md`. Do not start the next prompt until its acceptance gate passes. If a gate exposes a false assumption, fix the design and update `docs/foundation/architecture.md` before continuing. The prompts are written so they can be pasted into a coding-agent session directly.

## Shared rules and definition of done

- The input of record is the user-supplied XAP and its original `ZombiesWP7.dll`. The old `../z0mb1es-on-teh-fna` project is a **dirty reference**: use it to find attempted fixes and likely feature coverage, never as a golden test oracle or source to compile or copy into this project.
- Keep the original DLL and extracted assets byte-for-byte intact in ignored `private/`. All changed behavior must live in authored launcher, bridge, content, and hook code. Generate a prepared DLL locally; never check it in or include it in a distributable package.
- Prefer the original runtime on a WP7 device/emulator for behavioral observations if available. If it is unavailable, use original IL, assets, and controlled observations of this port, and label behavioral conclusions as inferred. A mismatch with the old port is a question to investigate, not a regression by itself.
- Every patch needs a version/hash guard, exact target signature, reason, and test. Fail loudly for unsupported XAPs and unimplemented reachable APIs. Avoid broad no-op stubs that make the game appear to run while changing logic silently.
- “Working” means a fresh checkout plus a user-provided supported XAP can prepare locally and run to the menu; start, play, pause, and exit each mode present in that XAP; use playable desktop input; load all needed content; play music/SFX; save and reload progress; and operate offline services without crashes. The final Windows setup must install and launch with only that XAP and no developer tools. Do not claim exact original parity beyond what the evidence supports.

## Status index

Folders show current work status. 'Done' records implementation work; any deferred hands-on acceptance remains documented in the [feature matrix](../../../records/feature-matrix.md) and [implementation log](../../../records/implementation-log.md).

### Done

- [Prompt 1 — Establish the evidence ledger and version boundary](01-evidence-ledger.md)
- [Prompt 2 — Create a clean FNA desktop host](02-desktop-host.md)
- [Prompt 3 — Implement deterministic local preparation](03-local-preparation.md)
- [Prompt 4 — Resolve the complete ABI before gameplay patches](04-abi-validation.md)
- [Prompt 5 — Build the guarded IL rewriter](05-il-rewriter.md)
- [Prompt 6 — Implement the minimum honest compatibility bridge](06-compatibility-bridge.md)
- [Prompt 7 — Make original content load locally](07-content-loading.md)
- [Prompt 8 — Reach a stable menu through the original game loop](08-menu-bringup.md)
- [Prompt 9 — Add playable desktop presentation and input](09-desktop-input.md)
- [Prompt 10 — Preserve saves and implement offline services](10-saves-and-services.md)
- [Prompt 11 — Finish media and phone-only API behavior](11-media-and-phone-apis.md)
- [Prompt 12 — Validate every mode against original evidence](12-feature-validation.md)
- [Prompt 13 — Build the user-friendly XAP installer](13-xap-installer.md)
- [Prompt 14 — Audit and rehearse the one-file release](14-release-audit.md)

### In progress

No prompts are currently in progress.

### To do

- [Game hot swap Prompt 0 — Inventory and decompile the 360 titles](../../to-do/2026-11-07-game-hot-swap.md#prompt-0--inventory-and-decompile-the-360-titles)
- [Game hot swap Prompt 1 — Prepare and boot each 360 title locally](../../to-do/2026-11-07-game-hot-swap.md#prompt-1--prepare-and-boot-each-360-title-locally)
- [Game hot swap Prompt 2 — Import optional titles through the installer](../../to-do/2026-11-07-game-hot-swap.md#prompt-2--import-optional-titles-through-the-installer)
- [Game hot swap Prompt 3 — Add source settings and game dispatch](../../to-do/2026-11-07-game-hot-swap.md#prompt-3--add-source-settings-and-game-dispatch)
- [Game hot swap Prompt 4 — Match pause, controls, display, and saves](../../to-do/2026-11-07-game-hot-swap.md#prompt-4--match-pause-controls-display-and-saves)
- [Game hot swap Prompt 5 — Wire supported 360 achievements](../../to-do/2026-11-07-game-hot-swap.md#prompt-5--wire-supported-360-achievements)
- [Game hot swap Prompt 6 — Validate combinations and audit the release](../../to-do/2026-11-07-game-hot-swap.md#prompt-6--validate-combinations-and-audit-the-release)

### Cancelled

No prompts are currently cancelled.

## If a prompt cannot pass

Keep the failure visible in `docs/records/implementation-log.md`: exact command, exception or observed behavior, version/hash, and the smallest next experiment. Revise an earlier prompt or the architecture when the evidence changes. A passing build alone does not close a runtime or behavior gate.
