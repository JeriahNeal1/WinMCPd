# Security Model

WindowsPowerUserMcp assumes the owner intentionally installed it on a personally owned Windows machine. The platform defaults to broad local capability, especially when the broker is elevated, but every operation is structured and auditable.

## Allowed By Design

- Full-drive file operations when the broker has rights.
- Shell commands, package installs, services, registry, scheduled tasks, WSL, Docker, and ADB operations.
- User-visible tray autostart and Windows Service install.
- User-confirmed app updates that preserve data by default.
- UI automation in the signed-in user session through DesktopAgent.
- OAuth/device-code/user-mediated logins where the user completes sensitive steps.

## Refused Or Paused

- UAC bypass or privilege escalation exploits.
- Credential dumping, token theft, browser password extraction, cookie extraction.
- MFA bypass.
- Stealth persistence, hiding files/processes/services, or evading security tools.
- Hidden/covert autostart or silent data deletion.
- Reading, logging, or summarizing password fields.
- Remote control of machines not explicitly configured by the owner.

## Risk Levels

Risk classification is metadata for Codex policy, audit logs, and user visibility. It does not impose artificial workspace roots. Levels are `read_only`, `low`, `medium`, `high`, `destructive`, `security_sensitive`, and `credential_sensitive`.

## Logs And Redaction

Audit logs are append-only JSONL under `%LOCALAPPDATA%\WindowsPowerUserMcp\logs` by default. Command lines and outputs are passed through secret redaction for common bearer tokens, JWT-like values, password/token assignments, cookies, API keys, and configured patterns.

## IPC

Default per-user broker runs use named pipes with `PipeOptions.CurrentUserOnly`.

For service mode under LocalSystem, `IpcCurrentUserOnly` must be set to `false` and `IpcAllowedUserSids` must contain the owner user SID(s) that are allowed to connect. The broker then creates the pipe with an explicit protected DACL that grants read/write/create-instance rights only to:

- the broker process identity, such as LocalSystem;
- configured `IpcAllowedUserSids`;
- Builtin Administrators only when `IpcAllowBuiltinAdministrators` is explicitly `true`.

The DACL does not grant `Everyone` or `Builtin Users`. Tests inspect the generated DACL to verify that world/user group access is absent.

`scripts\install-service.ps1` captures the installing user's SID by default and writes these settings to the installed `appsettings.json`. The WPF dashboard also exposes service-mode allowed SID settings.

```json
{
  "IpcCurrentUserOnly": false,
  "IpcAllowedUserSids": ["S-1-5-21-..."],
  "IpcAllowBuiltinAdministrators": false
}
```

Use a stable `IpcPipeName` shared by BrokerService, StdioBridge, and DesktopAgent. For high-trust labs with multiple owners, pass additional SIDs intentionally and keep the installed config auditable.

## Desktop App And Updates

`WindowsPowerUserMcp.App.exe` is visible desktop software. Tray autostart is a scheduled task named `WindowsPowerUserMcp Dashboard`; service mode is a Windows Service named `WindowsPowerUserMcp.Broker`. Both are removable through dashboard controls and scripts.

Updates require user confirmation. The update flow preserves task ledgers, logs, screenshots, and data roots unless the user explicitly chooses deletion during uninstall.

## Patch And Agent Import Safety

Imported agent patches are treated as untrusted. `validate_agent_output` and `import_agent_patch` scan for secret-looking material and unsafe patterns such as credential dumping, UAC bypass, browser cookie extraction, or security-tool evasion before applying a patch. Unsafe patches fail closed and return redacted findings.
