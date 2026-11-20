# Content-free Android player: users select their packages on the device.
[CmdletBinding()]
param(
    [string]$AndroidSdkDirectory = "$env:LOCALAPPDATA/Android/Sdk",
    [string]$JavaSdkDirectory = 'C:/Program Files/Java/jdk-17',
    [string]$NdkDirectory = "$PSScriptRoot/../../out/android/ndk/android-ndk-r27d"
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
    & ./scripts/build-offline.ps1
    if ($LASTEXITCODE -ne 0) { throw 'Managed build failed.' }
    & ./installer/android/build-audio.ps1 -NdkDirectory $NdkDirectory
    $package = Join-Path $root "out/android/fna.net.$($pin.fnaPackageVersion).nupkg"
    $fna = Join-Path $root 'out/android/fna-package/lib/net9.0-android35.0/FNA.NET.dll'
    if ((Get-FileHash $package).Hash -ine $pin.fnaPackageSha256 -or
        (Get-FileHash $fna).Hash -ine $pin.fnaAndroidAssemblySha256) { throw 'FNA.NET differs from the reviewed pin. Run bootstrap.ps1.' }
    New-Item -ItemType Directory -Force out/android/resources/drawable | Out-Null
    Copy-Item assets/icon/icon-black.png out/android/resources/drawable/icon.png -Force
    # Desktop and Android outputs are independent; never rebuild desktop
    # bridge/hooks against the fork or replace out/deps/managed/FNA.dll.
    foreach ($project in @('src/Port.AndroidHooks', 'src/Port.AndroidArt', 'tools/android-import-prepare')) {
        Invoke-ApkTool dotnet @('build', $project, '-c', 'Release', '--source', 'out/deps/cache', '-p:NuGetAudit=false', '-v:q')
    }
    $buildRoot = Join-Path $root ('private/android/build-' + [guid]::NewGuid().ToString('N').Substring(0,12))
    Invoke-ApkTool dotnet @('tools/android-import-prepare/bin/Release/net10.0/android-import-prepare.dll')
    $env:DOTNET_CLI_HOME = Join-Path $root 'out/android/dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $root 'out/android/nuget'
    $env:JAVA_HOME = [IO.Path]::GetFullPath($JavaSdkDirectory)
    $workloads = & $androidDotnet workload list | Out-String
    if ($LASTEXITCODE -ne 0 -or $workloads -notmatch ('(?m)^android\s+' + [regex]::Escape($pin.androidWorkload) + '/')) {
        throw 'The installed Android workload differs from the reviewed pin. Run bootstrap.ps1.'
    }
    Invoke-ApkTool $androidDotnet @('restore', 'src/Port.AndroidGame', '--locked-mode',
        '--source', 'out/deps/cache', '--source', 'out/android/nuget',
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
            '-dname', 'CN=Z0MB1ES on teh ph0ne', '-storetype', 'PKCS12', '-noprompt')
    }
    # apksigner caches readers by filename and consumes one line per password
    # request. Supply both identical PKCS12 passwords without command-line secrets.
    $signingPassword = [IO.File]::ReadAllLines($passwordFile)[0]
    if ([string]::IsNullOrWhiteSpace($signingPassword)) { throw 'Signing password is empty.' }
    [IO.File]::WriteAllText($passwordFile, $signingPassword + "`n" + $signingPassword + "`n")
    $apkDirectory = Join-Path $buildRoot 'apk'
    Invoke-ApkTool $androidDotnet @('publish', 'src/Port.AndroidGame', '-c', 'Release', '--no-restore',
        "-p:AndroidSdkDirectory=$AndroidSdkDirectory",
        "-p:JavaSdkDirectory=$JavaSdkDirectory", "-p:AndroidSdkBuildToolsVersion=$($pin.buildToolsVersion)",
        '-p:AndroidKeyStore=true', "-p:AndroidSigningKeyStore=$key", '-p:AndroidSigningKeyAlias=zombies-experimental',
        "-p:AndroidSigningStorePass=file:$passwordFile", "-p:AndroidSigningKeyPass=file:$passwordFile",
        '-p:NuGetAudit=false', '-o', $apkDirectory)
    $apk = Join-Path $apkDirectory "$($pin.applicationId)-Signed.apk"
    Invoke-ApkTool python @('installer/android/audit-importer.py', $apk)
    $metadata = & (Join-Path $AndroidSdkDirectory "build-tools/$($pin.buildToolsVersion)/aapt2.exe") dump badging $apk | Out-String
    if ($LASTEXITCODE -ne 0 -or $metadata -notmatch ([regex]::Escape("name='$($pin.applicationId)' versionCode='$($pin.applicationVersion)' versionName='$($pin.applicationDisplayVersion)'")) -or
        $metadata -notmatch ([regex]::Escape("application-label:'Z0MB1ES'"))) {
        throw 'The APK identity, version or display name differs from the player package.'
    }
    Invoke-ApkTool (Join-Path $AndroidSdkDirectory "build-tools/$($pin.buildToolsVersion)/apksigner.bat") @('verify', '--verbose', '--print-certs', $apk)
    $destination = Join-Path $root 'out/android/Z0MB1ES.apk'
    Copy-Item -LiteralPath $apk -Destination "$destination.tmp" -Force
    Move-Item -LiteralPath "$destination.tmp" -Destination $destination -Force
    Write-Host "APK_READY $destination"
    Write-Host 'Install the APK, then select your XAP and optional artwork in setup.'
} finally {
    $env:DOTNET_CLI_HOME = $oldCliHome
    $env:NUGET_PACKAGES = $oldPackages
    $env:JAVA_HOME = $oldJava
    Pop-Location
}
