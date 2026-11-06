# Prompt 4 — Resolve the complete ABI before gameplay patches

**Status:** Done. See the implementation log for validation limits.


```text
Read docs/foundation/architecture.md and docs/plans/done/2026-11-06-initial-implementation/implementation-prompts.md. Use the JSON inventory from Prompt 1 to classify every external type and member reference as FNA, Port.Bridge, .NET runtime, or unresolved. Compare full signatures, including generic arguments, fields, events, inheritance, overrides, and constructors, against the pinned FNA assembly. Investigate mscorlib/System 2.0.5.0 binding and isolated storage explicitly. Implement a machine-readable compatibility report and a validation command that fails if anything is unresolved. Add focused fixtures for tricky metadata shapes; do not use the old port as proof that a signature is correct.

Acceptance: the report accounts for every reference in the original DLL, lists each necessary rewrite/shim, and identifies any unavoidable blocker before IL rewriting begins. Passing means no entries are silently classified by name alone. Update docs/records/implementation-log.md.
```

