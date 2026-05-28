param(
    [string]$InstallRoot = "C:\Tools\WindowsPowerUserMcp",
    [string]$ServiceName = "WindowsPowerUserMcpBroker"
)

$ErrorActionPreference = "Stop"
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell session. No UAC bypass is attempted."
}

$exe = Join-Path $InstallRoot "WindowsPowerUserMcp.BrokerService.exe"
if (-not (Test-Path $exe)) {
    throw "Broker executable not found at $exe. Run scripts\install.ps1 first."
}

sc.exe create $ServiceName binPath= "`"$exe`"" start= delayed-auto DisplayName= "WindowsPowerUserMcp Broker"
sc.exe failure $ServiceName actions= restart/60000/restart/60000/none/60000 reset= 86400
Write-Host "Installed service $ServiceName"
