# Prompt 1 — Establish the evidence ledger and version boundary

**Status:** Done. See the implementation log for validation limits.


```text
Read docs/foundation/architecture.md and docs/plans/done/2026-11-06-initial-implementation/implementation-prompts.md. Work only in this new workspace. Build a baseline evidence ledger from the original XAP, not from the old port. Extend tools/inspect_assembly so it emits stable JSON with assembly identity, all AssemblyRef/TypeRef/MemberRef entries, inheritance and override relationships, method signatures, and candidate API calls needing adaptation. Identify known menu/mode entry points and asset references through metadata/IL inspection without compiling decompiled source. Add docs/records/behavior-evidence.md with each claim's evidence source and confidence; record the old port only as an untrusted comparison. Add a supported-version manifest for the exact XAP/DLL hashes already documented. Report unknowns, especially original viewport, content format, save format, and service behavior. Do not infer original behavior from the old project's changes.

Acceptance: the JSON is reproducible for this XAP; its counts and hashes match docs/foundation/architecture.md; unsupported input is rejected; the evidence ledger distinguishes observed, IL-derived, and old-port-only claims. Update docs/records/implementation-log.md with commands and results.
```

