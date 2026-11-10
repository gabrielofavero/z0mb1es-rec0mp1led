$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$pin = Get-Content -Raw -LiteralPath (Join-Path $root 'config/linux-packaging.json') | ConvertFrom-Json
$cache = Join-Path $root 'out/deps/cache'

function Assert-Hash([string]$path, [string]$expected) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $expected) {
        throw "Missing or mismatched pinned input: $path"
    }
}
function Invoke-Dotnet([string[]]$arguments) {
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($arguments[0]) failed ($LASTEXITCODE)" }
}

if ((dotnet --version).Trim() -ne $pin.dotnetSdk) { throw 'The pinned .NET SDK is required.' }
foreach ($property in $pin.runtimePacks.PSObject.Properties) {
    Assert-Hash (Join-Path $cache $property.Name) $property.Value
}
$nativeArchive = Join-Path $cache 'fnalibs-2026-11-06.zip'
Assert-Hash $nativeArchive $pin.nativeArchiveSha256
Assert-Hash (Join-Path $root 'private/tooling/ffmpeg-linux') $pin.ffmpegSha256
Assert-Hash (Join-Path $root 'installer/out/FFmpeg-8.1.1-source.zip') $pin.ffmpegBaseSourceBundleSha256
Assert-Hash (Join-Path $root 'installer/out/FFmpeg-8.1.1-linux-source.zip') $pin.ffmpegSourceBundleSha256
Assert-Hash (Join-Path $root 'private/tooling/nfpm_2.48.0_Linux_x86_64.tar.gz') $pin.nfpmArchiveSha256
if (-not (Test-Path -LiteralPath (Join-Path $root 'private/linux-art/dist/Port.AssetImporter'))) {
    throw 'Build the Linux art importer with installer/linux/build-art.sh first.'
}

$nativeTarget = Join-Path $root 'out/deps/native/linux-x64'
New-Item -ItemType Directory -Force -Path $nativeTarget | Out-Null
Add-Type -AssemblyName System.IO.Compression
$zip = [IO.Compression.ZipFile]::OpenRead($nativeArchive)
try {
    foreach ($property in $pin.nativeFiles.PSObject.Properties) {
        $entry = $zip.GetEntry("lib64/$($property.Name)")
        if ($null -eq $entry) { throw "Missing Linux native bundle entry $($property.Name)" }
        $target = Join-Path $nativeTarget $property.Name
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
        Assert-Hash $target $property.Value
    }
} finally { $zip.Dispose() }

Push-Location $root
try {
    $projects = @(
        @('src/Port.Launcher/Port.Launcher.csproj', 'game', $false),
        @('tools/inspect_assembly/inspect_assembly.csproj', 'inspect', $true),
        @('tools/abi_check/abi_check.csproj', 'abi', $false),
        @('tools/decode_360_xnb/decode_360_xnb.csproj', 'decode', $true)
    )
    foreach ($item in $projects) {
        Invoke-Dotnet -arguments @('restore', $item[0], '-r', 'linux-x64', '--source', $cache,
            '-p:NuGetAudit=false', '-v:q')
        Invoke-Dotnet -arguments @('publish', $item[0], '-c', 'Release', '-r', 'linux-x64',
            '--self-contained', 'true', '--no-restore',
            "-p:PublishSingleFile=$($item[2].ToString().ToLowerInvariant())",
            '-p:PublishTrimmed=false', '-o', "private/linux-publish/$($item[1])", '-v:q')
    }
    & wsl -- sh installer/linux/package.sh
    if ($LASTEXITCODE -ne 0) { throw "Linux package assembly failed ($LASTEXITCODE)" }
} finally { Pop-Location }
