# Use the same Linux commands from PowerShell and Bash.
$script:LinuxDevShell = Join-Path $PSScriptRoot '../../dev.sh'

function Invoke-LinuxDev([string]$Action, [object[]]$Arguments) {
    & bash $script:LinuxDevShell --platform linux $Action @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Linux $Action failed with exit code $LASTEXITCODE." }
}

function build-game { Invoke-LinuxDev build-game $args }
function build-installer { Invoke-LinuxDev build-installer $args }
function run-game { Invoke-LinuxDev run-game $args }
function dev-help { Invoke-LinuxDev help $args }
