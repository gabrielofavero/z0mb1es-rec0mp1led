# Prompt 7 — Make original content load locally

**Status:** Done. See the implementation log for validation limits.


```text
Read docs/foundation/architecture.md and docs/plans/done/2026-11-06-initial-implementation/implementation-prompts.md. Inventory every content path the original DLL can load and inspect the XNBm v5 data, font, three WMA files, and companion folders from this XAP. Test direct FNA loading first with a small authored probe. Where incompatible, implement a local conversion or a narrow content adapter in Port.Content/Port.Bridge. Keep converted files and manifests in private/; pin conversion dependencies and record licenses. Preserve asset names, dimensions, timing, and lookup semantics; do not impose the old port's PNG-scaling design unless needed and justified. Handle Arial.xnb explicitly.

Acceptance: a local content probe loads every required asset type and reports exact failures; generated outputs are deterministic; no game asset is tracked or packaged. Add an asset coverage report and update docs/records/implementation-log.md.
```

