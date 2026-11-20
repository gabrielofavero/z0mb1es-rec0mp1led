# Loaded by: source ./dev.sh --platform linux
. "$ZOMBIES_DEV_ROOT/scripts/dev/common.sh"
_zombies_help() {
    printf '%s\n' 'Linux: build-game (publish self-contained host/tools)' \
        '       build-installer (publish and package .deb/.rpm)' \
        '       run-game (launch the installed importer)' \
        'See build-and-run.md for native/art/nFPM prerequisites and personal asset preparation.'
}
_zombies_publish_linux() {
    [ "$(uname -s)" = Linux ] && [ "$(uname -m)" = x86_64 ] || {
        printf '%s\n' 'Linux x64 build host required.' >&2; return 1;
    }
    python3 installer/linux/bootstrap-native.py
    for spec in 'src/Port.Launcher game' 'tools/inspect_assembly inspect' \
                'tools/abi_check abi' 'tools/decode_360_xnb decode'; do
        project=${spec% *}
        name=${spec#* }
        single_file=false
        if [ "$name" = inspect ] || [ "$name" = decode ]; then single_file=true; fi
        dotnet restore "$project" -r linux-x64 --source out/deps/cache -p:NuGetAudit=false
        dotnet publish "$project" -c Release -r linux-x64 --self-contained true --no-restore \
            -p:PublishTrimmed=false -p:PublishSingleFile="$single_file" \
            -o "private/linux-publish/$name"
    done
}
_zombies_action() {
    case "$1" in
        build-game) _zombies_publish_linux ;;
        build-installer) _zombies_publish_linux; sh installer/linux/package.sh ;;
        run-game) shift; /opt/z0mb1es-rec0mp1led/launch.sh "$@" ;;
        *) printf 'Unknown Linux action: %s\n' "$1" >&2; return 2 ;;
    esac
}
