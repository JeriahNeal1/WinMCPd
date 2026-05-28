# Security Model

WindowsPowerUserMcp assumes the owner intentionally installed it on a personally owned Windows machine. The platform defaults to broad local capability, especially when the broker is elevated, but every operation is structured and auditable.

## Allowed By Design

- Full-drive file operations when the broker has rights.
- Shell commands, package installs, services, registry, scheduled tasks, WSL, Docker, and ADB operations.
- User-visible tray autostart and Windows Service install.
- UI automation in the signed-in user session through DesktopAgent.
- OAuth/device-code/user-mediated logins where the user completes sensitive steps.

## Refused Or Paused

- UAC bypass or privilege escalation exploits.
- Credential dumping, token theft, browser password extraction, cookie extraction.
- MFA bypass.
- Stealth persistence, hiding files/processes/services, or evading security tools.
- Reading, logging, or summarizing password fields.
- Remote control of machines not explicitly configured by the owner.

## Risk Levels

Risk classification is metadata for Codex policy, audit logs, and user visibility. It does not impose artificial workspace roots. Levels are `read_only`, `low`, `medium`, `high`, `destructive`, `security_sensitive`, and `credential_sensitive`.

## Logs And Redaction

Audit logs are append-only JSONL under `%LOCALAPPDATA%\WindowsPowerUserMcp\logs` by default. Command lines and outputs are passed through secret redaction for common bearer tokens, JWT-like values, password/token assignments, cookies, API keys, and configured patterns.

## IPC

The current milestone uses named pipes with `PipeOptions.CurrentUserOnly` for local per-user security. Service-to-user cross-session ACL hardening is documented as next work before production service deployment under LocalSystem.
