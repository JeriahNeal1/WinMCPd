# Troubleshooting

## StdioBridge Says Broker Is Unavailable

Start the broker:

```powershell
dotnet run --project src\WindowsPowerUserMcp.BrokerService\WindowsPowerUserMcp.BrokerService.csproj
```

If you need admin operations, start it elevated with `scripts\run-elevated.ps1` or install the service from an elevated shell.

## UI Tools Return DesktopAgent Unavailable

Start the DesktopAgent in the signed-in user session:

```powershell
dotnet run --project src\WindowsPowerUserMcp.DesktopAgent\WindowsPowerUserMcp.DesktopAgent.csproj
```

Services cannot perform interactive desktop automation directly.

## Logs

- Audit logs: `%LOCALAPPDATA%\WindowsPowerUserMcp\logs`
- Process logs: `%LOCALAPPDATA%\WindowsPowerUserMcp\processes`
- SQLite DB: `%LOCALAPPDATA%\WindowsPowerUserMcp\db\windows_power_user_mcp.sqlite3`
- Screenshots: `%LOCALAPPDATA%\WindowsPowerUserMcp\screenshots`

## HTTP Host

HTTP is localhost-only and disabled by default. Start with:

```powershell
$env:WINDOWS_POWER_USER_MCP_HTTP_TOKEN = "dev-local-token"
dotnet run --project src\WindowsPowerUserMcp.HttpHost\WindowsPowerUserMcp.HttpHost.csproj -- --enable-http
```
