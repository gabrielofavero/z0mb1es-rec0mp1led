param(
    [string]$Build,
    [switch]$List
)

$ErrorActionPreference = 'Stop'
$runRoot = Join-Path $PSScriptRoot 'private/run'

if (-not (Test-Path -LiteralPath $runRoot -PathType Container)) {
    throw "No staged builds found in $runRoot. Run scripts/stage-local-run.ps1 first."
}

$requiredFiles = @('Port.Launcher.dll', 'Port.Launcher.runtimeconfig.json', 'ZombiesWP7.dll',
    'patch-manifest.json', 'content-manifest.json')
$builds = @(Get-ChildItem -LiteralPath $runRoot -Directory | Where-Object {
    $directory = $_.FullName
    @($requiredFiles | Where-Object {
        Test-Path -LiteralPath (Join-Path $directory $_) -PathType Leaf
    }).Count -eq $requiredFiles.Count
} | Sort-Object LastWriteTime -Descending)

if ($builds.Count -eq 0) {
    throw "No runnable builds found in $runRoot. Run scripts/stage-local-run.ps1 first."
}

if ($List -or -not $Build) {
    Write-Host 'Available builds:'
    for ($i = 0; $i -lt $builds.Count; $i++) {
        Write-Host ("  {0,2}. {1}" -f ($i + 1), $builds[$i].Name)
    }
    if ($List) { return }
}

if ($Build) {
    $selected = $builds | Where-Object { $_.Name -eq $Build } | Select-Object -First 1
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

$launcher = Join-Path $selected.FullName 'Port.Launcher.dll'
$game = Join-Path $selected.FullName 'ZombiesWP7.dll'
Write-Host "Running $($selected.Name)..."
& dotnet $launcher --run $game --content $selected.FullName
if ($LASTEXITCODE -ne 0) { throw "Build $($selected.Name) exited with code $LASTEXITCODE." }
