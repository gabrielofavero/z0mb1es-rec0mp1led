# Select on the Mac build host: source ./dev.sh --platform ios
. "$ZOMBIES_DEV_ROOT/scripts/dev/common.sh"
_zombies_help() {
    printf '%s\n' 'iOS: build-game (content-free unsigned device IPA)' \
        '     build-bundled phone.xap zombies-360 time-viking-360 (personal IPA)' \
        'Sign, install, and launch with your sideloading tool; see build-and-run.md.'
}
_zombies_action() {
    case "$1" in
        build-game) sh installer/ios/build-game.sh ;;
        build-bundled) shift; sh installer/ios/build-bundled.sh "$@" ;;
        *) printf 'Unknown iOS action: %s\n' "$1" >&2; return 2 ;;
    esac
}
