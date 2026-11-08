# Dot-source from PowerShell: . .\dev.ps1
$script:DevRoot = $PSScriptRoot

function Invoke-DevDotnet {
    param([Parameter(Mandatory)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE." }
}

function Move-DevDirectory([string]$source, [string]$destination) {
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            [System.IO.Directory]::Move($source, $destination)
            return
        } catch {
            if ($attempt -eq 5) { throw }
            Start-Sleep -Milliseconds (250 * $attempt)
        }
    }
}

function Get-DevBuild {
    $current = Join-Path $script:DevRoot 'private/run/current'
    if (-not (Test-Path -LiteralPath $current -PathType Container)) { return }
    $required = @('ZombiesWP7.dll', 'patch-manifest.json', 'content-manifest.json')
    if (@($required | Where-Object {
        -not (Test-Path -LiteralPath (Join-Path $current $_) -PathType Leaf)
    }).Count -gt 0) { return }
    if (((Test-Path -LiteralPath (Join-Path $current 'Z0MB1ES.dll') -PathType Leaf) -and
         (Test-Path -LiteralPath (Join-Path $current 'Z0MB1ES.runtimeconfig.json') -PathType Leaf)) -or
        ((Test-Path -LiteralPath (Join-Path $current 'Port.Launcher.dll') -PathType Leaf) -and
         (Test-Path -LiteralPath (Join-Path $current 'Port.Launcher.runtimeconfig.json') -PathType Leaf))) {
        Get-Item -LiteralPath $current
    }
}

function Invoke-DevGame {
    param([Parameter(Mandatory)][System.IO.DirectoryInfo]$Build)
    $launcher = Join-Path $Build.FullName 'Z0MB1ES.dll'
    if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) {
        $launcher = Join-Path $Build.FullName 'Port.Launcher.dll'
    }
    $game = Join-Path $Build.FullName 'ZombiesWP7.dll'
    Write-Host "Running $($Build.Name)..."
    $arguments = @($launcher, '--run', $game, '--content', $Build.FullName)
    $localArt = Join-Path $script:DevRoot 'private/asset-swap/prepared'
    if (Test-Path -LiteralPath $localArt -PathType Container) {
        $arguments += @('--art-store', $localArt)
    }
    Invoke-DevDotnet -Arguments $arguments
}

function build-game {
    [CmdletBinding()]
    param(
        [string]$XapPath = (Join-Path $script:DevRoot 'game/Z0MB1ES (on teh ph0ne) v1.0.0.0.xap'),
        [string]$FfmpegPath = (Join-Path $script:DevRoot 'private/tooling/ffmpeg.exe')
    )
    $ErrorActionPreference = 'Stop'
    $xap = (Resolve-Path -LiteralPath $XapPath).Path
    $ffmpeg = (Resolve-Path -LiteralPath $FfmpegPath).Path
    $expectedFfmpeg = (Get-Content -Raw -LiteralPath (Join-Path $script:DevRoot 'config/content-tools.json') | ConvertFrom-Json).ffmpeg.executableSha256
    if ((Get-FileHash -LiteralPath $ffmpeg -Algorithm SHA256).Hash -ine $expectedFfmpeg) {
        throw 'FFmpeg does not match config/content-tools.json.'
    }

    $buildId = [guid]::NewGuid().ToString('N').Substring(0, 12)
    $work = Join-Path $script:DevRoot "private/build/tmp-$buildId"
    $rewritten = Join-Path $script:DevRoot "private/prepared/base-$buildId"
    $hooked = Join-Path $script:DevRoot "private/prepared/hook-$buildId"
    $candidate = Join-Path $script:DevRoot "private/run/tmp-$buildId"
    $backup = Join-Path $script:DevRoot "private/run/previous-$buildId"
    $run = Join-Path $script:DevRoot 'private/run/current'

    Push-Location $script:DevRoot
    try {
        & (Join-Path $script:DevRoot 'scripts/build-offline.ps1')
        if ($LASTEXITCODE -ne 0) { throw "Offline build failed with exit code $LASTEXITCODE." }

        $preparation = Join-Path $work 'preparation'
        $content = Join-Path $work 'content'
        $inventory = Join-Path $work 'evidence.json'
        $report = Join-Path $work 'abi-report.json'
        $original = Join-Path $preparation 'original-xap/ZombiesWP7.dll'
        $fna = Join-Path $script:DevRoot 'out/deps/managed/FNA.dll'
        $bridge = Join-Path $script:DevRoot 'src/Port.Bridge/bin/Release/net10.0/Port.Bridge.dll'
        $hooks = Join-Path $script:DevRoot 'src/Port.Hooks/bin/Release/net10.0/Port.Hooks.dll'
        $prepareCli = Join-Path $script:DevRoot 'tools/prepare-cli/bin/Release/net10.0/prepare.dll'

        New-Item -ItemType Directory -Path $work | Out-Null
        Invoke-DevDotnet -Arguments @($prepareCli, '--xap', $xap, '--output', $preparation)
        Invoke-DevDotnet -Arguments @((Join-Path $script:DevRoot 'tools/content-prepare/bin/Release/net10.0/content-prepare.dll'),
            '--xap', $xap, '--preparation', $preparation, '--ffmpeg', $ffmpeg, '--output', $content)
        Invoke-DevDotnet -Arguments @((Join-Path $script:DevRoot 'tools/inspect_assembly/bin/Release/net10.0/inspect_assembly.dll'),
            '--json', '--xap', $xap, '--output', $inventory)

        # The ABI checker may return a guarded behavior review while writing a valid pinned report.
        & dotnet (Join-Path $script:DevRoot 'tools/abi_check/bin/Release/net10.0/abi_check.dll') `
            --inventory $inventory --dll $original --fna $fna --bridge $bridge --output $report
        if ($LASTEXITCODE -notin @(0, 1) -or -not (Test-Path -LiteralPath $report -PathType Leaf)) {
            throw "ABI report generation failed with exit code $LASTEXITCODE."
        }

        Invoke-DevDotnet -Arguments @($prepareCli, 'rewrite', '--xap', $xap, '--dll', $original,
            '--abi-report', $report, '--fna', $fna, '--bridge', $bridge, '--output', $rewritten)
        Invoke-DevDotnet -Arguments @($prepareCli, 'hook', '--xap', $xap, '--dll', $original,
            '--prepared', (Join-Path $rewritten 'ZombiesWP7.dll'), '--hooks', $hooks, '--output', $hooked)
        & (Join-Path $script:DevRoot 'scripts/stage-local-run.ps1') `
            -PreparedDirectory $hooked -ContentDirectory $content -OutputDirectory $candidate
        if (-not (Test-Path -LiteralPath (Join-Path $candidate 'Z0MB1ES.dll') -PathType Leaf)) {
            throw 'Game staging did not produce a runnable build.'
        }
        $localArt = Join-Path $script:DevRoot 'private/asset-swap/prepared'
        if (Test-Path -LiteralPath $localArt -PathType Container) {
            Copy-Item -LiteralPath $localArt -Destination (Join-Path $candidate 'optional-art') -Recurse
        }
        $priorMoved = $false
        try {
            if (Test-Path -LiteralPath $run) {
                Move-DevDirectory $run $backup
                $priorMoved = $true
            }
            Move-DevDirectory $candidate $run
        } catch {
            if ($priorMoved -and -not (Test-Path -LiteralPath $run) -and (Test-Path -LiteralPath $backup)) {
                Move-DevDirectory $backup $run
            }
            throw
        }
        if ($priorMoved) { Remove-Item -LiteralPath $backup -Recurse -Force }
        Write-Host 'Built current. Use run-game to play it.'
    } finally {
        Pop-Location
        foreach ($temporary in @($work, $rewritten, $hooked, $candidate)) {
            if (Test-Path -LiteralPath $temporary) {
                Remove-Item -LiteralPath $temporary -Recurse -Force
            }
        }
    }
}

function run-game {
    [CmdletBinding()]
    param([string]$Build, [switch]$List)
    $ErrorActionPreference = 'Stop'
    if ($Build -and $Build -ne 'current') { throw "Build not found: $Build" }
    $current = Get-DevBuild
    if (-not $current) { throw 'No current staged build found in private/run/current/. Use build-game first.' }
    if ($List) {
        Write-Host ('  {0}  {1}' -f $current.LastWriteTime.ToString('yyyy-MM-dd HH:mm'), $current.Name)
        return
    }
    Invoke-DevGame -Build $current
}

function build-installer {
    [CmdletBinding()]
    param(
        [string]$FfmpegPath = (Join-Path $script:DevRoot 'private/tooling/ffmpeg.exe'),
        [string]$RuntimePackSource = (Join-Path $script:DevRoot 'out/deps/cache'),
        [string]$IsccPath = 'ISCC.exe',
        [string]$PyInstallerRoot = 'private/tooling/pyinstaller'
    )
    $ErrorActionPreference = 'Stop'
    Push-Location $script:DevRoot
    try {
        & (Join-Path $script:DevRoot 'installer/build.ps1') -FfmpegPath $FfmpegPath `
            -RuntimePackSource $RuntimePackSource -IsccPath $IsccPath -PyInstallerRoot $PyInstallerRoot
        if ($LASTEXITCODE -ne 0) { throw "Installer build failed with exit code $LASTEXITCODE." }
    } finally {
        Pop-Location
    }
}

function run-installer {
    [CmdletBinding()]
    param([string]$InstallerPath = (Join-Path $script:DevRoot 'installer/out/Z0MB1ES-0.1.0.exe'))

    $ErrorActionPreference = 'Stop'
    if (-not (Test-Path -LiteralPath $InstallerPath -PathType Leaf)) {
        throw "Installer not found: $InstallerPath. Use build-installer first."
    }
    $installer = (Resolve-Path -LiteralPath $InstallerPath).Path
    Write-Host "Running installer: $installer"
    & $installer
    if ($LASTEXITCODE -ne 0) { throw "Installer exited with code $LASTEXITCODE." }
}
