# Prompt 3 — Implement deterministic local preparation

**Status:** Done. See the implementation log for validation limits.


```text
Read docs/foundation/architecture.md, docs/plans/done/2026-11-06-initial-implementation/implementation-prompts.md, and installer/README.md. Implement one reusable .NET preparation engine behind `prepare --xap <path> --output <private-dir>` and future installer-helper commands. The current Python extractor remains a developer tool; players must not need Python. Verify package and DLL hashes before touching outputs, reject unsupported versions, extract safely, preserve the original DLL unchanged, and produce a provenance manifest containing input hashes, tool versions, and output hashes. Use temporary paths plus an atomic final move so failed attempts cannot look complete. Keep generated files under ignored private/ by default and document a safe explicit alternative output path. Do not yet claim the prepared game is playable.

Acceptance: two preparations of the same XAP produce identical manifests and files; a corrupted/unknown XAP fails with a useful error; failed preparation leaves no valid-looking output; no original material is tracked. Update docs/records/implementation-log.md.
```

