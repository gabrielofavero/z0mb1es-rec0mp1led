# Static ABI compatibility report (Prompt 4)

The `abi_check` command reads the verified Prompt 1 JSON inventory and original DLL, checks every inventory token against the DLL, then compares target metadata in the pinned FNA 26.11 assembly and the current .NET 10 runtime. It never loads or rewrites the original DLL. The current generated reports live in ignored `generated/abi-report-net10.json` and `generated/abi-report-bridge-net10.json`.

```powershell
./scripts/build-offline.ps1
dotnet run --no-build -c Release --project tools/abi_check -- --self-test tests/Abi.Fixtures/bin/Release/net10.0/Abi.Fixtures.dll
dotnet run --no-build -c Release --project tools/abi_check -- --inventory generated/evidence-from-xap.json --dll private/preparation-net10/original-xap/ZombiesWP7.dll --fna out/deps/managed/FNA.dll --output generated/abi-report-net10.json
```

The last command exits **1** while any reference or required runtime behavior rewrite remains unresolved. It still writes the complete report so each missing contract can be implemented and checked. Exit **0** is reserved for zero unresolved rows and required behavior rewrites. Wrong original DLL, changed inventory, and wrong FNA input fail before a report is written.

## Current result for the pinned XAP

| Check | Result |
| --- | ---: |
| Assembly references | 9, all accounted for with a target rewrite plan |
| External type references | 146: 44 verified in FNA, 83 in .NET, 19 unresolved |
| Member references | 382: 337 verified by full metadata signature, 45 unresolved |
| Generic method instantiations | 6, with resolved targets and matching arity |
| Virtual reuse slots | 151: 145 in original classes, 6 verified against external base classes |
| External inheritance/interface relationships | 0 unresolved |
| Original fields/properties/events inventoried | 1,132 / 16 / 0 |
| Required type/member/behavior actions | 65 |

The 19 missing types are 14 GamerServices contracts, including `GamerServicesComponent` (whose original scope is the XNA Game assembly), and five phone/sensor contracts. Their 45 member references are 34 GamerServices and 11 phone/sensor rows. Each token, full signature, proposed target, and reason is in `requiredActions` and the `types`/`members` arrays. These contracts remain **unimplemented** in `Port.Bridge`; the validator correctly fails. No unavoidable metadata blocker beyond these authored shims has been identified, but that does not prove the shims or game will work.

The comparison key includes CLI class/value kind, array rank and shape, required/optional modifiers, byrefs, pointers, generic arguments and arity, required parameter count, and the method calling-convention byte. Fields and constructors are compared as members; a target name alone never marks a member verified. The analyzer also checks the original field, method, property, type-specification, method-specification, and local signatures for class/value-kind conflicts; checks inherited virtual slots and class/interface shape; and checks event metadata for referenced event accessors. The authored fixture covers same-name overloads, a generic field and constructor, a generic method instantiation, a readonly byref modifier, an event, a nested generic type, and generic inheritance. This is static metadata evidence; it is not a loader or gameplay test.

## Silverlight runtime binding and storage

The original `mscorlib` and `System` references have version `2.0.5.0` and public key token `7cec85d7bea7798e`. In the analyzer's .NET 10 process, requesting those full identities returned `mscorlib`/`System` version `4.0.0.0` with token `b77a5c561934e089`. That is a redirect to different identities, not proof that the original DLL will load or that all its signatures bind. The guarded rewriter retargets the old identities and dependent metadata explicitly.

The original DLL calls `IsolatedStorageFile.GetUserStoreForApplication` and `OpenFile`. Both exact signatures exist in .NET 10's `System.IO.IsolatedStorage`. The .NET 10 menu run showed that the application store can be created in the user's Windows AppData directory under normal permissions, although a restricted workspace run was denied there. The analyzer still flags this as a `runtime-behavior-rewrite` because the final save location, persistence across installs, migration, and recovery policy remain Prompt 10 work; signature resolution and a successful menu load do not clear that gate.

## Next boundary

Prompt 5 can use the report's exact tokens/signatures and assembly rewrite plan to build a guarded rewriter. Prompt 6 must supply and verify the 19 missing type contracts and 45 member contracts before the ABI validator can pass on a prepared assembly. Runtime behavior, content loading, save compatibility, and reflection-only names remain later gates.

## Prompt 6 follow-up

With `--bridge src/Port.Bridge/bin/Release/net10.0/Port.Bridge.dll`, the ABI checker verifies all 19 formerly missing types and 45 members by full signature against the authored bridge. The Prompt 10 reproducible report is `generated/abi-report-bridge-prompt10-final.json` (SHA-256 `2fa3772b4d0035757223a6612b2b370fe21b854a236cda2b32f18705dd63ffae`). Its unresolved type, member, slot, inheritance, and method-specification counts are all zero. Its overall status stays `fail` because this prepatch report sees the original application-scoped isolated-storage call. The guarded [desktop storage patch](../../patches/desktop-storage.md) removes that call path in the prepared DLL; two separate runtime launches verified save writing and loading. See [bridge compatibility](bridge-compatibility.md).
