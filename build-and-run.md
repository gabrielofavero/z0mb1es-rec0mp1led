# Build and run Z0MB1ES

This guide covers the **1.1.0** game on Windows, macOS, Linux, Android, and iOS. Run commands from the repository root. PowerShell examples require PowerShell 7; `sh` examples run on the named Unix host. Replace example source paths with your own files.

An **importer build** contains the port and its dependencies, but no original game assets. The player supplies the supported Windows Phone XAP during installation or first launch. A **prepared build** includes the locally prepared game and assets: on desktop these are files beside the executable, and the personal iOS variant embeds them inside the IPA. Optional enhanced artwork is separate from the phone assets required to play.

## Available build routes

| Platform / architecture | Without original assets in the package | With assets prepared before launch | Terminal launch |
| --- | --- | --- | --- |
| Windows x64 | Inno Setup `.exe`; import during setup | Personal prepared folder via `build-game` | `run-game`, or `Z0MB1ES.exe --run ...` |
| macOS Apple Silicon | `.app` and `.pkg`; import on first launch | Personal prepared folder via `build-game` | `run-game`, the app executable, or `open` |
| Linux x64 | `.deb` and `.rpm`; import on first launch | Personal prepared folder using the CLI steps below | `Z0MB1ES --linux` or `Z0MB1ES --run ...` |
| Android ARM64 | Signed APK; import on the device | Personal signed APK with prepared game and optional artwork; see the integration note below | Install/start through `adb`; gameplay runs on the device |
| iOS ARM64 | Unsigned IPA; import on the device | Separate personal preinstalled IPA with both supported Xbox 360 art sources | Install/sign with a sideloading tool; gameplay runs on the device |

The configured desktop targets are Windows x64, Linux x64, and macOS ARM64. Intel macOS, desktop ARM Linux, x86 Windows, mobile simulators, and other Android ABIs have no packaging recipe here. `Port.IosProbe` is a historical diagnostic app, not another game edition.

The game executables and app labels are **Z0MB1ES**. Installer and package filenames can include versions, platforms, or a longer description.

## Device development scripts

Like Blitz Recomp, platform commands live under `scripts/dev/`, with root entry points for everyday use. Load one target per shell. PowerShell scripts are dot-sourced; the Unix entry point supports Bash and zsh, either sourced or executed with an action. Loading a script does not build or launch anything.

| Target | Load commands | Main commands |
| --- | --- | --- |
| Windows x64 | `. ./scripts/dev/dev-windows.ps1` | `build-game`, `run-game`, `build-installer`, `run-installer` |
| macOS ARM64 | `source ./dev.sh --platform mac` | `build-game`, `run-game`, `build-installer`, `run-installer`, `dev-help` |
| Linux x64 | `source ./dev.sh --platform linux` | `build-game`, `build-installer`, `run-game`, `dev-help` |
| Android ARM64 (Windows host) | `. ./scripts/dev/dev-android.ps1` | `bootstrap-android`, `build-apk`, `install-apk`, `run-android`, `log-android` |
| iOS ARM64 (Mac host) | `source ./dev.sh --platform ios` | `build-game`, `build-bundled`, `dev-help` |

`. ./dev.ps1` remains compatible: on Windows it loads desktop and Android commands; on macOS it loads the prepared desktop commands. `source ./dev.sh` detects macOS or Linux. Select iOS explicitly on its Mac build host. For a single command without changing your shell, use `bash ./dev.sh --platform mac build-installer` or `bash ./dev.sh --platform ios build-game`.

The commands wrap the platform recipes below and retain their prerequisites, audits, and output locations. On Windows/macOS, `build-game` prepares a personal desktop folder. On Linux, it publishes the self-contained host/tools to `private/linux-publish/`; preparing personal assets remains the explicit CLI workflow below. Linux `run-game` opens the installed importer. On iOS, `build-game` produces the content-free unsigned IPA; signing/installing remains a sideloading step.

Generated packages and installers use `Z0MB1ES-<version>-<architecture>-<os>.<extension>`. Architecture is `x64` for Windows/Linux and `arm64` for macOS/Android/iOS; OS names are `windows`, `linux`, `macos`, `android`, and `ios`. This naming applies to package filenames, including the exported macOS `.app`; installed app names and developer output folders keep their existing names.

## Shared prerequisites

Install .NET SDK **10.0.302**; [global.json](global.json) disables SDK roll-forward. Desktop projects target `net10.0`. Dependency and packaging pins live in:

- [Shared FNA and managed dependencies](config/dependencies.json)
- [Windows runtime packs](installer/config/runtime-packs.json)
- [macOS packaging inputs](config/macos-packaging.json)
- [Linux packaging inputs](config/linux-packaging.json)
- [Android tooling and packages](config/android-packaging.json)
- [iOS native and metadata inputs](config/ios-packaging.json)

Bootstrap official FNA 26.11, Mono.Cecil, and the shared dependency cache:

```powershell
./scripts/bootstrap-fna.ps1
```

For a disconnected bootstrap, first put the four archives named in `config/dependencies.json` in `out/deps/cache/`, then use `./scripts/bootstrap-fna.ps1 -Offline`. The script verifies the hashes, builds managed FNA, and extracts Windows native libraries. macOS and Linux also need their own native libraries as described below.

Build the authored desktop projects and preparation tools:

```powershell
./scripts/build-offline.ps1
```

This uses the local NuGet cache and expects the bootstrap outputs, including its Windows native files, even when building on another desktop host. Packaging additionally needs the pinned runtime `.nupkg` files in the cache locations checked by each platform script. Bootstrap does not provision every platform's packaging toolchain.

Windows, macOS, Linux, and Android use the vendored Rust WMA-to-QOA converter. Install Rust **1.100.0** and build the desktop converter on the desktop where it will run:

```powershell
$env:CARGO_TARGET_DIR = 'out/audio-converter'
cargo build --release --offline --manifest-path tools/audio-converter/Cargo.toml
```

The equivalent Unix command is:

```sh
CARGO_TARGET_DIR=out/audio-converter cargo build --release --offline \
  --manifest-path tools/audio-converter/Cargo.toml
```

Output is `out/audio-converter/release/port-audio-converter.exe` on Windows and `out/audio-converter/release/port-audio-converter` on Unix. Windows checks it against [config/content-tools.json](config/content-tools.json). macOS/Linux record its platform-specific hash. iOS uses its separate native converter recipe below.

For personal preparation, supply the exact supported phone XAP identified by [config/supported-versions.json](config/supported-versions.json). The default developer path is `game/Z0MB1ES (on teh ph0ne) v1.0.0.0.xap`. The original phone game's `1.0.0.0` version is independent of this port's `1.1.0` release.

Keep original packages and prepared game material in ignored `game/` or `private/` locations. Standard desktop/mobile importer package audits reject embedded game content. There is no asset-inclusive Windows/macOS/Linux installer switch.

## Windows x64

### Importer installer

In addition to shared prerequisites, install Inno Setup with `ISCC.exe` available, Python **3.12.1**, Pillow **12.3.0**, and NumPy **2.5.1**. The installer expects PyInstaller **6.22.3** under `private/tooling/pyinstaller/`, including its distribution metadata and license files. The sibling checkout `../xna-to-fna-asset-extractor/xnb_parse` must be at commit `cc3aef8fff4be1320068a78001795e7e1c8bd548`.

Supply the .NET **10.0.10** runtime packs pinned in `installer/config/runtime-packs.json`; the script verifies their archives in the user's NuGet package cache after restore. `-RuntimePackSource` can point to a package directory or NuGet source.

```powershell
. ./scripts/dev/dev-windows.ps1
build-installer
```

To specify local tools explicitly:

```powershell
build-installer -AudioConverterPath 'out/audio-converter/release/port-audio-converter.exe' `
    -RuntimePackSource 'out/deps/cache' `
    -IsccPath 'C:/Program Files (x86)/Inno Setup 6/ISCC.exe' `
    -PyInstallerRoot 'private/tooling/pyinstaller'
```

Output: `installer/out/Z0MB1ES-1.1.0-x64-windows.exe`. It includes the runtime, converter, and artwork importer. The player's machine does not need .NET, Python, Rust, or an SDK. The XAP and optional art are selected during installation.

Run setup from PowerShell using its explicit output path:

```powershell
run-installer -InstallerPath 'installer/out/Z0MB1ES-1.1.0-x64-windows.exe'
```

After installation, use the Z0MB1ES shortcut. The [Windows installer guide](installer/README.md) documents silent installation and optional artwork switches.

### Personal folder with prepared assets

```powershell
. ./scripts/dev/dev-windows.ps1
build-game -XapPath 'D:/Games/phone.xap'
run-game
```

`build-game` extracts and verifies the XAP, converts music, rewrites/hooks the game, and stages the result in `private/run/current/`. It replaces the previous current build after preparation succeeds. `run-game` launches that folder without rebuilding or opening an importer. This developer folder needs an installed .NET 10 runtime and is not a self-contained installer.

For a subsequent shell session:

```powershell
. ./scripts/dev/dev-windows.ps1
run-game -List
run-game -Build current
```

The helper uses `Documents/Z0MB1ES/Saves` and `Pictures`, with optional artwork from the staged `optional-art/` folder or the developer artwork store. See the shared desktop CLI and artwork sections below for explicit paths and controls.

## macOS Apple Silicon

### Native and packaging inputs

Use an Apple Silicon Mac, the shared SDK/FNA setup, Python 3, Rust 1.100.0, and PowerShell 7 for the prepared desktop helper. Desktop packaging accepts the exact Xcode **27.1** pin or the pinned Apple Command Line Tools **14.3.1** installation with SDK **13.3**, as checked in `config/macos-packaging.json`. The iOS toolchain below uses a different Xcode selection.

Provide the four reviewed ARM64 libraries in `out/deps/native/osx-arm64/`: `libSDL3.0.dylib`, `libFNA3D.0.dylib`, `libFAudio.0.dylib`, and `libtheorafile.dylib`. Their exact hashes are in the macOS pin file; the source is the FNA Apple daily archive dated 2026-11-06. Provide the pinned .NET 10.0.10 macOS runtime/host/ASP.NET runtime packages in `out/deps/cache/`.

For the frozen artwork importer, initialize the sibling `xnb_parse` checkout and install [the importer requirements](installer/linux/requirements-build.txt) into the selected Python environment. The recorded macOS environment uses Python 3.9.6 and macholib 1.16.4. Then:

```sh
sh installer/macos/build-art.sh
```

This writes `private/macos-inputs/Port.AssetImporter`. Packaging verifies its exact hash too. A newly produced binary that differs from the pin needs a reviewed pin update; changing the pin merely to suppress a mismatch defeats that check.

### Importer app and installer

With the pinned SDK on `PATH`:

```sh
bash ./dev.sh --platform mac build-installer
```

Outputs in `out/macos/packages/`:

- `Z0MB1ES-1.1.0-arm64-macos.app`
- `Z0MB1ES-1.1.0-arm64-macos.pkg`

The script builds the Rust converter, publishes self-contained game/tools, checks native dependencies, and assembles the app and installer. Archive an existing app output before reusing the same destination, or choose a fresh one:

```sh
bash ./dev.sh --platform mac build-installer --output out/macos/rebuilt-packages
```

Open the uninstalled app or install the package:

```sh
open out/macos/packages/Z0MB1ES-1.1.0-arm64-macos.app
open out/macos/packages/Z0MB1ES-1.1.0-arm64-macos.pkg
```

The installed app is `/Applications/Z0MB1ES.app`. Terminal launches can use either:

```sh
open /Applications/Z0MB1ES.app
/Applications/Z0MB1ES.app/Contents/MacOS/Z0MB1ES --macos
```

First launch imports the phone XAP and optional enhanced art. Later launches use `~/Library/Application Support/z0mb1es-rec0mp1led/`; saves, pictures, and imported assets are separate. This app is self-contained; the player does not need the build toolchain.

### Personal folder with prepared assets

Build the macOS desktop converter and provide the native libraries above. Then load the Mac commands in Bash or zsh:

```sh
source ./dev.sh --platform mac
build-game -XapPath '/path/to/phone.xap'
run-game
```

The prepared desktop commands invoke PowerShell 7 and accept its parameter names. You can also keep using `. ./dev.ps1` from PowerShell on the Mac.

This creates `private/run/current/` with macOS native libraries and the creator photo picker. It needs the .NET runtime and uses `~/Documents/Z0MB1ES/Saves` and `Pictures`, separately from the packaged app. It does not create an asset-inclusive `.app` or `.pkg`.

## Linux x64

### Importer packages

Build on Linux x64, or use the PowerShell cross-publish/WSL route below. Supply .NET SDK 10.0.302, the shared FNA setup, Rust 1.100.0, the Linux .NET 10.0.10 runtime packs from `config/linux-packaging.json`, and `out/deps/cache/fnalibs-2026-11-06.zip`.

Extract and verify Linux native libraries:

```sh
python3 installer/linux/bootstrap-native.py
```

Build the artwork importer in an Ubuntu 20.04 x64 environment with Python **3.8.10**, the sibling `xnb_parse` checkout, and the pinned requirements:

```sh
python3 -m pip install -r installer/linux/requirements-build.txt
sh installer/linux/build-art.sh
```

It writes `private/linux-art/dist/Port.AssetImporter`. Supply the pinned nFPM **2.48.0** archive at `private/tooling/nfpm_2.48.0_Linux_x86_64.tar.gz` and its executable at `private/tooling/nfpm`. Both are required by packaging.

On Windows with WSL, after preparing those Linux-side inputs, run:

```powershell
./installer/linux/build.ps1
```

It cross-publishes the game and tools to `private/linux-publish/`, then invokes `package.sh` in WSL. Rust, Python, nFPM, and the artwork helper must be usable inside WSL, and the checkout must be accessible there.

For a native Linux build, the Linux helper publishes the four host/tool projects and assembles both packages:

```sh
source ./dev.sh --platform linux
build-installer
```

Use `build-game` in that shell to publish the host/tools without packaging. After installing a package, `run-game` launches `/opt/z0mb1es-rec0mp1led/launch.sh`.

Keep the ABI checker as a directory publish: it needs its runtime assembly catalog. Packaging builds the Rust converter and writes:

- `out/linux/packages/Z0MB1ES-1.1.0-x64-linux.deb`
- `out/linux/packages/Z0MB1ES-1.1.0-x64-linux.rpm`

On the matching distribution, install one package:

```sh
sudo apt install ./out/linux/packages/Z0MB1ES-1.1.0-x64-linux.deb
# Or, on an RPM distribution:
sudo dnf install ./out/linux/packages/Z0MB1ES-1.1.0-x64-linux.rpm
```

Launch from the desktop menu or terminal:

```sh
/opt/z0mb1es-rec0mp1led/launch.sh
```

The wrapper sets `LD_LIBRARY_PATH` and runs `Z0MB1ES --linux`. The graphical importer requires Zenity and a desktop session. Imported assets, saves, and pictures live in `${XDG_DATA_HOME:-$HOME/.local/share}/z0mb1es-rec0mp1led/`. A headless WSL shell is useful for building packages but does not establish graphical gameplay support.

### Personal folder with prepared assets

The `dev.ps1` staging helper has Windows and macOS branches; it does not stage Linux native libraries. Use the native Linux publish above and the preparation CLI instead. After the shared bootstrap/offline build and Linux converter build, choose new output directories for each preparation attempt:

```sh
xap='/path/to/phone.xap'
dotnet tools/prepare-cli/bin/Release/net10.0/prepare.dll \
  --xap "$xap" --output private/linux-preparation
dotnet tools/content-prepare/bin/Release/net10.0/content-prepare.dll \
  --xap "$xap" --preparation private/linux-preparation \
  --converter out/audio-converter/release/port-audio-converter \
  --output private/linux-content \
  --converter-sha256 "$(sha256sum out/audio-converter/release/port-audio-converter | cut -d ' ' -f 1)"
dotnet tools/inspect_assembly/bin/Release/net10.0/inspect_assembly.dll \
  --json --xap "$xap" --output private/linux-inventory.json
dotnet tools/abi_check/bin/Release/net10.0/abi_check.dll \
  --inventory private/linux-inventory.json \
  --dll private/linux-preparation/original-xap/ZombiesWP7.dll \
  --fna out/deps/managed/FNA.dll \
  --bridge src/Port.Bridge/bin/Release/net10.0/Port.Bridge.dll \
  --output private/linux-abi-report.json
```

The ABI checker can return **1** for its guarded behavior review while writing a valid report; this is the same case handled by `build-game`. Inspect its output and continue only with the expected pinned report. The rewriter validates that report against the actual inputs; do not bypass a mismatch.

```sh
dotnet tools/prepare-cli/bin/Release/net10.0/prepare.dll rewrite \
  --xap "$xap" --dll private/linux-preparation/original-xap/ZombiesWP7.dll \
  --abi-report private/linux-abi-report.json --fna out/deps/managed/FNA.dll \
  --bridge src/Port.Bridge/bin/Release/net10.0/Port.Bridge.dll \
  --output private/prepared/linux-base
dotnet tools/prepare-cli/bin/Release/net10.0/prepare.dll hook \
  --xap "$xap" --dll private/linux-preparation/original-xap/ZombiesWP7.dll \
  --prepared private/prepared/linux-base/ZombiesWP7.dll \
  --hooks src/Port.Hooks/bin/Release/net10.0/Port.Hooks.dll \
  --output private/prepared/linux-hooked
mkdir -p private/run/linux-personal
cp -a private/linux-publish/game/. private/run/linux-personal/
cp -a private/linux-content/. private/run/linux-personal/
cp private/prepared/linux-hooked/ZombiesWP7.dll \
  private/prepared/linux-hooked/patch-manifest.json private/run/linux-personal/
cp out/deps/native/linux-x64/* private/run/linux-personal/
for library in SDL3 FNA3D FAudio; do
  cp "private/run/linux-personal/lib$library.so.0" "private/run/linux-personal/lib$library.so"
done
LD_LIBRARY_PATH="$PWD/private/run/linux-personal${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}" \
  private/run/linux-personal/Z0MB1ES \
  --run private/run/linux-personal/ZombiesWP7.dll --content private/run/linux-personal \
  --save-dir private/linux-user/Saves --pictures-dir private/linux-user/Pictures
```

Use a fresh `linux-personal` folder rather than copying a new build over an old one. This is a self-contained runtime folder with prepared assets beside the executable; it skips the first-launch importer. Do not pass this folder to the content-free `.deb`/`.rpm` packager.

## Desktop terminal arguments and enhanced artwork

All desktop game executables accept this direct launch form:

```text
Z0MB1ES --run <prepared-game.dll> --content <prepared-content-root>
        [--save-dir <directory>] [--pictures-dir <directory>] [--windowed]
        [--art-store <prepared-art-root>]
        [--art-normal original|steam|360] [--art-viking original|360]
        [--art-endless original|imported] [--frames <positive-number>]
```

On Windows the executable is `Z0MB1ES.exe`. For framework-dependent developer folders, `dotnet <folder>/Z0MB1ES.dll` accepts the same arguments. The game DLL and content root must be supplied together; the launcher verifies the patch and content manifests. Explicit save/picture paths help separate development runs from installed saves. `--frames` exits after a limited run; it still opens a graphical game window.

For example, after a Windows `build-game`:

```powershell
dotnet private/run/current/Z0MB1ES.dll `
    --run private/run/current/ZombiesWP7.dll --content private/run/current `
    --save-dir private/cli-user/Saves --pictures-dir private/cli-user/Pictures --windowed
```

For an installed Linux import, direct launch can reuse the prepared data:

```sh
data="${XDG_DATA_HOME:-$HOME/.local/share}/z0mb1es-rec0mp1led"
LD_LIBRARY_PATH="/opt/z0mb1es-rec0mp1led${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}" \
  /opt/z0mb1es-rec0mp1led/Z0MB1ES \
  --run "$data/assets/current/game/ZombiesWP7.dll" \
  --content "$data/assets/current/content" \
  --save-dir "$data/saves" --pictures-dir "$data/pictures" \
  --art-store "$data/assets/current/optional-art"
```

Use `--linux` or `--macos` for the normal installed import/reimport coordinator. Direct `--run` expects already prepared files and does not offer the importer's repair/reimport session.

Enhanced artwork is optional. Importer builds accept the supported I MAED Steam source or Xbox 360 package, and the supported Time Viking Xbox 360 package. Mobile Steam imports use a ZIP containing the matching executable and `Content` directory. The executable is verified as an input, not launched.

For desktop personal builds, prepare artwork from the CLI after extracting a phone reference. Build `tools/decode_360_xnb` as well if it is not already built, and provide Python, Pillow, NumPy, and the sibling `xnb_parse` checkout:

```sh
dotnet build tools/decode_360_xnb -c Release --source out/deps/cache -p:NuGetAudit=false
dotnet tools/prepare-cli/bin/Release/net10.0/prepare.dll \
  --xap '/path/to/phone.xap' --output private/art-phone-reference
python3 tools/prepare_optional_art.py prepare --kind i-maed-360 \
  --source '/path/to/zombies-360' --phone-root private/art-phone-reference/original-xap
python3 tools/prepare_optional_art.py prepare --kind time-viking-360 \
  --source '/path/to/time-viking-360' --phone-root private/art-phone-reference/original-xap
```

On Windows use `python` in place of `python3`. For Steam, use `--kind i-maed-steam --source '/path/to/Steam/game-directory'` instead of the I MAED 360 command. Each source is independently optional; importing also composes the mapped Endless art. The default store is `private/asset-swap/prepared/`.

On Windows/macOS, a subsequent `build-game` automatically copies that store into `private/run/current/optional-art/`. To select another existing store, use `build-game -ArtStorePath '<prepared-art-root>'`. On Linux, copy it into `private/run/linux-personal/optional-art/` and add `--art-store private/run/linux-personal/optional-art` to the launch command. Select Enhanced in Settings or use the explicit artwork arguments above. Preserve the whole store, including its manifests and `current.json` pointers.

## Android ARM64

### Importer APK

The supported packaging host is Windows x64 with PowerShell 7. In addition to shared managed prerequisites and Python 3, provide:

- JDK 17 or 21; the tested JDK is Oracle 17.0.10+11. Some five-component Microsoft JDK version strings are rejected by this Android tooling.
- Android SDK platform **36**, build tools **36.1.0**, and platform tools (`adb`).
- Android NDK **r27d / 27.3.13750724**.
- Rust **1.100.0**, with `rustup target add aarch64-linux-android`.

Bootstrap the isolated Android .NET SDK/workload and pinned FNA.NET package, then build:

```powershell
. ./scripts/dev/dev-android.ps1
bootstrap-android
build-apk -JavaSdkDirectory 'C:/Program Files/Java/jdk-17' `
    -AndroidSdkDirectory "$env:LOCALAPPDATA/Android/Sdk" `
    -NdkDirectory 'out/android/ndk/android-ndk-r27d'
```

The NDK override is passed to a native build helper; use an absolute path if your working directory differs from the repository root. Defaults are the JDK/SDK paths above and `out/android/ndk/android-ndk-r27d`.

The bootstrap installs .NET 10.0.302 and Android workload **36.1.2** into `out/android/`. The build creates separate Android hooks/artwork/preparation inputs, builds the native QOA converter, publishes the APK, audits it, and verifies its signature. It does not need a XAP or artwork source on the build host.

Output: `out/android/Z0MB1ES-1.1.0-arm64-android.apk`. The app ID is `org.z0mb1es.recompiled`; it requires ARM64, Android API **24+**, and OpenGL ES **3**. Back up `private/android/signing/experimental.keystore` and `password.txt` together; updates need the same signing identity. Their historical filenames do not change the displayed app name.

Install/start from PowerShell with a connected device authorized for USB debugging:

```powershell
install-apk
run-android
log-android
```

For multiple connected devices, pass `-Serial '<adb-device-serial>'` to each device command. Use `-AdbPath '<sdk>/platform-tools/adb.exe'` for a custom SDK, and `install-apk -ApkPath '<path.apk>'` for a different APK. Installation uses `adb install -r` to preserve app data; `log-android` streams until interrupted.

The launcher command opens the Android app; it does not run gameplay on the desktop. Choose the XAP and optional artwork using the app's package pickers. Prepared assets and saves persist in private app storage between updates; uninstalling removes them. Session diagnostics are also written to `files/android-session.log`.

### Personal APK with embedded game assets

Android already has an implemented bundled build pipeline. The [implementation log](docs/records/implementation-log.md) records a signed APK containing **788 bundled files**, with the payload audit and APK signature checks passing. The game and optional artwork are prepared on the desktop, embedded in the APK, then verified and extracted to app-private storage on the device. This edition skips the on-device source-package importer.

**Current importer integration:** The commands above describe the existing bundled CLI before the current working-tree importer rewrite. That rewrite replaced its `-XapPath`, `-ArtStorePath`, and `-SkipGameBuild` parameters with the importer-only build, and `MainActivity.RunPlayer` currently enters `ImportAssets.Store` without selecting `BundledAssets.Open`. Running those bundled commands against the importer-only wrapper will therefore fail parameter binding. The two build/startup selections need to be reconnected in this checkout to expose both editions together; bundled Android support and its separate audit already exist. The importer audit should continue to reject embedded content, while bundled builds use `audit-bundled.py`. No Android build was run to revalidate the bundled edition for 1.1.0 while writing this guide.

The bundled CLI implementation provides these commands after Android bootstrap and desktop converter setup:

```powershell
. ./scripts/dev/dev-android.ps1
build-apk -XapPath 'D:/Games/phone.xap'
```

To include an existing prepared enhanced-art store:

```powershell
build-apk -XapPath 'D:/Games/phone.xap' -ArtStorePath 'private/asset-swap/prepared'
```

To package an already prepared `private/run/current/` without rebuilding the desktop game:

```powershell
build-apk -SkipGameBuild
```

`-SkipGameBuild` cannot be combined with `-XapPath` or `-ArtStorePath`. The SDK and Java path overrides apply to the bundled implementation too. The recorded output is `private/android/Z0MB1ES-experimental.apk`; that filename is the existing personal package name, independent of the app label **Z0MB1ES**.

The existing pipeline consists of:

1. `build-game` creates the verified desktop stage and includes any selected prepared artwork.
2. [android-bundle-prepare](tools/android-bundle-prepare/Program.cs) adapts that stage to the Android framework, bridge, and hooks, creating a new `private/android/build-*/preinstalled/` payload and its receipt.
3. Android publish embeds that directory using `-p:BundledPayloadDirectory=<absolute-preinstalled-path>` and signs the APK with the existing private update key.
4. [audit-bundled.py](installer/android/audit-bundled.py) compares the APK against the prepared payload, and `apksigner` verifies the signature.
5. [BundledAssets.Open](src/Port.AndroidGame/BundledAssets.cs) verifies and extracts the embedded payload for gameplay; saves remain outside that versioned payload.

## iOS ARM64

### Mac prerequisites for both IPA variants

Use a compatible Apple Silicon Mac with full **Xcode 26.0.1**, .NET SDK **10.0.302**, runtime **10.0.10**, and iOS workload **26.0.11017**. The projects target `net10.0-ios26.0`, `ios-arm64`, with an iOS 15.0 deployment minimum. These recipes build device IPAs, not simulator apps.

The staging script reads runtime metadata specifically from `out/tooling/dotnet/packs/`, so provision the pinned SDK/workload there, even if a system SDK is also installed:

```sh
export PATH="$PWD/out/tooling/dotnet:$PATH"
export DEVELOPER_DIR=/Applications/Xcode_26.0.1.app/Contents/Developer
dotnet workload install ios --skip-manifest-update
dotnet workload list
```

Confirm that the installed iOS workload is 26.0.11017. Complete Xcode's initial setup before packaging. Also provide Python 3, `rg`, `make`, `pkg-config`, the shared FNA/bridge/hooks builds, and `out/deps/cache/fnalibs-apple-2026-11-06.zip` matching `config/ios-packaging.json`.

`installer/ios/stage-game-inputs.py` extracts five pinned `iphoneos` static libraries, generates icons, and checks the exact preparation-assembly and runtime-catalog hashes. Rebuilt assemblies that differ from those pins require a reviewed update before packaging can pass.

The iOS converter is still the FFmpeg/Ogg implementation, independent of the desktop/Android Rust converter. Put these reviewed source archives in `private/ffmpeg-macos/source/`, or point `IOS_AUDIO_SOURCES` at their directory:

- `ffmpeg-239f2c733d.tar.gz`
- `libogg-1.3.6.tar.gz`
- `libvorbis-1.3.7.tar.xz`

Their source locations and checksums are in [the converter source table](installer/ffmpeg/README.md). The iOS native script verifies those checksums before compiling. Both IPA scripts build `out/ios/native/libz0audio.a` if absent; after native source changes, rebuild it explicitly with `sh installer/ios/native/build.sh` rather than reusing an old archive.

### Importer IPA

```sh
source ./dev.sh --platform ios
build-game
```

Output: `src/Port.IosGame/bin/Release/net10.0-ios26.0/ios-arm64/Z0MB1ES-1.1.0-arm64-ios.ipa`. The script stages inputs, rebuilds the app, checks the actual packaged managed code, and runs the content-free IPA audit. It reads no phone XAP or optional artwork as build inputs. The runtime uses the interpreter for the dynamically prepared game.

Sign and install the unsigned IPA using your sideloading tool. Then launch **Z0MB1ES**, acknowledge the warning, and select the XAP and optional art through Files. Later launches use the prepared app-private assets. **Settings → Graphics → Assets → Reimport assets** opens setup again. After a completed reimport, reopen the app for a fresh game session.

### Personal IPA with embedded assets

Use the same prerequisites, including the repository-local SDK. Supply the phone XAP and **both** supported Xbox 360 packages:

```sh
source ./dev.sh --platform ios
build-bundled \
  '/path/to/phone.xap' '/path/to/zombies-360' '/path/to/time-viking-360'
```

Output: `private/ios-bundled/bin/Release/net10.0-ios26.0/ios-arm64/Z0MB1ES-1.1.0-arm64-ios.ipa`.

The recipe prepares the phone game, original art, all three songs, I MAED/Time Viking enhanced art, and composed Endless art before embedding them. It also builds the macOS native converter used during preparation if absent. Payloads, build logs, and audits stay under `private/ios-bundled/`. The app verifies its bundled resources and starts the game without a first-launch asset picker.

This wrapper requires all three source arguments, and its audit requires all three optional-art manifests. It does not offer an audited phone-only or Steam-art bundled variant. Although the lower-level preparation tool accepts fewer inputs, that does not make it a complete alternative to this audited recipe.

The importing and personal apps share the current bundle ID `io.github.z0mb1es.executionprobe`. Use consistent signing account/bundle-ID options when replacing one with the other; they are not two independent side-by-side editions. Saves remain outside the replaceable assets. Logs are exposed through Files as `ios-game-trace.txt`.

Both IPAs are unsigned build outputs. Their scripts do not sign, install, or launch them on an iPhone. Use a compatible signing/sideloading tool and its update/renewal procedure. There is no desktop terminal gameplay mode for an iOS device IPA.

## Checks and troubleshooting

The device helpers reuse the platform scripts and configured targets. Passing a desktop build or checking command dispatch does not validate another platform's packaging or device gameplay.

Check the helper scripts without building packages or contacting devices:

```powershell
pwsh -NoProfile -File tests/DevScripts.Checks.ps1
```

```sh
bash tests/dev-scripts-checks.sh
```

The shell checks require Bash (Git Bash works on Windows). They cover platform selection, argument forwarding with spaces, and stopping after failed prerequisites. The PowerShell checks cover loading from another directory, Android device selection, and failed `adb` commands.

- For code changes, the repository normally requires `. ./dev.ps1; build-game` in addition to relevant checks. This prepares a personal game and therefore needs the supported XAP and converter. It is not a replacement for a platform package audit.
- Missing SDK, workload, archive, runtime pack, or hash errors identify a required pinned input. Check the corresponding configuration file and platform script before retrying.
- Build-time importer packages reject prepared/proprietary content by design. Keep personal prepared folders and embedded IPAs separate from importer package outputs.
- A plain launcher executable needs its accompanying managed/native files. Move or copy the complete output folder, not only `Z0MB1ES` or `Z0MB1ES.exe`.
- Direct desktop runs require a graphical session and working graphics/audio libraries. Installing through `adb`, building under WSL, or producing an IPA does not itself validate rendering or gameplay.

Additional details: [Windows installer](installer/README.md), [macOS](installer/macos/README.md), [Linux](installer/linux/README.md), [Android](installer/android/README.md), [iOS](installer/ios/README.md), and [desktop converter](tools/audio-converter/README.md).
