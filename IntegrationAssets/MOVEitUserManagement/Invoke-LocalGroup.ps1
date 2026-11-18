#requires -Version 5.1
#requires -PSEdition Desktop
#requires -RunAsAdministrator
# Local test/admin CLI. Uses the same processor as the polling service; no broker or certificate required.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateSet('list','get','resolve','search','create','describe','add-member','remove-member','delete')][string]$Operation,
    [string]$GroupName, [string]$Description = '', [string]$Principal, [string]$MemberSid, [string]$Query, [string]$ConfirmDelete,
    [string]$ServiceExe = (Join-Path $PSScriptRoot 'Service\MauiLocalGroups.Service.exe')
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $ServiceExe -PathType Leaf)) { throw 'Build first, or specify -ServiceExe with the installed executable path.' }
function Invoke-CommandFile([hashtable]$Command) {
    $path = Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString('N') + '.maui-command.json')
    try {
        [IO.File]::WriteAllText($path, ($Command | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding $false))
        $text = & $ServiceExe --command $path
        $code = $LASTEXITCODE
        $result = $text | ConvertFrom-Json
        if ($code -ne 0 -or -not $result.success) { throw ($result.message + $(if ($result.outcomeUncertain) { ' Outcome uncertain: refresh before another change.' } else { '' })) }
        return $result
    } finally { if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force } }
}
$command = @{ operation=$Operation; groupName=$GroupName; description=$Description; principal=$Principal; memberSid=$MemberSid; query=$Query; confirmDelete=$ConfirmDelete }
if ($Operation -in @('get','describe','add-member','remove-member','delete')) {
    if (-not $GroupName) { throw '-GroupName is required.' }
    $list = Invoke-CommandFile @{ operation='list' }
    $groupMatches = @($list.groups | Where-Object { $_.name -ieq $GroupName })
    if ($groupMatches.Count -ne 1) { throw 'Local group not found.' }
    if ($groupMatches[0].protected) { throw 'Windows default/reserved groups cannot be managed.' }
    $snapshot = (Invoke-CommandFile @{ operation='get'; groupSid=$groupMatches[0].sid }).groups[0]
    $command.groupSid = $snapshot.sid
    $command.groupName = $snapshot.name
    $command.expectedVersion = $snapshot.version
}
Invoke-CommandFile $command
