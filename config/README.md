# Version and dependency pins

This folder will hold supported XAP/DLL fingerprints and pinned FNA, native library, IL writer, and content-tool versions. Keep it metadata-only: no original game bytes or generated outputs.

`supported-versions.json` pins the one verified XAP/DLL pair used by the metadata inspector. It is an exact-byte support boundary, not a catalog of all legitimate releases.

`dependencies.json` pins the authored host's FNA source, Windows native bundle, and build reference archives by SHA-256. Acquisition and license details are in `docs/foundation/dependencies.md`.

`content-tools.json` pins the Windows offline WMA-to-QOA converter built from `tools/audio-converter`.

`linux-packaging.json` pins the Linux x64 runtime packs, FNA native archive/files, and nFPM archive. Packaging builds the audio converter from vendored source and writes its hash into the app so first-launch preparation checks it before use.

`supported-360-versions.json` pins the two locally inspected `LIVE` packages. `supported-steam-versions.json` pins the observed Steam I MAED 1.0.0.0 executable and nine required visual textures. These are metadata-only boundaries for the completed [asset swap plan](../docs/plans/done/2026-11-07-asset-swap.md) and are consumed by the optional-art importer.
