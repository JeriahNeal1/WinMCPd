param(
    [string]$InstallRoot = "C:\Tools\WindowsPowerUserMcp",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$InstallService,
    [switch]$InstallTrayAutostart,
    [string]$AllowedUserSid = ""
)

$ErrorActionPreference = "Stop"
& "$PSScriptRoot\install-app.ps1" `
    -InstallRoot $InstallRoot `
    -Configuration $Configuration `
    -Runtime $Runtime `
    -InstallService:$InstallService `
    -InstallTrayAutostart:$InstallTrayAutostart `
    -AllowedUserSid $AllowedUserSid
