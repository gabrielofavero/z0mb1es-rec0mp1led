# Deterministic local XAP extraction

Prompt 3 adds a reusable .NET engine in `src/Port.Prepare` and a developer CLI in `tools/prepare-cli`. It accepts only the exact XAP/DLL pair in [`config/supported-versions.json`](../../config/supported-versions.json). This phase extracts the original package and records provenance; it does **not** rewrite the DLL, convert content, or make the game playable. The Python extractor remains a separate developer tool. The future installer helper should call `PreparationEngine.ValidateXap` and `PreparationEngine.Prepare` directly, so validation and extraction use the same rules.

From the repository root, after the Prompt 2 build:

```powershell
dotnet run --no-build -c Release --project tools/prepare-cli -- --xap 'game/Z0MB1ES (on teh ph0ne) v1.0.0.0.xap' --output private/my-preparation
```

The built CLI is named `prepare.exe`, so its direct form is `prepare --xap <path> --output <new-directory>`. Omitting `--output` uses `private/preparation` relative to the current directory. The destination **must not exist**, including as an empty directory; a prior import is never overwritten. For a second local run, choose another ignored directory such as `private/my-preparation-2`. A safe explicit alternative is a **new** directory outside the repository, for example `$env:LOCALAPPDATA\ZombiesPort\imports\sample-1`; keep it outside Git and do not point it at the source XAP or an existing install.

Successful output has this shape:

```text
<output>/
  original-xap/       # byte-identical original package files, including ZombiesWP7.dll
  provenance.json     # deterministic extraction record
```

The provenance records the supported version ID, package and DLL SHA-256/lengths, archive counts, preparation format/tool version, and each extracted file's relative path, length, and SHA-256. It contains no absolute paths or timestamps. File paths are sorted ordinally, so two runs from the same input and engine version yield identical manifest bytes. The manifest labels the phase `original-extracted` so it cannot be mistaken for a playable prepared assembly.

Before creating output, the engine opens the source read-only, verifies the full XAP hash against the embedded version table, checks ZIP paths, duplicates, links, entry counts and size, verifies the embedded DLL hash, and checks both app manifests. It then writes into a unique sibling staging directory, reads staged files back to verify their hashes, writes `provenance.json`, and atomically renames the staging directory to the requested final name. A failure or cancellation removes the owned staging directory; existing destinations remain untouched. The source XAP is not copied into output.

The output contains original copyrighted material. Keep it under ignored `private/` or a private external location. Do not commit or package it. The eventual player setup will run the same engine with its bundled .NET runtime, so the player will not need Python or the .NET SDK.
