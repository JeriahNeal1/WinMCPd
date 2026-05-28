param(
    [string]$TaskName = "WindowsPowerUserMcp DesktopAgent"
)

$ErrorActionPreference = "Continue"
Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false | Out-Null
Write-Host "Removed logon autostart task '$TaskName' if it existed"
