# Prompt 9 — Add playable desktop presentation and input

**Status:** Done. See the implementation log for validation limits.


```text
Read docs/foundation/architecture.md and docs/plans/done/2026-11-06-initial-implementation/implementation-prompts.md. Map desktop mouse, keyboard, and controller input into the original game's touch/gamepad expectations through Port.Bridge and small hooks. Derive original coordinate transforms, touch/gesture ordering, pause/back behavior, and viewport from the original DLL and assets. Preserve original gameplay logic; input translation should synthesize inputs, not rewrite game decisions. Add resize/fullscreen/letterboxing only after measuring the original aspect ratio and coordinate system. Treat the old InputMgr and scaler as examples of prior attempts, not specifications.

Acceptance: menu navigation, starting a mode, movement, aiming, pause/resume, back, quit, and resize work with no coordinate drift or stuck input. Test mouse and at least one controller on hardware if available; record any untested device clearly. Update docs/records/implementation-log.md.
```

