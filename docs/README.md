# Documentation

Start with the [architecture](foundation/architecture.md) and the [plan index](plans/done/2026-11-06-initial-implementation/implementation-prompts.md). The [installer design](../installer/README.md) describes player-facing setup.

## Foundation

- [Architecture](foundation/architecture.md): project boundaries and folder ownership.
- [Dependencies](foundation/dependencies.md): pinned FNA setup and offline build.
- [Local preparation](foundation/preparation.md): supported XAP verification and extraction.
- [.NET 10 migration](foundation/net10-migration.md): SDK pin and validation.

## Compatibility

- [ABI compatibility](compatibility/abi-compatibility.md): original DLL references and validator results.
- [Guarded IL rewriter](compatibility/il-rewriter.md): rewrite contract and checks.
- [Compatibility bridge](compatibility/bridge-compatibility.md): desktop API signatures and load checks.

## Runtime

- [Content coverage](runtime/content-coverage.md) and [audio portability](runtime/audio-portability.md): assets and conversion.
- [Menu bring-up](runtime/menu-bringup.md) and [desktop input](runtime/desktop-input.md): visible game loop and controls.
- [Offline services](runtime/offline-services.md) and [media/phone behavior](runtime/media-phone-behavior.md): saves, service screens, audio, and desktop fallbacks.

## Records

- [Behavior evidence](records/behavior-evidence.md): original observations and inferred behavior.
- [Feature matrix](records/feature-matrix.md): modes, menus, progression, saves, and achievements.
- [Implementation log](records/implementation-log.md): dated commands, results, and remaining checks.

## Plans

The [plan index](plans/done/2026-11-06-initial-implementation/implementation-prompts.md) lists the initial completed prompts and links to the [in-progress plan](plans/in-progress/). Deferred checks are recorded in the implementation log.
