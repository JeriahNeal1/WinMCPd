param(
    [string]$InstallRoot = "C:\Tools\WindowsPowerUserMcp",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$NoPublish,
    [switch]$InstallService,
    [switch]$InstallTrayAutostart,
    [string]$AllowedUserSid = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$publishRoot = Join-Path $repo "artifacts\publish\WindowsPowerUserMcp"

if (-not $NoPublish) {
    & "$PSScriptRoot\publish-app.ps1" -Configuration $Configuration -Runtime $Runtime -OutputPath $publishRoot
}

if (-not (Test-Path $publishRoot)) {
    throw "Publish output not found at $publishRoot. Run scripts\publish-app.ps1 first or omit -NoPublish."
}

New-Item -ItemType Directory -Force -Path $InstallRoot | Out-Null
Copy-Item (Join-Path $publishRoot "*") $InstallRoot -Recurse -Force

$app = Join-Path $InstallRoot "WindowsPowerUserMcp.App.exe"
if (Test-Path $app) {
    & $app --install --install-root $InstallRoot
}

if ($InstallService) {
    & "$PSScriptRoot\install-service.ps1" -InstallRoot $InstallRoot -AllowedUserSid $AllowedUserSid
}

if ($InstallTrayAutostart) {
    & "$PSScriptRoot\install-tray-autostart.ps1" -InstallRoot $InstallRoot
}

Write-Host "Installed WindowsPowerUserMcp app to $InstallRoot"
