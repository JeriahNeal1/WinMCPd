param(
    [string]$InstallRoot = "C:\Tools\WindowsPowerUserMcp"
)

$ErrorActionPreference = "Stop"
& "$PSScriptRoot\uninstall-tray-autostart.ps1"
& "$PSScriptRoot\uninstall-service.ps1"
if (Test-Path $InstallRoot) {
    Remove-Item -LiteralPath $InstallRoot -Recurse -Force
}

Write-Host "Removed WindowsPowerUserMcp binaries from $InstallRoot"
