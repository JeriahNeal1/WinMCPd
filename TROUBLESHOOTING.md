# Troubleshooting

## Dashboard Will Not Start

Build first:

```powershell
dotnet build WindowsPowerUserMcp.slnx
dotnet run --project src\WindowsPowerUserMcp.App\WindowsPowerUserMcp.App.csproj
```

Only one dashboard instance runs at a time. If it is already running, open it from the notification area.

## Codex Says Broker Is Unavailable

Start the broker from the dashboard or run:

```powershell
dotnet run --project src\WindowsPowerUserMcp.App\WindowsPowerUserMcp.App.csproj -- --broker
```

For admin operations, start elevated from the dashboard or install the service from an elevated shell. No UAC bypass is attempted.

## UI Tools Return DesktopAgent Unavailable

Start DesktopAgent from the dashboard or run:

```powershell
dotnet run --project src\WindowsPowerUserMcp.DesktopAgent\WindowsPowerUserMcp.DesktopAgent.csproj
```

Services run in Session 0 and cannot perform interactive desktop automation directly.

## Service Is Stale Or Points To Old Path

Open Dashboard > Diagnostics. If `stale_service_path` appears, use Repair Service from an elevated dashboard session or reinstall:

```powershell
.\scripts\uninstall-service.ps1
.\scripts\install-service.ps1 -InstallRoot C:\Tools\WindowsPowerUserMcp
```

## Tray Autostart Points To Old Path

Use Dashboard > Diagnostics > Repair Tray Autostart or run:

```powershell
.\scripts\install-tray-autostart.ps1 -InstallRoot C:\Tools\WindowsPowerUserMcp
```

If scheduled-task registration says `Access is denied` from a non-elevated shell, the script creates a visible Startup-folder shortcut instead. Check:

```powershell
shell:startup
```

## Logs And Data

- Audit logs: `%LOCALAPPDATA%\WindowsPowerUserMcp\logs`
- Process logs: `%LOCALAPPDATA%\WindowsPowerUserMcp\processes`
- SQLite DB: `%LOCALAPPDATA%\WindowsPowerUserMcp\db\windows_power_user_mcp.sqlite3`
- Screenshots: `%LOCALAPPDATA%\WindowsPowerUserMcp\screenshots`
- Diagnostic bundles: `%LOCALAPPDATA%\WindowsPowerUserMcp\diagnostics`

## HTTP Host

HTTP is localhost-only and disabled unless explicitly started:

```powershell
$env:WINDOWS_POWER_USER_MCP_HTTP_TOKEN = "dev-local-token"
dotnet run --project src\WindowsPowerUserMcp.App\WindowsPowerUserMcp.App.csproj -- --http
```
