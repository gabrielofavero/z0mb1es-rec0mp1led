# Z0MB1ES (on teh ph0ne) for Android

The Android port has been confirmed working on a device. The player APK contains
the port and import tools; users select their own game files on the device.
Setup shares the iOS FNA screen, verification, optional artwork importer and
transactional asset store.

## Install and import

1. Install `out/android/Z0MB1ES.apk` from a file manager, allowing it
   to install apps when Android asks. Updates with the same app ID keep saves.
2. Launch **Z0MB1ES** and acknowledge the flashing warning. The
   optional **Enable Reduce Flashing** choice is saved with your preferences.
3. Tap **Phone** and select the supported original XAP in Android's file picker.
4. Optionally select **I MAED** artwork (supported Steam ZIP or Xbox 360 package)
   and **Time Viking** artwork (supported Xbox 360 package). A Steam ZIP must
   contain one `Z0MB1ES.exe` with its matching `Content` folder. Verified artwork
   is shown as **Enhanced**.
5. Tap **Import and play**. Preparation, music conversion and verification run on
   the device. Later launches use private assets without source packages.

**Settings → Graphics → Assets → Reimport assets** saves and closes the game,
then opens setup. Artwork-only replacement reuses installed phone assets. Reopen
the app after reimport. Failed/canceled imports preserve active assets and saves.
Backgrounding during preparation requests cancellation. The picker copies selected
documents into private staging and removes those copies after use; original
files remain unchanged. No broad storage or network permission is requested.

The application ID is `org.z0mb1es.recompiled` (version 1.1.0, version code 4). Android installs
this separately from the old `org.z0mb1es.recompiled.experimental` package;
its private assets and saves are not transferred automatically. Keep the old app
installed if you need its data. The signing identity is retained. Saves and
pictures remain in `files/Saves/` and `files/Pictures/`, separate from imported
assets. Uninstalling removes app data.

## Build

From PowerShell 7 at the repository root:

```powershell
./installer/android/bootstrap.ps1
. ./dev.ps1
build-apk
```

The Windows x64 CLI needs the normal managed-build prerequisites, Python 3,
JDK 17 or 21, Android SDK platform 36 / build-tools 36.1.0, Rust 1.100.0 with
`aarch64-linux-android`, and Android NDK r27d (27.3.13750724). No XAP or artwork
is a build input. Install the Rust target with
`rustup target add aarch64-linux-android`. The existing repository-local Rust
toolchain is used when present.

```powershell
build-apk -JavaSdkDirectory 'C:/path/to/jdk' `
    -AndroidSdkDirectory 'D:/Android/Sdk' -NdkDirectory 'D:/Android/android-ndk-r27d'
```

Defaults are `C:/Program Files/Java/jdk-17`, `$env:LOCALAPPDATA/Android/Sdk`, and
`out/android/ndk/android-ndk-r27d`. SDK/workload/package caches stay isolated in
`out/android/`. The tested JDK is Oracle 17.0.10+11; the pinned Android tooling
cannot parse some five-component Microsoft JDK version strings.

The build uses separate Android bridge/hooks/artwork assemblies against pinned
FNA.NET. Preparation uses the reviewed ABI metadata plan, then checks every
Android framework/bridge/hook reference before retargeting. Desktop DLLs in the
preparation assets are metadata inputs and never loaded as executable code.
The native music library uses the desktop converter's guarded WMA decoder and
QOA encoder, with both vendored license notices and 16 KB ELF alignment.

The signed APK audit checks exact preparation inputs, pinned ARM64 libraries,
converter identity and absence of original/prepared game content. `apksigner`
checks its signature. Back up `private/android/signing/experimental.keystore`
and `password.txt` together; their historical names/alias preserve updates.
The CLI refuses an incomplete key/password pair.

Code changes still require `. .\dev.ps1; build-game` per `AGENTS.md`. Packaging
itself does not run that proprietary-input acceptance check.

## Controls and diagnostics

The APK targets API 36 and requires ARM64, Android API 24+ and OpenGL ES 3.
Landscape is the default; shared mobile settings control Auto Rotate.
Android gamepad events drive gameplay and menus with SDL fallback. Face/shoulder
buttons, sticks, D-pad/hats and triggers are supported. The most recently used
controller supplies player one; background/disconnect clears held inputs.
JNI runtime registration and Android-only native asset selection are preserved.

On Quest, pair an Xbox controller or enable **Use as gamepad** for the Quest
controllers in system settings. The app runs as a 2D panel. The earlier emulator
check reached the original menu but rendered black due to incomplete ARM64
OpenGL ES translation. Use a physical ARM64 device for visual acceptance.
The new package-picker/import flow still needs a device check, although the
preceding bundled port has been confirmed working.

Session events go to `files/android-session.log` and the `Z0MB1ES` log tag:

```powershell
& "$env:LOCALAPPDATA/Android/Sdk/platform-tools/adb.exe" install -r out/android/Z0MB1ES.apk
& "$env:LOCALAPPDATA/Android/Sdk/platform-tools/adb.exe" logcat -s Z0MB1ES SDL AndroidRuntime
```

Controller checks: `dotnet run --project tests/AndroidInput.Checks -c Release`.
Real-package importer checks use `tests/AndroidImport.Checks` with a supported
XAP and a host build of the converter shared library; optional I MAED and Time
Viking source arguments exercise enhanced artwork.
