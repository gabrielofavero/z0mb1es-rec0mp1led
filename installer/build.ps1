param(
    [Parameter(Mandatory = $true)][string]$FfmpegPath,
    [string]$RuntimePackSource = 'https://api.nuget.org/v3/index.json',
    [string]$IsccPath = 'ISCC.exe',
    [string]$PyInstallerRoot = 'private/tooling/pyinstaller'
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
python (Join-Path $PSScriptRoot 'ffmpeg/package-source.py') --ffmpeg-path $converter
if ($LASTEXITCODE -ne 0) { throw 'FFmpeg source bundle or license packaging failed.' }
$priorSetup = Join-Path $PSScriptRoot 'out/Z0MB1ES-1.0.0.exe'
if (Test-Path -LiteralPath $priorSetup) { Remove-Item -LiteralPath $priorSetup -Force }

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
    @{ Project='tools/abi_check'; Folder='abi'; SingleFile='false' },
    @{ Project='tools/decode_360_xnb'; Folder='decoder'; SingleFile='true' }
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
$parserRoot = [IO.Path]::GetFullPath((Join-Path $repo '../xna-to-fna-asset-extractor/xnb_parse'))
if (-not (Test-Path -LiteralPath (Join-Path $parserRoot 'xnb_parse/xnb_reader.py'))) {
    throw 'The xnb_parse source checkout is required to build the offline art importer.'
}
$parserCommit = (& git -C $parserRoot rev-parse HEAD).Trim()
if ($parserCommit -ne 'cc3aef8fff4be1320068a78001795e7e1c8bd548') {
    throw 'xnb_parse differs from the reviewed importer source commit.'
}
$pythonRoot = Split-Path (Get-Command python).Source -Parent
$artPythonVersions = & python -c 'import sys,PIL,numpy; print("%d.%d.%d %s %s" % (*sys.version_info[:3], PIL.__version__, numpy.__version__))'
if ($artPythonVersions -ne '3.12.1 12.3.0 2.5.1') {
    throw "Art importer Python, Pillow, or NumPy version differs: $artPythonVersions"
}
Copy-Item -LiteralPath (Join-Path $parserRoot 'LICENSE') -Destination (Join-Path $notices 'xnb-parse-LICENSE') -Force
Copy-Item -LiteralPath (Join-Path $pythonRoot 'LICENSE.txt') -Destination (Join-Path $notices 'Python-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $pythonRoot 'Lib/site-packages/pillow-12.3.0.dist-info/licenses/LICENSE') -Destination (Join-Path $notices 'Pillow-LICENSE') -Force
Copy-Item -LiteralPath (Join-Path $pythonRoot 'Lib/site-packages/numpy-2.5.1.dist-info/licenses/LICENSE.txt') -Destination (Join-Path $notices 'NumPy-LICENSE.txt') -Force
$runtimeNoticeRoot = Join-Path $env:USERPROFILE ".nuget/packages/microsoft.netcore.app.runtime.win-x64/$($packPins.version)"
foreach ($name in @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')) {
    Copy-Item -LiteralPath (Join-Path $runtimeNoticeRoot $name) -Destination (Join-Path $notices "dotnet-$name") -Force
}
& $converter -L | Set-Content -Path (Join-Path $notices 'FFmpeg-LICENSE.txt')
if ($LASTEXITCODE -ne 0) { throw 'Could not capture FFmpeg license.' }
Copy-Item -Path (Join-Path $PSScriptRoot 'out/ffmpeg-notices/*') -Destination $notices -Force
$tools = Join-Path $payload 'tools'
New-Item -ItemType Directory -Force -Path $tools | Out-Null
$oldAbi = Join-Path $tools 'abi_check.exe'
if (Test-Path -LiteralPath $oldAbi) { Remove-Item -LiteralPath $oldAbi -Force }
Copy-Item -LiteralPath (Join-Path $payload 'inspect/inspect_assembly.exe') -Destination $tools -Force
New-Item -ItemType Directory -Force -Path (Join-Path $tools 'abi') | Out-Null
Copy-Item -Path (Join-Path $payload 'abi/*') -Destination (Join-Path $tools 'abi') -Recurse -Force
Get-ChildItem -LiteralPath $tools -Filter '*.pdb' -Recurse -File | Remove-Item -Force
Copy-Item -LiteralPath (Join-Path $payload 'decoder/decode_360_xnb.exe') -Destination $tools -Force
foreach ($intermediate in @('abi','inspect','decoder')) {
    $target = [System.IO.Path]::GetFullPath((Join-Path $payload $intermediate))
    if (-not $target.StartsWith(($expectedPayload + [IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Unexpected intermediate publish path.'
    }
    Remove-Item -LiteralPath $target -Recurse -Force
}
Copy-Item -LiteralPath $converter -Destination (Join-Path $tools 'ffmpeg.exe') -Force
$pyInstallerPath = [IO.Path]::GetFullPath((Join-Path $repo $PyInstallerRoot))
if (-not (Test-Path -LiteralPath (Join-Path $pyInstallerPath 'PyInstaller/__main__.py'))) {
    throw 'PyInstaller is required under the configured ignored tooling path.'
}
Copy-Item -LiteralPath (Join-Path $pyInstallerPath 'pyinstaller-6.22.3.dist-info/licenses/COPYING.txt') -Destination (Join-Path $notices 'PyInstaller-COPYING.txt') -Force
$priorPythonPath = $env:PYTHONPATH
try {
    $env:PYTHONPATH = "$pyInstallerPath;$parserRoot"
    $pyInstallerVersion = & python -m PyInstaller --version
    if ($pyInstallerVersion -ne '6.22.3') { throw "PyInstaller version differs: $pyInstallerVersion" }
    python -m PyInstaller --onefile --noconfirm --clean --name Port.AssetImporter `
        --distpath $tools --workpath (Join-Path $PSScriptRoot 'out/pyinstaller-work') `
        --specpath (Join-Path $PSScriptRoot 'out') --paths $parserRoot `
        --collect-submodules xnb_parse `
        --add-data "$(Join-Path $repo 'config/supported-steam-versions.json');config" `
        --add-data "$(Join-Path $repo 'config/supported-360-versions.json');config" `
        --add-data "$(Join-Path $repo 'docs/records/asset-swap-inventory.json');docs/records" `
        --add-data "$(Join-Path $repo 'docs/records/zombies-360-inventory.json');docs/records" `
        --add-data "$(Join-Path $repo 'docs/records/viking-360-inventory.json');docs/records" `
        --add-data "$(Join-Path $repo 'docs/records/asset-swap-mappings');docs/records/asset-swap-mappings" `
        (Join-Path $repo 'tools/prepare_optional_art.py')
    if ($LASTEXITCODE -ne 0) { throw 'Offline art importer packaging failed.' }
} finally {
    $env:PYTHONPATH = $priorPythonPath
}
New-Item -ItemType Directory -Force -Path (Join-Path $payload 'config') | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'config/supported-versions.json') -Destination (Join-Path $payload 'config') -Force

$allowedRuntime = @(
    'Z0MB1ES.exe','Port.Bridge.dll','Port.Hooks.dll','FNA.dll','FNA.dll.config',
    'SDL3.dll','FNA3D.dll','FAudio.dll','libtheorafile.dll','D3D12/D3D12Core.dll',
    'notices/LICENSE','notices/lzxdecoder.LICENSE','notices/monoxna.LICENSE',
    'notices/FAudio-LICENSE','notices/FNA3D-LICENSE','notices/SDL3-CS-LICENSE',
    'notices/MojoShader-LICENSE.txt','notices/Theorafile-LICENSE',
    'notices/libogg-LICENSE','notices/libtheora-LICENSE','notices/libvorbis-LICENSE',
    'notices/Mono.Cecil-NOTICE.txt','notices/xnb-parse-LICENSE',
    'notices/Python-LICENSE.txt','notices/Pillow-LICENSE','notices/NumPy-LICENSE.txt',
    'notices/PyInstaller-COPYING.txt','notices/dotnet-LICENSE.TXT',
    'notices/dotnet-THIRD-PARTY-NOTICES.TXT','notices/FFmpeg-LICENSE.txt',
    'notices/FFmpeg-COPYING.LGPLv2.1','notices/FFmpeg-LICENSE.md',
    'notices/FFmpeg-libogg-COPYING','notices/FFmpeg-libvorbis-COPYING',
    'notices/FFmpeg-SOURCE.txt'
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
                $relative -in @('ffmpeg.exe','inspect_assembly.exe','decode_360_xnb.exe','Port.AssetImporter.exe') -or
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
