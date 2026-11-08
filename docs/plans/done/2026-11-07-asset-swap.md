# Optional asset swap for the phone games

**Status:** Done.

## Player contract

- Setup always requires the supported phone XAP. It may additionally import one player-owned **I MAED** source, either the supported Steam PC installation or the supported Xbox 360 package, and independently the supported **Time Viking** Xbox 360 package. Each import is optional. A later install or repair can add, replace, or remove an optional source without changing saves. The two I MAED alternatives are mutually exclusive for a given install; replacing one requires a new import.
- The phone `ZombiesWP7.dll` runs I MAED, Time Viking, and ZOMBIES ENDLESS in every case. The original menu, gameplay, pause, controls, achievements, saves, and offline behavior remain the game's implementation. No 360 or Steam executable runs.
- Add an **ASSETS** page under **HELP AND OPTIONS → SETTINGS**. Offer an independent choice for `I MAED`, `TIME VIKING`, and `ZOMBIES ENDLESS`: `ORIGINAL` or the available imported art. Label imported choices `360`, `STEAM`, or `MIXED` according to their provenance. A successfully imported source becomes the default for its corresponding game; existing installations and games without an applicable import use `ORIGINAL`. Endless is separately selectable and defaults to imported art only when a verified Endless mapping exists. Choices persist across restarts. If an import is removed or fails verification, show the choice as unavailable, explain repair, and render Original until repaired without deleting the saved preference.
- **Endless** has no standalone asset set. Its imported-art choice resolves each artwork element from I MAED 360 first, then Steam I MAED, then Time Viking 360 if a matching element exists, then the phone original. Steam I MAED alone now supplies 113 audited Endless elements. Never substitute a whole sheet merely because its name matches.
- This plan covers **visual texture assets**. Keep phone music, sound effects, fonts, touch controls, scene data, achievement art, and other phone-only files unless a separate evidence-backed asset mapping explicitly includes them. No optional source is bundled in Git or setup.

## Evidence and implementation rules

The current [comparison](../../runtime/asset-comparison.md) finds 2× I MAED backgrounds/sprites in Steam and 360, but the phone backgrounds include a baked vignette. Time Viking's `grass` icon layout differs, `moon1`–`moon3` are crops of a 360 atlas, and `maps`/`maps2` are already 2048×2048 on the phone. These are concrete reasons to map artwork at the **individual image or sprite-frame level**, including elements packed side by side or scattered across different source textures. A matching name, dimensions, or broad visual similarity is insufficient evidence for a whole-atlas replacement. The phone image is the fallback for every unresolved element.

Run the prompts below in order. Record exact source fingerprints, commands, measured results, mapping decisions, and unresolved mismatches in `docs/records/implementation-log.md`; update the feature matrix. Read the [initial implementation rules](../done/2026-11-06-initial-implementation/implementation-prompts.md), [installer contract](../../../installer/README.md), and [asset comparison](../../runtime/asset-comparison.md). Keep original packages, Steam files, extracted textures, generated atlases, and prepared outputs in ignored local or installed storage. Commit only authored recipes and metadata. Reject unsupported or changed input before mutating a working installation.

### Prompt 0 — Complete the visual asset inventory and mapping

1. Inventory every phone texture load and every source rectangle used by I MAED, Time Viking, and Endless, including animation frames, side-by-side art, UI overlays that appear during play, and dynamically selected regions. Identify the actual phone texture and rectangle for each logical artwork element. Do not infer Endless ownership from filenames alone.
2. Decode the pinned 360 XNBs and identify Steam I MAED's exact supported file/version fingerprints and decoding path. Build contact sheets and per-element comparisons against the phone art. For each element, record its preferred source file and rectangle, intended scale/crop/alpha treatment, and fallback. Search other 360 sheets when an expected element is absent from the similarly named one. Resolve duplicate 360 art in favor of I MAED for Endless. Mark unrelated, missing, changed, and uncertain art explicitly.
3. Define a versioned, metadata-only mapping manifest for each source and game. Give each mapped element a stable logical ID and source hash/rectangle; include the phone fallback and expected output geometry. Review the phone vignette and any game-applied effects so imported backgrounds retain intentional visual behavior without baking an unverified transformation.

**Acceptance:** Every phone-drawn element has a documented mapping or explicit Original fallback. Representative atlases are checked frame by frame, including `grass`, moon stages, I MAED sprites, and Endless art. The Steam source is version-pinned before it can be offered by setup. No proprietary pixel data is tracked.

### Prompt 1 — Prepare optional art locally

1. Implement importers for the supported Steam I MAED directory and the pinned I MAED/Time Viking `LIVE` packages. Reuse the Prompt 0 STFS inventory and package hashes where applicable. Verify source identity, required files, decoded texture type/dimensions, and hashes; fail with a title/version-specific error for unsupported inputs. Extract only artwork needed by the mapping, never prepare or launch source executables.
2. Produce phone-compatible, versioned prepared art with a manifest of input and output hashes. Repack or route individual source regions when atlas layouts differ or pieces are scattered. Preserve alpha, edge sampling, sprite spacing, and the original phone coordinate system. For mixed Endless art, compose each logical element from I MAED 360, Time Viking 360, or Original in that priority. Keep missing elements Original; do not fill them with a merely similar sprite.
3. Stage each optional import separately and verify before commit. A failed import leaves the phone install and any prior optional art usable. Keep originals untouched and outside the distributable payload. Build a local probe that checks every mapped rectangle lies within its decoded source and prepared output and that fallback coverage is complete.

**Acceptance:** Each supported optional source prepares independently with reproducible manifests, and invalid or incomplete sources fail without altering the working install. Every mapped output is decodable, bounds-safe, and has a verified Original fallback.

### Prompt 2 — Make the phone renderer accept both art sizes

1. Trace each phone draw path that assumes fixed texture dimensions, source rectangles, origins, UVs, render targets, or effects. Change only the relevant prepared-content/loader or guarded phone hooks so physical source pixels can differ while logical positions, hit boxes, animation timing, and the 600×360 phone viewport stay stable. Handle 2× images, same-size replacements, crops, and mixed-resolution sheets. Keep UI/input coordinates independent of texture pixel dimensions.
2. Validate padding, filtering, mipmaps, alpha edges, and atlas bleed at native and scaled display resolutions. Correct Time Viking `grass` and moon mapping and inspect every other nontrivial sheet; do not apply a global 2× multiplier to all source rectangles. Preserve the phone background vignette where it is part of the original look, with an explicit tested treatment.
3. Add focused rendering probes for Original and each prepared source in all three modes, including animation frames, unusual atlas regions, Endless mixed fallback, and resizing/display changes. Compare representative captures to mapped source crops and document visible differences.

**Acceptance:** All three phone modes start, play, pause, and render without missing art, wrong sprite regions, clipped frames, layout movement, atlas bleeding, or input misalignment at supported display modes. Original remains visually and behaviorally usable.

### Prompt 3 — Installer and Settings integration

1. Extend the Inno wizard and setup helper with optional I MAED `Steam` **or** `360` selection and independent Time Viking `360` selection. Show recognized title/version, expected asset coverage, and clear per-source errors before commit. Support later add/replace/repair/removal and preserve saves. A phone-only install or update never requires optional sources.
2. Store verified optional art and mapping manifests separately from the phone originals. Verify them at launch and selection. Add the **ASSETS** Settings page with three rows, source labels, availability, persistence, default selection on first successful import, Back behavior, and an actionable repair message. Do not change the existing game carousel or dispatch path.
3. Extend setup payload allow-lists and audits to reject bundled original, extracted, or prepared Steam/360 material. Confirm the one-file installer works offline with only the XAP and whichever optional user-owned sources were selected.

**Acceptance:** Phone only, each single import, I MAED plus Time Viking, replacement, repair, removal, and corrupted-import cases behave as specified. Settings selection survives restart; all games still execute the phone DLL and preserve the phone save/achievement state.

### Prompt 4 — Full visual and release validation

1. Exercise every mode under Original and every available imported choice at native, common widescreen, and ultrawide output sizes. Walk through menus, gameplay, pause, effects, animation, death/end states, and Endless fallback. Use controlled capture comparisons and a focused hands-on pass for areas automated probes cannot judge.
2. Test mismatched/corrupted source fingerprints, incomplete atlases, bad crops, installer rollback, preference migration, unavailable choices, reinstall, and clean phone-only setup. Check that the original XAP path remains sufficient to play and that optional art does not alter gameplay outcomes or saves.
3. Rebuild and audit setup; record exact supported versions, commands, results, remaining art gaps, and the installer hash. Complete the existing release rights review before publication.

**Acceptance:** The installer can import the requested art sources, Settings switches visual sets for each eligible phone mode, mixed Endless art follows the documented priority and fallback, and the phone game remains playable without imports. No source game code or proprietary art is shipped.
