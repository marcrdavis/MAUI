#requires -Version 5.1
#requires -PSEdition Desktop
[CmdletBinding()]param()
$sha = [Security.Cryptography.SHA256]::Create()
try {
    Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.HasPrivateKey } | ForEach-Object {
        [pscustomobject]@{
            Subject=$_.Subject; NotAfter=$_.NotAfter; Thumbprint=$_.Thumbprint
            SHA256=([BitConverter]::ToString($sha.ComputeHash($_.RawData))).Replace('-','')
            EnhancedKeyUsage=($_.EnhancedKeyUsageList.ObjectId -join ', ')
        }
    }
} finally { $sha.Dispose() }
