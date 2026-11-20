[CmdletBinding()]
param([string]$NdkDirectory = "$PSScriptRoot/../../out/android/ndk/android-ndk-r27d")
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$compiler = Join-Path $NdkDirectory 'toolchains/llvm/prebuilt/windows-x86_64/bin/aarch64-linux-android24-clang.cmd'
if (-not (Test-Path $compiler)) { throw 'Supply -NdkDirectory with Android NDK r27d (27.3.13750724).' }
if ((Get-Content (Join-Path $NdkDirectory 'source.properties') -Raw) -notmatch 'Pkg.Revision = 27\.3\.13750724') {
    throw 'The audio converter requires the reviewed Android NDK r27d.'
}
$oldCargo = $env:CARGO_HOME
$oldRustup = $env:RUSTUP_HOME
$oldTarget = $env:CARGO_TARGET_DIR
$oldLinker = $env:CARGO_TARGET_AARCH64_LINUX_ANDROID_LINKER
$oldFlags = $env:CARGO_TARGET_AARCH64_LINUX_ANDROID_RUSTFLAGS
Push-Location $root
try {
    if (Test-Path private/tooling/cargo-home/bin/cargo.exe) {
        $env:CARGO_HOME = Join-Path $root 'private/tooling/cargo-home'
        $env:RUSTUP_HOME = Join-Path $root 'private/tooling/rustup-home'
        $cargo = Join-Path $env:CARGO_HOME 'bin/cargo.exe'
    } else { $cargo = 'cargo' }
    $env:CARGO_TARGET_DIR = Join-Path $root 'out/android/audio-converter'
    $env:CARGO_TARGET_AARCH64_LINUX_ANDROID_LINKER = [IO.Path]::GetFullPath($compiler)
    $env:CARGO_TARGET_AARCH64_LINUX_ANDROID_RUSTFLAGS = '-C link-arg=-Wl,-z,max-page-size=16384'
    & $cargo build --release --offline --locked --lib --target aarch64-linux-android --manifest-path tools/audio-converter/Cargo.toml
    if ($LASTEXITCODE -ne 0) { throw 'Android audio converter build failed. Install Rust 1.100.0 and its aarch64-linux-android target.' }
    New-Item -ItemType Directory -Force out/android/native/notices | Out-Null
    Copy-Item out/android/audio-converter/aarch64-linux-android/release/libport_audio_converter.so out/android/native/libport_audio_converter.so -Force
    $identity = (Get-FileHash out/android/native/libport_audio_converter.so).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $root 'out/android/native/converter-identity.txt'), $identity)
    Copy-Item tools/audio-converter/vendor/gameframe-wma/LICENSE-APACHE out/android/native/notices/gameframe-wma.txt -Force
    Copy-Item tools/audio-converter/vendor/qoaudio/LICENSE out/android/native/notices/qoaudio.txt -Force
} finally {
    $env:CARGO_HOME = $oldCargo
    $env:RUSTUP_HOME = $oldRustup
    $env:CARGO_TARGET_DIR = $oldTarget
    $env:CARGO_TARGET_AARCH64_LINUX_ANDROID_LINKER = $oldLinker
    $env:CARGO_TARGET_AARCH64_LINUX_ANDROID_RUSTFLAGS = $oldFlags
    Pop-Location
}
