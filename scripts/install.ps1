param(
    [string]$InstallRoot = "C:\Tools\WindowsPowerUserMcp",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
New-Item -ItemType Directory -Force -Path $InstallRoot | Out-Null

dotnet publish (Join-Path $repo "src\WindowsPowerUserMcp.StdioBridge\WindowsPowerUserMcp.StdioBridge.csproj") -c $Configuration -o $InstallRoot
dotnet publish (Join-Path $repo "src\WindowsPowerUserMcp.BrokerService\WindowsPowerUserMcp.BrokerService.csproj") -c $Configuration -o $InstallRoot
dotnet publish (Join-Path $repo "src\WindowsPowerUserMcp.DesktopAgent\WindowsPowerUserMcp.DesktopAgent.csproj") -c $Configuration -o $InstallRoot
dotnet publish (Join-Path $repo "src\WindowsPowerUserMcp.HttpHost\WindowsPowerUserMcp.HttpHost.csproj") -c $Configuration -o $InstallRoot
Copy-Item (Join-Path $repo "config\appsettings.json") (Join-Path $InstallRoot "appsettings.json") -Force

Write-Host "Installed WindowsPowerUserMcp to $InstallRoot"
