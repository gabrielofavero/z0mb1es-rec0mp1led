# Recompilation without shipping the game's decompiled code

## Decision

Make this repository the new port project. Treat the original **compiled** `ZombiesWP7.dll` inside a user-supplied XAP as the game implementation. Keep the decompiled tree in `../z0mb1es-on-teh-fna` as a private, read-only reference; it is never a project reference, build input, package input, or submodule of the new project. All port behavior belongs in independently maintained launcher, compatibility, and patch code in this project. A local preparation command produces the runnable, transformed game assembly from the user's XAP. The transformed assembly and extracted assets remain local.

This is the architecture and migration plan for an early working port. The XAP extractor, read-only assembly metadata/IL inspector, authored FNA smoke host, deterministic .NET local extraction engine, static ABI validator, guarded local IL rewriter, compatibility bridge, [local content pipeline](../runtime/content-coverage.md), [desktop input](../runtime/desktop-input.md), [desktop saves and offline services](../runtime/offline-services.md), and [media and phone fallbacks](../runtime/media-phone-behavior.md) are implemented and tested here. The inspector produces a reproducible JSON inventory for the exact supported XAP; see [the evidence ledger](../records/behavior-evidence.md). The smoke host uses pinned FNA and native dependencies; see [dependency setup](dependencies.md). The preparation engine extracts and records original files; its [guarded rewrite command](../compatibility/il-rewriter.md) produces a local prepared DLL. The prepatch [ABI report](../compatibility/abi-compatibility.md) verifies all referenced bridge signatures and flags the original application-scoped isolated-storage path for review; the guarded desktop storage patch replaces that path. The [launcher load check](../compatibility/bridge-compatibility.md) enumerates every game type and JIT-prepares every method body. Feature parity and an interactive installer check on a separate account without developer tools remain later gates. No architecture alone establishes rights to distribute the patcher, the game, or derived outputs; get a rights review before release.

## Evidence from this copy

The local `Z0MB1ES (on teh ph0ne) v1.0.0.0.xap` is a ZIP archive with 127 entries and 76,993,533 uncompressed bytes. Its SHA-256 is `6b88c7fbd95444d2560b4b11e6f35d40a6c0f7abc30706155dcbccd18bc333fe`. It contains `ZombiesWP7.dll` (750,136 bytes), 91 `.xnb` files, three `.wma` files, and companion data. `AppManifest.xaml` names `ZombiesWP7.Game1` as the entry type; the phone manifest declares XNA and platform version 7.1.

The DLL's SHA-256 is `7b819cb562826ae4d2592ff741877e76da0e57bfbe48192aabb79319fdb36823`. It identifies itself as `ZombiesWP7 1.0.0.0` with 178 types and 818 methods. It references `mscorlib` and `System` **2.0.5.0**, five XNA 4.0 assemblies (including `Microsoft.Xna.Framework.GamerServices`), `Microsoft.Devices.Sensors` 7.0, and `Microsoft.Phone` 7.0. Its metadata exposes `Game1.Initialize`, `LoadContent`, `Update`, `Draw`, and `OnExiting`, plus `StorageMgr.Read/Write`, `TrialMgr.CheckTrial`, `AchievementMgr.AwardAchievement`, and `Music` methods. The `InputMgr` in the old source tree is a port addition; there is no `InputMgr` type in this original DLL.

Comparison against the old project's local FNA `net8.0` build found the core XNA type names, but 14 referenced GamerServices types are absent (including `GamerServicesComponent`). This is a **type-name check only**; it does not prove member signatures, virtual methods, content handling, or game execution. The old repository also changes `Game1` directly for rendering, input, saves, achievements, trial behavior, media, and assets, so it cannot be used as a distributable source base.

## Boundaries and build flow

```mermaid
flowchart LR
    X[User-supplied XAP] --> E[Local extraction and version check]
    E --> O[Private original DLL and assets]
    O --> P[Local IL preparation tool]
    P --> G[Private prepared game DLL]
    O --> C[Local content conversion]
    G --> L[Desktop launcher]
    C --> L
    B[Port-owned bridge and hooks] --> P
    B --> L
    F[FNA and native libraries] --> L
    R[Private decompiled reference] -. read only .-> B
```

Project layout, following the same separation used in the Xenon recomp while keeping .NET/FNA-specific projects:

```text
docs/foundation/architecture.md
docs/plans/done/2026-11-06-initial-implementation/implementation-prompts.md
config/                           # supported XAP identities, tool/dependency pins
docs/                             # evidence, guides, implementation log, limitations
game/                             # local XAP input; contents ignored
assets/                           # port-owned artwork for launcher/installer only
installer/                        # wizard, helper, build and tests; never game data
patches/                          # authored patch specs and guarded hooks
scripts/                          # build and distributable audits
src/Port.Launcher/                # authored desktop entry point and original-loop launcher
src/Port.Bridge/                  # authored API shim and service library boundary
src/Port.Prepare/                 # reusable verified local extraction engine
src/Port.Hooks/                   # authored guarded-hook library boundary
src/Port.Content/                 # reusable inventory and local content preparation
src/Port.Smoke/                   # separate authored FNA window/render probe
tools/abi_check/                  # implemented: static full-signature ABI validator
tools/extract_xap.py              # implemented: Python developer ZIP extraction tool
tools/prepare-cli/                # implemented: .NET developer preparation CLI
tools/inspect_assembly/           # implemented: read-only metadata inventory
tools/content-probe/              # implemented: direct FNA asset/audio load probe
tools/content-prepare/            # implemented: verified local content converter
generated/                        # ignored, reproducible metadata and ABI reports
out/                              # ignored, builds and installer artifacts
private/original-xap/             # ignored, developer extractor output
private/preparation/              # ignored, .NET preparer extraction and provenance
private/prepared/                 # ignored, rewritten and hooked DLLs
private/run/                      # ignored, staged local game runtime and content
../z0mb1es-on-teh-fna/            # existing private reference only
```

`src/` contains host-owned runtime code, `patches/` records exactly which original methods are changed, `config/` pins supported inputs and dependencies, and `docs/` carries evidence. These are the useful Xenon conventions. The Xbox translation SDK, CMake, generated C++, and STFS handling are specific to that project and do not belong in this FNA port. The XAP, `game/` contents, `generated/`, `out/`, and `private/` are ignored by this directory's `.gitignore`. Do not add original `.cs`, `.dll`, `.xnb`, `.wma`, maps, textures, or generated patched binaries to a release artifact or Git history. Check the release archive itself, including symbols and embedded resources. The old repository's history already contains decompiled code, so make the new release repository from a fresh history; copying that repository and deleting its source in a later commit would retain it in history. Existing forks and remote copies need separate handling.

### Folder ownership and use

The top-level layout follows the Xenon repository's `assets`, `config`, `docs`, `game`, `generated`, `installer`, `out`, `patches`, `scripts`, `src`, `tests`, and `tools` split. The extra `private/` folder is necessary here because preparing a user's XAP produces an original extraction, a rewritten DLL, and later converted content. Keeping those outputs apart from the source XAP and from `generated/` reports makes their origin clear.

| Folder | Concrete use | Stage |
| --- | --- | --- |
| `assets/` | Authored launcher icon and installer art; never extracted game assets | In use |
| `config/` | Supported XAP hashes and pinned dependency versions | In use |
| `docs/` | Evidence, design, preparation guides, and implementation log | In use |
| `game/` | Ignored local input XAP for developer commands; the installer accepts any selected path | In use |
| `generated/` | Ignored inspector inventory and ABI reports derived from the input | In use |
| `installer/` | Inno wizard, self-contained preparation helper, pinned runtime packs, and installer build | In use; interactive account check remains |
| `out/` | Ignored dependency cache, native libraries, and local build/release outputs | In use |
| `patches/` | Authored, hash-guarded method adaptation records; paired with `Port.Hooks` | In use |
| `private/` | Ignored XAP extraction, provenance, rewritten DLL, and converted content | In use |
| `scripts/` | Offline dependency/bootstrap build now; release audits in Prompt 14 | In use |
| `src/` | Authored launcher, bridge, hooks, preparation and content engines, smoke host | In use |
| `tests/` | Authored preparation, bridge, ABI, and rewrite checks | In use |
| `tools/` | Developer extractor, inspector, ABI checker, and preparation CLI | In use |

`game/` is the source package location for local examples; `private/` is output from that package. `assets/` may contain only port-owned artwork. `generated/` holds reproducible analysis reports, whereas `out/` holds dependencies and build products. These roles match Xenon's separation without copying its Xbox-only `rexglue-sdk`, CMake tree, or temporary probe folders. Empty reserved folders should gain the stated implementation at their prompt gate; if a gate uses no such asset or patch, remove its placeholder rather than keep an unused category.

| Xenon convention | This port |
| --- | --- |
| `game/` ignored local source | Local XAP folder for developer commands; installer also accepts any XAP path |
| `src/hooks/` and `patches/` | Authored .NET hooks plus guarded IL patch records |
| `config/` fingerprints/pins | Supported XAP identities and pinned FNA/content/IL dependencies |
| `generated/`, `out/` | Ignored local generated data and build products |
| `docs/` and bring-up records | Evidence ledger, implementation log, guides, limitations |
| `installer/` wizard and helper | Inno Setup wizard plus .NET preparation/install helper |
| `scripts/setup/audit_distributable` | New release audit for the one-file setup executable |

### Preparation order

1. Require the user's XAP path. Verify its SHA-256 against an explicit supported-version table, then inspect package structure. Fail closed on unknown builds. The hash above identifies this one local sample; do not hard-code a claim that it identifies every legitimate copy.
2. Use `Port.Prepare` through `tools/prepare-cli` to validate the exact XAP and DLL, then extract original files into a new private output directory with deterministic provenance. `tools/extract_xap.py` remains a developer-only comparison tool. Keep the source DLL byte-for-byte intact and use a new output directory per attempt.
3. Prepare assets locally. Adapt the old extractor *workflow* for this project, with its dependencies pinned and licenses recorded; it converts phone XNB/audio and moves companion folders. Do not copy extracted content into tracked source. Preserve `Arial.xnb` handling as a separate validation item, since the old pipeline explicitly excluded it from conversion. The inspected XNB samples use `XNBm` version 5, so test actual load behavior with FNA rather than assuming compatibility.
4. Build the launcher, bridge, and hooks from authored source. Run a deterministic IL rewrite of the **original DLL on the user's machine**. Use a managed IL writer such as [Mono.Cecil](https://www.mono-project.com/docs/tools%2Blibraries/libraries/Mono.Cecil/) for this stage, pinned to a tested version. `System.Reflection.Metadata` in the current inspector is read-only here.
5. Launch the prepared assembly only after all references and required patches pass validation. The launcher creates `ZombiesWP7.Game1` through reflection and calls `Run` within FNA's runtime. Do not distribute a preprepared DLL.

### Player-facing installer

Follow the Xenon project's **product shape**: one Windows setup executable carrying the authored runtime and a preparation helper. Use an [Inno Setup custom wizard page](https://jrsoftware.org/ishelp/topic_scriptpages.htm) for the XAP picker; keep ZIP parsing, hashes, IL rewriting, content conversion, file placement, and manifests in the .NET helper, not Pascal Script. The release setup must work offline and must not require Python, the .NET SDK, or a compiler on the player's machine. `tools/extract_xap.py` remains a developer tool; the helper must implement the same safe extraction contract in the shipped runtime.

**The player selects a XAP they already have.** The setup contains no XAP, original DLL, content, or preprepared game output. The implemented flow and current test limits are recorded in [installer/README.md](../../installer/README.md).

The wizard flow is: welcome and requirements; install folder; XAP selection and read-only validation; ready summary; preparation/install progress; finish with Launch or Close. Validate identity and required entries before any installation changes. Stage the prepared DLL/content, verify them, then commit so a failed import cannot leave an apparently successful install or destroy a prior playable one. Record input/output hashes and versions in a local install manifest and human-readable report. Reinstall/update may reuse verified installed generated game data when compatible; otherwise ask for the XAP again. Do not retain a copy of the XAP by default. Keep saves outside the install directory and preserve them on uninstall unless the player explicitly chooses removal.

### Why the rewrite must precede hooks

The CLR resolves type and assembly references while loading types and JIT-compiling methods. A runtime hook cannot repair a missing base type such as `GamerServicesComponent` after loading fails. The preparer must first retarget XNA core type references to the one `FNA` assembly; route GamerServices and phone-only types to `Port.Bridge`; and rewrite unsupported .NET/Windows Phone calls. It must update *type scopes, member references, signatures, generic arguments, custom attributes, and exception handlers* as applicable, not just rename AssemblyRef rows. Validate every changed method body and every referenced member before running. Avoid pretending an assembly-resolution callback alone can make old XNA types appear in FNA.

Prefer narrow authored compatibility types for services that the original DLL expects (`GamerServicesComponent`, sign-in/profile, achievement and leaderboard APIs, sensors, vibration, phone tasks). Implement only the signatures actually used by the pinned DLL. The bridge owns the desktop behavior: local profile, save path, offline achievements, trial policy, and media access. Core FNA types come from FNA, not shadow copies of XNA's assemblies. Inspect FNA's APIs against *members*, not just names, and patch or adapt mismatches explicitly. The `mscorlib`/`System` 2.0.5.0 references and isolated storage calls are a separate binding/compatibility gate, not an assumed automatic fix.

### Patch contract

Keep each adaptation in `Port.Hooks` or `Port.Bridge`; the preparer only wires calls to it. A patch record should identify the original XAP hash, target assembly/type/method with full signature, expected method-body hash or structural IL pattern, transformation, bridge method, and postcondition. Refuse to patch if an anchor or expected body differs. Write the prepared DLL to a new path, never over the original. Ensure repeat runs produce the same output hash for the same inputs and tool versions.

Use the smallest safe interception point. For example, a prefix or replacement of `StorageMgr.Read/Write` can delegate to an authored desktop save service; `Game1.Draw` can be wrapped by an authored presenter; `Game1.Update` can call an authored input adapter before the original logic. These are candidate hooks, not verified requirements. For features that require changing many instructions, replace only the identified method body with a call to authored logic **after** mapping its required original behavior. Preserve original object fields, static initialization, game timing, and `base` calls where required. Hook methods can accept `object` and stable primitives or use a generated, local-only interface facade; the distributable source must not compile against decompiled game source. Reflection is acceptable at narrow boundaries but should not be the entire hot path.

Runtime patching (for example, [Harmony](https://harmony.pardeike.net/v2/)) can supplement optional behavior **after** the prepared assembly loads, but is not the primary compatibility layer. Start with deterministic, testable pre-load IL rewriting. A compiler from a decompiled project would reintroduce the source distribution problem.

## Migration of work from the old repository

The original 600×360 viewport and `TouchMgr.Update` call order are now established from the original DLL and running port. The desktop input boundary is four guarded call-operand changes in that method, with authored contact and gesture generation in `Port.Hooks`. The launcher permits resizing and maps pointer coordinates through a centered 5:3 content rectangle. Mouse aiming derives a direction from the original on-screen player position to the pointer, then supplies a right-stick contact. A focused run reached gameplay, registered mouse aim, and opened and resumed the original pause menu with Escape and Enter. Controller Start/Select behavior and live resize remain for hands-on confirmation. The bridge's full-game trial choice is an explicit offline desktop policy, separate from the original phone licensing service. See [desktop input](../runtime/desktop-input.md).

| Existing work seen in `../z0mb1es-on-teh-fna` | New owned boundary | First verification |
| --- | --- | --- |
| Desktop `Program` and modified `Game1` lifecycle | `Port.Launcher` plus narrow `Game1` hooks | Construct, load, update, draw, exit |
| Mouse/controller additions in new `InputMgr` | `Port.Bridge/Input` and update hook | Touch, mouse, pad, pause, menu in both modes |
| Render target, integer scaling, asset scaler | `Port.Bridge/Presentation` and content adapter | Determine original viewport first; then verify resizing, fullscreen, texture dimensions |
| Phone storage changed to `FileStream` | `Port.Bridge/Storage` and storage method hooks | Read/write fresh and existing saves; version handling |
| GamerServices, trial, achievements, leaderboards | `Port.Bridge/Services` plus selected hooks | Offline behavior, progress persistence, no stalled dialogs |
| Media library and converted WMA/audio | `Port.Bridge/Media` plus local content pipeline | All songs/SFX play and stop at correct times |
| Sensors, vibration, marketplace tasks | `Port.Bridge/Phone` compatibility methods | No startup or gameplay load failures |
| Optional PNG swapping and upscaling | Later bridge/content feature | Base game works first; overrides load only when selected |

Use the old commits and source only as a requirements checklist. Their output is a diagnostic reference, not a behavioral oracle: it may contain accidental gameplay changes. Reimplement authored behaviors in new files, and review each candidate copied helper for original game code or data before moving it. Some work will be reusable as an idea or independently written algorithm; the decompiled game methods themselves stay private.

## Proof gates before calling this architecture working

1. **Metadata/ABI gate:** The [machine-readable ABI report](../compatibility/abi-compatibility.md) accounts for all external type and member reference tokens and checks full signatures, inheritance, virtual slots, and event accessors against pinned FNA, .NET 10, and the authored bridge. All 19 GamerServices/phone types and 45 members now resolve. The prepared assembly loads, all game types enumerate, and all 818 method bodies JIT-prepare. The report flags the original application's isolated-storage call for review; the guarded desktop storage patch replaces it. Reflection-observed names remain future work.
2. **Boot gate:** Rewrite the exact sample DLL, launch without the phone assemblies, reach the menu, and exit cleanly with no `FileNotFoundException`, `TypeLoadException`, `MissingMethodException`, `InvalidProgramException`, or native library failure.
3. **Content gate:** The [Prompt 7 probe](../runtime/content-coverage.md) loads all original XNBs and all locally prepared XNBs, including the font and five path aliases. The three WMA companions need pinned local Ogg conversion for nonzero FNA playback duration. Custom companion files are copied byte for byte. Actual game lookup and music timing remain to be checked during the boot and behavior gates; no original-runtime observation is available yet.
4. **Behavior gate:** Test every mode and feature confirmed in the original XAP, including endless/perks where present, plus input, save/load, achievements, pause/resume, quit/restart, and different resolutions. Recheck the exact methods and effects of each patch.
5. **Release gate:** Build the one-file installer containing only launcher, bridge, hooks, preparation helper, pinned permitted dependencies, and instructions. Scan tracked files and the setup executable for the original assembly, decompiled source, game assets, and generated outputs. The automated audit and local silent-install rehearsal passed for Prompt 14. Test a clean Windows machine with an independently supplied XAP and no developer tools; this visual check was deferred by the user. Obtain a rights review before any public distribution. The release gate remains open.

## Tools available now

From the repository root on Windows with Python 3.12 and the pinned .NET 10 SDK:

```powershell
python tools/extract_xap.py 'game/Z0MB1ES (on teh ph0ne) v1.0.0.0.xap' --list
python tools/extract_xap.py 'game/Z0MB1ES (on teh ph0ne) v1.0.0.0.xap' --output private/original-xap
dotnet run --project tools/inspect_assembly -- private/original-xap/ZombiesWP7.dll
dotnet run --project tools/inspect_assembly -- private/original-xap/ZombiesWP7.dll out/deps/managed/FNA.dll
dotnet run --project tools/inspect_assembly -- --json --xap 'game/Z0MB1ES (on teh ph0ne) v1.0.0.0.xap' --output generated/evidence-from-xap.json
dotnet run --project tools/prepare-cli -- --xap 'game/Z0MB1ES (on teh ph0ne) v1.0.0.0.xap' --output private/my-preparation
```

The extractor validates archive paths, rejects links and duplicate names, refuses to write into a nonempty destination, and prints a package hash and size summary. The inspector uses only .NET's metadata reader; it does not execute or decompile the game. Its `--json` mode verifies the exact supported XAP and DLL hashes, then emits metadata rows and IL call/string candidates under ignored `generated/`. The extraction command above has already succeeded for this XAP and produced the ignored `private/original-xap/` tree. Re-extraction requires a new empty destination or removing the old local copy deliberately.

FNA's [documentation](https://fna-xna.github.io/docs/) describes it as an XNA 4.0 reimplementation; its [XNA porting guide](https://fna-xna.github.io/docs/2a%3A-Building-XNA-Games-with-FNA/) discusses content and native libraries. These references support the chosen base library, while the phone-specific ABI and this game's runtime behavior still need the proof gates above.
