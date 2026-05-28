# Install

## Build

```powershell
dotnet build WindowsPowerUserMcp.slnx
```

## Publish To `C:\Tools`

```powershell
.\scripts\install.ps1 -InstallRoot C:\Tools\WindowsPowerUserMcp
```

## Run As Current User

```powershell
.\scripts\run-user.ps1
.\scripts\run-user.ps1 -DesktopAgent
```

## Run Broker Elevated

```powershell
.\scripts\run-elevated.ps1
```

This intentionally uses Windows UAC consent. It does not bypass UAC.

## Install Broker As Service

From an elevated PowerShell session:

```powershell
.\scripts\install.ps1
.\scripts\install-service.ps1
```

The service is installed as delayed auto-start with recovery restart actions. The script configures service-mode named-pipe ACLs by writing the installing user's SID into `C:\Tools\WindowsPowerUserMcp\appsettings.json`.

Useful options:

```powershell
.\scripts\install-service.ps1 `
  -AllowedUserSid "S-1-5-21-..." `
  -PipeName "WindowsPowerUserMcp.Broker" `
  -Start
```

Use `-AllowBuiltinAdministrators` only when you intentionally want all local administrators to connect to the broker pipe. Without it, only the broker identity and configured owner SID(s) can connect.

## Install Tray Autostart

```powershell
.\scripts\install-tray-autostart.ps1
```

This creates a visible scheduled task at user logon. Remove it with:

```powershell
.\scripts\uninstall-tray-autostart.ps1
```

## Uninstall

```powershell
.\scripts\uninstall.ps1
```

## Data Locations

Per-user runs store state under `%LOCALAPPDATA%\WindowsPowerUserMcp`. Service runs use `%PROGRAMDATA%\WindowsPowerUserMcp` unless `ServiceDataRoot` is configured. Process logs live under `processes`, audit JSONL under `logs`, SQLite under `db`, screenshots under `screenshots`, handoffs under `handoffs`, and patch/ACL backups under `patches`.
