# Build and release checks

This folder will hold repeatable build, clean-install acceptance, and distributable-audit scripts. The audit must inspect both tracked files and the completed installer for game code/assets and local paths, not just trust `.gitignore`.

`bootstrap-fna.ps1` verifies and prepares pinned FNA and native dependencies under ignored `out/`. `build-offline.ps1` restores the authored solution from the local cache and builds it without network access. The installer and release audits remain future work.

`stage-local-run.ps1` copies the authored runtime, a verified locally prepared DLL, and locally prepared content into a new ignored `private/run/` directory. FNA resolves `TitleContainer` files beside the launcher, so this stage mirrors the eventual install layout.

Dot-source the developer commands from the repository root with `. .\dev.ps1`:

- `build-game` compiles the solution, prepares the supported local XAP, and replaces the single staged build at ignored `private/run/current/`. It builds in temporary directories and keeps the previous runnable build until the new one is staged. It accepts optional `-XapPath` and `-FfmpegPath` parameters.
- `run-game` launches `private/run/current/` directly. Use `run-game -List` to show that build without launching; `-Build current` is also accepted.
- `run-game-latest` is an alias for launching `private/run/current/`.
- `build-installer` invokes the existing installer build with the pinned converter at `private/tooling/ffmpeg.exe` and the local runtime-pack cache. It accepts the installer script's optional path and source parameters.
- `run-installer` opens `installer/out/Z0MB1ES-0.1.0.exe` after it has been built. Pass `-InstallerPath <path>` to open a different setup executable.

`build-installer-art.py` renders the authored square icon and cover SVGs into committed executable icons and Inno Setup wizard images. It uses local Edge or Chrome plus Pillow and NumPy only on the developer machine; see [artwork](../assets/README.md).
