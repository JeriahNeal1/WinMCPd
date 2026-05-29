param(
    [string]$InstallRoot = "C:\Tools\WindowsPowerUserMcp",
    [switch]$DeleteLogs,
    [switch]$DeleteScreenshots,
    [switch]$DeleteTaskLedger,
    [switch]$DeleteAllUserData
)

$ErrorActionPreference = "Stop"
& "$PSScriptRoot\uninstall-tray-autostart.ps1"

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    & "$PSScriptRoot\uninstall-service.ps1"
} else {
    Write-Warning "Service uninstall skipped because this PowerShell session is not elevated."
}

if (Test-Path $InstallRoot) {
    Remove-Item -LiteralPath $InstallRoot -Recurse -Force
    Write-Host "Removed WindowsPowerUserMcp binaries from $InstallRoot"
}

$dataRoot = Join-Path $env:LOCALAPPDATA "WindowsPowerUserMcp"
if ($DeleteAllUserData) {
    if (Test-Path $dataRoot) { Remove-Item -LiteralPath $dataRoot -Recurse -Force }
    Write-Host "Deleted all per-user WindowsPowerUserMcp data"
    return
}

if ($DeleteLogs) {
    $path = Join-Path $dataRoot "logs"
    if (Test-Path $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}
if ($DeleteScreenshots) {
    $path = Join-Path $dataRoot "screenshots"
    if (Test-Path $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}
if ($DeleteTaskLedger) {
    $path = Join-Path $dataRoot "db"
    if (Test-Path $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}

Write-Host "Uninstall complete. User data was preserved unless explicit delete switches were supplied."
