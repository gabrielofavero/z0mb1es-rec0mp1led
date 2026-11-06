param([switch]$Offline)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$config = Get-Content -Raw -LiteralPath (Join-Path $root 'config/dependencies.json') | ConvertFrom-Json
$deps = Join-Path $root 'out/deps'
$cache = Join-Path $deps 'cache'
$sourceArchive = Join-Path $cache $config.fna.cacheFile
$nativeArchive = Join-Path $cache $config.native.cacheFile
$referenceArchive = Join-Path $cache 'netstandard.library.2.0.3.nupkg'
$ilWriterArchive = Join-Path $cache 'mono.cecil.0.11.6.nupkg'
$sourceRoot = Join-Path $deps ("fna-$($config.fna.version)")
$nativeRoot = Join-Path $deps 'native/win-x64'
New-Item -ItemType Directory -Force -Path $cache, $nativeRoot | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $root 'out/dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME | Out-Null

function Confirm-Archive([string]$path, [string]$url, [string]$expected) {
    if (-not (Test-Path -LiteralPath $path)) {
        if ($Offline) { throw "Missing cached archive for offline setup: $path" }
        $partial = "$path.download"
        try {
            Invoke-WebRequest -Uri $url -OutFile $partial
            $downloadHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $partial).Hash
            if ($downloadHash -ine $expected) { throw "Downloaded dependency hash mismatch for $url`: $downloadHash" }
            Move-Item -LiteralPath $partial -Destination $path
        } finally {
            if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial }
        }
    }
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
    if ($actual -ine $expected) { throw "Dependency hash mismatch for $path`: $actual" }
}

Confirm-Archive $sourceArchive $config.fna.archiveUrl $config.fna.archiveSha256
Confirm-Archive $nativeArchive $config.native.archiveUrl $config.native.archiveSha256
Confirm-Archive $referenceArchive $config.netstandardReference.archiveUrl $config.netstandardReference.archiveSha256
Confirm-Archive $ilWriterArchive $config.ilWriter.archiveUrl $config.ilWriter.archiveSha256

Expand-Archive -LiteralPath $sourceArchive -DestinationPath $sourceRoot -Force

$project = Join-Path $sourceRoot $config.fna.project
dotnet restore $project --source $cache -p:NuGetAudit=false -v:q
if ($LASTEXITCODE -ne 0) { throw "FNA restore failed ($LASTEXITCODE)" }
dotnet build $project --no-restore -c Release -v:q "-p:PathMap=$sourceRoot=/_/fna"
if ($LASTEXITCODE -ne 0) { throw "FNA build failed ($LASTEXITCODE)" }

$built = Join-Path $sourceRoot 'FNA/bin/Release/netstandard2.0'
$managed = Join-Path $deps 'managed'
New-Item -ItemType Directory -Force -Path $managed | Out-Null
Copy-Item -LiteralPath (Join-Path $built 'FNA.dll') -Destination $managed
Copy-Item -LiteralPath (Join-Path $built 'FNA.dll.config') -Destination $managed

Add-Type -AssemblyName System.IO.Compression
$zip = [IO.Compression.ZipFile]::OpenRead($nativeArchive)
try {
    foreach ($name in $config.native.files) {
        $entry = $zip.GetEntry($(if ($name.StartsWith('D3D12/')) { $name } else { "x64/$name" }))
        if ($null -eq $entry) { throw "Native package is missing $name" }
        $target = Join-Path $nativeRoot $name
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        $stream = [IO.File]::Open($target, [IO.FileMode]::Create, [IO.FileAccess]::Write)
        try {
            $inputStream = $entry.Open()
            try { $inputStream.CopyTo($stream) } finally { $inputStream.Dispose() }
        } finally { $stream.Dispose() }
    }
} finally { $zip.Dispose() }

# This optional AV1 decoder was present only in the previous third-party bundle.
# Leaving it beside the new official libraries would silently mix native versions.
$legacyAv1 = Join-Path $nativeRoot 'dav1dfile.dll'
if (Test-Path -LiteralPath $legacyAv1) { Remove-Item -LiteralPath $legacyAv1 }

Write-Output "Pinned FNA $($config.fna.version) and $($config.native.runtime) native libraries are ready in out/deps."
