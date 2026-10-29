# Behavioral checks against the same Windows PowerShell validator used by Build and Publish.
# Only disposable copies are changed; no MAUI process, database or MOVEit calls are started.
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$PackageDirectory)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$sourceRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$validator = Join-Path $sourceRoot 'Build/Verify-ReleasePackage.ps1'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('maui package tests/' + [Guid]::NewGuid().ToString('N'))
$releaseFiles = @('MoveItAutomationUI.dll', 'appsettings.template.json', 'upgrade.json')
$checks = 0

function Reset-Package {
    foreach ($name in $releaseFiles) {
        Copy-Item -LiteralPath (Join-Path $PackageDirectory $name) -Destination (Join-Path $fixture $name) -Force
    }
}

function Set-ReleaseValue([string]$File, [scriptblock]$Change) {
    $path = Join-Path $fixture $File
    $json = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    & $Change $json
    $json | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $path -Encoding UTF8
}

function Assert-Validation([string]$Name, [bool]$Success, [string]$Message, [string]$ExpectedVersion = '') {
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = (Get-Command powershell.exe).Source
    $start.Arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $validator +
        '" -PackageDirectory "' + $fixture + '"'
    if ($ExpectedVersion) { $start.Arguments += ' -ExpectedVersion "' + $ExpectedVersion + '"' }
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    try {
        $output = $process.StandardOutput.ReadToEnd() + $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        if (($process.ExitCode -eq 0) -ne $Success -or -not $output.Contains($Message)) {
            throw "$Name failed (exit $($process.ExitCode)): $output"
        }
    } finally { $process.Dispose() }
    if ([IO.File]::ReadAllText((Join-Path $fixture 'appsettings.json')) -ne 'installation-owned; not JSON' -or
        [IO.File]::ReadAllText((Join-Path $fixture 'Data/keep.txt')) -ne 'preserve installation data') {
        throw "$Name changed installation-owned state."
    }
    $script:checks++
}

try {
    [IO.Directory]::CreateDirectory((Join-Path $fixture 'Data')) | Out-Null
    [IO.File]::WriteAllText((Join-Path $fixture 'appsettings.json'), 'installation-owned; not JSON')
    [IO.File]::WriteAllText((Join-Path $fixture 'Data/keep.txt'), 'preserve installation data')
    Reset-Package
    Assert-Validation 'Matching package; live settings never read' $true 'MAUI release package verified'

    Set-ReleaseValue 'appsettings.template.json' { param($json) $json.Ui.Version = '0.18.484-RC3' }
    Assert-Validation 'Stale template' $false 'appsettings.template.json Ui.Version'
    Reset-Package
    Set-ReleaseValue 'upgrade.json' { param($json) $json.TargetVersion = '0.18.486-RC3' }
    Assert-Validation 'Stale manifest' $false 'upgrade.json TargetVersion'
    Set-ReleaseValue 'appsettings.template.json' { param($json) $json.Ui.Version = '0.18.486-RC3' }
    Assert-Validation 'Matching JSON files but wrong DLL' $false 'MoveItAutomationUI.dll is'
    Reset-Package
    Assert-Validation 'Stale no-build output' $false 'Rebuild before publishing' '0.0.1-RC3'

    foreach ($name in $releaseFiles) {
        Reset-Package
        Remove-Item -LiteralPath (Join-Path $fixture $name)
        Assert-Validation "Missing $name" $false "$name is missing"
    }
    foreach ($name in @('appsettings.template.json', 'upgrade.json')) {
        Reset-Package
        [IO.File]::WriteAllText((Join-Path $fixture $name), 'not JSON')
        Assert-Validation "Malformed $name" $false 'could not be read as JSON'
    }
    Reset-Package
    Set-ReleaseValue 'appsettings.template.json' { param($json) $json.UpgradeState.ConfigurationVersion = 2 }
    Assert-Validation 'Configuration schema mismatch' $false 'ConfigurationVersion must match'
    Reset-Package
    Set-ReleaseValue 'appsettings.template.json' { param($json) $json.UpgradeState.ConfigurationVersion = '1' }
    Assert-Validation 'Configuration version is not numeric' $false 'positive integer'
    Reset-Package
    Set-ReleaseValue 'appsettings.template.json' { param($json) $json.Ui.PSObject.Properties.Remove('Version') }
    Assert-Validation 'Missing template version' $false 'Ui.Version is missing'
    Reset-Package
    Set-ReleaseValue 'upgrade.json' { param($json) $json.PSObject.Properties.Remove('RequiredConfigurationVersion') }
    Assert-Validation 'Missing configuration contract' $false 'RequiredConfigurationVersion is missing'
    Write-Output "Passed $checks release-package validation checks; installation-owned state preserved."
} finally {
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force }
}
