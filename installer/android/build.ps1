# Personal experiment: embeds locally supplied proprietary content. Do not publish.
[CmdletBinding()]
param(
    [string]$XapPath = '',
    [string]$ArtStorePath = '',
    [string]$AndroidSdkDirectory = "$env:LOCALAPPDATA/Android/Sdk",
    [string]$JavaSdkDirectory = 'C:/Program Files/Java/jdk-17',
    [switch]$SkipGameBuild
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$pin = Get-Content -Raw (Join-Path $root 'config/android-packaging.json') | ConvertFrom-Json
$androidDotnet = Join-Path $root 'out/android/dotnet/dotnet.exe'
function Invoke-ApkTool([string]$Executable, [string[]]$ToolArguments) {
    & $Executable @ToolArguments
    if ($LASTEXITCODE -ne 0) { throw "$([IO.Path]::GetFileName($Executable)) failed ($LASTEXITCODE)." }
}
Push-Location $root
$oldCliHome = $env:DOTNET_CLI_HOME
$oldPackages = $env:NUGET_PACKAGES
$oldJava = $env:JAVA_HOME
try {
    if (-not (Test-Path $androidDotnet)) { throw 'Run installer/android/bootstrap.ps1 first.' }
    if ((& $androidDotnet --version) -ne $pin.dotnetSdk) { throw 'Unexpected Android .NET SDK version.' }
    if (-not (Test-Path (Join-Path $AndroidSdkDirectory "platforms/android-$($pin.targetApi)/android.jar"))) { throw 'Android SDK platform 36 is required.' }
    if (-not (Test-Path (Join-Path $JavaSdkDirectory 'bin/keytool.exe'))) { throw 'Supply -JavaSdkDirectory with JDK 17 or 21.' }
    if (-not $SkipGameBuild) {
        . ./dev.ps1
        $gameParameters = @{}
        if ($XapPath) { $gameParameters.XapPath = $XapPath }
        if ($PSBoundParameters.ContainsKey('ArtStorePath')) { $gameParameters.ArtStorePath = $ArtStorePath }
        build-game @gameParameters
    } elseif ($XapPath -or $PSBoundParameters.ContainsKey('ArtStorePath')) {
        throw 'XAP/artwork parameters require a fresh game build. Remove -SkipGameBuild.'
    }
    $package = Join-Path $root "out/android/fna.net.$($pin.fnaPackageVersion).nupkg"
    $fna = Join-Path $root 'out/android/fna-package/lib/net9.0-android35.0/FNA.NET.dll'
    if ((Get-FileHash $package).Hash -ine $pin.fnaPackageSha256 -or
        (Get-FileHash $fna).Hash -ine $pin.fnaAndroidAssemblySha256) { throw 'FNA.NET differs from the reviewed pin. Run bootstrap.ps1.' }
    New-Item -ItemType Directory -Force out/android/resources/drawable | Out-Null
    Copy-Item assets/icon/icon-black.png out/android/resources/drawable/icon.png -Force
    # Desktop and experiment outputs are independent; never rebuild desktop
    # bridge/hooks against the fork or replace out/deps/managed/FNA.dll.
    foreach ($project in @('src/Port.AndroidHooks', 'tools/android-bundle-prepare')) {
        Invoke-ApkTool dotnet @('build', $project, '-c', 'Release', '--source', 'out/deps/cache', '-p:NuGetAudit=false', '-v:q')
    }
    $buildRoot = Join-Path $root ('private/android/build-' + [guid]::NewGuid().ToString('N').Substring(0,12))
    Invoke-ApkTool dotnet @('tools/android-bundle-prepare/bin/Release/net10.0/android-bundle-prepare.dll',
        'private/run/current', $fna, 'src/Port.AndroidBridge/bin/Release/net10.0/Port.Bridge.dll',
        'src/Port.AndroidHooks/bin/Release/net10.0/Port.Hooks.dll', $buildRoot)
    $env:DOTNET_CLI_HOME = Join-Path $root 'out/android/dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $root 'out/android/nuget'
    $env:JAVA_HOME = [IO.Path]::GetFullPath($JavaSdkDirectory)
    $workloads = & $androidDotnet workload list | Out-String
    if ($LASTEXITCODE -ne 0 -or $workloads -notmatch ('(?m)^android\s+' + [regex]::Escape($pin.androidWorkload) + '/')) {
        throw 'The installed Android workload differs from the reviewed pin. Run bootstrap.ps1.'
    }
    Invoke-ApkTool $androidDotnet @('restore', 'src/Port.AndroidGame', '--locked-mode',
        '--source', 'https://api.nuget.org/v3/index.json', '-p:NuGetAudit=false')
    foreach ($item in @(
        @('fna.net.nativeassets', $pin.nativePackageVersion, $pin.nativePackageSha256),
        @('sdldroidsharp-jbindings', $pin.sdlBindingsVersion, $pin.sdlBindingsPackageSha256))) {
        $archive = Join-Path $env:NUGET_PACKAGES "$($item[0])/$($item[1])/$($item[0]).$($item[1]).nupkg"
        if ((Get-FileHash $archive).Hash -ine $item[2]) { throw "Native package differs from reviewed pin: $($item[0])" }
    }
    $signing = Join-Path $root 'private/android/signing'
    $key = Join-Path $signing 'experimental.keystore'
    $passwordFile = Join-Path $signing 'password.txt'
    New-Item -ItemType Directory -Force $signing | Out-Null
    if ((Test-Path $key) -xor (Test-Path $passwordFile)) { throw 'Incomplete signing key/password pair. Restore its backup; do not replace the update key.' }
    if (-not (Test-Path $key)) {
        $newPassword = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
        [IO.File]::WriteAllText($passwordFile, $newPassword + "`n" + $newPassword + "`n")
        Invoke-ApkTool (Join-Path $JavaSdkDirectory 'bin/keytool.exe') @('-genkeypair', '-keystore', $key,
            '-alias', 'zombies-experimental', '-keyalg', 'RSA', '-keysize', '3072', '-validity', '10000',
            '-storepass:file', $passwordFile, '-keypass:file', $passwordFile,
            '-dname', 'CN=Z0MB1ES Personal Experiment', '-storetype', 'PKCS12', '-noprompt')
    }
    # apksigner caches readers by filename and consumes one line per password
    # request. Supply both identical PKCS12 passwords without command-line secrets.
    $signingPassword = [IO.File]::ReadAllLines($passwordFile)[0]
    if ([string]::IsNullOrWhiteSpace($signingPassword)) { throw 'Signing password is empty.' }
    [IO.File]::WriteAllText($passwordFile, $signingPassword + "`n" + $signingPassword + "`n")
    $apkDirectory = Join-Path $buildRoot 'apk'
    Invoke-ApkTool $androidDotnet @('publish', 'src/Port.AndroidGame', '-c', 'Release', '--no-restore',
        "-p:BundledPayloadDirectory=$buildRoot/preinstalled", "-p:AndroidSdkDirectory=$AndroidSdkDirectory",
        "-p:JavaSdkDirectory=$JavaSdkDirectory", "-p:AndroidSdkBuildToolsVersion=$($pin.buildToolsVersion)",
        '-p:AndroidKeyStore=true', "-p:AndroidSigningKeyStore=$key", '-p:AndroidSigningKeyAlias=zombies-experimental',
        "-p:AndroidSigningStorePass=file:$passwordFile", "-p:AndroidSigningKeyPass=file:$passwordFile",
        '-p:NuGetAudit=false', '-o', $apkDirectory)
    $apk = Join-Path $apkDirectory "$($pin.applicationId)-Signed.apk"
    Invoke-ApkTool python @('installer/android/audit-bundled.py', $apk, "$buildRoot/preinstalled")
    Invoke-ApkTool (Join-Path $AndroidSdkDirectory "build-tools/$($pin.buildToolsVersion)/apksigner.bat") @('verify', '--verbose', '--print-certs', $apk)
    $destination = Join-Path $root 'private/android/Z0MB1ES-experimental.apk'
    Copy-Item -LiteralPath $apk -Destination "$destination.tmp" -Force
    Move-Item -LiteralPath "$destination.tmp" -Destination $destination -Force
    Write-Host "APK_READY $destination"
    Write-Host 'Personal bundled test APK. Physical ARM64 device acceptance is still required.'
} finally {
    $env:DOTNET_CLI_HOME = $oldCliHome
    $env:NUGET_PACKAGES = $oldPackages
    $env:JAVA_HOME = $oldJava
    Pop-Location
}
