# Loaded by dev.sh. Subshells preserve the caller's directory and shell options.
zombies-dev() (
    set -eu
    cd "$ZOMBIES_DEV_ROOT"
    action=${1:-help}
    if [ "$#" -gt 0 ]; then shift; fi
    case "$action" in
        help|--help|-h) _zombies_help ;;
        *) _zombies_action "$action" "$@" ;;
    esac
)
build-game() { zombies-dev build-game "$@"; }
run-game() { zombies-dev run-game "$@"; }
build-installer() { zombies-dev build-installer "$@"; }
run-installer() { zombies-dev run-installer "$@"; }
build-bundled() { zombies-dev build-bundled "$@"; }
dev-help() { zombies-dev help; }
