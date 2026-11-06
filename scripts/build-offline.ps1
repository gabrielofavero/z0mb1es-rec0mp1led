param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$deps = Join-Path $root 'out/deps'
$cache = Join-Path $deps 'cache'
$ilWriter = Join-Path $cache 'mono.cecil.0.11.6.nupkg'
if (-not (Test-Path -LiteralPath $ilWriter)) { throw 'Missing pinned Mono.Cecil package. Run scripts/bootstrap-fna.ps1 first.' }
$pin = (Get-Content -Raw -LiteralPath (Join-Path $root 'config/dependencies.json') | ConvertFrom-Json).ilWriter.archiveSha256
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $ilWriter).Hash -ine $pin) { throw 'Mono.Cecil package hash differs from config/dependencies.json.' }
$env:DOTNET_CLI_HOME = Join-Path $root 'out/dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME | Out-Null

$config = Get-Content -Raw -LiteralPath (Join-Path $root 'config/dependencies.json') | ConvertFrom-Json
$required = @('managed/FNA.dll', 'managed/FNA.dll.config') + @($config.native.files | ForEach-Object { "native/win-x64/$_" })
foreach ($file in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $deps $file))) {
        throw "Missing pinned dependency $file. Run scripts/bootstrap-fna.ps1 first."
    }
}

$solution = Join-Path $root 'ZombiesPort.sln'
dotnet restore $solution --source $cache -p:NuGetAudit=false -v:q
if ($LASTEXITCODE -ne 0) { throw "Offline solution restore failed ($LASTEXITCODE)" }
dotnet build $solution --no-restore -c $Configuration -v:q
if ($LASTEXITCODE -ne 0) { throw "Offline solution build failed ($LASTEXITCODE)" }
