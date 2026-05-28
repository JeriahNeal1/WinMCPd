param(
    [string]$Project = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Project)) {
    $Project = Join-Path $repo "src\WindowsPowerUserMcp.BrokerService\WindowsPowerUserMcp.BrokerService.csproj"
}

Start-Process -FilePath "powershell.exe" -Verb RunAs -ArgumentList @(
    "-NoProfile",
    "-ExecutionPolicy", "RemoteSigned",
    "-Command",
    "dotnet run --project `"$Project`""
)
