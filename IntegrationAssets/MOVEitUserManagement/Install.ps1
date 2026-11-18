#requires -Version 5.1
#requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
$exe = Join-Path $PWD.Path 'MauiLocalGroups.Service.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw 'Run this script from the folder containing MauiLocalGroups.Service.exe.'
}
New-Service -Name 'MauiLocalGroups' -DisplayName 'MAUI Local Groups Service' -BinaryPathName ('"' + $exe + '"') -StartupType Manual
