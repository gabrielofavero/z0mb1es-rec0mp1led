# Windows installer design

This is a design for the future installer, following the user-facing shape of `rb-blitz-xenon-recomp/installer`. It is **not implemented yet**. The product is one setup executable containing the authored FNA port runtime, native dependencies, and a preparation helper. The player supplies a supported `Z0MB1ES (on teh ph0ne)` XAP when setup asks for it. No original game package, DLL, content, or prepared output is embedded in the setup.

## User experience

| Wizard page | Behavior |
| --- | --- |
| Welcome | Says a local copy of the game XAP is required and that setup works offline. |
| Destination | Defaults to a per-user install location, using Inno Setup's [`PrivilegesRequired=lowest`](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm) to avoid an admin prompt. |
| Game XAP | Browse to a `.xap`; verify its hash, original DLL hash, manifest identity, and required entries before Next is enabled. Explain wrong versions clearly. |
| Ready | Show destination, detected game version, and what will be installed. |
| Progress | Show extraction, compatibility preparation, content conversion, verification, and commit as separate stages. Support cancel without leaving a claimed-successful installation. |
| Finish | Choose Launch game or Close. A failed install never launches. |

Use [Inno Setup custom wizard pages](https://jrsoftware.org/ishelp/topic_scriptpages.htm) for presentation and sequencing. Put archive parsing, cryptographic checks, IL rewriting, content conversion, file placement, and logs in a shipped .NET helper that shares the same preparation engine as the CLI. The player should need only the setup executable and their XAP: no Python, .NET SDK, compiler, internet connection, or manual file copying. Bundle the runtime required by the helper and launcher.

## Boundaries

The setup may contain port-authored executables, FNA/native dependencies with their notices, patch recipes, metadata-only XAP fingerprints, and port-owned artwork. It must not contain a XAP, original `ZombiesWP7.dll`, decompiled source, converted content, or a locally prepared DLL. The developer's existing XAP in the repository root is a local input and must fail the release audit if accidentally included.

The **player points setup at their own XAP**. The source XAP stays where the player selected it; setup records a hash and version, not a second copy of the package. Generated game files are local install output.

## Files and lifecycle

```text
installer/setup.iss                 # future wizard and sequencing
installer/src/Port.SetupHelper/    # future validation, preparation, install commands
installer/config/                  # future installer version and payload allow-list
installer/tests/                   # future helper and clean-install tests
installer/out/                     # ignored: built payload and setup executable
{user install}/ZombiesPort.exe     # authored launcher
{user install}/game/               # locally generated DLL/content
{user install}/install-manifest.json
{user install}/install-report.txt
{user data}/ZombiesPort/           # saves/settings, outside install directory
```

The helper exposes stable commands for read-only `validate-xap`, staged `prepare`, `verify-installed`, and `commit`. It emits structured progress for the wizard. A stage writes to a temporary sibling directory, verifies hashes and required files, then commits; failure or cancellation removes staging and leaves any prior working installation usable. The wizard must never report success based only on a zero-exit extraction step.

On reinstall or update, verify the existing generated game data. Reuse it when its source XAP hash and preparation format remain compatible; otherwise ask for the XAP again. Do not silently re-import from a remembered arbitrary path. Preserve saves by default on uninstall; any deletion of player data must be an explicit choice. Do not remove the source XAP.

## Release and acceptance

Build from a clean checkout into one setup executable. Use an allow-list for the files embedded by the setup. Scan that executable, its payload, symbols, manifests, and tracked files for original game bytes, assets, machine paths, and secrets. Test an install on a Windows account with no development tools, no network, and a supported XAP; also test invalid XAP, cancelled preparation, fresh install, reinstall, update, uninstall, and preserved saves. The game-play gates in [implementation-prompts.md](../docs/plans/implementation-prompts.md) must pass before the installer is presented as ready.
