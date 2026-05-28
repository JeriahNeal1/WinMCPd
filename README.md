# WindowsPowerUserMcp

WindowsPowerUserMcp is an owner-authorized Windows 11 MCP platform for Codex. It exposes typed local Windows capabilities through a fast MCP stdio bridge while keeping durable work in a background broker and interactive desktop control in a per-user DesktopAgent.

This repository targets the newest .NET SDK installed on this machine. At creation time that is .NET SDK `10.0.300`, so projects target `net10.0` and `net10.0-windows`. To move to `net11.0` preview later, install the SDK, update `global.json`, and change target frameworks in the project files.

## Built Milestone

- `WindowsPowerUserMcp.StdioBridge`: official MCP C# SDK stdio server for Codex.
- `WindowsPowerUserMcp.BrokerService`: console/service-capable broker with named-pipe IPC, durable SQLite task ledger, JSONL audit logging, typed tool registry, command runner, tracked processes, Windows operations, and DesktopAgent delegation.
- `WindowsPowerUserMcp.DesktopAgent`: WPF tray/dashboard process for the interactive user session, with UI Automation snapshots, screenshots, clipboard, clicks, hotkeys, and simple UI plans.
- `WindowsPowerUserMcp.HttpHost`: optional localhost Streamable HTTP MCP host, disabled unless explicitly enabled.
- `WindowsPowerUserMcp.Core`, `Security`, `Orchestration`, `Windows`, `Tray`: shared contracts and subsystem libraries.
- xUnit tests for completed behavior.

## Quick Start

```powershell
dotnet build WindowsPowerUserMcp.slnx
dotnet run --project src\WindowsPowerUserMcp.BrokerService\WindowsPowerUserMcp.BrokerService.csproj
dotnet run --project src\WindowsPowerUserMcp.StdioBridge\WindowsPowerUserMcp.StdioBridge.csproj
```

For UI automation, start the DesktopAgent in the signed-in user session:

```powershell
dotnet run --project src\WindowsPowerUserMcp.DesktopAgent\WindowsPowerUserMcp.DesktopAgent.csproj
```

Storage defaults to `%LOCALAPPDATA%\WindowsPowerUserMcp`.

## Safety Boundary

This is not a hacking tool. It does not implement UAC bypass, privilege escalation exploits, credential dumping, browser password or cookie extraction, stealth persistence, security evasion, lateral movement, MFA bypass, or covert operation. Admin capabilities require the user to intentionally run or install the broker elevated.

See `SECURITY_MODEL.md`, `TOOL_REFERENCE.md`, and `INSTALL.md`.
