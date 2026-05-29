# Install

## Build From Source

```powershell
dotnet build WindowsPowerUserMcp.slnx
dotnet run --project src\WindowsPowerUserMcp.App\WindowsPowerUserMcp.App.csproj
```

`scripts\run-user.ps1` is a convenience wrapper:

```powershell
.\scripts\run-user.ps1 -Mode dashboard
.\scripts\run-user.ps1 -Mode broker
.\scripts\run-user.ps1 -Mode stdio
```

## Publish App Bundle

```powershell
.\scripts\publish-app.ps1
```

The default publish is self-contained, win-x64, single-file where practical:

```powershell
dotnet publish src\WindowsPowerUserMcp.App\WindowsPowerUserMcp.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  /p:PublishSingleFile=true `
  /p:IncludeNativeLibrariesForSelfExtract=true `
  /p:EnableCompressionInSingleFile=true `
  -o artifacts\publish\WindowsPowerUserMcp
```

The script also publishes the standalone DesktopAgent, StdioBridge, BrokerService, and HttpHost executables into the same bundle for diagnostics and fallback.

## Install To `C:\Tools`

```powershell
.\scripts\install-app.ps1 -InstallRoot C:\Tools\WindowsPowerUserMcp
```

Optional visible tray autostart:

```powershell
.\scripts\install-app.ps1 -InstallTrayAutostart
```

## Install Broker Service

Run from an elevated PowerShell session:

```powershell
.\scripts\install-app.ps1 -InstallService -InstallTrayAutostart
```

The service name is `WindowsPowerUserMcp.Broker`. The service binary path is:

```text
C:\Tools\WindowsPowerUserMcp\WindowsPowerUserMcp.App.exe --broker --service
```

The service uses delayed auto-start and restart recovery actions. It does not perform desktop UI automation; the DesktopAgent must run in the signed-in user session.

## Service-Mode Pipe ACLs

When the broker runs as LocalSystem, `PipeOptions.CurrentUserOnly` is not enough for cross-session IPC. `install-service.ps1` writes explicit ACL config to the installed app settings:

```powershell
.\scripts\install-service.ps1 `
  -AllowedUserSid "S-1-5-21-..." `
  -PipeName "WindowsPowerUserMcp.Broker"
```

If `-AllowedUserSid` is omitted, the installing user's SID is used. `-AllowBuiltinAdministrators` is available for high-trust lab machines but defaults to false.

## Tray Autostart

```powershell
.\scripts\install-tray-autostart.ps1
.\scripts\uninstall-tray-autostart.ps1
```

The scheduled task is visible as `WindowsPowerUserMcp Dashboard` and launches:

```text
WindowsPowerUserMcp.App.exe --dashboard
```

On Windows configurations that deny non-elevated scheduled-task creation, the script falls back to a visible Startup-folder shortcut:

```text
%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\WindowsPowerUserMcp Dashboard.lnk
```

`uninstall-tray-autostart.ps1` removes both the scheduled task and this shortcut.

## Uninstall

```powershell
.\scripts\uninstall-app.ps1
```

User data is preserved by default. Explicit deletion switches:

```powershell
.\scripts\uninstall-app.ps1 -DeleteLogs -DeleteScreenshots -DeleteTaskLedger
.\scripts\uninstall-app.ps1 -DeleteAllUserData
```

## Data Locations

Per-user state: `%LOCALAPPDATA%\WindowsPowerUserMcp`

Service state: `%PROGRAMDATA%\WindowsPowerUserMcp`

Important subdirectories: `db`, `logs`, `tasks`, `processes`, `screenshots`, `handoffs`, and `patches`.
