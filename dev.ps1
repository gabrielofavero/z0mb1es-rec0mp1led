# Dot-source from PowerShell: . .\dev.ps1
$script:DevRoot = $PSScriptRoot

function Invoke-DevDotnet {
    param([Parameter(Mandatory)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE." }
}

function Get-DevBuilds {
    $runRoot = Join-Path $script:DevRoot 'private/run'
    if (-not (Test-Path -LiteralPath $runRoot -PathType Container)) { return }
    $required = @('ZombiesWP7.dll', 'patch-manifest.json', 'content-manifest.json')
    Get-ChildItem -LiteralPath $runRoot -Directory | Where-Object {
        $directory = $_.FullName
        @($required | Where-Object {
            Test-Path -LiteralPath (Join-Path $directory $_) -PathType Leaf
        }).Count -eq $required.Count -and (
            ((Test-Path -LiteralPath (Join-Path $directory 'Z0MB1ES.dll') -PathType Leaf) -and
             (Test-Path -LiteralPath (Join-Path $directory 'Z0MB1ES.runtimeconfig.json') -PathType Leaf)) -or
            ((Test-Path -LiteralPath (Join-Path $directory 'Port.Launcher.dll') -PathType Leaf) -and
             (Test-Path -LiteralPath (Join-Path $directory 'Port.Launcher.runtimeconfig.json') -PathType Leaf))
        )
    } | Sort-Object LastWriteTimeUtc -Descending
}

function Invoke-DevGame {
    param([Parameter(Mandatory)][System.IO.DirectoryInfo]$Build)
    $launcher = Join-Path $Build.FullName 'Z0MB1ES.dll'
    if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) {
        $launcher = Join-Path $Build.FullName 'Port.Launcher.dll'
    }
    $game = Join-Path $Build.FullName 'ZombiesWP7.dll'
    Write-Host "Running $($Build.Name)..."
    Invoke-DevDotnet -Arguments @($launcher, '--run', $game, '--content', $Build.FullName)
}

function build-game {
    [CmdletBinding()]
    param(
        [string]$Name = ('dev-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)),
        [string]$XapPath = (Join-Path $script:DevRoot 'game/Z0MB1ES (on teh ph0ne) v1.0.0.0.xap'),
        [string]$FfmpegPath = (Join-Path $script:DevRoot 'private/tooling/ffmpeg.exe')
    )
    $ErrorActionPreference = 'Stop'
    if ($Name -notmatch '^[A-Za-z0-9][A-Za-z0-9_-]*$') {
        throw 'Build name may contain only letters, numbers, hyphens, and underscores.'
    }
    $xap = (Resolve-Path -LiteralPath $XapPath).Path
    $ffmpeg = (Resolve-Path -LiteralPath $FfmpegPath).Path
    $expectedFfmpeg = (Get-Content -Raw -LiteralPath (Join-Path $script:DevRoot 'config/content-tools.json') | ConvertFrom-Json).ffmpeg.executableSha256
    if ((Get-FileHash -LiteralPath $ffmpeg -Algorithm SHA256).Hash -ine $expectedFfmpeg) {
        throw 'FFmpeg does not match config/content-tools.json.'
    }

    $work = Join-Path $script:DevRoot "private/build/$Name"
    $rewritten = Join-Path $script:DevRoot "private/prepared/$Name-base"
    $hooked = Join-Path $script:DevRoot "private/prepared/$Name"
    $run = Join-Path $script:DevRoot "private/run/$Name"
    foreach ($output in @($work, $rewritten, $hooked, $run)) {
        if (Test-Path -LiteralPath $output) { throw "Build output already exists: $output" }
    }

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
            -PreparedDirectory $hooked -ContentDirectory $content -OutputDirectory $run
        if (-not (Test-Path -LiteralPath (Join-Path $run 'Z0MB1ES.dll') -PathType Leaf)) {
            throw 'Game staging did not produce a runnable build.'
        }
        Write-Host "Built $Name. Use run-game-latest to play it."
    } finally {
        Pop-Location
    }
}

function run-game {
    [CmdletBinding()]
    param([string]$Build, [switch]$List)
    $ErrorActionPreference = 'Stop'
    $builds = @(Get-DevBuilds)
    if ($builds.Count -eq 0) { throw 'No staged builds found in private/run/. Use build-game first.' }

    if ($List -or -not $Build) {
        Write-Host 'Available builds (latest edited first):'
        for ($i = 0; $i -lt $builds.Count; $i++) {
            Write-Host ('  {0,2}. {1}  {2}' -f ($i + 1), $builds[$i].LastWriteTime.ToString('yyyy-MM-dd HH:mm'), $builds[$i].Name)
        }
        if ($List) { return }
    }
    if ($Build) {
        $selected = $builds | Where-Object Name -eq $Build | Select-Object -First 1
        if (-not $selected) { throw "Build not found: $Build" }
    } else {
        while ($true) {
            $choice = Read-Host 'Choose a build number (or Q to quit)'
            if ($choice -eq 'q') { return }
            $number = 0
            if ([int]::TryParse($choice, [ref]$number) -and $number -ge 1 -and $number -le $builds.Count) {
                $selected = $builds[$number - 1]
                break
            }
            Write-Warning "Enter a number from 1 to $($builds.Count), or Q."
        }
    }
    Invoke-DevGame -Build $selected
}

function run-game-latest {
    [CmdletBinding()]
    param()
    $ErrorActionPreference = 'Stop'
    $builds = @(Get-DevBuilds)
    if ($builds.Count -eq 0) { throw 'No staged builds found in private/run/. Use build-game first.' }
    Invoke-DevGame -Build $builds[0]
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
