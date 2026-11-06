# Prompt 6 — Implement the minimum honest compatibility bridge

**Status:** Done. See the implementation log for validation limits.


```text
Read docs/foundation/architecture.md and docs/plans/done/2026-11-06-initial-implementation/implementation-prompts.md. Implement only the exact GamerServices, Windows Phone, sensor, and runtime compatibility signatures required by the pinned original DLL. Match call shapes and object lifetimes; route actual desktop policies through small authored services. For paths not yet implemented, throw a specific diagnostic instead of returning a misleading success/default. Use the old port only to discover candidate policies, then verify those policies against original IL and any original-runtime observation. Add tests for the signatures and for service initialization. Make Port.Prepare retarget references to these bridge types.

Acceptance: the prepared DLL can be loaded and all game types enumerated by the launcher without FileNotFoundException, TypeLoadException, MissingMethodException, or InvalidProgramException; the bridge's unsupported paths are explicit and logged. Update docs/records/implementation-log.md.
```

