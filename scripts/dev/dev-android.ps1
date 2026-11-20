# Dot-source on the Windows Android build host.
$script:AndroidDevRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))

function bootstrap-android {
    & (Join-Path $script:AndroidDevRoot 'installer/android/bootstrap.ps1') @args
    if ($LASTEXITCODE -ne 0) { throw "Android bootstrap failed ($LASTEXITCODE)." }
}

function build-apk {
    & (Join-Path $script:AndroidDevRoot 'installer/android/build.ps1') @args
    if ($LASTEXITCODE -ne 0) { throw "Android build failed ($LASTEXITCODE)." }
}

function Invoke-DevAdb {
    param([string]$AdbPath, [string]$Serial, [string[]]$Arguments)
    $device = @()
    if ($Serial) { $device = @('-s', $Serial) }
    & $AdbPath @device @Arguments
    if ($LASTEXITCODE -ne 0) { throw "adb $($Arguments[0]) failed ($LASTEXITCODE)." }
}

function install-apk {
    [CmdletBinding()]
    param(
        [string]$ApkPath = (Join-Path $script:AndroidDevRoot 'out/android/Z0MB1ES.apk'),
        [string]$Serial,
        [string]$AdbPath = "$env:LOCALAPPDATA/Android/Sdk/platform-tools/adb.exe"
    )
    $ErrorActionPreference = 'Stop'
    $apk = (Resolve-Path -LiteralPath $ApkPath).Path
    Invoke-DevAdb -AdbPath $AdbPath -Serial $Serial -Arguments @('install', '-r', $apk)
}

function run-android {
    [CmdletBinding()]
    param([string]$Serial, [string]$AdbPath = "$env:LOCALAPPDATA/Android/Sdk/platform-tools/adb.exe")
    $ErrorActionPreference = 'Stop'
    $pin = Get-Content -Raw (Join-Path $script:AndroidDevRoot 'config/android-packaging.json') | ConvertFrom-Json
    Invoke-DevAdb -AdbPath $AdbPath -Serial $Serial -Arguments @(
        'shell', 'monkey', '-p', $pin.applicationId, '-c', 'android.intent.category.LAUNCHER', '1')
}

function log-android {
    [CmdletBinding()]
    param([string]$Serial, [string]$AdbPath = "$env:LOCALAPPDATA/Android/Sdk/platform-tools/adb.exe")
    Invoke-DevAdb -AdbPath $AdbPath -Serial $Serial -Arguments @('logcat', '-s', 'Z0MB1ES', 'SDL', 'AndroidRuntime')
}
