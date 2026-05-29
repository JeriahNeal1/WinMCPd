param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputPath = "",
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repo "artifacts\publish\WindowsPowerUserMcp"
}

if (Test-Path $OutputPath) {
    Remove-Item -LiteralPath $OutputPath -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null

$selfContained = -not $FrameworkDependent
$singleFileProps = @(
    "/p:PublishSingleFile=true",
    "/p:IncludeNativeLibrariesForSelfExtract=true",
    "/p:EnableCompressionInSingleFile=true"
)

dotnet publish (Join-Path $repo "src\WindowsPowerUserMcp.App\WindowsPowerUserMcp.App.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained:$selfContained `
    @singleFileProps `
    -o $OutputPath

foreach ($project in @(
    "WindowsPowerUserMcp.DesktopAgent",
    "WindowsPowerUserMcp.StdioBridge",
    "WindowsPowerUserMcp.BrokerService",
    "WindowsPowerUserMcp.HttpHost"
)) {
    dotnet publish (Join-Path $repo "src\$project\$project.csproj") `
        -c $Configuration `
        -r $Runtime `
        --self-contained:$selfContained `
        @singleFileProps `
        -o $OutputPath
}

Copy-Item (Join-Path $repo "config") (Join-Path $OutputPath "config") -Recurse -Force
Copy-Item (Join-Path $repo "scripts") (Join-Path $OutputPath "scripts") -Recurse -Force
Copy-Item (Join-Path $repo "*.md") $OutputPath -Force
Copy-Item (Join-Path $repo "global.json") $OutputPath -Force

Write-Host "Published WindowsPowerUserMcp app bundle to $OutputPath"
