# NestVault — Windows Client

Native Windows client for the [NestVault](https://github.com/vcmilani/NestVault) self-hosted backup server, built with WinUI 3 and C# 12 / .NET 8.

---

## Requirements

| Item | Minimum |
|------|---------|
| Windows | 10 version 1903 (build 18362) |
| .NET | 8.0 SDK |
| Windows App SDK | 1.5 |
| Server | backup_files v2.6+ |

---

## Project Structure

```
NestVault_Windows/
├── NestVault_Windows.sln
└── NestVault_Windows/
    ├── NestVault_Windows.csproj   # net8.0-windows10.0.19041.0, WinUI 3
    ├── App.xaml + .xaml.cs        # Tray icon (H.NotifyIcon), static services
    ├── MainWindow.xaml + .xaml.cs # NavigationView + Frame navigation
    ├── app.manifest
    ├── Models/
    │   └── Models.cs              # All data models (API contract + BackupSchedule)
    ├── Services/
    │   ├── APIService.cs          # Network layer
    │   ├── ConfigStore.cs         # Local profile persistence (AppData JSON)
    │   ├── BackupRunner.cs        # Single backup engine
    │   ├── BackupQueue.cs         # Sequential queue engine
    │   ├── ScheduleManager.cs     # Timer-based scheduler
    │   ├── PowerMonitor.cs        # Battery + network (Windows.Devices.Power)
    │   ├── StartupManager.cs      # Auto-start via Registry HKCU\Run
    │   ├── ToastHelper.cs         # Toasts via AppNotifications (WinRT)
    │   └── TaskbarProgressHelper.cs  # Taskbar progress via ITaskbarList3
    ├── ViewModels/
    │   ├── DashboardViewModel.cs
    │   ├── BackupsViewModel.cs
    │   ├── BackupConfigsViewModel.cs
    │   ├── CleanupViewModel.cs
    │   ├── SettingsViewModel.cs
    │   ├── BackupRunnerViewModel.cs
    │   └── BackupQueueViewModel.cs
    ├── Views/
    │   ├── Converters.cs
    │   ├── AppResources.xaml
    │   ├── DashboardPage.xaml + .xaml.cs
    │   ├── BackupsPage.xaml + .xaml.cs
    │   ├── BackupConfigsPage.xaml + .xaml.cs
    │   ├── CleanupPage.xaml + .xaml.cs
    │   ├── SettingsPage.xaml + .xaml.cs
    │   ├── BackupRunnerDialog.xaml + .xaml.cs
    │   ├── BackupQueueDialog.xaml + .xaml.cs
    │   └── Controls/
    │       └── StatCard.xaml + .xaml.cs
    ├── Strings/
    │   ├── en-US/Resources.resw
    │   └── pt-BR/Resources.resw
    └── Assets/
        ├── app.ico    # Multi-size icon (16–256px)
        ├── app.png    # 256×256 PNG
        ├── tray.ico   # Tray icon (16/32px)
        └── tray.png   # 32×32 PNG
```

---

## Setup

### Development (Windows VM)

```powershell
# Install .NET 8 SDK + Windows App SDK workload
winget install Microsoft.DotNet.SDK.8
dotnet workload install windows

# Build and run
dotnet build NestVault_Windows/NestVault_Windows.csproj
dotnet run  --project NestVault_Windows/NestVault_Windows.csproj
```

### VS Code

Open the project folder in VS Code. The `.vscode/` directory contains pre-configured `tasks.json` (build / run / publish) and `launch.json` (debug). Recommended extensions are listed in `.vscode/extensions.json` (C# Dev Kit).

### Publish (self-contained)

```powershell
dotnet publish NestVault_Windows/NestVault_Windows.csproj \
  -c Release -r win-x64 --self-contained true \
  -o publish/win-x64
```

---

## Features

### Dashboard
- Cards: total backups, versions, files, and storage
- List of active backups with size and last version date
- Alert banner when the server is unreachable

### Backups (Server Browser)
- 3-panel layout: backups → versions → files
- Filter by backup label and file name
- File table with SHA-256, status, size
- Delete individual version via context menu

### My Backups (Local Profiles)
- Create and manage local backup profiles
- Each profile: name, label, source folder, server override, workers, prefix, excludes, schedule
- Native folder picker (`FolderPicker` via WinRT)
- 4-tab editor: General / Server / Schedule / Exclusions
- Run individual backup (dialog with live log)
- Run queue with selection UI and per-item progress
- Delete backup from server

### Smart Skip
- Optional per-profile toggle: skip if no local changes detected
- Compares local file tree against hash cache (`mtime` + `size`) before running
- If 0 files changed: calls `POST /absorb` instead of uploading — no network traffic
- 1-week safety override: forces a full backup if the last full run was more than 7 days ago

### Scheduling
- 5 modes: Disabled / Hourly / Daily / Weekly / Custom (minutes)
- Daily and Weekly respect a configured time-of-day
- Checks every 30s; respects network, battery state, and active backup lock

### Cleanup
- Mode: all backups or specific label
- Keep N most recent versions (default: 5)
- Per-label preview before executing
- Mandatory confirmation dialog

### System Tray
- Minimize to tray on close (`H.NotifyIcon.WinUI`)
- Context menu: Open NestVault / Quit
- Toast notifications via Windows App Notifications after each backup

### Settings
- **General:** startup toggle (Registry `HKCU\Run`), network status, battery/power source
- **Server:** URL and API Key, test connection
- **Queue Schedule:** schedule settings
- **About:** version, links

---

## Platform-Specific Implementation

| Feature | Implementation |
|---------|---------------|
| Tray icon | `H.NotifyIcon.WinUI` 2.1.0 |
| Notifications | `Microsoft.Windows.AppNotifications` (WinRT) |
| Taskbar progress | `ITaskbarList3` (COM, `SetProgressValue`) |
| Auto-start | Registry `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` |
| Battery | `Windows.Devices.Power.Battery.AggregateBattery` |
| Network | `NetworkInterface.GetAllNetworkInterfaces()` |
| Folder picker | `FolderPicker` + `WinRT.Interop.InitializeWithWindow` |
| UI dispatch | `DispatcherQueue.TryEnqueue` |

---

## API Contract (v2.6)

| Method | Endpoint | Usage |
|--------|----------|-------|
| `GET` | `/health` | Check connection |
| `GET` | `/backups` | List backups |
| `GET` | `/backups/{label}/versions` | List versions |
| `GET` | `/files?backup_label=&version_key=` | List files |
| `POST` | `/backups` | Create backup |
| `POST` | `/backups/{label}/versions` | Create version |
| `POST` | `/check/batch` | Check up to 100 files |
| `POST` | `/upload` | Upload file (binary or header-only) |
| `POST` | `/sync` | Mark absent files as deleted |
| `PATCH` | `/backups/{label}/versions/{key}` | Finalize version |
| `POST` | `/backups/{label}/cleanup` | Remove old versions |
| `DELETE` | `/backups/{label}/versions/{key}` | Delete version |
| `DELETE` | `/backups/{label}` | Delete entire backup |

---

## Local Persistence

Profiles and settings are stored as JSON in `%APPDATA%\NestVault\config.json` via `ConfigStore`.

---

## Localization

Supported languages: **English** (default) and **Brazilian Portuguese**. The app uses the system language automatically via `.resw` resource files.

---

## Server Quick Start

```bash
cd backup_files/server
python3 -m venv .venv && source .venv/bin/activate
pip install -r requirements.txt

export BACKUP_API_KEY="your-key"
export STORAGE_DIR="/mnt/external/backups"

uvicorn main:app --host 0.0.0.0 --port 8000
```

Swagger UI: `http://<server-ip>:8000/docs`
