# Loaded by: source ./dev.sh --platform mac
. "$ZOMBIES_DEV_ROOT/scripts/dev/common.sh"
_zombies_help() {
    printf '%s\n' 'macOS: build-game [-XapPath path] [-AudioConverterPath path] [-ArtStorePath path]' \
        '       run-game [-List] [-Build current]' \
        '       build-installer [--output directory]' \
        '       run-installer [path-to-app-or-pkg]' 'See build-and-run.md for prerequisites.'
}
_zombies_action() {
    case "$1" in
        build-game|run-game)
            pwsh -NoProfile -File "$ZOMBIES_DEV_ROOT/scripts/dev/invoke-desktop.ps1" "$@" ;;
        build-installer) shift; sh installer/macos/build.sh "$@" ;;
        run-installer)
            shift
            open "${1:-out/macos/packages/Z0MB1ES-1.1.0-osx-arm64.pkg}" ;;
        *) printf 'Unknown macOS action: %s\n' "$1" >&2; return 2 ;;
    esac
}
