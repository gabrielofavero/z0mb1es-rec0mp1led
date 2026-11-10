# iOS execution decision and fallback design

Prompt 6 is **in progress**. The first experiment is a .NET 10 Mono interpreter host. There is no real-device execution trace, IPA, iOS FNA host, or evidence of imported gameplay yet. The [probe guide](../../installer/ios/README.md) contains reproducible source, desktop checks, build blockers, and the device procedure. The user confirmed AltStore setup on 2026-11-10; device details and observations are pending.

## Decision criteria

Keep original phone gameplay as the preferred path if ordinary AltStore Home Screen launches can interpret the locally rewritten game reliably, including fresh launch after reboot, all modes, media time, saves, and acceptable performance. Avoid NativeAOT for dynamic assembly import. Do not require per-launch JIT, distribute a prepared game DLL, or ask players to compile.

The generated-IL probe isolates one prerequisite. It cannot prove original-game ABI compatibility, interpreter handling of every generic/reflection path, native callbacks, graphics, audio, or performance. The minimum follow-up is an iOS build of FNA 26.11 plus device static libraries, an in-process preparation/ABI runner, and mobile adapters. Run the existing mode-cycle diagnostic through original `Game1` with isolated app-private saves, then visibly play every mode. Record native exceptions/crash reports, memory peaks, frame times and song clock drift. The desktop `RuntimeHelpers.PrepareMethod` sweep is not an iOS acceptance test.

If generated IL fails, preserve the Release build properties, full exception/native crash, payload hash, and a second run without a debugger/JIT service before concluding the configuration is unusable. If synthetic IL passes but the game fails, isolate the first actual ABI/runtime/interop problem before choosing a replacement. Toolchain absence does not invalidate the interpreter design.

## Concrete authored-gameplay alternative (contingent, not implemented)

If imported executable IL proves unreliable, ship authored gameplay compiled into the IPA and import **data only**. The same gameplay assembly can later target an Android-supported renderer. Its proposed boundaries are:

| Component | Responsibility and preservation requirement |
| --- | --- |
| `Mobile.Gameplay.Core` | Framework-independent fixed-step simulation, mode state machines, collision, spawning, scoring and achievements. Inputs are authored action records and a song-clock snapshot; outputs are draw/audio events and state snapshots. No runtime reflection into phone types. |
| `Mobile.Gameplay.Normal` | Recreate original `ZombieGame.Init(false)` / `Play(false)` behavior: movement/aim, enemies and projectiles, song-phase director, deaths, win/end state, score thresholds. Menu/play states correspond to original 2/3. |
| `Mobile.Gameplay.Endless` | Recreate `ZombieGame.Init(true)` / `Play(true)`, room scrolling, rounds, enemy progression and powerups with its independent music clock. Preserve round-completion achievements and normal/Endless distinctions. Original states are 6/7. |
| `Mobile.Gameplay.Viking` | Recreate `VikingGame.Init` / `Play`, character movement/attack, tower cells and scrolling, moon interactions, song phases, height/score and ending conditions. Original states are 4/5. |
| `Mobile.Gameplay.Presentation` | Scene/menu state, animation, particles, sprites and audio events driven by simulation state; renderer consumes imported phone textures/fonts/scenes and optional mapped art. Reproduce the original 600×360 behavior before adapting mobile aspect/safe areas in Prompt 7. |
| `Mobile.DataImport` | Exact XAP guard; XNB/scene/map decoding and the in-process song converter; explicit schemas for imported asset references and declarative tables. It must never load the game assembly as executable code. Optional-art inputs remain optional and independent. |
| `Mobile.SaveCodec` | Implement the original version-101 stream format independently from its observed schema, with golden local fixtures for profile, player and customizer data. Preserve original backups until a new format/roundtrip is verified; do not overwrite a desktop save merely because fields appear similar. |
| `Mobile.Host` | Statically compiled iOS game entry point, system pickers, persistent transaction, graphics/audio, input, suspend/resume and photo import. Android later implements the same services using its selected runtime. |

The state numbers and entry points above are established by the current [mode evidence](../records/feature-matrix.md) and authored desktop `ModeProbe`; they are not a complete gameplay specification. No claim of exact parity follows from them. Multiplayer/player-count behavior, random sequence, scene choreography, enemy rules, score formulas and endings require explicit evidence before implementation.

Build an evidence ledger per system from controlled runs of the user-owned XAP. Record action sequences, fixed elapsed times, song positions, state transitions, entity snapshots, collision outcomes, score/achievement changes and save bytes. Label anything inferred from original IL. Extracted numerical tables, choreography and metadata remain local; distribute only authored extraction recipes and schema/hash guards. Do not smuggle a bytecode interpreter or a translated method instruction stream into a supposedly data-only import. Behavior embedded in code must be reimplemented as authored code from an independently reviewed specification.

Implement the common world/action/save contracts first, then one complete original-art Normal cycle, Endless rooms/rounds, and Viking tower/ending. For each system, replay the same controlled input sequence through the original desktop runtime and authored engine, normalize observation fields, and compare timing/state/events. Use original phone hardware observations when available; desktop hooks are not a golden oracle. Complete long-form play and audiovisual comparisons after the short mode cycles. Retain separate legacy and authored save fixtures until every serializer field and achievement trigger has a documented mapping.

## Preservation and licensing limits

This alternative changes the gameplay implementation and introduces a substantial parity risk. The existing project intentionally executes original phone logic; the old decompiled project cannot become the source for a bundled replacement. Keep specs, observations and independently written implementation provenance distinct. No decompiled methods, original strings/tables, choreography or media may be checked in or bundled merely because they are easy to recover. Player ownership and local import do not by themselves settle distribution rights. The existing release rights review must cover the authored implementation, extraction recipes and dependency licenses before publication.

This document records an actionable alternative architecture and the evidence work needed. It does not select that alternative or close the plan's acceptance gate: imported IL has not failed on a device, and the replacement's complete behavior specification and preservation review do not yet exist. Continue Prompt 6 until one route has sufficient evidence; leave Prompts 7–9 open.
