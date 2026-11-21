# Audio asset portability audit

The supported XAP contains 19 sound-effect XNBs and three Song XNBs with WMA companions. This audit distinguishes the source files from the locally prepared content that a port build runs.

| Asset | Format found in this XAP | Desktop preparation | Cross-platform result |
| --- | --- | --- | --- |
| 19 sound effects | `XNBm` v5 `SoundEffectReader` payloads; WaveFormat tag `0x0001` (PCM), 16-bit, mono or stereo, at 11,025–48,000 Hz | Copy byte for byte | FNA's `SoundEffectReader` passes the PCM format to FAudio. No codec conversion is needed. |
| Three music tracks | Song XNBs point to same-name `.wma` files containing WMA v2 audio, stereo 44,100 Hz | Convert WMA to `.qoa` with the vendored Rust converter; retain the original XNB metadata | FNA's `SongReader` resolves the `.wma` stem to `.qoa` and plays it through FAudio. The prepared tracks are stereo 44,100 Hz. |

The original FFmpeg audit below is historical; current preparation uses QOA on every platform. The source sound-effect format tags were read from the 19 XNB payloads, not inferred from their extensions. FFprobe identified all three source tracks as `wmav2`; the prepared Ogg identification headers report Vorbis, two channels, and 44,100 Hz for `endless`, `epicopus`, and `timeviking`. The original Song XNBs contain the matching lower-case `.wma` basenames; the prepared `.ogg` names preserve that spelling for case-sensitive filesystems. A fresh preparation with the new Ogg header check produced the same content-manifest SHA-256 as the previous Windows preparation: `239e04ae6cadf64b937bb1e7bc2cd5eaa511a7998096fa7d6dde33c25c60ab77`.

The pinned [FNA `SongReader`](https://github.com/FNA-XNA/FNA/blob/master/src/Content/ContentReaders/SongReader.cs) documents `.ogg`, `.oga`, and `.qoa` lookup. Its [`SoundEffectReader`](https://github.com/FNA-XNA/FNA/blob/master/src/Content/ContentReaders/SoundEffectReader.cs) reads the embedded WaveFormat block. FNA's [setup documentation](https://fna-xna.github.io/docs/1%3A-Setting-Up-FNA/) identifies FAudio as the native audio/media dependency. These source facts support format compatibility; actual Linux and macOS playback still requires platform-native FAudio/SDL libraries and a device run.

The current content-preparation CLI checks the Windows Rust converter hash from `config/content-tools.json`; non-Windows development hosts pass `--converter-sha256 <64-hex-digest>`. Every generated music file must have a stereo 44.1 kHz QOA header before the staged content is committed. Desktop packages include a converter executable; Android and iOS link the vendored Rust library in-process. No platform requires a separate FFmpeg source package.

```text
dotnet tools/content-prepare/bin/Release/net10.0/content-prepare.dll --xap <supported.xap> --preparation <verified-extraction> --converter <local-converter> --output private/content-prepared/<new-directory> --converter-sha256 <verified-sha256>
```

The Windows FNA probe loaded and played all prepared sounds, including simultaneous sound effects and music pause/resume. That validates this prepared content and code path on Windows only. That audit did not test Linux or macOS. Linux runtime acceptance remains open; macOS Apple Silicon manual acceptance was subsequently confirmed by the user on 2026-11-10, as recorded in the [implementation log](../records/implementation-log.md).

The [iOS host](../../installer/ios/README.md) now builds the same Rust WMA-to-QOA converter as desktop and Android. The adapters serialize conversions to limit peak buffered PCM memory, and packaging embeds the converter notices and audits the linked executable for FFmpeg symbols. Earlier Vorbis conversion and device results apply to the retired converter. QOA import, playback timing, and performance still need a device check after this migration.
