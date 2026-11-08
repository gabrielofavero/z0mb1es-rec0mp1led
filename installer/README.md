# Windows installer design and implementation status

This installer follows the user-facing shape of `rb-blitz-xenon-recomp/installer`. `setup.iss`, `build.ps1`, and `Port.SetupHelper` build one offline setup executable. The player supplies a supported `Z0MB1ES (on teh ph0ne)` XAP. No original game package, DLL, content, or prepared output is embedded in setup. The user deferred the interactive wizard check on a Windows account without developer tools; the silent install and helper tests are recorded in the implementation log.

Build from the repository root after dependency bootstrap:

```powershell
./installer/build.ps1 -FfmpegPath 'C:/path/to/pinned/ffmpeg.exe'
```

The converter must match `config/content-tools.json`. The build checks the .NET runtime-pack hashes in `installer/config/runtime-packs.json` and writes `installer/out/Z0MB1ES-0.1.0.exe` plus `installer/out/FFmpeg-8.1.1-source.zip`. Both files must be offered together at the same release location; the player only needs setup, their XAP, and any optional Steam/360 source they choose. See the [FFmpeg source build and delivery instructions](ffmpeg/README.md). For automated testing, setup accepts `/XAP="C:\path\to\game.xap"` together with Inno Setup's `/VERYSILENT` and `/DIR` switches. Silent optional import uses `/ARTNORMAL=steam|360`, `/ARTNORMALSOURCE=<path>`, `/ARTVIKING=360`, and `/ARTVIKINGSOURCE=<path>`; `keep` and `none` support later updates and removal.

## User experience

| Wizard page | Behavior |
| --- | --- |
| Welcome | Introduces the fan-made PC port and says the player's own XAP is required. |
| Destination | Defaults to a per-user install location, using Inno Setup's [`PrivilegesRequired=lowest`](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm) to avoid an admin prompt. |
| Game XAP | Browse to a `.xap`; Next checks the exact supported package SHA-256 without starting the .NET helper. Full DLL, manifest, and entry checks run during local preparation. Explain wrong versions clearly. |
| I MAED A GAM3 W1TH Z0MB1ES 1N IT!!!1 artwork | Choose no import, Steam, or an Xbox 360 package. Keep and remove appear when this install contains imported artwork. Original artwork stays available in game settings. |
| TIME VIKING artwork | Independently choose no import or an Xbox 360 package. Keep and remove appear when this install contains imported artwork. |
| Shortcuts | Choose Start Menu and Desktop shortcuts independently. Start Menu is checked on a new install. |
| Ready | Show destination, detected game version, and what will be installed. |
| Progress | Show extraction, compatibility preparation, content conversion, verification, and commit as separate stages. Support cancel without leaving a claimed-successful installation. |
| Finish | Choose Launch game or Close. A failed install never launches. |

Use [Inno Setup custom wizard pages](https://jrsoftware.org/ishelp/topic_scriptpages.htm) for presentation and sequencing. Put archive parsing, cryptographic checks, IL rewriting, content conversion, file placement, and logs in a shipped .NET helper that shares the same preparation engine as the CLI. The player should need only the setup executable and their XAP: no Python, .NET SDK, compiler, internet connection, or manual file copying. Bundle the runtime required by the helper and launcher.

After an import, open Settings → ASSETS to choose Original or the installed source for I MAED, Time Viking, and Endless separately. The first verified import becomes the default. The game checks imported pixels at launch and selection; an unavailable source uses Original art and the Settings row directs the player to rerun setup to repair it. Choices are stored beside the phone save in `assets.json`, so an update or uninstall does not erase them.

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
{user install}/current/Z0MB1ES.exe
{user install}/current/ZombiesWP7.dll
{user install}/current/Content/
{user install}/current/optional-art/  # verified optional generations and route manifests
{user install}/current/install-manifest.json
{user install}/current/install-report.txt
{local app data}/Z0MB1ES on teh ph0ne/  # saves, outside install directory
```

The helper exposes `validate-xap`, `validate-art`, `verify-installed`, and `install`. The wizard uses an exact SHA-256 check on Next; the helper performs full XAP verification during installation. Setup unpacks the runtime and only the tools needed for fresh preparation or selected artwork. It generates the ABI report locally, reuses `Port.Prepare` and `Port.Content`, converts the three music tracks concurrently, prepares selected art with the bundled offline importer, hashes every staged file, then commits. The progress bar follows completed files and FFmpeg encoding time; stage labels explain the current work. On update, the previous `current/` directory is kept as `current.previous/` until Inno finishes. If Inno fails later, `DeinitializeSetup` restores it. An interrupted preparation cannot create a successful install manifest at the destination. A forced process kill can leave an abandoned work directory; a later build or manual cleanup can remove that ignored directory.

On reinstall or update, verify the existing generated game data. Reuse it when its source XAP hash and preparation format remain compatible; otherwise ask for the XAP again. Optional art can be kept, replaced, repaired, or removed independently, including when one installed generation is corrupt. Do not silently re-import from a remembered arbitrary path. Preserve saves by default on uninstall; any deletion of player data must be an explicit choice. Do not remove source game packages.

## Release and acceptance

Run `python installer/audit-release.py --xap <supported-local-xap>` after building. This developer-side audit checks repository inputs, the strict payload allow-list, original XAP entry hashes, symbols, machine paths, secrets, the completed setup executable, and the matching FFmpeg source ZIP. It runs five negative controls. The build removes stale publish directories before packaging. The resulting setup contains dependency notices under `current/notices/` after installation.

For support, the local installation has `current/install-report.txt` and `current/install-manifest.json`. The Release game executable is built as a Windows GUI app, so launching it from a shortcut or the Finish page does not open a console window; runtime diagnostics still go to the per-user run log. Debug builds retain a console. An Inno setup log can be requested with `/LOG=<local-file>`; review it before sharing because it records local paths. Saves remain outside the install directory. Original-behavior uncertainty is tracked in the [feature matrix](../docs/records/feature-matrix.md).

Prompt 4's [asset-swap validation](../docs/records/asset-swap-p4.md) rebuilt and audited setup, checked all art choices across native/widescreen/ultrawide output, exercised source failures and preference fallback, and tested clean phone-only setup, optional-art reinstall, and post-commit rollback. The rollback cleanup uses the .NET helper because generated art paths can reach the Windows legacy 260-character limit. The previous GPL FFmpeg source gap is addressed by the [LGPL rebuild and matching source package](../docs/records/ffmpeg-redistribution.md); keep both release files together. The focused hands-on visual pass, interactive wizard check on a separate clean Windows account, and final review of other dependency notices remain open. The release gate is not closed and nothing has been published. The [implementation log](../docs/records/implementation-log.md) records the local tests and package hashes.
