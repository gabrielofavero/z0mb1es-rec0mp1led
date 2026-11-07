# Build and release checks

This folder will hold repeatable build, clean-install acceptance, and distributable-audit scripts. The audit must inspect both tracked files and the completed installer for game code/assets and local paths, not just trust `.gitignore`.

`bootstrap-fna.ps1` verifies and prepares pinned FNA and native dependencies under ignored `out/`. `build-offline.ps1` restores the authored solution from the local cache and builds it without network access. The installer and release audits remain future work.

`stage-local-run.ps1` copies the authored runtime, a verified locally prepared DLL, and locally prepared content into a new ignored `private/run/` directory. FNA resolves `TitleContainer` files beside the launcher, so this stage mirrors the eventual install layout.

Run `./dev.ps1` from the repository root to choose and launch any staged build in `private/run/`. The menu discovers builds each time it runs. Use `./dev.ps1 -List` to see available builds or `./dev.ps1 -Build <name>` to launch one directly.

`build-installer-art.py` renders the authored square icon and cover SVGs into committed executable icons and Inno Setup wizard images. It uses local Edge or Chrome plus Pillow and NumPy only on the developer machine; see [artwork](../assets/README.md).
