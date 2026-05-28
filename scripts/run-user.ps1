param(
    [switch]$DesktopAgent
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
if ($DesktopAgent) {
    dotnet run --project (Join-Path $repo "src\WindowsPowerUserMcp.DesktopAgent\WindowsPowerUserMcp.DesktopAgent.csproj")
} else {
    dotnet run --project (Join-Path $repo "src\WindowsPowerUserMcp.BrokerService\WindowsPowerUserMcp.BrokerService.csproj")
}
