# Z0MB1ES (on teh ph0ne) FNA port

This repository is the clean port workspace. The game comes from a XAP supplied locally by the player; the project does not ship the original game code or assets. The port and its user-friendly installer are planned, not yet playable.

- [Architecture](docs/architecture.md): original DLL boundary, FNA bridge, and local preparation.
- [Implementation prompts](docs/plans/implementation-prompts.md): ordered work and acceptance gates.
- [Installer design](installer/README.md): one Windows setup executable that asks for the player's XAP.
- [Developer tools](tools/): safe XAP extraction and read-only assembly inspection.

The old `../z0mb1es-on-teh-fna` project is a private diagnostic reference. Its output is not a behavioral standard for this port.
