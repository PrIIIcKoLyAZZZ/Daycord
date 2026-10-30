# Daycord

**Daycord** is an offline-first desktop application for task management, time planning, work tracking, and configurable Excel reporting.

It provides a local workspace for organizing tasks, tracking planned and actual work, reviewing daily activity, and exporting reports.

Daycord is built with C# and .NET 10 using Avalonia UI and SQLite.

The application does not require a user account, a server, or a continuous internet connection.

**Current version:** 0.1.0 (MVP)

## Features

### Task Management

- Create, edit, and delete tasks.
- Quickly create tasks using Quick Task.
- Create and edit tasks in the full Task Details editor.
- Assign task numbers and dates.
- Add short descriptions and Markdown content.
- Preview Markdown descriptions.
- Manage task completion states.
- Search, filter, and sort tasks.
- View planned and actual time for each task.

### Time Planning

- Add multiple planning periods to a task.
- Assign an independent date to each planning period.
- Edit and delete planning periods.
- View total planned time.
- Enter durations using hours, minutes, and days.

### Work Tracking

- Record actual work performed on tasks.
- Add multiple work entries.
- Assign independent dates to work entries.
- Record work descriptions and notes.
- Edit and delete work entries.
- Switch between work-entry drafts without immediately saving them.
- View total actual time.

### Calendar and Dashboard

- View a monthly calendar.
- Navigate between months and years.
- View task dates, planned time, and actual work.
- Inspect activity for a selected day.
- Open tasks directly from calendar entries.
- Review today's tasks and time totals on the Home dashboard.

### Excel Reports

- Generate XLSX reports for a selected date range.
- Export data spanning multiple months.
- Include planned and actual work information.
- Customize report columns and their order.
- Customize column headers.
- Create, rename, select, and delete custom report presets.
- Restore the built-in report configuration.
- Generate localized report headings and totals.

Reports are generated locally using ClosedXML.

### Backup and Restore

- Create SQLite database backups.
- Validate a backup before restoring it.
- Prepare a restoration from a selected backup file.
- Apply restoration during the next application startup.
- Preserve a safety backup of the previous database.
- Store tasks, time records, report presets, and application settings in the database.

### Application Settings

- English and Russian interface languages.
- System, Light, and Dark appearance modes.
- Configurable duration of one day.
- Persistent settings stored in SQLite.
- Language and appearance changes without restarting the application.

## Supported Platforms

Daycord 0.1.0 targets:

| Platform | Architecture | Distribution |
|---|---|---|
| macOS | Apple Silicon (ARM64) | DMG, ZIP |
| Windows | x64 | Setup.exe, portable ZIP |

Linux, iOS, Android, and other architectures are not official release targets for version 0.1.0.

Release builds are self-contained and include the required .NET runtime components.

## Installation

Obtain a release artifact from the project owner or an authorized distribution location.

### macOS

#### DMG Installation

1. Open `Daycord-0.1.0-osx-arm64.dmg`.
2. Move `Daycord.app` into the Applications folder.
3. Open Daycord from Applications or Spotlight.

#### ZIP Installation

1. Extract `Daycord-0.1.0-osx-arm64.zip`.
2. Move `Daycord.app` into the Applications folder.
3. Launch the application.

The macOS release is intended for Apple Silicon devices.

**Security notice:** Version 0.1.0 is not signed with an Apple Developer ID certificate and has not been notarized by Apple. macOS may display a security warning or block the application. Review the source and origin of the distribution before deciding whether to open it.

### Windows

#### Installer

1. Run `Daycord-Setup-0.1.0-win-x64.exe`.
2. Approve the administrator permission request if you trust the installer.
3. Complete the installation.
4. Launch Daycord from the Start menu.

The installer uses a machine-wide installation under Program Files.

An uninstaller is registered with Windows Installed Apps.

#### Portable ZIP

1. Extract `Daycord-0.1.0-win-x64.zip` into a suitable folder.
2. Keep the extracted application files together.
3. Run `Daycord.Desktop.exe`.

The portable distribution does not require running the installer.

**Security notice:** The Windows installer is not Authenticode-signed. Windows may display an unknown-publisher or SmartScreen warning. Verify the source of the release before running it.

## Getting Started

### 1. Launch Daycord

On the first launch, Daycord creates its local SQLite database and initializes the application.

The default interface language is English, and the default appearance follows the operating system.

### 2. Create a Task

Use Quick Task for fast task creation or open the full task editor to enter additional details.

You can specify a title, date, task number, descriptions, and task status as supported by the editor.

### 3. Plan Your Work

Open Task Details and add planned time.

A task may contain multiple planning periods with different dates.

### 4. Record Actual Work

Add work entries to record time spent on a task.

Each entry has its own work date and may include a description and note.

### 5. Review Your Activity

Use Home to see today's activity or Calendar to inspect tasks and time records for specific dates.

### 6. Export an Excel Report

Open Reports, select the reporting period and desired report preset, and export an XLSX file.

The built-in report provides a standard layout. Custom presets allow changes to report columns.

### 7. Create a Backup

Open Settings and use Backup & Restore to save a copy of your application data.

Keep backups in a safe location, preferably separate from your working computer.

For detailed instructions, see [User Guide](docs/user-guide.md).

## Duration Format

Daycord accepts duration expressions such as:

| Input | Meaning |
|---|---|
| `1` | 1 hour |
| `30m` | 30 minutes |
| `1h 30m` | 1 hour and 30 minutes |
| `2h` | 2 hours |
| `1d` | 1 configured day |
| `2d 3h` | 2 configured days and 3 hours |

By default, one day equals 24 hours (1,440 minutes).

The day length can be changed in Settings.

Previously saved durations remain stored as minutes and are not recalculated when this preference changes.

## Building from Source

### Prerequisites

- .NET SDK 10.0.201 or a compatible later patch version in the same SDK feature band.
- Git, if cloning the source repository.
- A supported desktop operating system.
- JetBrains Rider or another compatible .NET development environment (optional).

The required SDK version and roll-forward policy are defined in `global.json`.

### Restore Dependencies

From the repository root:

```bash
dotnet restore Daycord.slnx
```

### Debug Build

```bash
dotnet build Daycord.slnx -c Debug
```

### Release Build

```bash
dotnet build Daycord.slnx -c Release
```

### Run the Application

```bash
dotnet run --project src/Daycord.Desktop/Daycord.Desktop.csproj
```

### Publish for macOS ARM64

```bash
dotnet publish src/Daycord.Desktop/Daycord.Desktop.csproj \
  -c Release \
  -r osx-arm64 \
  --self-contained true
```

### Publish for Windows x64

```bash
dotnet publish src/Daycord.Desktop/Daycord.Desktop.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true
```

The current release-packaging automation runs on macOS and is integrated into the MSBuild publish process.

Publishing the macOS target creates the application bundle and release archives. Publishing the Windows target invokes the NSIS packaging workflow.

Creating the Windows installer requires the NSIS compiler (`makensis`).

Detailed prerequisites and artifact verification procedures are documented in [Releasing](docs/releasing.md).

## Technology Stack

| Component | Technology |
|---|---|
| Language | C# |
| Framework | .NET 10 |
| Desktop UI | Avalonia UI 12 |
| MVVM | CommunityToolkit.Mvvm |
| Dependency Injection | Microsoft.Extensions.DependencyInjection |
| Local Database | SQLite |
| Data Access | Dapper |
| SQLite Provider | Microsoft.Data.Sqlite |
| Excel Export | ClosedXML |
| Database Migrations | Embedded versioned SQL |
| macOS Packaging | MSBuild, shell scripts, DMG |
| Windows Packaging | MSBuild, NSIS |

The application does not use Entity Framework Core.

Dependencies are managed centrally through `Directory.Packages.props`.

## Project Structure

The solution contains three main projects:

```text
Daycord/
├── Daycord.slnx
├── global.json
├── Directory.Build.props
├── Directory.Build.targets
├── Directory.Packages.props
├── README.md
├── docs/
├── scripts/
└── src/
    ├── Daycord.Core/
    ├── Daycord.Infrastructure/
    └── Daycord.Desktop/
```

### Daycord.Core

Contains the domain entities, application contracts, models, and business services.

Core does not depend on Desktop or Infrastructure.

### Daycord.Infrastructure

Implements SQLite persistence, repositories, migrations, backup and restore operations, and Excel export.

Infrastructure depends on Core.

### Daycord.Desktop

Contains Avalonia views, ViewModels, navigation, localization, themes, user settings, and dependency injection configuration.

Desktop depends on Core and Infrastructure.

For more information, see [Architecture](docs/architecture.md).

## Data Storage

Daycord stores application data locally in a SQLite database named `daycord.db`.

The location is determined by `Environment.SpecialFolder.LocalApplicationData`.

Typical locations:

**macOS**

```text
~/Library/Application Support/Daycord/daycord.db
```

**Windows**

```text
%LOCALAPPDATA%\Daycord\daycord.db
```

The database contains tasks, task statuses, planned time, actual work, report presets, report settings, and application preferences.

The application initializes the database automatically.

Database migrations are applied during startup.

The current migration files are:

```text
001_InitialSchema.sql
002_ApplicationSettings.sql
```

Previously applied migrations are protected by SHA-256 checksum validation.

Do not edit the database manually unless you understand its schema and persistence conventions.

Do not delete `daycord.db` as a troubleshooting step.

Additional technical details are available in [Database](docs/database.md).

## Backup and Restore

Backups can be created from the application's Settings section.

A backup contains a consistent SQLite snapshot of the application database.

Restoration follows a deferred process:

1. Select a backup file.
2. Allow Daycord to validate it.
3. Confirm the restoration.
4. Close Daycord.
5. Launch Daycord again.
6. Daycord applies the pending restoration before normal database initialization.

Restoration replaces the current application data with the backed-up state.

Daycord creates a safety backup before replacing the working database. However, users should maintain their own independent backups.

Do not modify the database or restore staging files while a restoration is pending.

See [Backup and Recovery](docs/backup-and-recovery.md) for implementation details.

## Documentation

The following documents describe Daycord 0.1.0:

| Document | Description |
|---|---|
| [Architecture](docs/architecture.md) | Application layers, services, and data flows |
| [Database](docs/database.md) | SQLite schema, persistence, and migrations |
| [Development](docs/development.md) | Developer setup and implementation conventions |
| [User Guide](docs/user-guide.md) | End-user workflows and features |
| [Releasing](docs/releasing.md) | Release builds, installation packages, and verification |
| [Backup and Recovery](docs/backup-and-recovery.md) | Backup and restoration internals |
| [Testing](docs/testing.md) | QA procedures and verification records |

## Known Limitations

Daycord 0.1.0 is an MVP with the following release limitations:

- Official release targets are limited to macOS ARM64 and Windows x64.
- Application data is stored locally; built-in cloud synchronization is not provided.
- macOS packages are not signed or notarized.
- Windows installers are not digitally signed.
- Cross-version application upgrade scenarios have not been fully verified.
- Some extended recovery and failure scenarios have not been independently verified.
- Known defects from MVP testing are tracked separately and may remain unresolved in version 0.1.0.

For detailed verification status, see [Testing](docs/testing.md).

## License

As of version 0.1.0, this repository does not include a `LICENSE` file.

No open-source license or redistribution permission is granted by this README.

Contact the project owner for licensing and distribution permissions.