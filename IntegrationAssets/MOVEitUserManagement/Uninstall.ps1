#requires -Version 5.1
#requires -RunAsAdministrator
& sc.exe delete 'MauiLocalGroups'
if ($LASTEXITCODE -ne 0) {
    throw "SCM deregistration failed (exit code $LASTEXITCODE)."
}
