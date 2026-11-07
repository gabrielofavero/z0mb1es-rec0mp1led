# Version and dependency pins

This folder will hold supported XAP/DLL fingerprints and pinned FNA, native library, IL writer, and content-tool versions. Keep it metadata-only: no original game bytes or generated outputs.

`supported-versions.json` pins the one verified XAP/DLL pair used by the metadata inspector. It is an exact-byte support boundary, not a catalog of all legitimate releases.

`dependencies.json` pins the authored host's FNA source, Windows native bundle, and build reference archives by SHA-256. Acquisition and license details are in `docs/foundation/dependencies.md`.

`content-tools.json` pins the exact local FFmpeg executable used for the Prompt 7 WMA conversion. Its observed license and acquisition route are recorded in `docs/runtime/content-coverage.md`.

`supported-360-versions.json` pins the two locally inspected `LIVE` packages. `supported-steam-versions.json` pins the observed Steam I MAED 1.0.0.0 executable and nine required visual textures. These are metadata-only boundaries for the [asset swap plan](../docs/plans/in-progress/2026-11-07-asset-swap.md); no importer consumes them yet.
