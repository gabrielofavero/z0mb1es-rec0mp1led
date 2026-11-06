# Prompt 5 — Build the guarded IL rewriter

**Status:** Done. See the implementation log for validation limits.


```text
Read docs/foundation/architecture.md and docs/plans/done/2026-11-06-initial-implementation/implementation-prompts.md. Implement Port.Prepare using a pinned managed IL writer. Retarget original XNA core references to FNA and phone/service references to Port.Bridge according to the ABI report. Rewrite all applicable metadata and IL operands, not only AssemblyRef names. Preserve the original DLL, emit the result only to private/prepared/, and record each change in a patch manifest with source hash, target signature, expected body shape/hash, new target, and postcondition. Reject any unexpected anchor or unsupported XAP version. Test the rewriter first on tiny authored fixture assemblies, then on the original DLL. Do not add gameplay changes in this stage.

Acceptance: deterministic output for the pinned input; the rewritten assembly passes metadata/IL validation; no original phone/XNA references remain where the report says they must be replaced; a tampered method fails closed; a second run never edits the original DLL. Update docs/records/implementation-log.md.
```

