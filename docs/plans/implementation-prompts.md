# Implementation prompts for the XAP-based FNA port

Run these prompts **in order**, one at a time, in this new workspace. Each prompt asks for code, a testable result, and a short update to `docs/implementation-log.md`. Do not start the next prompt until its acceptance gate passes. If a gate exposes a false assumption, fix the design and update `docs/architecture.md` before continuing. The prompts are written so they can be pasted into a coding-agent session directly.

## Shared rules and definition of done

- The input of record is the user-supplied XAP and its original `ZombiesWP7.dll`. The old `../z0mb1es-on-teh-fna` project is a **dirty reference**: use it to find attempted fixes and likely feature coverage, never as a golden test oracle or source to compile or copy into this project.
- Keep the original DLL and extracted assets byte-for-byte intact in ignored `private/`. All changed behavior must live in authored launcher, bridge, content, and hook code. Generate a prepared DLL locally; never check it in or include it in a distributable package.
- Prefer the original runtime on a WP7 device/emulator for behavioral observations if available. If it is unavailable, use original IL, assets, and controlled observations of this port, and label behavioral conclusions as inferred. A mismatch with the old port is a question to investigate, not a regression by itself.
- Every patch needs a version/hash guard, exact target signature, reason, and test. Fail loudly for unsupported XAPs and unimplemented reachable APIs. Avoid broad no-op stubs that make the game appear to run while changing logic silently.
- “Working” means a fresh checkout plus a user-provided supported XAP can prepare locally and run to the menu; start, play, pause, and exit each mode present in that XAP; use playable desktop input; load all needed content; play music/SFX; save and reload progress; and operate offline services without crashes. The final Windows setup must install and launch with only that XAP and no developer tools. Do not claim exact original parity beyond what the evidence supports.

## Prompt 1 — Establish the evidence ledger and version boundary

```text
Read docs/architecture.md and docs/plans/implementation-prompts.md. Work only in this new workspace. Build a baseline evidence ledger from the original XAP, not from the old port. Extend tools/inspect_assembly so it emits stable JSON with assembly identity, all AssemblyRef/TypeRef/MemberRef entries, inheritance and override relationships, method signatures, and candidate API calls needing adaptation. Identify known menu/mode entry points and asset references through metadata/IL inspection without compiling decompiled source. Add docs/behavior-evidence.md with each claim's evidence source and confidence; record the old port only as an untrusted comparison. Add a supported-version manifest for the exact XAP/DLL hashes already documented. Report unknowns, especially original viewport, content format, save format, and service behavior. Do not infer original behavior from the old project's changes.

Acceptance: the JSON is reproducible for this XAP; its counts and hashes match docs/architecture.md; unsupported input is rejected; the evidence ledger distinguishes observed, IL-derived, and old-port-only claims. Update docs/implementation-log.md with commands and results.
```

## Prompt 2 — Create a clean FNA desktop host

```text
Read docs/architecture.md, docs/plans/implementation-prompts.md, and installer/README.md. Create the new .NET solution and src/Port.Launcher, Port.Bridge, Port.Hooks, Port.Prepare, and Port.Content projects with clear one-way references. Port.Prepare and Port.Content must expose reusable libraries so the developer CLI and installer helper can call the same code. Keep the Xenon-inspired boundaries already documented: config/ for pins, docs/ for evidence, game/ for ignored local input, patches/ for guarded changes, scripts/ for audits, installer/ for packaging, and out/ for ignored output. Pin a clean FNA version and its native libraries with license and acquisition instructions; do not depend on a modified FNA build or the old game's source project. Add a tiny authored FNA smoke game that opens a window, clears the frame, accepts close/escape, and logs native-load errors. Keep this smoke game separate from the user game. Record exact toolchain/dependency versions and an offline-friendly build command.

Acceptance: a fresh build of the authored host succeeds; the smoke game opens, renders, and exits on the target desktop; no original game code or content is compiled or packaged. Update docs/implementation-log.md with the observed result and any platform limits.
```

## Prompt 3 — Implement deterministic local preparation

```text
Read docs/architecture.md, docs/plans/implementation-prompts.md, and installer/README.md. Implement one reusable .NET preparation engine behind `prepare --xap <path> --output <private-dir>` and future installer-helper commands. The current Python extractor remains a developer tool; players must not need Python. Verify package and DLL hashes before touching outputs, reject unsupported versions, extract safely, preserve the original DLL unchanged, and produce a provenance manifest containing input hashes, tool versions, and output hashes. Use temporary paths plus an atomic final move so failed attempts cannot look complete. Keep generated files under ignored private/ by default and document a safe explicit alternative output path. Do not yet claim the prepared game is playable.

Acceptance: two preparations of the same XAP produce identical manifests and files; a corrupted/unknown XAP fails with a useful error; failed preparation leaves no valid-looking output; no original material is tracked. Update docs/implementation-log.md.
```

## Prompt 4 — Resolve the complete ABI before gameplay patches

```text
Read docs/architecture.md and docs/plans/implementation-prompts.md. Use the JSON inventory from Prompt 1 to classify every external type and member reference as FNA, Port.Bridge, .NET runtime, or unresolved. Compare full signatures, including generic arguments, fields, events, inheritance, overrides, and constructors, against the pinned FNA assembly. Investigate mscorlib/System 2.0.5.0 binding and isolated storage explicitly. Implement a machine-readable compatibility report and a validation command that fails if anything is unresolved. Add focused fixtures for tricky metadata shapes; do not use the old port as proof that a signature is correct.

Acceptance: the report accounts for every reference in the original DLL, lists each necessary rewrite/shim, and identifies any unavoidable blocker before IL rewriting begins. Passing means no entries are silently classified by name alone. Update docs/implementation-log.md.
```

## Prompt 5 — Build the guarded IL rewriter

```text
Read docs/architecture.md and docs/plans/implementation-prompts.md. Implement Port.Prepare using a pinned managed IL writer. Retarget original XNA core references to FNA and phone/service references to Port.Bridge according to the ABI report. Rewrite all applicable metadata and IL operands, not only AssemblyRef names. Preserve the original DLL, emit the result only to private/prepared/, and record each change in a patch manifest with source hash, target signature, expected body shape/hash, new target, and postcondition. Reject any unexpected anchor or unsupported XAP version. Test the rewriter first on tiny authored fixture assemblies, then on the original DLL. Do not add gameplay changes in this stage.

Acceptance: deterministic output for the pinned input; the rewritten assembly passes metadata/IL validation; no original phone/XNA references remain where the report says they must be replaced; a tampered method fails closed; a second run never edits the original DLL. Update docs/implementation-log.md.
```

## Prompt 6 — Implement the minimum honest compatibility bridge

```text
Read docs/architecture.md and docs/plans/implementation-prompts.md. Implement only the exact GamerServices, Windows Phone, sensor, and runtime compatibility signatures required by the pinned original DLL. Match call shapes and object lifetimes; route actual desktop policies through small authored services. For paths not yet implemented, throw a specific diagnostic instead of returning a misleading success/default. Use the old port only to discover candidate policies, then verify those policies against original IL and any original-runtime observation. Add tests for the signatures and for service initialization. Make Port.Prepare retarget references to these bridge types.

Acceptance: the prepared DLL can be loaded and all game types enumerated by the launcher without FileNotFoundException, TypeLoadException, MissingMethodException, or InvalidProgramException; the bridge's unsupported paths are explicit and logged. Update docs/implementation-log.md.
```

## Prompt 7 — Make original content load locally

```text
Read docs/architecture.md and docs/plans/implementation-prompts.md. Inventory every content path the original DLL can load and inspect the XNBm v5 data, font, three WMA files, and companion folders from this XAP. Test direct FNA loading first with a small authored probe. Where incompatible, implement a local conversion or a narrow content adapter in Port.Content/Port.Bridge. Keep converted files and manifests in private/; pin conversion dependencies and record licenses. Preserve asset names, dimensions, timing, and lookup semantics; do not impose the old port's PNG-scaling design unless needed and justified. Handle Arial.xnb explicitly.

Acceptance: a local content probe loads every required asset type and reports exact failures; generated outputs are deterministic; no game asset is tracked or packaged. Add an asset coverage report and update docs/implementation-log.md.
```

## Prompt 8 — Reach a stable menu through the original game loop

```text
Read docs/architecture.md and docs/plans/implementation-prompts.md. Connect the prepared DLL, FNA host, bridge, and local content. Instantiate ZombiesWP7.Game1, call Run, and let the original Initialize/Update/Draw sequence execute. Patch only failures demonstrated by traces, each with the guarded patch contract. Add structured diagnostics for missing members/content and unhandled exceptions without dumping proprietary assets or IL. Determine the original render/backbuffer settings from original code and runtime evidence before changing them. The old port's 600×360 presentation is not an acceptance target.

Acceptance: from a clean private preparation, the game reaches a responsive menu, renders multiple frames, and exits cleanly on the target desktop. Document every hook and why it is needed; no broad replacement of Game1 is accepted. Update docs/implementation-log.md.
```

## Prompt 9 — Add playable desktop presentation and input

```text
Read docs/architecture.md and docs/plans/implementation-prompts.md. Map desktop mouse, keyboard, and controller input into the original game's touch/gamepad expectations through Port.Bridge and small hooks. Derive original coordinate transforms, touch/gesture ordering, pause/back behavior, and viewport from the original DLL and assets. Preserve original gameplay logic; input translation should synthesize inputs, not rewrite game decisions. Add resize/fullscreen/letterboxing only after measuring the original aspect ratio and coordinate system. Treat the old InputMgr and scaler as examples of prior attempts, not specifications.

Acceptance: menu navigation, starting a mode, movement, aiming, pause/resume, back, quit, and resize work with no coordinate drift or stuck input. Test mouse and at least one controller on hardware if available; record any untested device clearly. Update docs/implementation-log.md.
```

## Prompt 10 — Preserve saves and implement offline services

```text
Read docs/architecture.md and docs/plans/implementation-prompts.md. Implement desktop storage and offline profile/achievement/trial/leaderboard behavior at narrow bridge or hook points. First inspect the original serialization and service call order. Keep the game's serialized data format where feasible; store it under an application data directory with atomic writes and backup/recovery. Make any policy that differs from the phone service explicit (for example, no Xbox sign-in or online leaderboard). Avoid auto-awarding achievements or changing trial/gameplay flags just because the old port did. Add tests for fresh profiles, round trips, corrupt/old saves, and multiple restarts.

Acceptance: progress and settings persist across restart; achievements occur only at intended original call sites; service screens do not hang or crash offline; the exact differences from original phone services are documented. Update docs/implementation-log.md.
```

## Prompt 11 — Finish media and phone-only API behavior

```text
Read docs/architecture.md and docs/plans/implementation-prompts.md. Implement music, SFX, media-library, vibration, accelerometer, and marketplace-task boundaries exercised by the original XAP. Preserve the original timing/control flow where practical, using local converted audio and explicit desktop fallbacks for absent phone hardware/services. Inspect the original calls before choosing fallbacks; do not silently swallow errors or pretend a marketplace request succeeded. Add probes for song start/stop/pause/resume, SFX overlap, device absence, and quitting during playback.

Acceptance: all music and SFX needed by the menu and game modes play without exceptions; pause/resume and mode changes behave consistently; optional phone hardware absence is visible but does not block play. Update docs/implementation-log.md.
```

## Prompt 12 — Validate every mode against original evidence

```text
Read docs/architecture.md and docs/plans/implementation-prompts.md. Build a feature matrix from this original XAP: every menu path, original game mode (including Time Viking and endless/perks if confirmed present), difficulty/progression path, save point, and achievement trigger. Exercise each path on the new port with reproducible steps and logs. When behavior differs from the old port, inspect the original DLL and any original-runtime observation before deciding which behavior is correct. Fix port-caused differences with narrow guarded hooks; record unresolved parity questions instead of guessing. Use automated checks for deterministic logic where possible and manual play checks for visuals, audio, and input.

Acceptance: each mode can start, play for a meaningful session, pause/resume, end or quit, and return to the menu without crashes; progression and saves survive restart; the feature matrix has evidence and test status for every entry. Update docs/implementation-log.md.
```

## Prompt 13 — Build the user-friendly XAP installer

```text
Read docs/architecture.md, docs/plans/implementation-prompts.md, and installer/README.md. Build one Windows setup executable in installer/ using Inno Setup for the wizard and a bundled .NET helper that reuses Port.Prepare and Port.Content. The setup contains only authored runtime/tooling, pinned permitted dependencies, notices, and metadata-only XAP fingerprints. Implement Welcome, per-user Destination, Game XAP picker with read-only validation before Next, Ready, staged progress, and Finish with Launch or Close. Show clear wrong-version and corrupted-XAP errors. No Python, .NET SDK, compiler, or network may be required on the player's machine. Stage and verify generated DLL/content before committing, preserve a previous working install on failure/cancel, keep saves outside the install directory, and write local manifest/report. On reinstall/update, reuse verified compatible generated data or ask for the XAP again; never remove the source XAP. Do not bundle the XAP or any generated game file.

Acceptance: install with a supported XAP reaches a launchable game; invalid XAP is rejected before installation; failure/cancel leaves no claimed-successful partial install and preserves an earlier working one; reinstall/update/uninstall preserve saves by default. Test the helper independently and run the wizard on a Windows account without developer tools. Update docs/implementation-log.md with commands and observed results.
```

## Prompt 14 — Audit and rehearse the one-file release

```text
Read docs/architecture.md, docs/plans/implementation-prompts.md, and installer/README.md. Build the setup from a fresh checkout using a strict payload allow-list. Audit tracked files, installer payload, completed setup executable, symbols, embedded resources, manifests, and logs for original game DLL/IL, decompiled source, XAP, assets, generated game output, developer-machine paths, and secrets. Add negative-control tests that inject forbidden material and prove the audit fails. Run clean-install acceptance offline with a user-supplied supported XAP, and separately test unsupported XAP, reinstall, update, cancel, uninstall, and save preservation. Document licenses, support logs, and any remaining original-behavior uncertainty. Do not publish or upload without explicit user authorization and a rights review.

Acceptance: the one setup executable installs and launches on a Windows machine with no developer tools; every working-game gate above still passes; the audit reports clean and its negative controls fail as intended; no original or prepared game material is in the release artifact. Update docs/implementation-log.md with the exact final commands, results, and package hash.
```

## If a prompt cannot pass

Keep the failure visible in `docs/implementation-log.md`: exact command, exception or observed behavior, version/hash, and the smallest next experiment. Revise an earlier prompt or the architecture when the evidence changes. A passing build alone does not close a runtime or behavior gate.
