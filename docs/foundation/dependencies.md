# Pinned FNA dependency and build

The authored projects use **official FNA 26.11 source**, built unchanged as `FNA.dll`, and a pinned official Windows x64 native build. No assembly or native library from the old port is a project input. Exact archive URLs and SHA-256 values are in [`config/dependencies.json`](../../config/dependencies.json).

| Dependency | Acquisition and role | License / notice |
| --- | --- | --- |
| .NET SDK 10.0.302 | Install this exact .NET 10 SDK; `global.json` disables SDK roll-forward. The projects target `net10.0`. | [Microsoft .NET license information](https://dotnet.microsoft.com/en-us/platform/free) |
| [Official FNA 26.11 source](https://fna.flibitijibibo.com/archive/FNA-2611.zip) | `bootstrap-fna.ps1` verifies the archive hash and builds its `FNA.NetStandard.csproj` without source edits. | [FNA Microsoft Public License](https://github.com/FNA-XNA/FNA/blob/26.11/licenses/LICENSE), plus its bundled `lzxdecoder.LICENSE` and `monoxna.LICENSE`. |
| [Official fnalibs build, 2026-11-06](https://github.com/FNA-XNA/fnalibs-dailies/actions/runs/42165304917) | Artifact `13478882166` supplies SDL3, FNA3D, FAudio, Theorafile, and `D3D12/D3D12Core.dll`. The public [nightly.link download](https://nightly.link/FNA-XNA/fnalibs-dailies/actions/artifacts/13478882166.zip) is checked against GitHub's published SHA-256 digest. | Retain the native components' own notices when packaging. Review [SDL](https://github.com/libsdl-org/SDL/blob/main/LICENSE.txt), [FNA3D](https://github.com/FNA-XNA/FNA3D/blob/master/LICENSE), [FAudio](https://github.com/FNA-XNA/FAudio/blob/master/LICENSE), [Theorafile](https://github.com/FNA-XNA/Theorafile/blob/master/LICENSE), and the [DirectX 12 Agility SDK redistribution terms](https://devblogs.microsoft.com/directx/gettingstarted-dx12agility/). |
| [NETStandard.Library 2.0.3](https://www.nuget.org/packages/NETStandard.Library/2.0.3) | Cached reference package for the official FNA `netstandard2.0` build; it is not a game runtime asset. | [NuGet package license](https://www.nuget.org/packages/NETStandard.Library/2.0.3#license). |
| [Mono.Cecil 0.11.6](https://www.nuget.org/packages/Mono.Cecil/0.11.6) | Managed metadata/IL writer for local preparation. Its archive SHA-256 is pinned and checked before the offline build. | [MIT license in the upstream package specification](https://github.com/jbevain/cecil/blob/master/Mono.Cecil.nuspec). |

FNA's [setup guide](https://fna-xna.github.io/docs/1%3A-Setting-Up-FNA/) identifies SDL for window/input, FNA3D for graphics, and FAudio for audio. The native build is a daily development artifact produced after FNA 26.11, so compatibility is established here by ABI, content, smoke, and menu checks rather than by a matching release tag. GitHub's artifact metadata gives SHA-256 `c79c41819651d839066314f4556e679c6b609c7f035f241dfe81d63f09dfb3e6`; it expires on **2027-02-04**. Keep the verified archive under `out/deps/cache/` for offline bootstrap. After expiry, a fresh checkout will need this archive supplied locally or a newly selected and tested artifact with an updated pin. The official bundle does not contain `dav1dfile.dll`; AV1 video playback is therefore outside the current tested runtime. The previous third-party `dav1dfile.dll` is removed during bootstrap to avoid mixing native builds.

From a fresh checkout on Windows x64:

```powershell
./scripts/bootstrap-fna.ps1          # downloads and checks four archives into ignored out/deps/cache/
./scripts/build-offline.ps1          # restores from that cache, then builds without network
dotnet run --no-build -c Release --project src/Port.Smoke -- --frames 120
```

For a fully disconnected first bootstrap, place the four archives named in `config/dependencies.json` under `out/deps/cache/`, then run `./scripts/bootstrap-fna.ps1 -Offline`. The bootstrap verifies SHA-256 before extraction. `Port.Smoke` opens a 640×360 test window, clears each frame, accepts Escape or window close, and can exit after a chosen frame count for automation. Those dimensions belong only to the smoke test; they are not evidence for the original game's viewport.

The dependency cache, FNA build, native DLLs, and all authored build outputs stay in ignored `out/` or `bin/` directories. The installer copies the available FNA, native, .NET runtime, and FFmpeg notices into its payload; it also includes the Mono.Cecil package's MIT license declaration and license link. A redistribution rights review remains open before any public release.
