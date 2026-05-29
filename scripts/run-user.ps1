param(
    [ValidateSet("dashboard", "broker", "stdio", "desktop-agent", "http")]
    [string]$Mode = "dashboard"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo "src\WindowsPowerUserMcp.App\WindowsPowerUserMcp.App.csproj"

if ($Mode -eq "dashboard") {
    dotnet run --project $project
} else {
    dotnet run --project $project -- "--$Mode"
}
