param(
    [string]$InstallRoot = "C:\Tools\WindowsPowerUserMcp",
    [switch]$DeleteLogs,
    [switch]$DeleteScreenshots,
    [switch]$DeleteTaskLedger,
    [switch]$DeleteAllUserData
)

$ErrorActionPreference = "Stop"
& "$PSScriptRoot\uninstall-app.ps1" `
    -InstallRoot $InstallRoot `
    -DeleteLogs:$DeleteLogs `
    -DeleteScreenshots:$DeleteScreenshots `
    -DeleteTaskLedger:$DeleteTaskLedger `
    -DeleteAllUserData:$DeleteAllUserData
