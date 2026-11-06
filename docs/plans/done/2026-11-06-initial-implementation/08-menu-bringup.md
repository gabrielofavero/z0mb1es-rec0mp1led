# Prompt 8 — Reach a stable menu through the original game loop

**Status:** Done. See the implementation log for validation limits.


```text
Read docs/foundation/architecture.md and docs/plans/done/2026-11-06-initial-implementation/implementation-prompts.md. Connect the prepared DLL, FNA host, bridge, and local content. Instantiate ZombiesWP7.Game1, call Run, and let the original Initialize/Update/Draw sequence execute. Patch only failures demonstrated by traces, each with the guarded patch contract. Add structured diagnostics for missing members/content and unhandled exceptions without dumping proprietary assets or IL. Determine the original render/backbuffer settings from original code and runtime evidence before changing them. The old port's 600×360 presentation is not an acceptance target.

Acceptance: from a clean private preparation, the game reaches a responsive menu, renders multiple frames, and exits cleanly on the target desktop. Document every hook and why it is needed; no broad replacement of Game1 is accepted. Update docs/records/implementation-log.md.
```

