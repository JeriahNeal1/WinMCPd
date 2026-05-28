param(
    [string]$InstallRoot = "C:\Tools\WindowsPowerUserMcp",
    [string]$TaskName = "WindowsPowerUserMcp DesktopAgent"
)

$ErrorActionPreference = "Stop"
$exe = Join-Path $InstallRoot "WindowsPowerUserMcp.DesktopAgent.exe"
if (-not (Test-Path $exe)) {
    throw "DesktopAgent executable not found at $exe. Run scripts\install.ps1 first."
}

$action = New-ScheduledTaskAction -Execute $exe
$trigger = New-ScheduledTaskTrigger -AtLogOn
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel LeastPrivilege
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Force | Out-Null
Write-Host "Installed visible logon autostart task '$TaskName'"
