# Audio asset portability audit

The supported XAP contains 19 sound-effect XNBs and three Song XNBs with WMA companions. This audit distinguishes the source files from the locally prepared content that a port build runs.

| Asset | Format found in this XAP | Desktop preparation | Cross-platform result |
| --- | --- | --- | --- |
| 19 sound effects | `XNBm` v5 `SoundEffectReader` payloads; WaveFormat tag `0x0001` (PCM), 16-bit, mono or stereo, at 11,025–48,000 Hz | Copy byte for byte | FNA's `SoundEffectReader` passes the PCM format to FAudio. No codec conversion is needed. |
| Three music tracks | Song XNBs point to same-name `.wma` files containing WMA v2 audio, stereo 44,100 Hz | Convert WMA to `.ogg` with Vorbis audio; retain the original XNB metadata | FNA's `SongReader` resolves the `.wma` stem to `.ogg` and plays it through FAudio. The prepared tracks are stereo 44,100 Hz. |

The source sound-effect format tags were read from the 19 XNB payloads, not inferred from their extensions. FFprobe identified all three source tracks as `wmav2`; the prepared Ogg identification headers report Vorbis, two channels, and 44,100 Hz for `endless`, `epicopus`, and `timeviking`. The original Song XNBs contain the matching lower-case `.wma` basenames; the prepared `.ogg` names preserve that spelling for case-sensitive filesystems. A fresh preparation with the new Ogg header check produced the same content-manifest SHA-256 as the previous Windows preparation: `239e04ae6cadf64b937bb1e7bc2cd5eaa511a7998096fa7d6dde33c25c60ab77`.

The pinned [FNA `SongReader`](https://github.com/FNA-XNA/FNA/blob/master/src/Content/ContentReaders/SongReader.cs) documents `.ogg`, `.oga`, and `.qoa` lookup. Its [`SoundEffectReader`](https://github.com/FNA-XNA/FNA/blob/master/src/Content/ContentReaders/SoundEffectReader.cs) reads the embedded WaveFormat block. FNA's [setup documentation](https://fna-xna.github.io/docs/1%3A-Setting-Up-FNA/) identifies FAudio as the native audio/media dependency. These source facts support format compatibility; actual Linux and macOS playback still requires platform-native FAudio/SDL libraries and a device run.

The content-preparation CLI keeps the repository-pinned Windows FFmpeg hash. On another host it now requires `--ffmpeg-sha256 <64-hex-digest>` for a local FFmpeg executable. The executable is hash-checked before any output is created. Each converted file must begin with a stereo 44.1 kHz Ogg Vorbis identification packet before the staged content is committed. The output manifest records the converter hash and every generated file hash. A future Linux or macOS release must pin, test, and package its own permitted converter and native libraries; passing a local hash alone is a development path, not a release pin.

Example on a non-Windows development host after obtaining and checking a local FFmpeg executable:

```text
dotnet tools/content-prepare/bin/Release/net10.0/content-prepare.dll --xap <supported.xap> --preparation <verified-extraction> --ffmpeg <local-ffmpeg> --output private/content-prepared/<new-directory> --ffmpeg-sha256 <verified-sha256>
```

The Windows FNA probe loaded and played all prepared sounds, including simultaneous sound effects and music pause/resume. That validates this prepared content and code path on Windows only. That audit did not test Linux or macOS. Linux runtime acceptance remains open; macOS Apple Silicon manual acceptance was subsequently confirmed by the user on 2026-11-10, as recorded in the [implementation log](../records/implementation-log.md).

The [Prompt 6 iOS experiment](../../installer/ios/README.md) adds a static libav/libvorbis converter implementing `IContentAudioConverter` without a process. Its macOS check converted all three pinned songs concurrently, produced valid Vorbis, and passed full decode checks (302.04517, 817.94612 and 907.66803 seconds). This is desktop evidence for the library API, not iOS playback evidence. iOS static compilation, device memory/performance, actual game music time and the static-link distribution review remain open.
