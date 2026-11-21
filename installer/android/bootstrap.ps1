# Windows x64 build host; all downloaded tooling remains under ignored out/.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$pin = Get-Content -Raw (Join-Path $root 'config/android-packaging.json') | ConvertFrom-Json
$work = Join-Path $root 'out/android'
$sdk = Join-Path $work 'dotnet'
$oldCliHome = $env:DOTNET_CLI_HOME
$oldPackages = $env:NUGET_PACKAGES
New-Item -ItemType Directory -Force $work | Out-Null
try {
    if (-not (Test-Path (Join-Path $sdk 'dotnet.exe'))) {
        $archive = Join-Path $work "dotnet-sdk-$($pin.dotnetSdk)-win-x64.zip"
        if (-not (Test-Path $archive)) {
            Invoke-WebRequest "https://builds.dotnet.microsoft.com/dotnet/Sdk/$($pin.dotnetSdk)/dotnet-sdk-$($pin.dotnetSdk)-win-x64.zip" -OutFile $archive
        }
        if ((Get-FileHash $archive -Algorithm SHA512).Hash -ine $pin.dotnetSdkWinX64Sha512) { throw 'SDK archive checksum differs.' }
        Expand-Archive -LiteralPath $archive -DestinationPath $sdk -Force
    }
    $androidDotnet = Join-Path $sdk 'dotnet.exe'
    if ((& $androidDotnet --version) -ne $pin.dotnetSdk) { throw 'Unexpected local SDK version.' }
    $env:DOTNET_CLI_HOME = Join-Path $work 'dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $work 'nuget'
    & $androidDotnet workload install android --skip-manifest-update --source https://api.nuget.org/v3/index.json
    if ($LASTEXITCODE -ne 0) { throw 'Android workload installation failed.' }
    $manifest = Join-Path $sdk "sdk-manifests/10.0.100/microsoft.net.sdk.android/$($pin.androidWorkload)/WorkloadManifest.json"
    if (-not (Test-Path $manifest)) { throw 'Android workload differs from the reviewed manifest.' }
    $package = Join-Path $work "fna.net.$($pin.fnaPackageVersion).nupkg"
    if (-not (Test-Path $package)) {
        Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/fna.net/$($pin.fnaPackageVersion)/fna.net.$($pin.fnaPackageVersion).nupkg" -OutFile $package
    }
    if ((Get-FileHash $package).Hash -ine $pin.fnaPackageSha256) { throw 'FNA.NET package checksum differs.' }
    [IO.Compression.ZipFile]::ExtractToDirectory($package, (Join-Path $work 'fna-package'), $true)
    if ((Get-FileHash (Join-Path $work 'fna-package/lib/net9.0-android35.0/FNA.NET.dll')).Hash -ine $pin.fnaAndroidAssemblySha256) {
        throw 'FNA.NET Android assembly checksum differs.'
    }
    Write-Host 'Android SDK/workload and FNA.NET are ready.'
    Write-Host 'Also supply Android SDK platform 36 / build-tools 36.1.0 and JDK 17 or 21. See installer/android/README.md.'
} finally {
    $env:DOTNET_CLI_HOME = $oldCliHome
    $env:NUGET_PACKAGES = $oldPackages
}
