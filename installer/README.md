# Windows installer design and implementation status

This installer follows the user-facing shape of `rb-blitz-xenon-recomp/installer`. `setup.iss`, `build.ps1`, and `Port.SetupHelper` build one offline setup executable. The player supplies a supported `Z0MB1ES (on teh ph0ne)` XAP. No original game package, DLL, content, or prepared output is embedded in setup. The user deferred the interactive wizard check on a Windows account without developer tools; the silent install and helper tests are recorded in the implementation log.

Build from the repository root after dependency bootstrap:

```powershell
./installer/build.ps1 -FfmpegPath 'C:/path/to/pinned/ffmpeg.exe'
```

The converter must match `config/content-tools.json`. The build checks the .NET runtime-pack hashes in `installer/config/runtime-packs.json` and writes `installer/out/ZombiesPort-Setup.exe`. The player needs only that executable and their XAP. For automated testing, setup accepts `/XAP="C:\path\to\game.xap"` together with Inno Setup's `/VERYSILENT` and `/DIR` switches.

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

The setup may contain port-authored executables, FNA/native dependencies with their notices, patch recipes, metadata-only XAP fingerprints, and port-owned artwork. It must not contain a XAP, original `ZombiesWP7.dll`, decompiled source, converted content, or a locally prepared DLL. The developer's local XAP in `game/` is input only and must fail the release audit if accidentally included.

The installer artwork is prepared in [assets](../assets/README.md). `setup.iss` uses the red icon, gray uninstaller icon, top badge, and seven-size side-image ladder.

The **player points setup at their own XAP**. The source XAP stays where the player selected it; setup records a hash and version, not a second copy of the package. Generated game files are local install output.

## Files and lifecycle

```text
installer/setup.iss                 # wizard, validation, progress, rollback, uninstall
installer/src/Port.SetupHelper/    # validation, preparation, verification, commit
installer/config/runtime-packs.json # pinned self-contained .NET runtime packs
installer/out/                     # ignored: built payload and setup executable
{user install}/current/Port.Launcher.exe
{user install}/current/ZombiesWP7.dll
{user install}/current/Content/
{user install}/current/install-manifest.json
{user install}/current/install-report.txt
{local app data}/Z0MB1ES on teh ph0ne/  # saves, outside install directory
```

The helper exposes `validate-xap`, `verify-installed`, and `install`. It verifies the selected XAP before Inno begins installation, generates the ABI report locally, reuses `Port.Prepare` and `Port.Content`, stages output beside the destination, hashes every staged file, then commits. Inno displays the helper's stage lines. On update, the previous `current/` directory is kept as `current.previous/` until Inno finishes. If Inno fails later, `DeinitializeSetup` restores it. An interrupted preparation cannot create a successful install manifest at the destination. A forced process kill can leave an abandoned work directory; a later build or manual cleanup can remove that ignored directory.

On reinstall or update, verify the existing generated game data. Reuse it when its source XAP hash and preparation format remain compatible; otherwise ask for the XAP again. Do not silently re-import from a remembered arbitrary path. Preserve saves by default on uninstall; any deletion of player data must be an explicit choice. Do not remove the source XAP.

## Release and acceptance

Run `python installer/audit-release.py --xap <supported-local-xap>` after building. This developer-side audit checks repository inputs, the strict payload allow-list, original XAP entry hashes, symbols, machine paths, secrets, and the completed setup executable. It runs five negative controls. The build removes stale publish directories before packaging. The resulting setup contains dependency notices under `current/notices/` after installation.

For support, the local installation has `current/install-report.txt` and `current/install-manifest.json`. An Inno setup log can be requested with `/LOG=<local-file>`; review it before sharing because it records local paths. Saves remain outside the install directory. Original-behavior uncertainty is tracked in the [feature matrix](../docs/records/feature-matrix.md).

Prompt 14's automated audit and local silent-install rehearsal passed. The clean-account visual check, full hands-on game gates, and redistribution rights review remain open, so the release gate is not closed and nothing has been published. Prompt 13 is finished for the agreed scope; its interactive wizard check on a separate Windows account without developer tools was deferred by the user. The [implementation log](../docs/records/implementation-log.md) records the exact local test results and package hash.
