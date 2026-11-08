# Repository agent instructions

- Before delivering a code change, run `. .\dev.ps1; build-game` from the repository root. Treat a successful `build-game` run as a required acceptance check, in addition to any focused tests.
- If `build-game` fails, diagnose and fix the failure, then run it again. Do not report a code change as complete while this check is failing; state any external blocker clearly if it cannot be resolved in the workspace.
