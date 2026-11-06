# Prompt 11 — Finish media and phone-only API behavior

**Status:** Done. See the implementation log for validation limits.


```text
Read docs/foundation/architecture.md and docs/plans/done/2026-11-06-initial-implementation/implementation-prompts.md. Implement music, SFX, media-library, vibration, accelerometer, and marketplace-task boundaries exercised by the original XAP. Preserve the original timing/control flow where practical, using local converted audio and explicit desktop fallbacks for absent phone hardware/services. Inspect the original calls before choosing fallbacks; do not silently swallow errors or pretend a marketplace request succeeded. Add probes for song start/stop/pause/resume, SFX overlap, device absence, and quitting during playback.

Acceptance: all music and SFX needed by the menu and game modes play without exceptions; pause/resume and mode changes behave consistently; optional phone hardware absence is visible but does not block play. Update docs/records/implementation-log.md.
```

