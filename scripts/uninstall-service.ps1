param(
    [string]$ServiceName = "WindowsPowerUserMcpBroker"
)

$ErrorActionPreference = "Continue"
sc.exe stop $ServiceName | Out-Null
Start-Sleep -Seconds 2
sc.exe delete $ServiceName | Out-Null
Write-Host "Removed service $ServiceName if it existed"
