# Repository agent instructions

- Run `. .\dev.ps1; build-game` from the repository root when changes affect executable code, game behavior, dependencies, or build/packaging logic. A successful run is required for those changes, in addition to relevant focused checks.
- Skip `build-game` for documentation-only edits, documentation moves, comments, agent instructions, and reference/help-text/test-fixture path updates that do not affect runtime or build behavior. Use only checks relevant to the change, such as verifying links or running focused script checks.
- If a required `build-game` run fails, diagnose and fix the failure, then run it again. Do not report the affected change as complete while this check is failing; state any external blocker clearly if it cannot be resolved in the workspace.
- Settings menu rows deliberately use different horizontal offsets. Preserve each page's staggered row positions when adding or moving Settings options.
