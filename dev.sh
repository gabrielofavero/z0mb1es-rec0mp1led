#!/usr/bin/env bash
# Source in Bash/zsh to load commands, or execute with an action.
if [ -n "${ZSH_VERSION:-}" ]; then
    eval '_zombies_source=${(%):-%x}'
else
    _zombies_source=${BASH_SOURCE[0]}
fi
ZOMBIES_DEV_ROOT=$(CDPATH= cd -- "$(dirname -- "$_zombies_source")" && pwd)
_zombies_platform=${ZOMBIES_DEV_PLATFORM:-}
if [ "${1:-}" = --platform ]; then
    if [ "$#" -lt 2 ]; then
        printf '%s\n' '--platform requires mac, linux, or ios.' >&2
        return 2 2>/dev/null || exit 2
    fi
    _zombies_platform=$2
    shift 2
fi
if [ -z "$_zombies_platform" ]; then
    case "$(uname -s)" in
        Darwin) _zombies_platform=mac ;;
        Linux) _zombies_platform=linux ;;
        *) printf '%s\n' 'Use . ./dev.ps1 on Windows.' >&2; return 2 2>/dev/null || exit 2 ;;
    esac
fi
case "$_zombies_platform" in
    mac|linux|ios) . "$ZOMBIES_DEV_ROOT/scripts/dev/dev-$_zombies_platform.sh" ;;
    *) printf 'Unknown platform: %s\n' "$_zombies_platform" >&2; return 2 2>/dev/null || exit 2 ;;
esac
if [ -n "${ZSH_VERSION:-}" ]; then
    case "$ZSH_EVAL_CONTEXT" in *:file) _zombies_sourced=yes ;; *) _zombies_sourced=no ;; esac
elif [ "${BASH_SOURCE[0]}" != "$0" ]; then
    _zombies_sourced=yes
else
    _zombies_sourced=no
fi
if [ "$_zombies_sourced" = no ]; then
    zombies-dev "$@"
fi
