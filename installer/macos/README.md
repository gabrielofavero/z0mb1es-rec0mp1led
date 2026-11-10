# macOS Apple Silicon packages

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

Optional-art prompts provide a **Skip** button. Cancelling an optional source choice or file picker skips that artwork and continues with the phone assets; cancelling optional-only reimport resumes the current game. The bundled art importer writes into the host's app-data staging tree, with a separate scratch directory, and needs no repository, Python installation, or .NET SDK on the player's machine.

For a new-user test, install the `.pkg` and launch from Finder in a fresh macOS account. The `.pkg` and extracted `.app.zip` use the same account's persistent data, so changing formats does not reset onboarding. To repeat onboarding in your existing account, quit the game and temporarily rename `~/Library/Application Support/z0mb1es-rec0mp1led` to a backup before launching. Keep that backup to preserve saves and imported assets. Test the ZIP separately if distributing both formats.

## Unsigned install

After downloading the package from a trusted project source, open the `.pkg`. If macOS blocks it, try opening it once, then go to **System Settings → Privacy & Security**, scroll down, click **Open Anyway**, and confirm **Open** in the next warning. Install the app, open **Z0MB1ES** from Applications, and repeat the same procedure for the app if macOS blocks its first launch. Apple documents this exact override for an unsigned app or installer in [Safely open apps on your Mac](https://support.apple.com/en-us/102445). No SDK or developer account is required for a player; the one-time security override is required.

Local validation on the Apple M1 passed the required `build-game`, guarded rewrite fixture checks, the complete preparation pipeline using the bundled tools, and a 180-frame windowed run of the packaged executable using Metal. The menu loaded, animated, and rendered, and the game wrote its isolated test save. Expanding the installer found 425 files matching the app, all required executables with arm64 slices, and no proprietary game payload. Finder onboarding, installation through Installer.app, controller input, and a listening test remain manual acceptance checks.
