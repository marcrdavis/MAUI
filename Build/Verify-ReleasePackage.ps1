# Author: Marc R. Davis (marcdavis@comcast.net)
# Checks release-owned files only. Never starts MAUI or reads/writes live settings or Data.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageDirectory,
    [string]$ExpectedVersion = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Read-ReleaseJson([string]$Name) {
    $path = Join-Path $PackageDirectory $Name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Incomplete release package: $Name is missing. Deploy the complete publish output."
    }
    try {
        return (Get-Content -LiteralPath $path -Raw | ConvertFrom-Json)
    } catch {
        throw "Invalid release package: $Name could not be read as JSON. Deploy the complete publish output."
    }
}

function Read-ReleaseValue($Document, [string[]]$Path, [string]$Name) {
    $value = $Document
    foreach ($part in $Path) {
        if ($null -eq $value -or $null -eq $value.PSObject.Properties[$part]) {
            throw "Invalid release package: $Name is missing. Deploy the complete publish output."
        }
        $value = $value.PSObject.Properties[$part].Value
    }
    return $value
}

try {
    $assemblyPath = Join-Path $PackageDirectory 'MoveItAutomationUI.dll'
    if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
        throw 'Incomplete release package: MoveItAutomationUI.dll is missing. Deploy the complete publish output.'
    }

    # ProductVersion carries InformationalVersion, including the optional build/revision suffix.
    # Inspecting file metadata does not load application code, configuration, COM or workers.
    $productVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($assemblyPath).ProductVersion
    if ([string]::IsNullOrWhiteSpace($productVersion)) {
        throw 'Invalid release package: MoveItAutomationUI.dll has no product version.'
    }
    $binaryVersion = ($productVersion -split '\+', 2)[0].Trim()
    if ([string]::IsNullOrWhiteSpace($ExpectedVersion)) { $ExpectedVersion = $binaryVersion }
    if (-not [string]::Equals($binaryVersion, $ExpectedVersion.Trim(), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Release package mismatch: MoveItAutomationUI.dll is '$binaryVersion', but the build requires '$ExpectedVersion'. Rebuild before publishing."
    }

    $manifest = Read-ReleaseJson 'upgrade.json'
    $template = Read-ReleaseJson 'appsettings.template.json'
    foreach ($item in @(
        @{ Name = 'upgrade.json TargetVersion'; Version = (Read-ReleaseValue $manifest @('TargetVersion') 'upgrade.json TargetVersion') },
        @{ Name = 'appsettings.template.json Ui.Version'; Version = (Read-ReleaseValue $template @('Ui', 'Version') 'appsettings.template.json Ui.Version') }
    )) {
        if (-not [string]::Equals(([string]$item.Version).Trim(), $binaryVersion, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Release package mismatch: $($item.Name) is '$($item.Version)', but MoveItAutomationUI.dll is '$binaryVersion'. Deploy matching files from one publish output."
        }
    }

    $configurationVersion = Read-ReleaseValue $template @('UpgradeState', 'ConfigurationVersion') 'template ConfigurationVersion'
    $requiredVersion = Read-ReleaseValue $manifest @('RequiredConfigurationVersion') 'manifest RequiredConfigurationVersion'
    if (($configurationVersion -isnot [int] -and $configurationVersion -isnot [long]) -or
        ($requiredVersion -isnot [int] -and $requiredVersion -isnot [long]) -or
        $configurationVersion -lt 1 -or $configurationVersion -ne $requiredVersion) {
        throw 'Release package mismatch: template ConfigurationVersion must match upgrade.json RequiredConfigurationVersion as a positive integer.'
    }

    Write-Output "MAUI release package verified: $binaryVersion (DLL, template and upgrade manifest match)."
    exit 0
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
