# Behavior evidence ledger — supported WP7 XAP

This ledger covers only the exact package in [`config/supported-versions.json`](../../config/supported-versions.json). `generated/evidence-from-xap.json` is the ignored, reproducible metadata and IL inventory. “Observed” means package bytes or manifests were inspected locally; it does **not** mean the game was observed running on a phone. “IL-derived” means a static call, signature, or literal in the original DLL. No original-runtime session was available for this prompt.

| Claim | Source and check | Class | Confidence / limit |
| --- | --- | --- | --- |
| This package is the documented v1.0.0.0 XAP and contains `ZombiesWP7.dll` | SHA-256 of the XAP and embedded DLL; `AppManifest.xaml`; `WMAppManifest.xml`; version manifest | Observed | High for this exact package only. The manifest does not identify other legitimate releases. |
| The package has 127 ZIP entries and 76,993,533 uncompressed bytes; the DLL is 750,136 bytes | ZIP central directory and embedded entry, checked by `inspect_assembly --json --xap` | Observed | High for this exact package. |
| The DLL has 178 type definitions, 818 method definitions, 9 AssemblyRefs, 146 TypeRefs, and 382 MemberRefs | Original DLL ECMA-335 metadata inventory | Observed | High; row counts are independent of game execution. |
| The entry type is `ZombiesWP7.Game1` | `AppManifest.xaml` entry point plus matching type definition | Observed | High for the manifest declaration; actual boot remains untested. |
| `Game1` supplies `Initialize`, `LoadContent`, `Update`, `Draw`, and `OnExiting` as virtual reuse-slot methods | Original method flags and signatures; `Game1` derives from `Microsoft.Xna.Framework.Game` | IL-derived | High for metadata; exact external base slot resolution awaits the ABI gate. |
| The main load constructs `Viking_x86.main.MainMenu`, `IMAK3Z0MB1EGAEM.ZombieGame`, and `Viking_x86.VikingGame` | Calls in `Game1.ThreadedMainLoad`; subsequent `Game1.Update` and `Draw` calls target each type | IL-derived | High that all three paths exist; user-facing mode names and reachability still need runtime checks. |
| Zombie game has normal and endless loading paths; Viking game has its own load path | `ZombieGame.ThreadedLoadNormal`, `ThreadedLoadEndless`, and `VikingGame.ThreadedLoad` method definitions and asset literals | IL-derived | Medium for mode identity; exact selection, rules, and availability remain unknown. |
| Startup reads storage and checks trial state; exit writes storage | `Game1.ThreadedMainLoad` calls `StorageMgr.Read` and `TrialMgr.CheckTrial`; `Game1.OnExiting` calls `StorageMgr.Write` | IL-derived | High for call sites; save format, failures, and service policy remain unknown. |
| Achievement award calls and gamer service references exist | 22 call sites to `AchievementMgr.AwardAchievement`; 14 GamerServices TypeRefs across XNA assembly scopes | IL-derived | High for references; actual award conditions and online behavior need deeper inspection. |
| Asset loading uses XNA content paths, including `gfx/`, `gfx_2/`, `sfx/music/`, and companion data paths | `ContentManager.Load` call sites and nearby `ldstr` literals in original IL; package entry list | IL-derived | Medium. The JSON records **candidates**, including diagnostic strings; dynamic concatenation can hide further paths. |
| The package includes 91 XNB files, three WMA music files, and companion folders; sampled XNB headers are `XNBm` version 5 | Original extracted package entries and first bytes of `Content/Arial.xnb`, `Content/gfx/1x1.xnb`, and `Content/gfx/glow.xnb` | Observed | High for inventory and sampled headers; FNA compatibility is untested. |
| A desktop `InputMgr` and a 600×360 presentation exist in the old port | Old port comparison described in `architecture.md` | Old-port-only, untrusted | No confidence as original-game behavior. Neither is an acceptance target. |

## Unknowns and next evidence

- **Original viewport and coordinate transform:** inspect `Game1` graphics/backbuffer setup and touch transforms, then compare with an original-runtime observation if available. No size is established here.
- **Content format and loading:** At this Prompt 1 snapshot, `XNBm` v5 headers were observed but direct FNA behavior was unknown. The later [content coverage report](../runtime/content-coverage.md) probes all 91 XNBs and documents the three WMA playback failures and local preparation. Dynamic runtime reachability still needs the game-loop gate.
- **Save format:** `StorageMgr.Read/Write` call sites are known; serialization layout, storage filename, versioning, and recovery behavior need IL inspection and runtime tests.
- **Services:** trial, sign-in, achievements, leaderboards, sensors, marketplace, and media APIs are referenced, but their order, outcomes, and offline behavior need focused inspection. No desktop policy follows from the old port.
- **Mode reachability:** constructor and update/draw calls establish implementations, not which menus expose them or how a session ends.

## Reproduce the inventory

From the repository root with the pinned .NET 10 SDK and the user-supplied supported XAP in the ignored `game/` directory:

```powershell
dotnet run --project tools/inspect_assembly -- --json --xap 'game/Z0MB1ES (on teh ph0ne) v1.0.0.0.xap' --output generated/evidence-from-xap.json
```

The tool checks the exact package and DLL hashes before writing output. It records metadata rows in token order and IL calls in method/offset order, and excludes timestamps and absolute paths. `--dll private/original-xap/ZombiesWP7.dll` can inventory the already extracted DLL, with the report clearly marking that the XAP itself was not checked in that run. Reports remain under ignored `generated/` because they contain original-game metadata and string literals.
