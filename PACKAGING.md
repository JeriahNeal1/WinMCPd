# Packaging

Publish the app bundle:

```powershell
.\scripts\publish-app.ps1
```

Default output:

```text
artifacts\publish\WindowsPowerUserMcp
```

The publish script creates win-x64 self-contained single-file outputs where practical for:

- `WindowsPowerUserMcp.App`
- `WindowsPowerUserMcp.DesktopAgent`
- `WindowsPowerUserMcp.StdioBridge`
- `WindowsPowerUserMcp.BrokerService`
- `WindowsPowerUserMcp.HttpHost`

It also copies `config`, `scripts`, Markdown docs, and `global.json`.

Install the bundle:

```powershell
.\scripts\install-app.ps1 -InstallRoot C:\Tools\WindowsPowerUserMcp
```

The installed Codex command should point to:

```text
C:\Tools\WindowsPowerUserMcp\WindowsPowerUserMcp.App.exe --stdio
```
