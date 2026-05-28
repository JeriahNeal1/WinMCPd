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

The service is installed as delayed auto-start with recovery restart actions.

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
