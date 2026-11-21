# Run with PowerShell 7. No builds or device commands are executed.
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
foreach ($file in @(Get-ChildItem "$root/scripts/dev/*.ps1") + @(Get-Item "$root/dev.ps1")) {
    $tokens = $null
    $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors)
    if ($errors.Count) { throw ($errors | Out-String) }
}

function Assert-Arguments([string[]]$Expected) {
    if (($script:AdbArguments | ConvertTo-Json -Compress) -cne ($Expected | ConvertTo-Json -Compress)) {
        throw "Unexpected adb arguments: $script:AdbArguments"
    }
}
function Mock-Adb {
    $script:AdbArguments = @($args)
    $global:LASTEXITCODE = $script:AdbExitCode
}

Push-Location $PSScriptRoot
try {
    . "$root/scripts/dev/dev-windows.ps1"
    . "$root/scripts/dev/dev-android.ps1"
    if ($script:DevRoot -ne $root -or $script:AndroidDevRoot -ne $root) { throw 'Wrong repository root.' }
    foreach ($name in @('build-game', 'run-game', 'build-installer', 'run-installer', 'build-apk')) {
        if (-not (Get-Command $name -CommandType Function)) { throw "Missing command: $name" }
    }
    $script:AdbExitCode = 0
    # This existing file stands in for an APK, avoiding any generated fixture.
    install-apk -ApkPath "$root/docs/build-and-run.md" -Serial 'device serial' -AdbPath Mock-Adb
    Assert-Arguments @('-s', 'device serial', 'install', '-r', (Resolve-Path "$root/docs/build-and-run.md").Path)
    $pin = Get-Content -Raw "$root/config/android-packaging.json" | ConvertFrom-Json
    run-android -AdbPath Mock-Adb
    Assert-Arguments @('shell', 'monkey', '-p', $pin.applicationId, '-c', 'android.intent.category.LAUNCHER', '1')
    log-android -Serial 'device serial' -AdbPath Mock-Adb
    Assert-Arguments @('-s', 'device serial', 'logcat', '-s', 'Z0MB1ES', 'SDL', 'AndroidRuntime')
    $script:AdbExitCode = 23
    $failed = $false
    try { run-android -AdbPath Mock-Adb } catch { $failed = $_.Exception.Message -match 'failed \(23\)' }
    if (-not $failed) { throw 'adb failure was swallowed.' }
    if ($IsWindows) {
        . "$root/dev.ps1"
        if (-not (Get-Command build-game,build-apk)) { throw 'Compatibility entry point failed.' }
    }
} finally { Pop-Location }
$global:LASTEXITCODE = 0
Write-Host 'Developer PowerShell checks passed.'
