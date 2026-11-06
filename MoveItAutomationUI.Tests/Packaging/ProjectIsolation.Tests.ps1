# Build regressions for a reused source folder. Probe files deliberately remain in place.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('Seed', 'Verify')][string]$Mode,
    [string]$PackageDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$sourceRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$marker = 'MauiProjectIsolationProbe'
$attributes = @'
[assembly: System.Reflection.AssemblyCompany("MauiProjectIsolationProbe")]
[assembly: System.Reflection.AssemblyConfiguration("Probe")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("Probe")]
[assembly: System.Reflection.AssemblyProduct("Probe")]
[assembly: System.Reflection.AssemblyTitle("Probe")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETCoreApp,Version=v8.0")]
'@
$invalidSource = '#error MauiProjectIsolationProbe must never be compiled into MAUI.'
$resource = @'
<root>
  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
  <resheader name="version"><value>2.0</value></resheader>
  <data name="MauiProjectIsolationProbe" xml:space="preserve"><value>Must not ship.</value></data>
</root>
'@
$probes = [ordered]@{
    'Features/UserManagement/MauiProjectIsolationProbe.cs' = $invalidSource
    'Views/Configuration/_UserManagementMauiProjectIsolationProbe.cshtml' = '@model MauiProjectIsolationProbe.MissingViewModel'
    'MoveItAutomationUI.Tests/UserManagement/MauiProjectIsolationProbe.cs' = $invalidSource
    'MoveItAutomationUI.Tests/FixtureBuildOutputs/obj/Debug/net8.0/MauiProjectIsolationProbe.cs' = $invalidSource
    'IntegrationAssets/MOVEitUserManagement/MauiProjectIsolationProbe.json' = '{"MauiProjectIsolationProbe":true}'
    'wwwroot/js/user-management.js' = '// MauiProjectIsolationProbe: must not ship.'
    'wwwroot/css/user-management.css' = '/* MauiProjectIsolationProbe: must not ship. */'
    'Tools/MauiProjectIsolationProbe/Source.cs' = $invalidSource
    'Tools/MauiProjectIsolationProbe/MauiProjectIsolationProbe.json' = '{"MauiProjectIsolationProbe":true}'
    'Tools/MauiProjectIsolationProbe/MauiProjectIsolationProbe.resx' = $resource
    'FixtureBuildOutputs/bin/Debug/net8.0/MauiProjectIsolationProbe.cs' = $invalidSource
    'FixtureBuildOutputs/obj/Release/net8.0/MauiProjectIsolationProbe.cs' = $invalidSource
    'FixtureBuildOutputs/bin/Debug/net8.0/MauiProjectIsolationProbe.json' = '{"MauiProjectIsolationProbe":true}'
    'FixtureBuildOutputs/obj/Release/net8.0/MauiProjectIsolationProbe.resx' = $resource
}
foreach ($project in @('MauiUserManagement.Core', 'MauiUserManagement.Contracts', 'MauiUserManagement.Service')) {
    foreach ($configuration in @('Debug', 'Release')) {
        $intermediate = "Tools/$project/obj/$configuration/net8.0"
        if ($project -eq 'MauiUserManagement.Service') { $intermediate += '/win-x64' }
        # Use a unique filename so an actual standalone project can coexist with this fixture.
        $probes["$intermediate/MauiProjectIsolationProbe.AssemblyInfo.cs"] = $attributes
    }
}
if ($Mode -eq 'Seed') {
    foreach ($entry in $probes.GetEnumerator()) {
        $path = Join-Path $sourceRoot $entry.Key
        if ((Test-Path -LiteralPath $path) -and [IO.File]::ReadAllText($path) -ne $entry.Value) {
            throw "Refusing to overwrite an existing file: $path"
        }
        New-Item -ItemType Directory -Path (Split-Path $path) -Force | Out-Null
        [IO.File]::WriteAllText($path, $entry.Value)
    }
    Write-Host "Seeded $($probes.Count) source, duplicate-attribute, resource and content probes."
    exit 0
}
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) { throw 'PackageDirectory is required for Verify.' }
# A clean/rebuild must succeed by excluding the debris, not by deleting the test condition.
foreach ($entry in $probes.GetEnumerator()) {
    $path = Join-Path $sourceRoot $entry.Key
    if (-not (Test-Path -LiteralPath $path) -or [IO.File]::ReadAllText($path) -ne $entry.Value) {
        throw "The reused-folder probe was removed or changed before verification: $path"
    }
}
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path
$leaked = @(Get-ChildItem -LiteralPath $package -File -Recurse -Force | Where-Object {
    $relative = $_.FullName.Substring($package.Length).TrimStart('\', '/')
    $_.Name.Contains($marker) -or $_.Name -in @('user-management.js', 'user-management.css') -or $relative -match '(^|[\\/])(Tools|MoveItAutomationUI\.Tests|FixtureBuildOutputs|UserManagement|MOVEitUserManagement)([\\/]|$)'
})
if ($leaked.Count) { throw ('Separate project/build content entered the package: ' + ($leaked.FullName -join ', ')) }
$assemblyPath = Join-Path $package 'MoveItAutomationUI.dll'
if ([Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($assemblyPath)).Contains($marker)) {
    throw 'A probe source or embedded resource entered the application assembly.'
}
[xml]$projectFile = Get-Content -LiteralPath (Join-Path $sourceRoot 'MoveItAutomationUI.csproj') -Raw
$expectedVersion = $projectFile.SelectSingleNode('//AssemblyVersion').InnerText
$actualVersion = [Reflection.AssemblyName]::GetAssemblyName($assemblyPath).Version.ToString()
if ($actualVersion -ne $expectedVersion) {
    throw "MAUI assembly metadata was lost: expected $expectedVersion, got $actualVersion."
}
Write-Host "PASS project isolation: $($probes.Count) probes remain on disk; package and assembly exclude them; MAUI metadata is intact."
