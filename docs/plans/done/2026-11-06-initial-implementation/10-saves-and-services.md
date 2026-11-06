# Prompt 10 — Preserve saves and implement offline services

**Status:** Done. See the implementation log for validation limits.


```text
Read docs/foundation/architecture.md and docs/plans/done/2026-11-06-initial-implementation/implementation-prompts.md. Implement desktop storage and offline profile/achievement/trial/leaderboard behavior at narrow bridge or hook points. First inspect the original serialization and service call order. Keep the game's serialized data format where feasible; store it under an application data directory with atomic writes and backup/recovery. Make any policy that differs from the phone service explicit (for example, no Xbox sign-in or online leaderboard). Avoid auto-awarding achievements or changing trial/gameplay flags just because the old port did. Add tests for fresh profiles, round trips, corrupt/old saves, and multiple restarts.

Acceptance: progress and settings persist across restart; achievements occur only at intended original call sites; service screens do not hang or crash offline; the exact differences from original phone services are documented. Update docs/records/implementation-log.md.
```

