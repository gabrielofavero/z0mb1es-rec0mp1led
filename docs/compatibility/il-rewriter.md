# Guarded local IL rewrite (Prompt 5)

`Port.Prepare` uses pinned Mono.Cecil 0.11.6 to retarget the verified original DLL's type scopes. The pinned ABI report supplies the exact 146 type and 382 member reference anchors. Uses of those type references in method signatures, fields, inheritance, generic instantiations, custom attributes, exception handlers, and IL operands follow the rewritten scopes. The writer makes no deliberate gameplay or method instruction changes.

From the repository root, after generating `generated/abi-report-bridge-net10.json` as described in [the ABI report](abi-compatibility.md):

```powershell
./scripts/build-offline.ps1
dotnet run --no-build -c Release --project tools/prepare-cli -- rewrite --xap 'game/Z0MB1ES (on teh ph0ne) v1.0.0.0.xap' --dll private/preparation-net10/original-xap/ZombiesWP7.dll --abi-report generated/abi-report-bridge-net10.json --fna out/deps/managed/FNA.dll --bridge src/Port.Bridge/bin/Release/net10.0/Port.Bridge.dll --output private/prepared/my-run
```

The command verifies the supported XAP before rewriting. The output directory must be new and inside ignored `private/prepared/`. It contains `ZombiesWP7.dll` and `patch-manifest.json`. The original DLL remains under `private/preparation-net10/original-xap/`, unchanged. The manifest records source and output hashes, each retargeted type token, and every method with affected IL operands. Method records include the source method signature, original body SHA-256, changed instruction offsets and new referenced targets, and a postcondition. The tool verifies source DLL and ABI report hashes, the FNA hash, type/member token anchors, decodable output method bodies, and absence of old phone/XNA and Silverlight 2.0.5.0 assembly references before committing the staged directory. Cecil can drop unreferenced source metadata rows; the output may therefore have fewer TypeRef and MemberRef rows.

The rewrite itself is a **static metadata result**. The bridge now supplies the 19 missing types and 45 members, and Prompt 8 separately adds a guarded media-source hook. The prepared assembly has been loaded and run to the menu on .NET 10; the application-scoped isolated-storage call still needs a final save policy in Prompt 10. No gameplay hook is included in this base rewrite command.
