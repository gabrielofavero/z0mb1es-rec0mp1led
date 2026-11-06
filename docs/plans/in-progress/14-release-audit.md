# Prompt 14 — Audit and rehearse the one-file release

**Status:** In progress. Automated audit and local silent-install rehearsal passed; the clean-account visual check, full hands-on game gates, and rights review remain open.


```text
Read docs/foundation/architecture.md, docs/plans/done/2026-11-06-initial-implementation/implementation-prompts.md, and installer/README.md. Build the setup from a fresh checkout using a strict payload allow-list. Audit tracked files, installer payload, completed setup executable, symbols, embedded resources, manifests, and logs for original game DLL/IL, decompiled source, XAP, assets, generated game output, developer-machine paths, and secrets. Add negative-control tests that inject forbidden material and prove the audit fails. Run clean-install acceptance offline with a user-supplied supported XAP, and separately test unsupported XAP, reinstall, update, cancel, uninstall, and save preservation. Document licenses, support logs, and any remaining original-behavior uncertainty. Do not publish or upload without explicit user authorization and a rights review.

Acceptance: the one setup executable installs and launches on a Windows machine with no developer tools; every working-game gate above still passes; the audit reports clean and its negative controls fail as intended; no original or prepared game material is in the release artifact. Update docs/records/implementation-log.md with the exact final commands, results, and package hash.
```

