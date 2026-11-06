# Prompt 12 — Validate every mode against original evidence

**Status:** Finished for the agreed scope. Hands-on acceptance checks remain deferred by the user and are recorded in the feature matrix. This does not establish full gameplay parity.


```text
Read docs/foundation/architecture.md and docs/plans/done/2026-11-06-initial-implementation/implementation-prompts.md. Build a feature matrix from this original XAP: every menu path, original game mode (including Time Viking and endless/perks if confirmed present), difficulty/progression path, save point, and achievement trigger. Exercise each path on the new port with reproducible steps and logs. When behavior differs from the old port, inspect the original DLL and any original-runtime observation before deciding which behavior is correct. Fix port-caused differences with narrow guarded hooks; record unresolved parity questions instead of guessing. Use automated checks for deterministic logic where possible and manual play checks for visuals, audio, and input.

Acceptance: each mode can start, play for a meaningful session, pause/resume, end or quit, and return to the menu without crashes; progression and saves survive restart; the feature matrix has evidence and test status for every entry. Update docs/records/implementation-log.md.
```


