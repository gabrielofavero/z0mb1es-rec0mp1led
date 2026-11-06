# Original game-loop menu bring-up (Prompt 8)

The launcher now loads the locally prepared `ZombiesWP7` assembly, constructs `ZombiesWP7.Game1`, and calls its inherited FNA `Run()` method. Original `Initialize`, `LoadContent`, `Update`, and `Draw` remain the game's methods. A host-owned `DrawableGameComponent` observes the state, render output, and exit conditions. It stops after a requested frame count or Ctrl+Q; frame-limited diagnostic runs also have a 60-second timeout. Escape now reaches the original Back/pause path. Failures are emitted as structured `run-failure` JSON with an exception type and message, without printing original IL or asset contents.

## Render setting evidence

The original `Game1` static initializer sets `VIEWPORT` to 600×360. Its `Initialize` applies that width and height as the backbuffer and requests fullscreen. The fresh run observed a 600×360 backbuffer. The `--windowed` option switches only the host window out of fullscreen after original initialization; it keeps the original backbuffer dimensions. No old-port scaler or viewport value was used as an acceptance target.

## Startup adaptations

| Traced failure | Narrow behavior for this bring-up |
| --- | --- |
| Original `AccelMgr` calls `Accelerometer.Start()` at construction; a desktop without that phone sensor cannot start it. | The bridge records an idempotent start, logs that no readings will arrive, and leaves the original game without sensor events. |
| The game's update checks `Gamer.SignedInGamers`; the initial unsupported bridge call repeatedly threw. | The bridge returns an empty read-only collection, representing no Xbox Live sign-in. |
| `CustomMgr.Init` calls FNA's unimplemented phone media-source enumeration on the loading thread. | One [guarded call-target patch](../../patches/menu-media-source.md) returns an empty media-source list and logs it. |
| `TrialMgr.CheckTrial` reads `Guide.IsTrialMode`; the initial unsupported bridge call stopped loading. | The bridge reports trial mode for this menu gate. This is a conservative temporary entitlement policy, not proof of the original phone license state. Prompt 10 must define and test the final offline policy. |

FNA's `TitleContainer` resolves the game's custom `chardef/`, scene, and other paths beside the launcher executable. The launcher therefore requires the prepared DLL and content beside itself. `scripts/stage-local-run.ps1` creates that layout under ignored `private/run/`, keeping the original XAP and derived files out of Git. The launcher verifies its prepared-DLL patch manifest, supported source XAP identity, and every content output's size and SHA-256 before starting. It sets the working directory to that staged runtime before construction for relative game paths. Under .NET 10, `IsolatedStorageFile.GetUserStoreForApplication` uses the user's Windows AppData location, so the user account needs write access there. A save created during initial .NET 8 tests was preserved under `private/run/initial-isolated-storage-p8`; Prompt 10 must define a stable desktop save policy and migration.

## Reproduce the fresh local run

From the repository root, after [offline dependency setup](../foundation/dependencies.md), with the supported XAP and pinned FFmpeg executable available:

```powershell
./scripts/build-offline.ps1
dotnet tools/prepare-cli/bin/Release/net10.0/prepare.dll --xap 'game/Z0MB1ES (on teh ph0ne) v1.0.0.0.xap' --output private/preparation-my-net10
$ffmpeg = '<absolute path to pinned ffmpeg.exe>'
dotnet tools/content-prepare/bin/Release/net10.0/content-prepare.dll --xap 'game/Z0MB1ES (on teh ph0ne) v1.0.0.0.xap' --preparation private/preparation-my-net10 --ffmpeg $ffmpeg --output private/content-prepared/my-net10
dotnet tools/abi_check/bin/Release/net10.0/abi_check.dll --inventory generated/evidence-from-xap.json --dll private/preparation-my-net10/original-xap/ZombiesWP7.dll --fna out/deps/managed/FNA.dll --bridge src/Port.Bridge/bin/Release/net10.0/Port.Bridge.dll --output generated/abi-report-bridge-net10.json
dotnet tools/prepare-cli/bin/Release/net10.0/prepare.dll rewrite --xap 'game/Z0MB1ES (on teh ph0ne) v1.0.0.0.xap' --dll private/preparation-my-net10/original-xap/ZombiesWP7.dll --abi-report generated/abi-report-bridge-net10.json --fna out/deps/managed/FNA.dll --bridge src/Port.Bridge/bin/Release/net10.0/Port.Bridge.dll --output private/prepared/my-net10-base
dotnet tools/prepare-cli/bin/Release/net10.0/prepare.dll hook --xap 'game/Z0MB1ES (on teh ph0ne) v1.0.0.0.xap' --dll private/preparation-my-net10/original-xap/ZombiesWP7.dll --prepared private/prepared/my-net10-base/ZombiesWP7.dll --hooks src/Port.Hooks/bin/Release/net10.0/Port.Hooks.dll --output private/prepared/my-net10-hook
./scripts/stage-local-run.ps1 -PreparedDirectory private/prepared/my-net10-hook -ContentDirectory private/content-prepared/my-net10 -OutputDirectory private/run/my-net10
dotnet private/run/my-net10/Port.Launcher.dll --run private/run/my-net10/ZombiesWP7.dll --content private/run/my-net10 --frames 150
```

Every output directory in this example must be new. The ABI checker reports zero unresolved references but currently exits with validation `fail` because isolated-storage behavior remains a Prompt 10 task; the pinned report still authorizes the guarded rewrite. The `--windowed` option is useful for local inspection; omit it for the original fullscreen request. Omit `--frames` for an interactive run. The current diagnostic host exits with Ctrl+Q or window close; Escape follows the game's Back/pause path. The 60-second timeout applies only to frame-limited diagnostic runs.

On the Windows x64 test desktop, the .NET 10 fresh-stage run used SDL GPU Vulkan on an NVIDIA GeForce GTX 970. It observed `originalViewport={X:600 Y:360} actualBackBuffer=600x360 fullscreen=True`, reached `STATE_MAINMENU=1`, read 153,935 nonblack pixels and at least eight colors on frame 120, and ended `updates=151 frames=150 menuReached=True menuAnimated=True rendered=True escaped=False timedOut=False` with exit code 0. After the FNA 26.11 update, the 150-frame menu check passed with 153,831 nonblack pixels and eight sample colors at frame 120. The exact pixel count can vary between runs. `--check-load` reported 177 types and 818 JIT-prepared methods. The earlier .NET 8 windowed and fullscreen results remain in the implementation log. This establishes an advancing, rendered menu and clean frame-limit exit on this desktop. Pointer/controller menu navigation and gameplay inputs are Prompt 9 work; save and offline-service behavior are Prompt 10 work.
