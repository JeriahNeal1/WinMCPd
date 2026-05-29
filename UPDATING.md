# Updating

Update checks use GitHub releases by default:

```text
JeriahNeal1/WinMCPd
```

The repository is configurable in app settings. The dashboard can check releases, show release notes, stage the first zip/exe asset, and apply the staged update.

## Update Flow

1. Check current version against latest release metadata.
2. Optionally include prereleases from Settings.
3. Download a release asset into `%LOCALAPPDATA%\WindowsPowerUserMcp\updates`.
4. Preserve `%LOCALAPPDATA%` and `%PROGRAMDATA%` data roots.
5. Back up existing install-root files to `%PROGRAMDATA%\WindowsPowerUserMcp\update-backups`.
6. Extract a zip asset or copy an exe asset into the install root.
7. Write updated install state.

The app never auto-installs updates without confirmation. If no GitHub release assets exist, the dashboard reports that gracefully.

Because a running executable cannot overwrite itself directly in every scenario, production update flows should launch a copied temporary app with `--apply-update` after managed services/processes have stopped.
