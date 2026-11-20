# Dot-source on Windows: . ./scripts/dev/dev-windows.ps1
. "$PSScriptRoot/desktop.ps1"

function build-installer {
    [CmdletBinding()]
    param(
        [string]$AudioConverterPath = (Join-Path $script:DevRoot 'out/audio-converter/release/port-audio-converter.exe'),
        [string]$RuntimePackSource = (Join-Path $script:DevRoot 'out/deps/cache'),
        [string]$IsccPath = 'ISCC.exe',
        [string]$PyInstallerRoot = 'private/tooling/pyinstaller'
    )
    $ErrorActionPreference = 'Stop'
    Push-Location $script:DevRoot
    try {
        & (Join-Path $script:DevRoot 'installer/build.ps1') -AudioConverterPath $AudioConverterPath `
            -RuntimePackSource $RuntimePackSource -IsccPath $IsccPath -PyInstallerRoot $PyInstallerRoot
        if ($LASTEXITCODE -ne 0) { throw "Installer build failed with exit code $LASTEXITCODE." }
    } finally {
        Pop-Location
    }
}

function run-installer {
    [CmdletBinding()]
    param([string]$InstallerPath = (Join-Path $script:DevRoot 'installer/out/Z0MB1ES-1.1.0.exe'))

    $ErrorActionPreference = 'Stop'
    if (-not (Test-Path -LiteralPath $InstallerPath -PathType Leaf)) {
        throw "Installer not found: $InstallerPath. Use build-installer first."
    }
    $installer = (Resolve-Path -LiteralPath $InstallerPath).Path
    Write-Host "Running installer: $installer"
    & $installer
    if ($LASTEXITCODE -ne 0) { throw "Installer exited with code $LASTEXITCODE." }
}
