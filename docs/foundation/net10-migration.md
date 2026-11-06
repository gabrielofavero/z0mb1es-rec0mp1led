# .NET 10 migration (2026-11-06)

The developer SDK was pinned to **10.0.302** in `global.json`; authored projects target `net10.0`. At the time of this migration, FNA 26.02 built as `netstandard2.0` with that SDK. The FNA archive and native bundle were subsequently updated to 26.11; see [current dependencies](dependencies.md). NETStandard.Library, Mono.Cecil, FFmpeg, the supported XAP, and the original DLL identities did not change. The .NET 8 results in `implementation-log.md` are historical and remain available for comparison.

| Generated input for guarded preparation at the time of this migration | .NET 10 SHA-256 |
| --- | --- |
| Built `out/deps/managed/FNA.dll` | `6b7a6b6a54338fd8ff381ff2ccdd5129173059f153b900e3c5ad7996d1fcf06a` |
| Built `Port.Bridge.dll` | `4800c38ea91f6d43469173a6fa9fea8f9614106fc804ade4982833288648377f` |
| Built `Port.Hooks.dll` | `0da7b8604f5e40f609f1a1b92ff084b995ddc8eaded4be7c74172f28f7c11348` |
| `generated/abi-report-net10.json` without bridge | `a32e01134afdac86286d717e2ca4935fb084100b9870edeea03042e991483ada` |
| `generated/abi-report-bridge-net10.json` | `fe7a560fd752c57be8c7e286584d761b4ac82302a22e682171556c715f31df96` |

The pinned reports were regenerated from the exact original DLL and FNA build. With the bridge, all 146 type references, 382 member references, virtual slots, inheritance relationships, and method specifications resolve. The report still marks the desktop save-policy behavior as pending. `IlRewriter` and `GameplayPatcher` now require the new hashes; feeding the old .NET 8 bridge report to the .NET 10 rewriter failed before creating output.

Fresh extraction from the supported XAP yielded 110 original files and provenance SHA-256 `7992122d01d979f80dfdc668b40c28d999ff00dfa7c63c839aecc6211e0aff62`. The .NET 10 content preparation produced the same manifest SHA-256 as before: `239e04ae6cadf64b937bb1e7bc2cd5eaa511a7998096fa7d6dde33c25c60ab77`. The base rewritten game DLL SHA-256 is `1f8863b33e47e808a505a8d20ac4fc3fcee79032c3c5bc79a4b5621d7fda9aaf`; the guarded media-hook output is `34e3dd2c016a4283941317d4ba9db1be3ccf87bfe69fba99339ca877d1190569`. The original DLL remains unchanged. All generated outputs are ignored under `generated/` or `private/`.

Verification on the Windows x64 test desktop:

- Two offline FNA bootstraps produced the same managed DLL hash; repeated offline solution builds had zero warnings/errors and stable bridge/hook hashes.
- The inspector regenerated the original inventory byte for byte (SHA-256 `1518e1916cad6b6405f5da7d04a543430eb60f27526e6c294503b5aae0c01747`). ABI fixture, preparation, rewrite fixture, and bridge checks passed.
- The content probe loaded all 96 prepared XNBs with zero failures. The independent FNA smoke host rendered 60 frames and exited 0.
- `--check-load` resolved 177 game types and JIT-prepared all 818 methods. A fresh staged 150-frame fullscreen run reached original menu state 1, observed a changing menu animation field and 153,935 nonblack backbuffer pixels at frame 120, and exited 0 with `menuReached=True menuAnimated=True rendered=True timedOut=False`.

The .NET 10 runtime placed `IsolatedStorageFile.GetUserStoreForApplication` data under the current user's Windows AppData folder. A filesystem-restricted test could not create that folder and logged `UnauthorizedAccessException`; the normal user-profile run succeeded. The launcher's working directory remains the staged runtime for relative content paths, but it does not redirect .NET 10 isolated storage. Prompt 10 must define a stable save path, migration, and failure handling before the player installer is built. No save data was deleted during this migration.

Use [menu bring-up](../runtime/menu-bringup.md#reproduce-the-fresh-local-run) for current end-to-end commands. Microsoft lists .NET 10 as an LTS release supported through November 2028: [releases and support](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support).
