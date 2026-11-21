# Compatibility entry point. PowerShell 7: . ./dev.ps1
if ($IsWindows) {
    . "$PSScriptRoot/scripts/dev/dev-windows.ps1"
    . "$PSScriptRoot/scripts/dev/dev-android.ps1"
} elseif ($IsMacOS) {
    . "$PSScriptRoot/scripts/dev/desktop.ps1"
} elseif ($IsLinux) {
    . "$PSScriptRoot/scripts/dev/dev-linux.ps1"
} else {
    throw 'See docs/build-and-run.md for supported platforms.'
}
