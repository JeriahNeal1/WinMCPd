# App Shell

`WindowsPowerUserMcp.App.exe` is the main desktop product shell.

## UI Technology Decision

Qt 6 was considered first. It was not selected for this milestone because local Qt tools such as `qmake` and `windeployqt` were not present, and a reliable .NET 10 C# binding path would add native deployment and packaging risk. WPF was selected because it is already used in the DesktopAgent, builds cleanly on `net10.0-windows`, supports reliable tray integration through WinForms `NotifyIcon`, and works with self-contained single-file publishing.

## Dashboard

Navigation:

- Home
- Backend
- Desktop Agent
- Codex Setup
- Install & Update
- Tasks
- Logs
- Diagnostics
- Settings
- About

The Home view shows broker, DesktopAgent, Codex readiness, install/update state, version, install/data/log roots, task/process counts, and elevation state.

## Tray Behavior

The tray icon appears while the dashboard runs. Closing the window hides it to tray by default. Explicit exit is available from the dashboard Exit button or tray menu.

Autostart is visible and reversible. The installer first attempts a scheduled task named `WindowsPowerUserMcp Dashboard`. If the current Windows session is not allowed to create scheduled tasks, it falls back to a Startup-folder shortcut named `WindowsPowerUserMcp Dashboard.lnk`.

Tray menu:

- Open Dashboard
- Start Backend
- Start Backend Elevated
- Stop Backend
- Restart Backend
- Install/Update
- Open Logs
- Open Data Folder
- Diagnostics
- Exit Dashboard
- Exit and Stop Backend

On explicit exit, the app asks whether to exit the dashboard only, leave backend running, stop the user-mode broker, or stop the service when the dashboard is elevated.

## Modes

```text
WindowsPowerUserMcp.App.exe
WindowsPowerUserMcp.App.exe --dashboard
WindowsPowerUserMcp.App.exe --broker
WindowsPowerUserMcp.App.exe --broker --service
WindowsPowerUserMcp.App.exe --stdio
WindowsPowerUserMcp.App.exe --desktop-agent
WindowsPowerUserMcp.App.exe --http
WindowsPowerUserMcp.App.exe --install
WindowsPowerUserMcp.App.exe --uninstall
WindowsPowerUserMcp.App.exe --update
WindowsPowerUserMcp.App.exe --apply-update --staging <asset>
```

`--stdio` does not start the dashboard UI.
