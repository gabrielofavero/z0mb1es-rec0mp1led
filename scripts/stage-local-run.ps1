param(
    [Parameter(Mandatory = $true)][string]$PreparedDirectory,
    [Parameter(Mandatory = $true)][string]$ContentDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'private/run'))
$prepared = (Resolve-Path -LiteralPath $PreparedDirectory).Path
$content = (Resolve-Path -LiteralPath $ContentDirectory).Path
$runtime = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'src/Port.Launcher/bin/Release/net10.0'))
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
if (-not $output.StartsWith($runRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must be a new directory under private/run/.'
}
if (Test-Path -LiteralPath $output) { throw "OutputDirectory already exists: $output" }
foreach ($required in @(
    (Join-Path $prepared 'ZombiesWP7.dll'),
    (Join-Path $prepared 'patch-manifest.json'),
    (Join-Path $content 'content-manifest.json'),
    (Join-Path $content 'Content/Arial.xnb'),
    (Join-Path $runtime 'Z0MB1ES.dll'),
    (Join-Path $runtime 'D3D12/D3D12Core.dll')
)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Missing stage input: $required" }
}

$parent = [System.IO.Path]::GetDirectoryName($output)
[System.IO.Directory]::CreateDirectory($parent) | Out-Null
$stage = Join-Path $parent ('.' + [System.IO.Path]::GetFileName($output) + '.stage-' + [guid]::NewGuid().ToString('N'))
try {
    [System.IO.Directory]::CreateDirectory($stage) | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $runtime -File) {
        Copy-Item -LiteralPath $file.FullName -Destination $stage
    }
    Copy-Item -LiteralPath (Join-Path $runtime 'D3D12') -Destination $stage -Recurse
    Copy-Item -LiteralPath (Join-Path $prepared 'ZombiesWP7.dll') -Destination $stage
    Copy-Item -LiteralPath (Join-Path $prepared 'patch-manifest.json') -Destination $stage
    foreach ($item in Get-ChildItem -LiteralPath $content) {
        Copy-Item -LiteralPath $item.FullName -Destination $stage -Recurse
    }
    foreach ($name in @('ZombiesWP7.dll', 'patch-manifest.json')) {
        $sourceHash = (Get-FileHash -LiteralPath (Join-Path $prepared $name) -Algorithm SHA256).Hash
        $stageHash = (Get-FileHash -LiteralPath (Join-Path $stage $name) -Algorithm SHA256).Hash
        if ($sourceHash -ne $stageHash) { throw "Stage verification failed: $name" }
    }
    $contentSourceHash = (Get-FileHash -LiteralPath (Join-Path $content 'content-manifest.json') -Algorithm SHA256).Hash
    $contentStageHash = (Get-FileHash -LiteralPath (Join-Path $stage 'content-manifest.json') -Algorithm SHA256).Hash
    if ($contentSourceHash -ne $contentStageHash) { throw 'Stage verification failed: content-manifest.json' }
    $d3d12SourceHash = (Get-FileHash -LiteralPath (Join-Path $runtime 'D3D12/D3D12Core.dll') -Algorithm SHA256).Hash
    $d3d12StageHash = (Get-FileHash -LiteralPath (Join-Path $stage 'D3D12/D3D12Core.dll') -Algorithm SHA256).Hash
    if ($d3d12SourceHash -ne $d3d12StageHash) { throw 'Stage verification failed: D3D12/D3D12Core.dll' }
    [System.IO.Directory]::Move($stage, $output)
    Write-Output "RUN_STAGE $output"
} finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}
