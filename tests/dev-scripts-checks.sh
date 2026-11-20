#!/usr/bin/env bash
# Run with Bash; mocks ensure no packaging, signing, or device actions happen.
set -eu
repo=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
for script in "$repo/dev.sh" "$repo"/scripts/dev/*.sh; do bash -n "$script"; done
for platform in mac linux ios; do
    bash "$repo/dev.sh" --platform "$platform" help >/dev/null
done
if bash "$repo/dev.sh" --platform invalid help >/dev/null 2>&1; then
    echo 'Invalid platform was accepted' >&2; exit 1
fi
if bash "$repo/dev.sh" --platform >/dev/null 2>&1; then
    echo 'Missing platform was accepted' >&2; exit 1
fi

actual=$(
    cd "$repo/tests"
    source "$repo/dev.sh" --platform mac
    pwsh() { printf '<%s>\n' "$@"; }
    build-game -XapPath 'phone with spaces.xap' -ArtStorePath 'art with spaces'
    [ "$PWD" = "$repo/tests" ] || exit 1
)
expected=$(printf '<%s>\n' -NoProfile -File "$repo/scripts/dev/invoke-desktop.ps1" \
    build-game -XapPath 'phone with spaces.xap' -ArtStorePath 'art with spaces')
[ "$actual" = "$expected" ] || { echo 'Mac argument forwarding failed' >&2; exit 1; }

actual=$(
    source "$repo/dev.sh" --platform ios
    sh() { printf '<%s>\n' "$@"; }
    build-bundled 'phone with spaces.xap' 'zombies 360' 'viking 360'
)
expected=$(printf '<%s>\n' installer/ios/build-bundled.sh 'phone with spaces.xap' 'zombies 360' 'viking 360')
[ "$actual" = "$expected" ] || { echo 'iOS argument forwarding failed' >&2; exit 1; }

# Execute the action in a fresh shell so its set -e is active, then check its exit.
set +e
bash -c 'source "$1/dev.sh" --platform linux; uname() { if [ "$1" = -s ]; then echo Linux; else echo x86_64; fi; }; python3() { return 19; }; dotnet() { echo "Unexpected publish" >&2; return 99; }; build-installer' _ "$repo"
result=$?
set -e
[ "$result" -eq 19 ] || { echo 'Linux prerequisite failure did not stop packaging' >&2; exit 1; }
echo 'Developer shell checks passed.'
