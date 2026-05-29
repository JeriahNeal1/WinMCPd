param(
    [string]$InstallRoot = "C:\Tools\WindowsPowerUserMcp",
    [string]$TaskName = "WindowsPowerUserMcp Dashboard"
)

$ErrorActionPreference = "Stop"
$appExe = Join-Path $InstallRoot "WindowsPowerUserMcp.App.exe"
$legacyDesktopAgentExe = Join-Path $InstallRoot "WindowsPowerUserMcp.DesktopAgent.exe"
if (Test-Path $appExe) {
    $exe = $appExe
    $argument = "--dashboard"
} elseif (Test-Path $legacyDesktopAgentExe) {
    $exe = $legacyDesktopAgentExe
    $argument = ""
} else {
    throw "Dashboard/DesktopAgent executable not found. Publish or install WindowsPowerUserMcp first."
}

$action = if ([string]::IsNullOrWhiteSpace($argument)) {
    New-ScheduledTaskAction -Execute $exe
} else {
    New-ScheduledTaskAction -Execute $exe -Argument $argument
}
$trigger = New-ScheduledTaskTrigger -AtLogOn
$identityName = [Security.Principal.WindowsIdentity]::GetCurrent().Name
if ([string]::IsNullOrWhiteSpace($identityName)) {
    $identityName = "$env:USERDOMAIN\$env:USERNAME"
}
$principal = New-ScheduledTaskPrincipal -UserId $identityName -LogonType Interactive -RunLevel Limited
try {
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Force -ErrorAction Stop | Out-Null
    $startup = [Environment]::GetFolderPath([Environment+SpecialFolder]::Startup)
    $shortcutPath = Join-Path $startup "$TaskName.lnk"
    if (Test-Path $shortcutPath) {
        Remove-Item -LiteralPath $shortcutPath -Force
        Write-Host "Removed fallback Startup folder shortcut because scheduled task registration succeeded"
    }
    Write-Host "Installed visible logon autostart task '$TaskName'"
    Write-Host "Action: $exe $argument"
} catch {
    $registeredTask = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($registeredTask) {
        Write-Warning "Scheduled task registration reported an error, but task '$TaskName' exists. Leaving scheduled task in place and skipping shortcut fallback. Error: $($_.Exception.Message)"
        Write-Host "Action: $exe $argument"
        return
    }

    $startup = [Environment]::GetFolderPath([Environment+SpecialFolder]::Startup)
    $shortcutPath = Join-Path $startup "$TaskName.lnk"
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $exe
    $shortcut.Arguments = $argument
    $shortcut.WorkingDirectory = Split-Path -Parent $exe
    $shortcut.IconLocation = "$exe,0"
    $shortcut.Description = "Starts WindowsPowerUserMcp dashboard at logon."
    $shortcut.Save()
    Write-Warning "Scheduled task registration failed: $($_.Exception.Message)"
    Write-Host "Installed visible Startup folder shortcut '$shortcutPath' instead"
    Write-Host "Action: $exe $argument"
}
