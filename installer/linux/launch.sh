#!/bin/sh
set -eu
app=/opt/z0mb1es-rec0mp1led
export LD_LIBRARY_PATH="$app${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
exec "$app/Z0MB1ES" --linux
