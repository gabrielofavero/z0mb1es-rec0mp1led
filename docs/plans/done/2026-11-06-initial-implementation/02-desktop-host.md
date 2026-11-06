# Prompt 2 — Create a clean FNA desktop host

**Status:** Done. See the implementation log for validation limits.


```text
Read docs/foundation/architecture.md, docs/plans/done/2026-11-06-initial-implementation/implementation-prompts.md, and installer/README.md. Create the new .NET solution and src/Port.Launcher, Port.Bridge, Port.Hooks, Port.Prepare, and Port.Content projects with clear one-way references. Port.Prepare and Port.Content must expose reusable libraries so the developer CLI and installer helper can call the same code. Keep the Xenon-inspired boundaries already documented: config/ for pins, docs/ for evidence, game/ for ignored local input, patches/ for guarded changes, scripts/ for audits, installer/ for packaging, and out/ for ignored output. Pin a clean FNA version and its native libraries with license and acquisition instructions; do not depend on a modified FNA build or the old game's source project. Add a tiny authored FNA smoke game that opens a window, clears the frame, accepts close/escape, and logs native-load errors. Keep this smoke game separate from the user game. Record exact toolchain/dependency versions and an offline-friendly build command.

Acceptance: a fresh build of the authored host succeeds; the smoke game opens, renders, and exits on the target desktop; no original game code or content is compiled or packaged. Update docs/records/implementation-log.md with the observed result and any platform limits.
```

