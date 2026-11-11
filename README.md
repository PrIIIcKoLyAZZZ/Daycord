# Daycord

**Daycord** is an offline-first desktop application for task management, time planning, work tracking, and Excel reporting.

It provides a local workspace for organizing tasks, tracking planned and actual work, reviewing daily activity, and generating customizable reports.

Built with **C# / .NET 10**, **Avalonia UI**, and **SQLite**, Daycord works without an account, server, or continuous internet connection.

**Latest published release:** [0.1.1](https://github.com/PrIIIcKoLyAZZZ/Daycord/releases/tag/0.1.1)

## Features

### Task Management

- Create, edit, and delete tasks.
- Use Quick Task for fast task creation.
- Manage tasks through the full Task Details editor.
- Assign task numbers, dates, and statuses.
- Write short descriptions and Markdown notes.
- Search, filter, and sort tasks.
- Open tasks using the mouse or keyboard.
- Navigate directly from Home to tasks scheduled for today.

### Time Planning and Tracking

- Add multiple planning periods to a task.
- Assign independent dates and durations to planning periods.
- Record actual work with descriptions and notes.
- Edit and delete individual time records.
- Switch between work-entry drafts while editing.
- Compare planned and actual time.
- Enter durations in minutes, hours, or configurable days.

### Home and Calendar

- Review today's tasks, completed tasks, planned time, and actual work.
- Navigate through a monthly calendar.
- View tasks, planning periods, and work entries by date.
- Open task details from calendar entries.
- Select dates using Daycord's integrated calendar controls.

Task dates, planning dates, and actual work dates are stored independently.

### Reports

- Generate Excel reports in XLSX format.
- Select custom reporting periods.
- Include tasks with dates even when they have no time records.
- Review report statistics independently of Excel presets.
- Create and manage custom report presets.
- Choose report columns, their order, and their headings.
- Generate localized report headings and totals.

Reports are generated locally using ClosedXML.

### Backup and Recovery

Daycord provides three data-protection mechanisms:

- **User Backup & Restore:** Create database backups and restore a selected backup through Settings.
- **Automatic Backup:** Maintain up to three verified SQLite snapshots after database changes.
- **Emergency Recovery:** Attempt safe recovery from eligible database corruption using a verified automatic backup.

Automatic backups are stored in the application's internal `Backups/Automatic` directory.

Automatic backup creation is scheduled and may not happen immediately after every change.

**Important:** Restoring a backup returns the database to the state captured in that backup. Later changes may be lost.

Automatic backups do not replace the need to maintain independent copies of important data.

See the [User Guide](docs/user-guide.md) and [Backup and Recovery](docs/backup-and-recovery.md) documentation.

### Application Settings

- English and Russian interface languages.
- System, Light, and Dark appearance modes.
- Configurable duration of one day.
- Language and appearance changes without restarting.
- Persistent application preferences.
- User-controlled database backup and restoration.

## Supported Platforms

| Platform | Architecture | Distribution |
|---|---|---|
| macOS | Apple Silicon (ARM64) | DMG, ZIP |
| Windows | x64 | Installer, portable ZIP |

Linux and other architectures are not official release targets.

Published release builds are self-contained and include the required .NET runtime components.

## Installation

Download an available package from [GitHub Releases](https://github.com/PrIIIcKoLyAZZZ/Daycord/releases).

### macOS

**DMG:**

1. Download the macOS ARM64 DMG.
2. Open the downloaded file.
3. Drag `Daycord.app` into Applications.
4. Launch Daycord from Applications.

**ZIP:**

1. Download the macOS ARM64 ZIP.
2. Extract the archive.
3. Move `Daycord.app` into Applications.
4. Launch the application.

The macOS packages are not currently Developer ID signed or notarized. macOS may display a security warning.

Only authorize an application blocked by macOS after verifying that the package came from a trusted source.

### Windows

**Installer:**

1. Download the Windows x64 installer.
2. Run the installer.
3. Follow the installation instructions.
4. Launch Daycord from the Start menu.

**Portable ZIP:**

1. Download the Windows x64 ZIP.
2. Extract the archive into a suitable folder.
3. Keep the application files together.
4. Run `Daycord.Desktop.exe`.

The Windows installer is not currently Authenticode-signed. Windows may display an unknown-publisher or SmartScreen warning.

Verify the source of the package before running it.

## Getting Started

1. Open Daycord.
2. Create a task using Quick Task or the full task editor.
3. Assign a task date and status.
4. Add planned time if needed.
5. Record actual work as it is completed.
6. Review activity on Home or Calendar.
7. Export an Excel report from Reports.
8. Create a personal backup in Settings.

For detailed instructions, see the [User Guide](docs/user-guide.md).

## Duration Format

Daycord supports duration expressions such as:

| Input | Meaning |
|---|---|
| `1` | 1 hour |
| `30m` | 30 minutes |
| `1h 30m` | 1 hour and 30 minutes |
| `2h` | 2 hours |
| `1d` | 1 configured day |
| `2d 3h` | 2 configured days and 3 hours |

By default, one day equals 24 hours.

The duration of one day can be changed in Settings.

Previously saved durations are stored as minutes and are not recalculated when this preference changes.

## Data Storage

Daycord stores application data locally in a SQLite database named `daycord.db`.

Typical locations:

**macOS:**

```text
~/Library/Application Support/Daycord/daycord.db
```

**Windows:**

```text
%LOCALAPPDATA%\Daycord\daycord.db
```

The database contains tasks, statuses, planning periods, work entries, report presets, and application settings.

Daycord initializes the database and applies supported schema migrations during startup.

**Do not delete the database or application data directory to resolve startup problems.**

### Updating Daycord

Application data is stored separately from the application executable.

When updating Daycord:

1. Create a user backup through Settings.
2. Keep a copy of the backup in a safe location.
3. Install or extract the newer application package.
4. Start Daycord and allow database initialization to complete.
5. Verify that your tasks and settings are available.

An ordinary application update does not require deleting or recreating the database.

If startup fails, preserve the application data and diagnostic information before attempting recovery.

## Building from Source

### Requirements

- .NET SDK 10.0.201, or a compatible patch version allowed by `global.json`.
- A supported desktop development environment.
- Git for cloning the repository.

Clone the repository:

```bash
git clone https://github.com/PrIIIcKoLyAZZZ/Daycord.git
cd Daycord
```

Restore dependencies:

```bash
dotnet restore Daycord.slnx
```

Build the solution:

```bash
dotnet build Daycord.slnx
```

Run the application:

```bash
dotnet run --project src/Daycord.Desktop/Daycord.Desktop.csproj
```

For platform-specific packaging instructions, see [Releasing](docs/releasing.md).

## Technology Stack

| Component | Technology |
|---|---|
| Language | C# |
| Framework | .NET 10 |
| Desktop UI | Avalonia UI |
| MVVM | CommunityToolkit.Mvvm |
| Database | SQLite |
| Data Access | Dapper |
| Excel Export | ClosedXML |
| Dependency Injection | Microsoft.Extensions.DependencyInjection |

The solution contains three main projects:

- `Daycord.Core` — domain models, contracts, and application services.
- `Daycord.Infrastructure` — persistence, reporting, backup, and recovery.
- `Daycord.Desktop` — user interface, navigation, localization, and application startup.

## Documentation

- [User Guide](docs/user-guide.md) — Everyday workflows, tasks, reports, and data recovery.
- [Architecture](docs/architecture.md) — Application structure and service responsibilities.
- [Database](docs/database.md) — SQLite persistence, schema, and migrations.
- [Backup and Recovery](docs/backup-and-recovery.md) — Data protection and recovery mechanisms.
- [Development](docs/development.md) — Development environment and implementation conventions.
- [Testing](docs/testing.md) — Automated checks and testing instructions.
- [Releasing](docs/releasing.md) — Release packaging and distribution.

## Limitations

- Official distribution targets are macOS ARM64 and Windows x64.
- Data is stored locally; built-in cloud synchronization is not available.
- Distribution packages are not currently digitally signed.
- Automatic recovery applies only to supported database corruption scenarios.
- A backup cannot guarantee preservation of changes made after its creation.

## License

See the repository for licensing information.