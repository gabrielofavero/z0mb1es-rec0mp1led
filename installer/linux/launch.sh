#!/bin/sh
set -eu
app=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
export LD_LIBRARY_PATH="$app${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
exec "$app/Z0MB1ES" --linux
