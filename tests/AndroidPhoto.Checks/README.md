# Android creator photo checks

This standalone Android app compiles the production picker and runs its result
handling, decoder and private storage against generated images. It intercepts
picker presentation so it can check cancellation, duplicate results, EXIF
rotation, sRGB thumbnails, failure recovery and the collection limit unattended.
It does not contain or require game assets. The game uses a separate app ID.

After Android bootstrap and building `src/Port.AndroidHooks`, build with the
repository's Android SDK from the repository root (supply SDK/JDK paths if needed):

```powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD 'out/android/dotnet-home'
$env:NUGET_PACKAGES = Join-Path $PWD 'out/android/nuget'
& ./out/android/dotnet/dotnet.exe build tests/AndroidPhoto.Checks -c Release
```

The default runtime is `android-x64` for an emulator. Override with
`-p:RuntimeIdentifier=android-arm64` for a physical ARM64 device. Install the
`org.z0mb1es.photochecks-Signed.apk` from the project's runtime output directory,
then launch it and inspect its log:

```powershell
adb -s emulator-5554 shell monkey -p org.z0mb1es.photochecks 1
adb -s emulator-5554 logcat -d -s PhotoChecks
```

Success ends with `ANDROID_PHOTOS_OK`; failures end with `ANDROID_PHOTOS_FAILED`.
Run on API 24-27 to exercise the legacy decoder; API 28+ uses ImageDecoder.
The shared creator interaction checks remain in `tests/IosGame.Checks`.
