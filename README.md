# WindowsPowerUserMcp

WindowsPowerUserMcp is an owner-authorized Windows 11 MCP platform for Codex. It exposes typed local Windows capabilities through a fast MCP stdio bridge, keeps durable task/process state in a broker, and performs interactive desktop control through a user-session DesktopAgent.

Current target is .NET SDK `10.0.300`, so projects target `net10.0` and `net10.0-windows`. Do not retarget this milestone to .NET 11; when a .NET 11 preview SDK is installed later, update `global.json`, project target frameworks, and rerun the full test/publish pass.

## Main App

The primary user-facing executable is now:

```powershell
src\WindowsPowerUserMcp.App\WindowsPowerUserMcp.App.csproj
```

`WindowsPowerUserMcp.App.exe` supports:

- default or `--dashboard`: polished dashboard plus tray icon.
- `--broker` and `--broker --service`: broker host and Windows Service host mode.
- `--stdio`: fast Codex MCP stdio mode.
- `--desktop-agent`: launch the companion DesktopAgent.
- `--http`: localhost HTTP MCP host.
- `--install`, `--uninstall`, `--update`, `--apply-update`: command-line release operations.

Qt 6 was audited for this milestone. It was not selected because Qt tooling was not installed on this machine and a reliable .NET 10 C# binding path would add native deployment fragility. The app shell uses WPF for stable Windows 11 desktop UX, tray behavior, self-contained publish support, and reuse of the existing DesktopAgent foundation.

## Quick Start

```powershell
dotnet build WindowsPowerUserMcp.slnx
dotnet run --project src\WindowsPowerUserMcp.App\WindowsPowerUserMcp.App.csproj
```

Start broker mode from source:

```powershell
dotnet run --project src\WindowsPowerUserMcp.App\WindowsPowerUserMcp.App.csproj -- --broker
```

Use App stdio mode from Codex:

```toml
[mcp_servers.windows_power_user]
command = "dotnet"
args = [
  "run",
  "--project",
  "C:\\Users\\Natal\\OneDrive\\Documents\\WinMCPd\\src\\WindowsPowerUserMcp.App\\WindowsPowerUserMcp.App.csproj",
  "--",
  "--stdio"
]
startup_timeout_sec = 30
tool_timeout_sec = 300
default_tools_approval_mode = "prompt"
```

## Publish

```powershell
.\scripts\publish-app.ps1
```

Default output:

```text
artifacts\publish\WindowsPowerUserMcp
```

Install to `C:\Tools\WindowsPowerUserMcp`:

```powershell
.\scripts\install-app.ps1 -InstallTrayAutostart
```

Install the service from an elevated PowerShell session:

```powershell
.\scripts\install-app.ps1 -InstallService -InstallTrayAutostart
```

## Safety Boundary

This is not a hacking tool. It does not implement UAC bypass, privilege escalation exploits, credential dumping, browser password or cookie extraction, MFA bypass, stealth persistence, hiding processes/files/services, security-tool evasion, lateral movement, or covert operation. Admin capabilities require intentional elevation or service installation.

See `APP_SHELL.md`, `INSTALL.md`, `SECURITY_MODEL.md`, `CODEX_CONFIG.md`, and `TOOL_REFERENCE.md`.
