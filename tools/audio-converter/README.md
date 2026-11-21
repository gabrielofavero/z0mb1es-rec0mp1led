# Offline music converter

This program reads the exact ASF/WMA v2 packet layout in the supported phone XAP and writes stereo 44.1 kHz QOA music for FNA. It has no runtime codec library dependencies. The source includes the Apache-2.0-licensed `gameframe-wma` decoder and MIT-licensed `qoaudio` encoder in `vendor/`; their license texts are copied into desktop installers.

Vendored source revisions: `gameframe-wma` from game-frame commit `c30e3fc95c748a8ec44e3989faafc4220895cbab`, and `qoaudio` commit `c9abb17969d89b9ca328968dc4772432e6d4bb70`.

Build on the target platform with Rust 1.100.0:

```sh
CARGO_TARGET_DIR=out/audio-converter cargo build --release --offline --manifest-path tools/audio-converter/Cargo.toml
```

On Windows PowerShell, set `$env:CARGO_TARGET_DIR = 'out/audio-converter'` and run the same `cargo build` command. Windows release packaging verifies the resulting executable against `config/content-tools.json`. Linux and macOS packaging compile it from the same vendored source and record the actual executable hash inside their packages.

Usage: `port-audio-converter <supported.wma> <new.qoa>`. The caller checks the exact XAP fingerprint before conversion. This converter rejects other ASF/WMA layouts.

For iOS, `sh installer/ios/native/build.sh` compiles a static library with Rust 1.100.0 and the `aarch64-apple-ios` target. `sh installer/ios/native/build.sh osx-arm64` builds the matching macOS dylib for import checks and personal payload preparation. Both use the two-path `z0_audio_convert` C ABI in `src/lib.rs`. The iOS adapter serializes conversions to limit peak PCM-buffer memory. Converter license notices are embedded in the IPA; this path uses no FFmpeg and generates no companion source download.
