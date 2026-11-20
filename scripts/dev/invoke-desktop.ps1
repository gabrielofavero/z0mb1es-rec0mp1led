# Bridge from the macOS shell commands to the existing desktop workflow.
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)][ValidateSet('build-game', 'run-game')][string]$Action,
    [string]$XapPath,
    [string]$AudioConverterPath,
    [string]$ArtStorePath,
    [string]$Build,
    [switch]$List
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/desktop.ps1"
$forward = @{} + $PSBoundParameters
$forward.Remove('Action')
& $Action @forward
