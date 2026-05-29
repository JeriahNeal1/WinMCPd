param(
    [string]$InstallRoot = "C:\Tools\WindowsPowerUserMcp",
    [string]$ServiceName = "WindowsPowerUserMcp.Broker",
    [string]$AllowedUserSid = "",
    [string]$PipeName = "WindowsPowerUserMcp.Broker",
    [switch]$AllowBuiltinAdministrators,
    [switch]$Start
)

$ErrorActionPreference = "Stop"
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell session. No UAC bypass is attempted."
}

$appExe = Join-Path $InstallRoot "WindowsPowerUserMcp.App.exe"
$legacyBrokerExe = Join-Path $InstallRoot "WindowsPowerUserMcp.BrokerService.exe"
if (Test-Path $appExe) {
    $exe = $appExe
    $arguments = "--broker --service"
} elseif (Test-Path $legacyBrokerExe) {
    $exe = $legacyBrokerExe
    $arguments = ""
} else {
    throw "Broker executable not found. Publish or install WindowsPowerUserMcp first."
}

if ([string]::IsNullOrWhiteSpace($AllowedUserSid)) {
    $AllowedUserSid = ([Security.Principal.WindowsIdentity]::GetCurrent()).User.Value
}

$configPath = Join-Path $InstallRoot "appsettings.json"
if (Test-Path $configPath) {
    function Set-JsonProperty {
        param(
            [Parameter(Mandatory=$true)]$Object,
            [Parameter(Mandatory=$true)][string]$Name,
            [Parameter(Mandatory=$true)]$Value
        )

        if ($Object.PSObject.Properties.Name -contains $Name) {
            $Object.$Name = $Value
        } else {
            $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value
        }
    }

    $config = Get-Content -Raw $configPath | ConvertFrom-Json
    Set-JsonProperty $config "IpcPipeName" $PipeName
    Set-JsonProperty $config "IpcCurrentUserOnly" $false
    Set-JsonProperty $config "IpcAllowedUserSids" @($AllowedUserSid)
    Set-JsonProperty $config "IpcAllowBuiltinAdministrators" ([bool]$AllowBuiltinAdministrators)
    Set-JsonProperty $config "ServiceDataRoot" "%PROGRAMDATA%\WindowsPowerUserMcp"
    $config | ConvertTo-Json -Depth 20 | Set-Content -Path $configPath -Encoding UTF8
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    throw "Service $ServiceName already exists. Use the app Repair Service action or uninstall-service.ps1 first."
}

$binPath = if ([string]::IsNullOrWhiteSpace($arguments)) { "`"$exe`"" } else { "`"$exe`" $arguments" }
sc.exe create $ServiceName binPath= $binPath start= delayed-auto DisplayName= "WindowsPowerUserMcp Broker"
sc.exe failure $ServiceName actions= restart/60000/restart/60000/none/60000 reset= 86400
sc.exe description $ServiceName "Owner-authorized WindowsPowerUserMcp broker. Pipe ACL permits SID(s): $AllowedUserSid"

if ($Start) {
    sc.exe start $ServiceName
}

Write-Host "Installed service $ServiceName"
Write-Host "Binary path: $binPath"
Write-Host "Configured broker pipe '$PipeName' for allowed user SID $AllowedUserSid"
