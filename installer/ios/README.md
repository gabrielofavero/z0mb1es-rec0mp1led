# iOS execution experiment (Prompt 6, acceptance open)

This is a content-free UIKit feasibility probe, not an iOS game release. No IPA or real-device result has been produced in the current workspace. The available Mac runs Ventura 13.5 with Command Line Tools only; the pinned iOS SDK needs full Xcode on a newer macOS. The user has an iPhone and reported AltStore setup complete. Device model and iOS version remain unrecorded.

`src/Port.IosProbe` exercises runtime-created authored IL, Cecil read/write/rewrite, `Assembly.LoadFrom`, reflection and delegate calls, persistent storage, exact XAP validation/extraction, and three-song conversion through a static native library. On ordinary launch it rechecks persistent IL and imported-file hashes without a source path. It can share its text trace. It does **not** yet supply FNA or execute `ZombiesWP7.Game1`; a successful generated-IL call cannot close Prompt 6.

## Build on a compatible Mac

The experimental baseline is .NET SDK **10.0.302**, iOS manifest/ref **26.0.11017**, full **Xcode 26.0.1**, and `ios-arm64`. The probe deployment minimum is iOS 15.0; this is not a promised game support floor. [.NET's release notes](https://github.com/dotnet/macios/wiki/.NET-10-release-notes) require Xcode 26 / macOS 15.6 or newer for this baseline. Simulator builds and Command Line Tools are insufficient.

With that SDK selected, install its baseline workload using `dotnet workload install ios --skip-manifest-update`. Put the three reviewed archives from the [converter source table](../ffmpeg/README.md) in ignored `private/ffmpeg-macos/source`, or set `IOS_AUDIO_SOURCES` to their directory. The native recipe verifies every archive before extraction. `pkg-config`, `make`, Python 3, and `rg` must be available.

```sh
export DEVELOPER_DIR=/Applications/Xcode_26.0.1.app/Contents/Developer
sh installer/ios/build-probe.sh
```

The project explicitly uses `UseInterpreter=true`, `PublishAot=false`, `TrimMode=copy`, `EnableCodeSigning=false`, and `BuildIpa=true` in Release. Mono's interpreter is the candidate for executing imported IL without a JIT. NativeAOT cannot dynamically load the game. Microsoft's [interpreter guide](https://learn.microsoft.com/en-us/dotnet/maui/macios/interpreter?view=net-maui-10.0) and [host build properties](https://github.com/dotnet/macios/blob/main/docs/building-apps/build-properties.md) describe these settings; the build log and device run must verify their effect in this plain .NET iOS host.

Expected output is `src/Port.IosProbe/bin/Release/net10.0-ios26.0/ios-arm64/Z0MB1ES-execution-probe.ipa` (the audit locates the actual SDK output), plus `out/ios/probe-build.binlog` and `out/ios/probe-audit.json`. The audit rejects source game files, prepared-game manifests, original XAP entry hashes when a local XAP is available, an unexpected app identity, and a non-arm64 executable. It checks the converter archive identity. Neither script has been accepted against a real built IPA yet.


## AltStore and device trace

Use [AltStore Classic's Mac instructions](https://faq.altstore.io/altstore-classic/how-to-install-altstore-macos) for AltServer, Finder Wi-Fi visibility, device trust, regular Apple ID signing, and Developer Mode where required. Import the built IPA using **My Apps → +** in AltStore. Free-account installations need periodic refresh; see [AltStore's refresh guide](https://faq.altstore.io/altstore-classic/your-altstore). Never put account credentials in the repository, build workflow, or trace.

1. Record iPhone model, iOS version, AltStore/AltServer versions, IPA checksum, and the exact build log. Reboot the phone so an earlier debugging/JIT session cannot contaminate the result. Launch from its Home Screen with no debugger or Enable JIT action.
2. Tap **Test generated IL**. A passing call returns 42 after rewriting an authored constant from 1 to 37. The assembly is created after installation and has no build-time AOT image. Save the trace even if the runtime crashes; a managed `try/catch` cannot capture every native runtime abort.
3. Force-quit, reopen normally, then reboot and reopen again. The trace must show `persistent-il=passed` using the same payload hash. Flags such as `IsDynamicCodeCompiled` are context, not proof that JIT was disabled.
4. Select the supported XAP through **Import XAP and convert songs**. Check extraction, real-game metadata roundtrip, all three songs, and `persistent-import=passed` on restart. Remove only a disposable source copy and repeat. Cancel selection; try a corrupt/unsupported copy; check previous imported results remain intact.
5. **Share device trace** exports text only. Keep raw traces under ignored `private/ios-validation`; summarize observations and hashes in the implementation log. Obtain the iOS crash report if the process dies between trace boundaries.

This experiment intentionally retains successful attempts in app-private Application Support so failures cannot erase earlier evidence. It is not the production Reimport transaction and has no save-game access. Uninstalling the probe removes its private data. Storage cleanup and background import cancellation belong to the production mobile flow.

## Audio implementation and local checks

`native/audio.c` calls libavformat/libavcodec/libavutil and libvorbis directly; it never starts FFmpeg or calls its CLI entry point. It accepts the pinned stereo 44.1 kHz WMA v2 format, queues decoded planar-float samples in a bounded FIFO, encodes Vorbis quality 5, flushes decoder/encoder, and muxes Ogg. Each conversion owns its state, supporting the shared preparer's three concurrent calls. The shared content verifier checks the Ogg header before accepting output. Conversion progress currently reports stage completion rather than native sample progress.

The native build disables programs, networking, GPL and nonfree components, and unneeded codecs. Source versions are FFmpeg `239f2c733d` (8.1.1), libogg 1.3.6, and libvorbis 1.3.7, with the same archive hashes as desktop. The generated SHA-256 identifies the combined static archive and is recorded in the content manifest's legacy converter-hash field. It is a build identity, not a reviewed iOS release pin. The current macOS check archive hash is recorded in the implementation log; an iOS archive must be built and reviewed separately.

```sh
sh installer/ios/native/build.sh osx-arm64
dotnet restore tests/IosProbe.Checks --source out/deps/cache -p:NuGetAudit=false
dotnet run --project tests/IosProbe.Checks -c Release --no-restore -- \
  'game/Z0MB1ES (on teh ph0ne) v1.0.0.0.xap' out/ios/checks/native/libz0audio.dylib
dotnet run --project tests/IosProbe.Checks -c Release --no-build -- --generate out/ios/restart-check
dotnet run --project tests/IosProbe.Checks -c Release --no-build -- --resume out/ios/restart-check
```

These checks passed on macOS. The UI also compiled against the 26.0.11017 reference assembly, without linking an iOS executable. Neither result establishes device support.

## Remaining execution gate and fallback

The chosen **experiment** is Mono plus interpreter, with imported game execution still undecided. The desktop `GamePreparationPipeline` starts inspector/ABI executables; an iOS host must call their authored analysis code in-process, generate and review the exact iOS ABI report, then use the existing guarded rewriter and hook contracts. Desktop report hashes must not be added as iOS evidence. FNA and its SDL3/FNA3D/FAudio/Theorafile iOS static libraries, bridge, mobile hooks, lifecycle and graphics entry point are still absent from this small probe.

After generated IL passes on a real device, add that actual FNA host and test the prepared phone assembly in normal, Viking and Endless modes, including music-driven progression, pause and resume. Use the [device gate and authored-gameplay fallback design](../../docs/runtime/ios-execution.md) to choose the production path. The absence of Xcode here is not evidence that imported IL fails. Do not advance to Prompt 7/8 acceptance until the real-game gate passes or the fallback's behavior specification and implementation scope have been accepted.

## Distribution constraints

This recipe links FFmpeg statically into the app, unlike the desktop converter process. Desktop executable notices alone do not cover this arrangement. Before distributing a probe or release, assemble the applicable FFmpeg LGPL notices, Xiph notices, matching source/build scripts and a usable relinking/rebuild route for the static combination, plus Mono.Cecil/.NET notices; review the resulting artifact. Retain the matching source archives, static libraries and build scripts for that work; the local build does not establish license compliance or authorize publication. Proprietary game IL, music, artwork, extracted files and metadata-roundtrip DLLs stay on the importing device or in ignored developer storage.
