# Prompt 13 — Build the user-friendly XAP installer

**Status:** Finished for the agreed scope. The interactive tool-free-account check was deferred by the user.

The one-file setup, staged helper, rollback path, and silent install/reinstall/uninstall checks are implemented and recorded in [the implementation log](../../../records/implementation-log.md). A visible wizard run on a separate Windows account without developer tools remains unverified. The sandbox account used for automated checks has developer tools installed globally, even though its launch `PATH` excluded them.


```text
Read docs/foundation/architecture.md, docs/plans/done/2026-11-06-initial-implementation/implementation-prompts.md, and installer/README.md. Build one Windows setup executable in installer/ using Inno Setup for the wizard and a bundled .NET helper that reuses Port.Prepare and Port.Content. The setup contains only authored runtime/tooling, pinned permitted dependencies, notices, and metadata-only XAP fingerprints. Implement Welcome, per-user Destination, Game XAP picker with read-only validation before Next, Ready, staged progress, and Finish with Launch or Close. Show clear wrong-version and corrupted-XAP errors. No Python, .NET SDK, compiler, or network may be required on the player's machine. Stage and verify generated DLL/content before committing, preserve a previous working install on failure/cancel, keep saves outside the install directory, and write local manifest/report. On reinstall/update, reuse verified compatible generated data or ask for the XAP again; never remove the source XAP. Do not bundle the XAP or any generated game file.

Acceptance: install with a supported XAP reaches a launchable game; invalid XAP is rejected before installation; failure/cancel leaves no claimed-successful partial install and preserves an earlier working one; reinstall/update/uninstall preserve saves by default. Test the helper independently and run the wizard on a Windows account without developer tools. Update docs/records/implementation-log.md with commands and observed results.
```


