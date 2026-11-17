# Personal Android experiment

Build a signed ARM64 APK with the locally prepared phone game, converted audio,
and available prepared optional artwork inside it. This is the initial FNA.NET
experiment requested for device testing. It contains proprietary inputs and must
remain local under ignored `private/`; it is not the content-free Android release
or first-launch XAP importer from the platform plan.

From PowerShell 7 at the repository root:

```powershell
# First time: download the isolated SDK, Android workload and verified FNA.NET.
./installer/android/bootstrap.ps1

. ./dev.ps1
build-apk
```

The Windows x64 CLI needs the repository's normal desktop build prerequisites,
Python 3, JDK 17 or 21, Android SDK platform 36 and build-tools 36.1.0. Defaults
use `C:/Program Files/Java/jdk-17` and `$env:LOCALAPPDATA/Android/Sdk`. Override
them when necessary:

```powershell
build-apk -JavaSdkDirectory 'C:/path/to/jdk' -AndroidSdkDirectory 'D:/Android/Sdk'
```

The tested Java installation is Oracle JDK 17.0.10+11. Microsoft's current JDK
21.0.13.0.2 emits a five-component version the pinned Android tooling cannot
parse; use a supported JDK with a conventional version string. No global .NET
workload is installed. The SDK, workload and NuGet cache live in `out/android/`.
Tooling and framework hashes are in `config/android-packaging.json`; transitive
NuGet dependencies are locked in `src/Port.AndroidGame/packages.lock.json`.

`build-apk` runs `build-game` first, using the supported local XAP and prepared
artwork defaults. Close any desktop game instance before building. It accepts
`-XapPath` and `-ArtStorePath` for those inputs. If `build-game` has already passed
for the current inputs, `build-apk -SkipGameBuild` reuses `private/run/current/`.
This option verifies that staged DLL and every content file against their
manifests; it does not accept XAP/artwork overrides.

The experiment compiles separate bridge/hooks from the shared sources against
the Android FNA.NET assembly, checks the prepared game's type and exact member
references, retargets its framework identity, and writes a new payload receipt.
The existing upstream FNA dependencies, desktop projects and preparation pins
remain in use for all other platforms. The original XAP, original DLL and
desktop payload are not modified by Android retargeting.

Output:

```text
private/android/Z0MB1ES-experimental.apk
private/android/build-<id>/android-abi-report.json
private/android/build-<id>/preinstalled/
private/android/build-<id>/apk/
private/android/signing/experimental.keystore
private/android/signing/password.txt
```

Keep a private backup of the keystore and password together. The CLI reuses this
key for updates and refuses an incomplete pair. It checks the signed APK with
`apksigner`, compares every bundled asset to its receipt and local source, and
checks all five native libraries against the Android AAR hashes. The package's
Linux RID fallback assets are explicitly excluded: without this, Linux's
`libtheorafile.so` overrides the Android library. The reviewed Android libraries
all have 16 KB ELF load-segment alignment.

Install the APK from a file manager after allowing that app to install unknown
apps. Alternatively, with a physical ARM64 phone connected and USB debugging
authorized:

```powershell
& "$env:LOCALAPPDATA/Android/Sdk/platform-tools/adb.exe" install -r private/android/Z0MB1ES-experimental.apk
& "$env:LOCALAPPDATA/Android/Sdk/platform-tools/adb.exe" logcat -s Z0MB1ES SDL AndroidRuntime
```

The application ID is `org.z0mb1es.recompiled.experimental`. It targets API 36,
requires API 24+, uses OpenGL ES 3, runs managed code without trimming/AOT, and
loads the prepared original game from a verified private copy of bundled assets.
First launch asks for flashing-warning acknowledgment and the Reduce Flashing
choice. Landscape is the default; the shared mobile settings control Auto
Rotate. Saves and the warning preference live in app-private `files/Saves/`,
separate from versioned immutable assets, and survive an APK replacement signed
with the same key. Uninstall removes app data. No storage or network permission
is requested. Session events also go to private `files/android-session.log`.

No physical Android execution is established by the build. Test startup and
content loading, all three modes, touch/controller behavior, music and song
timing, background/resume, rotation, and save/reload after a full app restart.
Native photo picking, haptics, first-launch package import, foldable safe-area
handling and release readiness remain later work. The fork documents ARM64
devices and does not support emulators: see its [platform notes](https://github.com/FNA-NET/FNA)
and [Android sample](https://github.com/FNA-NET/Samples/tree/main/AndroidGame1).
