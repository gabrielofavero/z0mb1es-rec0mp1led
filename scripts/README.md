# Build and release checks

This folder contains repeatable build and staging scripts. Release audits must inspect both tracked files and the completed installer for game code/assets and local paths, not just trust `.gitignore`.

`bootstrap-fna.ps1` verifies and prepares pinned FNA and native dependencies under ignored `out/`. `build-offline.ps1` restores the authored solution from the local cache and builds it without network access. Packaging and audit recipes live under `installer/`.

`stage-local-run.ps1` copies the authored runtime, a verified locally prepared DLL, and locally prepared content into a new ignored `private/run/` directory. FNA resolves `TitleContainer` files beside the launcher, so this stage mirrors the eventual install layout.

Device commands live in `scripts/dev/`, following the Blitz Recomp layout. See [build and run](../build-and-run.md#device-development-scripts) for platform setup, examples, and output paths.

- `dev-windows.ps1`: dot-source for `build-game`, `run-game`, `build-installer`, and `run-installer`. Prepared builds use `private/run/current/`; build options are `-XapPath`, `-AudioConverterPath`, and `-ArtStorePath`. Setup defaults to `installer/out/Z0MB1ES-1.1.0-x64-windows.exe`.
- `dev-android.ps1`: dot-source on Windows for `bootstrap-android`, `build-apk`, `install-apk`, `run-android`, and `log-android`. Device commands accept `-Serial` and `-AdbPath`. The current builder produces the content-free `out/android/Z0MB1ES-1.1.0-arm64-android.apk`.
- `dev-mac.sh`: selected by `source ./dev.sh --platform mac`; prepares/runs desktop builds through PowerShell 7 and wraps macOS `.app`/`.pkg` packaging.
- `dev-linux.sh`: selected by `source ./dev.sh --platform linux`; `build-game` publishes the self-contained host/tools, `build-installer` publishes and packages `.deb`/`.rpm`, and `run-game` launches the installed importer. Personal asset preparation remains the documented CLI workflow.
- `dev-ios.sh`: selected by `source ./dev.sh --platform ios` on the Mac build host; `build-game` creates the importer IPA and `build-bundled` wraps the personal three-source IPA recipe. Signing and installation use a sideloading tool.

The root `dev.ps1` preserves the existing desktop commands and Windows `build-apk` entry point. The root `dev.sh` detects macOS/Linux, accepts `--platform mac|linux|ios`, and can execute an action directly, for example `bash ./dev.sh --platform ios build-game`. Run `dev-help` after sourcing for available actions. Shared desktop preparation is in `scripts/dev/desktop.ps1`; `invoke-desktop.ps1` forwards Mac shell arguments without constructing a command string.

`build-installer-art.py` renders the authored square icon and cover SVGs into committed executable icons and Inno Setup wizard images. It uses local Edge or Chrome plus Pillow and NumPy only on the developer machine; see [artwork](../assets/README.md).
