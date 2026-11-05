#Requires -Version 7.0
<#
.SYNOPSIS
    Restores, builds and tests RDPManager.

.DESCRIPTION
    Runs `dotnet restore`, `dotnet build` and `dotnet test` against RDPManager.slnx.
    The same steps run in GitHub Actions.

.PARAMETER Configuration
    Build configuration: Debug or Release. Default: Release.

.PARAMETER SkipTests
    Build only; do not run the tests.

.EXAMPLE
    ./build.ps1
    ./build.ps1 -Configuration Debug -SkipTests
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$solution = Join-Path $PSScriptRoot 'RDPManager.slnx'

# Opt out of Avalonia's anonymous build-time telemetry (Avalonia.BuildServices).
$env:AVALONIA_TELEMETRY_OPTOUT = '1'

function Invoke-Step {
    param([string] $Name, [scriptblock] $Command)
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK was not found. Install the .NET 10 SDK (for example: winget install --id Microsoft.DotNet.SDK.10 -e).'
}

Invoke-Step 'Restore' { dotnet restore $solution }
Invoke-Step "Build ($Configuration)" { dotnet build $solution --configuration $Configuration --no-restore }

if (-not $SkipTests) {
    Invoke-Step 'Test' { dotnet test $solution --configuration $Configuration --no-build }
}

Write-Host 'Done.' -ForegroundColor Green
