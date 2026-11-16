# macOS Apple Silicon packages

Before importing assets or starting animated game content, the app shows a one-time flashing lights warning with an **Enable Reduce Flashing** checkbox. **Continue** saves the choice and acknowledgment; **Quit** leaves the warning pending for the next launch. The checkbox reflects an existing graphics preference and is unchecked for a fresh account. Acknowledgment is stored beside saves in `saves/flashing-warning-acknowledged`, outside the app and imported assets, so updates and reimports do not show it again. The setting remains available in **Settings → Graphics**; reduced flashing does not eliminate all flashing visuals.

The macOS host uses the shared first-launch import and Settings → Graphics → Assets → Reimport flow. It stores imported assets, saves, and creator photos in `~/Library/Application Support/z0mb1es-rec0mp1led/`, logs in `~/Library/Logs/z0mb1es-rec0mp1led/`, and keeps all generated content outside the app bundle. The first launch asks for the supported phone XAP; I MAED and Time Viking art are optional. Once import succeeds, the original packages may be removed. The app bundle and package must contain no XAP, phone DLL, XNB, or generated assets.

`build.sh` packages Apple Silicon only. The pinned toolchain is .NET SDK 10.0.302, runtime 10.0.10, and FNA 26.11. It accepts Xcode 27.1 or Apple Command Line Tools 14.3.1 with SDK 13.3. The first local build was completed on macOS Ventura 13.5 with an Apple M1 and the Command Line Tools. Exact input hashes are recorded in `config/macos-packaging.json`.

Runtime packs go in `out/deps/cache`, native libraries in `out/deps/native/osx-arm64`, and converter/importer binaries in `private/macos-inputs`. The native libraries came from the official FNA Apple daily archive dated 2026-11-06. Build the FFmpeg 8.1.1 converter with `build-ffmpeg.sh` after downloading the three pinned archives listed in [the FFmpeg guide](../ffmpeg/README.md) into `private/ffmpeg-macos/source`. This requires `make`, Clang, and `pkg-config`. Build the frozen optional-art importer with `build-art.sh` using `installer/linux/requirements-build.txt` and the initialized sibling extractor checkout. The local build used Python 3.9.6 and macholib 1.16.4. Review and pin replacement inputs before packaging; hashes are specific to the exact binaries.

Run `sh installer/macos/build.sh` from the repository root with the pinned SDK on `PATH`. If using this checkout's local tools:

```sh
export PATH="$PWD/out/tooling/dotnet:$PWD/out/tooling/powershell:$PATH"
export DOTNET_CLI_HOME="$PWD/out/dotnet-home"
sh installer/macos/build.sh
pwsh -NoProfile -Command '. .\dev.ps1; build-game'
```

The script verifies hashes and architectures, publishes self-contained tools, audits bundle payloads, and builds `Z0MB1ES-1.0.0-osx-arm64.app`, `Z0MB1ES-1.0.0-osx-arm64.pkg`, and `FFmpeg-8.1.1-macos-source.zip` in `out/macos/packages`. Ship the matching FFmpeg source ZIP beside the installer. Archive the previous app before repackaging. The ABI checker remains a directory publish so it can inspect the runtime assembly catalog. The package has no interactive install script; file selection occurs in the app on first launch.

The installer uses `components.plist` to disable bundle relocation and install at `/Applications/Z0MB1ES.app`, even when a development or extracted ZIP copy with the same bundle identifier exists elsewhere. Reinstalling replaces the app at that destination while persistent player data remains outside the bundle.

To build into a different output directory, use `sh installer/macos/build.sh --output out/macos/rebuilt-packages`. To package already-published binaries, use `python3 installer/macos/package.py --output out/macos/rebuilt-packages`. This also avoids modifying a development app that a previous installer has made root-owned.

The app's `Z0MB1ES.icns` uses the 1024px `assets/icon/icon-macos.png` artwork: a rounded black tile with transparent corners and padding. Regenerate the macOS SVG and PNG with `python scripts/build-installer-art.py --macos-icon-only` before packaging.

Each import picker identifies its source game and platform: the Z0MB1ES Windows Phone XAP, the I MAED Steam folder or Xbox 360 LIVE package, or the Time Viking Xbox 360 LIVE package.

Optional-texture prompts provide a **Skip** button. Cancelling an optional source choice or file picker skips those textures and continues with the phone assets; cancelling optional-only reimport resumes the current game. The bundled art importer writes into the host's app-data staging tree, with a separate scratch directory, and needs no repository, Python installation, or .NET SDK on the player's machine.

For a new-user test, install the `.pkg` and launch from Finder in a fresh macOS account. The `.pkg` and extracted `.app.zip` use the same account's persistent data, so changing formats does not reset onboarding. To repeat onboarding in your existing account, quit the game and temporarily rename `~/Library/Application Support/z0mb1es-rec0mp1led` to a backup before launching. Keep that backup to preserve saves and imported assets. Test the ZIP separately if distributing both formats.

## Direct CLI build without an installer

`dev.ps1` provides the direct build strategy on Apple Silicon: `build-game` prepares the supplied XAP and stages a runnable game in ignored `private/run/current/`; `run-game` launches it without installing a `.pkg` or importing assets through the app. The build includes the pinned macOS native libraries and the creator photo picker. It requires PowerShell, the pinned .NET SDK, managed FNA and the local NuGet cache prepared by `scripts/bootstrap-fna.ps1`, the native libraries above, and the pinned converter at `private/macos-inputs/ffmpeg`. It does not require building the installer or the optional-art importer for a phone-textures-only build.

From the repository root, with those prerequisites available:

```sh
export PATH="$PWD/out/tooling/dotnet:$PWD/out/tooling/powershell:$PATH"
export DOTNET_ROOT="$PWD/out/tooling/dotnet"
export DOTNET_CLI_HOME="$PWD/out/dotnet-home"
pwsh -NoProfile -Command '. ./dev.ps1; build-game'
pwsh -NoProfile -Command '. ./dev.ps1; run-game'
```

The default XAP is `game/Z0MB1ES (on teh ph0ne) v1.0.0.0.xap`. To supply another path, use `build-game -XapPath "/path/to/game.xap"`. CLI saves and creator pictures use `~/Documents/Z0MB1ES on teh ph0ne/Saves` and `Pictures`, separately from the packaged app's data. The first run shows the flashing lights warning.

Prepared optional textures at `private/asset-swap/prepared/` are automatically copied into the build. You can also supply another prepared store, including textures previously imported by the packaged macOS app:

```sh
pwsh -NoProfile -Command '. ./dev.ps1; build-game -ArtStorePath "$HOME/Library/Application Support/z0mb1es-rec0mp1led/assets/current/optional-art"'
```

Use the actual prepared-art directory from your import; a store contains source folders such as `i-maed-steam`, `i-maed-360`, or `time-viking-360` with `current.json` pointers. The copied textures stay with the CLI build, so the source store is no longer needed to run it. Choose **Enhanced** or **Original** in the game's asset settings.

To prepare textures entirely through the CLI, build the optional-art importer with `build-art.sh` as described above. After an initial `build-game`, build the Xbox texture decoder, extract a separate phone reference, and import your supported source packages:

```sh
dotnet build tools/decode_360_xnb -c Release --source out/deps/cache -p:NuGetAudit=false
dotnet tools/prepare-cli/bin/Release/net10.0/prepare.dll \
  --xap 'game/Z0MB1ES (on teh ph0ne) v1.0.0.0.xap' --output private/cli-art-reference
export PORT_ASSET_PRIVATE="$PWD/private"
export PORT_ASSET_DECODER="$PWD/tools/decode_360_xnb/bin/Release/net10.0/decode_360_xnb"
export PORT_ASSET_FNA="$PWD/out/deps/managed/FNA.dll"
private/macos-inputs/Port.AssetImporter prepare --kind i-maed-360 \
  --source '/path/to/I MAED LIVE package' --phone-root private/cli-art-reference/original-xap
private/macos-inputs/Port.AssetImporter prepare --kind time-viking-360 \
  --source '/path/to/Time Viking LIVE package' --phone-root private/cli-art-reference/original-xap
pwsh -NoProfile -Command '. ./dev.ps1; build-game'
pwsh -NoProfile -Command '. ./dev.ps1; run-game'
```

For Steam I MAED textures, replace the first import's kind with `i-maed-steam` and its source with the Steam game directory. Either import is optional; importing also composes the mapped Endless textures. The phone-reference output must be a new directory. Personal builds contain proprietary game content and stay under ignored `private/`.

## Unsigned install

After downloading the package from a trusted project source, open the `.pkg`. If macOS blocks it, try opening it once, then go to **System Settings → Privacy & Security**, scroll down, click **Open Anyway**, and confirm **Open** in the next warning. Install the app, open **Z0MB1ES** from Applications, and repeat the same procedure for the app if macOS blocks its first launch. Apple documents this exact override for an unsigned app or installer in [Safely open apps on your Mac](https://support.apple.com/en-us/102445). No SDK or developer account is required for a player; the one-time security override is required.

Local validation on the Apple M1 passed the required `build-game`, guarded rewrite fixture checks, the complete preparation pipeline using the bundled tools, and a 180-frame windowed run of the packaged executable using Metal. The menu loaded, animated, and rendered, and the game wrote its isolated test save. Expanding the installer found 425 files matching the app, all required executables with arm64 slices, and no proprietary game payload. The user confirmed full manual validation on 2026-11-10, closing the remaining player-flow and hardware acceptance for Apple Silicon (platform expansion Prompts 4 and 5). This is user-reported acceptance in addition to the automated checks above; see the [implementation log](../../docs/records/implementation-log.md).

Optional-art regression checks cover skipping both sources, cancelling source and file pickers, keeping the other selected source, and resuming a cancelled reimport. A self-contained import check runs from a temporary directory with sandbox rules denying the repository, sibling parser, developer SDK, Homebrew, and network access. It imports the player-owned I MAED and Time Viking Xbox 360 packages with the bundled tools and verifies restart after moving the input package paths away.
