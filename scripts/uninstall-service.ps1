param(
    [string]$ServiceName = "WindowsPowerUserMcp.Broker"
)

$ErrorActionPreference = "Stop"
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell session. No UAC bypass is attempted."
}

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne "Stopped") {
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        $service.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
    }
    sc.exe delete $ServiceName
    Write-Host "Removed service $ServiceName"
} else {
    Write-Host "Service $ServiceName is not installed"
}
