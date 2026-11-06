param(
    [Parameter(Mandatory = $true)][string]$FfmpegPath,
    [string]$RuntimePackSource = 'https://api.nuget.org/v3/index.json',
    [string]$IsccPath = 'ISCC.exe'
)

$ErrorActionPreference = 'Stop'
$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$payload = Join-Path $PSScriptRoot 'out/payload'
$cache = Join-Path $repo 'out/deps/cache'
$packPins = Get-Content -Raw (Join-Path $PSScriptRoot 'config/runtime-packs.json') | ConvertFrom-Json
$expected = (Get-Content -Raw (Join-Path $repo 'config/content-tools.json') | ConvertFrom-Json).ffmpeg.executableSha256
$converter = (Resolve-Path -LiteralPath $FfmpegPath).Path
if ((Get-FileHash -LiteralPath $converter -Algorithm SHA256).Hash -ine $expected) {
    throw 'FFmpeg does not match the pinned converter.'
}

$expectedPayload = [System.IO.Path]::GetFullPath((Join-Path $repo 'installer/out/payload'))
if ([System.IO.Path]::GetFullPath($payload) -ne $expectedPayload) {
    throw 'Unexpected installer payload path.'
}
if (Test-Path -LiteralPath $payload) {
    if ((Get-Item -LiteralPath $payload -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw 'Installer payload is a reparse point.'
    }
    Remove-Item -LiteralPath $payload -Recurse -Force
}

& (Join-Path $repo 'scripts/build-offline.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Offline build failed.' }

foreach ($item in @(
    @{ Project='installer/src/Port.SetupHelper'; Folder='helper'; SingleFile='true' },
    @{ Project='src/Port.Launcher'; Folder='runtime'; SingleFile='true' },
    @{ Project='tools/inspect_assembly'; Folder='inspect'; SingleFile='true' },
    @{ Project='tools/abi_check'; Folder='abi'; SingleFile='false' }
)) {
    $project = Join-Path $repo $item.Project
    $target = Join-Path $payload $item.Folder
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    dotnet restore $project -r win-x64 --source $cache --source $RuntimePackSource -p:NuGetAudit=false -v:q
    if ($LASTEXITCODE -ne 0) { throw "Restore failed: $project" }
    foreach ($pin in $packPins.packages.PSObject.Properties) {
        $file = Join-Path $env:USERPROFILE ".nuget/packages/$($pin.Name)/$($packPins.version)/$($pin.Name).$($packPins.version).nupkg"
        if (-not (Test-Path -LiteralPath $file) -or
            (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine $pin.Value) {
            throw "Runtime pack differs from installer/config/runtime-packs.json: $($pin.Name)"
        }
    }
    dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore "-p:PublishSingleFile=$($item.SingleFile)" -p:PublishTrimmed=false -o $target -v:q
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $project" }
}
Get-ChildItem -LiteralPath (Join-Path $payload 'helper') -Filter '*.pdb' -File | Remove-Item -Force

$runtime = Join-Path $payload 'runtime'
foreach ($name in @('FNA.dll','FNA.dll.config')) {
    Copy-Item -LiteralPath (Join-Path $repo "out/deps/managed/$name") -Destination $runtime -Force
}
foreach ($name in @('Port.Bridge.dll','Port.Hooks.dll')) {
    $project = [System.IO.Path]::GetFileNameWithoutExtension($name)
    Copy-Item -LiteralPath (Join-Path $repo "src/$project/bin/Release/net10.0/$name") -Destination $runtime -Force
}
Copy-Item -Path (Join-Path $repo 'out/deps/native/win-x64/*') -Destination $runtime -Recurse -Force
Get-ChildItem -LiteralPath $runtime -Filter '*.pdb' -Recurse -File | Remove-Item -Force
$notices = Join-Path $runtime 'notices'
New-Item -ItemType Directory -Force -Path $notices | Out-Null
$fnaSource = Join-Path $repo 'out/deps/fna-26.11/FNA'
Copy-Item -Path (Join-Path $fnaSource 'licenses/*') -Destination $notices -Force
foreach ($entry in @(
    @{ From='lib/FAudio/LICENSE'; To='FAudio-LICENSE' },
    @{ From='lib/FNA3D/LICENSE'; To='FNA3D-LICENSE' },
    @{ From='lib/FNA3D/MojoShader/LICENSE.txt'; To='MojoShader-LICENSE.txt' },
    @{ From='lib/SDL3-CS/LICENSE'; To='SDL3-CS-LICENSE' },
    @{ From='lib/Theorafile/licenses/LICENSE'; To='Theorafile-LICENSE' },
    @{ From='lib/Theorafile/licenses/libogg.LICENSE'; To='libogg-LICENSE' },
    @{ From='lib/Theorafile/licenses/libtheora.LICENSE'; To='libtheora-LICENSE' },
    @{ From='lib/Theorafile/licenses/libvorbis.LICENSE'; To='libvorbis-LICENSE' }
)) {
    Copy-Item -LiteralPath (Join-Path $fnaSource $entry.From) -Destination (Join-Path $notices $entry.To) -Force
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'notices/Mono.Cecil-NOTICE.txt') -Destination $notices -Force
$runtimeNoticeRoot = Join-Path $env:USERPROFILE ".nuget/packages/microsoft.netcore.app.runtime.win-x64/$($packPins.version)"
foreach ($name in @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')) {
    Copy-Item -LiteralPath (Join-Path $runtimeNoticeRoot $name) -Destination (Join-Path $notices "dotnet-$name") -Force
}
& $converter -L | Set-Content -Path (Join-Path $notices 'FFmpeg-LICENSE.txt')
if ($LASTEXITCODE -ne 0) { throw 'Could not capture FFmpeg license.' }
$tools = Join-Path $payload 'tools'
New-Item -ItemType Directory -Force -Path $tools | Out-Null
$oldAbi = Join-Path $tools 'abi_check.exe'
if (Test-Path -LiteralPath $oldAbi) { Remove-Item -LiteralPath $oldAbi -Force }
Copy-Item -LiteralPath (Join-Path $payload 'inspect/inspect_assembly.exe') -Destination $tools -Force
New-Item -ItemType Directory -Force -Path (Join-Path $tools 'abi') | Out-Null
Copy-Item -Path (Join-Path $payload 'abi/*') -Destination (Join-Path $tools 'abi') -Recurse -Force
Get-ChildItem -LiteralPath $tools -Filter '*.pdb' -Recurse -File | Remove-Item -Force
foreach ($intermediate in @('abi','inspect')) {
    $target = [System.IO.Path]::GetFullPath((Join-Path $payload $intermediate))
    if (-not $target.StartsWith(($expectedPayload + [IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Unexpected intermediate publish path.'
    }
    Remove-Item -LiteralPath $target -Recurse -Force
}
Copy-Item -LiteralPath $converter -Destination (Join-Path $tools 'ffmpeg.exe') -Force
New-Item -ItemType Directory -Force -Path (Join-Path $payload 'config') | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'config/supported-versions.json') -Destination (Join-Path $payload 'config') -Force

$allowedRuntime = @(
    'Port.Launcher.exe','Port.Bridge.dll','Port.Hooks.dll','FNA.dll','FNA.dll.config',
    'SDL3.dll','FNA3D.dll','FAudio.dll','libtheorafile.dll','D3D12/D3D12Core.dll',
    'notices/LICENSE','notices/lzxdecoder.LICENSE','notices/monoxna.LICENSE',
    'notices/FAudio-LICENSE','notices/FNA3D-LICENSE','notices/SDL3-CS-LICENSE',
    'notices/MojoShader-LICENSE.txt','notices/Theorafile-LICENSE',
    'notices/libogg-LICENSE','notices/libtheora-LICENSE','notices/libvorbis-LICENSE',
    'notices/Mono.Cecil-NOTICE.txt','notices/dotnet-LICENSE.TXT',
    'notices/dotnet-THIRD-PARTY-NOTICES.TXT','notices/FFmpeg-LICENSE.txt'
)
foreach ($folder in @('helper','runtime','tools','config')) {
    $folderPath = Join-Path $payload $folder
    foreach ($file in Get-ChildItem -LiteralPath $folderPath -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath($folderPath,$file.FullName).Replace('\','/')
        $allowed = switch ($folder) {
            'helper' { $relative -eq 'Port.SetupHelper.exe' }
            'runtime' { $relative -in $allowedRuntime }
            'config' { $relative -eq 'supported-versions.json' }
            'tools' {
                $relative -in @('ffmpeg.exe','inspect_assembly.exe') -or
                ($relative -match '^abi/[^/]+$' -and $file.Extension -in @('.dll','.exe','.json'))
            }
        }
        if (-not $allowed) { throw "Unexpected installer payload file: $folder/$relative" }
    }
}

foreach ($forbidden in @('ZombiesWP7.dll','*.xap','*.xnb','*.wma')) {
    if (Get-ChildItem -LiteralPath $payload -Recurse -File -Filter $forbidden) {
        throw "Game data found in installer payload: $forbidden"
    }
}

& $IsccPath (Join-Path $PSScriptRoot 'setup.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
