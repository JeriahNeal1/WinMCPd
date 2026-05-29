param(
    [string]$TaskName = "WindowsPowerUserMcp Dashboard"
)

$ErrorActionPreference = "Stop"
$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($task) {
    try {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction Stop
        Write-Host "Removed tray autostart task '$TaskName'"
    } catch {
        Write-Warning "Could not remove scheduled task '$TaskName': $($_.Exception.Message)"
    }
} else {
    Write-Host "Tray autostart task '$TaskName' is not installed"
}

$startup = [Environment]::GetFolderPath([Environment+SpecialFolder]::Startup)
$shortcutPath = Join-Path $startup "$TaskName.lnk"
if (Test-Path $shortcutPath) {
    Remove-Item -LiteralPath $shortcutPath -Force
    Write-Host "Removed Startup folder shortcut '$shortcutPath'"
}
