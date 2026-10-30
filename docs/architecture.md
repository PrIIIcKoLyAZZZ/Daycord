# Daycord Architecture

## 1. Overview

Daycord is an offline-first desktop application built with .NET 10, Avalonia UI, and SQLite.

It provides task management, planned time tracking, actual work logging, calendar views, configurable Excel reporting, and local database backup and recovery.

Version 0.1.0 uses a three-project architecture with a clear separation between application logic, infrastructure, and user interface.

The application does not require a backend server or external authentication service.

## 2. Architectural Principles

Daycord follows these principles:

1. **Local-first data ownership:** Application data resides in the user's local SQLite database.
2. **Separation of concerns:** Domain models, persistence, and presentation are implemented in separate projects.
3. **Dependency direction:** Infrastructure depends on Core; Desktop depends on Core and Infrastructure.
4. **MVVM:** Avalonia views communicate with ViewModels rather than implementing application business rules.
5. **Dependency injection:** Services and ViewModels are composed through Microsoft.Extensions.DependencyInjection.
6. **Explicit persistence:** SQLite repositories use Dapper and SQL instead of Entity Framework Core.
7. **Data preservation:** Schema upgrades and database restoration must not silently discard user data.
8. **Independent time records:** Task dates, planning dates, and actual work dates are distinct concepts.

The application operates as a single local desktop client.

## 3. Solution Structure

```text
Daycord/
├── Daycord.slnx
├── global.json
├── Directory.Build.props
├── Directory.Build.targets
├── Directory.Packages.props
├── src/
│   ├── Daycord.Core/
│   │   ├── Application/
│   │   │   ├── Contracts/
│   │   │   ├── Models/
│   │   │   └── Services/
│   │   └── Domain/
│   │       └── Entities/
│   │
│   ├── Daycord.Infrastructure/
│   │   ├── Persistence/
│   │   │   ├── Migrations/
│   │   │   └── Repositories/
│   │   └── Reports/
│   │
│   └── Daycord.Desktop/
│       ├── Assets/
│       ├── Bootstrap/
│       ├── Localization/
│       ├── Navigation/
│       ├── Packaging/
│       ├── Reports/
│       ├── Settings/
│       ├── Themes/
│       ├── ViewModels/
│       └── Views/
│
├── scripts/
└── docs/
```

## 4. Dependency Architecture

The project dependency structure is:

```text
          Daycord.Desktop
            /        \
           v          v
   Daycord.Core <-- Daycord.Infrastructure
```

`Daycord.Core` has no project dependency on Infrastructure or Desktop.

`Daycord.Infrastructure` references Core.

`Daycord.Desktop` references both Core and Infrastructure.

The dependency direction allows application services to use repository contracts without depending directly on SQLite implementations.

### 4.1 Daycord.Core

Core contains domain entities, application models, repository contracts, and business services.

Domain entities include:

- `TaskItem`
- `TaskStatusDefinition`
- `PlanningPeriod`
- `WorkEntry`

Application services include:

- `TaskService`
- `WorkReportService`
- `WorkReportPresetService`

Application contracts include:

- `ITaskRepository`
- `IDatabaseInitializer`
- `IAppSettingsRepository`
- `IUserBackupService`
- `IWorkReportExporter`
- `IWorkReportPresetRepository`

Core defines application behavior and persistence abstractions without referencing Avalonia or SQLite-specific implementation types.

### 4.2 Daycord.Infrastructure

Infrastructure provides implementations of persistence and export operations.

Major components include:

- `AppDataPaths`
- `SqliteConnectionFactory`
- `SqliteTypeHandlers`
- `DatabaseInitializer`
- `MigrationRunner`
- `DatabaseBackupService`
- `SqliteTaskRepository`
- `SqliteAppSettingsRepository`
- `SqliteWorkReportPresetRepository`
- `UserBackupService`
- `PendingRestoreCoordinator`
- `SqliteSnapshotIO`
- `SqliteSnapshotValidator`
- `ClosedXmlWorkReportExporter`

Infrastructure is responsible for communicating with SQLite, validating snapshots, applying database migrations, and creating Excel files.

### 4.3 Daycord.Desktop

Desktop is the executable application and presentation layer.

It contains:

- Avalonia Views and AXAML resources.
- ViewModels and UI state.
- Navigation services.
- Localization services.
- Application settings integration.
- Common calendar controls.
- Report editing and export interactions.
- User-facing backup and restoration workflows.
- Dependency injection registration.
- Desktop startup and window lifecycle management.

The presentation layer uses application services for task, planning, work-entry, and reporting operations.

## 5. Application Startup

The application entry point is `Program.cs`.

Avalonia initializes the `App` class, which creates the dependency injection container and the main application window.

Database initialization starts after the main window opens.

The startup sequence is:

```text
Program
   |
   v
Avalonia App
   |
   v
ServiceRegistration
   |
   v
MainWindow
   |
   v
Acquire application database lock
   |
   v
Apply pending restore, if present
   |
   v
DatabaseInitializer
   |
   v
MigrationRunner
   |
   v
Load AppSettings from SQLite
   |
   v
Apply language and appearance
   |
   v
Activate application features
```

### 5.1 Database Initialization

`DatabaseInitializer` ensures that the application data directory exists and delegates migration processing to `MigrationRunner`.

Migration processing validates previously applied versions and applies pending migrations.

The current migrations are:

- `001_InitialSchema.sql`
- `002_ApplicationSettings.sql`

These files are embedded in the Infrastructure assembly.

### 5.2 Initialization Failure

If normal database initialization fails, Daycord does not silently replace the user's database.

The application displays a database initialization failure state instead of activating normal data-dependent features.

When failure occurs after installing a pending restoration, the startup process attempts to restore the safety backup before continuing.

If automatic rollback also fails, startup remains blocked to protect the database.

## 6. Dependency Injection

Services are registered in:

`src/Daycord.Desktop/Bootstrap/ServiceRegistration.cs`

The application uses `Microsoft.Extensions.DependencyInjection`.

### 6.1 Infrastructure Services

Database paths, connection factory, migration runner, database initializer, backup services, and restore coordinator are registered through the DI container.

`IDatabaseInitializer` resolves to `DatabaseInitializer`.

`IUserBackupService` resolves to `UserBackupService`.

### 6.2 Task Services

`ITaskRepository` resolves to `SqliteTaskRepository`.

`TaskService` operates through `ITaskRepository`.

Application service consumers do not need to construct SQLite repositories directly.

### 6.3 Report Services

The reporting subsystem registers:

- `WorkReportService`
- `WorkReportPresetService`
- `IWorkReportPresetRepository`
- `IWorkReportExporter`
- `ReportsViewModel`

The exporter implementation is `ClosedXmlWorkReportExporter`.

The exporter receives access to the current application language and captures it for each export operation.

### 6.4 Desktop Services

Navigation and localization services are shared throughout the application.

Several main-screen ViewModels are registered as singletons.

Task Details and Quick Task use factories to create appropriate ViewModel instances for editing sessions.

Settings-related components are initialized only after the persisted application settings have been loaded.

### 6.5 Registration Rules

When introducing a new service:

1. Define an application contract in Core if an abstraction is required.
2. Implement infrastructure-specific behavior in Infrastructure.
3. Register the implementation in `ServiceRegistration.cs`.
4. Inject the service into consumers through constructors.
5. Select a lifetime consistent with the service's state and dependencies.
6. Avoid duplicate registrations and service-location logic inside Views.

Do not remove registrations based only on a class name suggesting legacy behavior. Verify active consumers and navigation paths first.

## 7. Task Management

`TaskService` is the main application service for task operations.

It validates input and coordinates repository operations.

The main entity is `TaskItem`.

Tasks can contain:

- A unique identifier.
- An optional task date.
- An optional task number.
- A title.
- A short description.
- A Markdown description.
- A status reference.
- Creation and modification timestamps.
- A completion timestamp.

Task statuses are stored separately in `TaskStatuses`.

Status completion semantics are determined by `IsCompleted`, not by comparing a localized status name.

### 7.1 Create Task

The normal data flow is:

```text
Quick Task / Task Details
           |
           v
       TaskService
           |
           v
     ITaskRepository
           |
           v
 SqliteTaskRepository
           |
           v
         SQLite
```

When a task is created with planned time, the service can create the task and its initial planning period together.

### 7.2 Update Task

Task editing uses application models such as `UpdateTaskRequest`.

The service validates the requested values and delegates persistence to the repository.

Task creation timestamps are distinct from update timestamps.

Task completion timestamps reflect changes to the completion state.

### 7.3 Delete Task

Task deletion is initiated by the UI and handled through `TaskService` and the repository.

Referential integrity is enforced through the database schema and repository behavior.

Dependent planning and work records must be handled consistently with the task deletion operation.

## 8. Planned Time

Planned time is represented by `PlanningPeriod`.

A planning period contains:

- Its own identifier.
- The related task identifier.
- `PlannedDate`.
- `PlannedMinutes`.

A task may contain multiple planning periods.

`TaskService` exposes operations for listing, adding, updating, and deleting planning periods.

Typical flow:

```text
Task Details
     |
     v
TaskService
     |
     v
ITaskRepository
     |
     v
PlanningPeriods
```

Planned time is stored as integer minutes.

`PlannedDate` is independent of the task's `TaskDate`.

## 9. Actual Time

Actual work is represented by `WorkEntry`.

A work entry contains:

- Its own identifier.
- The related task identifier.
- `WorkDate`.
- `ActualMinutes`.
- An optional work description.
- An optional note.

A task can contain multiple work entries, including multiple entries for the same date.

Actual work is stored separately from planned time.

Typical flow:

```text
Task Details / Work Log
           |
           v
       TaskService
           |
           v
     ITaskRepository
           |
           v
       WorkEntries
```

### 9.1 Work-Entry Drafts

Task Details supports independent in-memory drafts for work-entry editing.

Users can switch between entries without immediately persisting each change.

Unsaved edits remain part of the editing session until the user explicitly saves or discards them.

These drafts are not an autosave mechanism.

## 10. Duration Parsing

Duration parsing is centralized in:

`Daycord.Desktop.ViewModels.PlannedDuration`

The parser supports hours, minutes, days, and localized duration expressions.

Examples:

- `1` means one hour.
- `30m` means thirty minutes.
- `1h 30m` means ninety minutes.
- `1d` means one configured day.
- `2d 3h` combines days and hours.

The `MinutesPerDay` application setting controls the number of minutes represented by `d`.

The default is 1,440 minutes.

Stored durations remain integer minutes and are not retroactively recalculated when `MinutesPerDay` changes.

Do not introduce another independent duration parser for new UI components.

## 11. Calendar Architecture

The Calendar subsystem retrieves data through:

`TaskService.GetCalendarDataAsync`

The returned `CalendarData` model exposes three independent collections:

- `Tasks`
- `PlannedWork`
- `ActualWork`

Each collection uses its own date field:

| Collection | Date field |
|---|---|
| Tasks | `TaskDate` |
| PlannedWork | `PlannedDate` |
| ActualWork | `WorkDate` |

These dates must not be combined into one generic task date.

A task without `TaskDate` may still have planning periods or work entries associated with other dates.

`CalendarPageViewModel` prepares the calendar presentation.

`DaycordCalendarView` is a reusable calendar control used for date selection.

New views should reuse this component instead of duplicating its calendar implementation.

## 12. Home Dashboard

The Home dashboard presents information for the current local date.

Its ViewModel is:

`HomeDashboardViewModel`

The dashboard uses application data to display task activity and planned and actual time.

Completion counts must use the underlying status completion semantics.

The dashboard and Calendar consume task-related information without becoming owners of persistence logic.

## 13. Reports Architecture

Daycord 0.1.0 includes configurable Excel reports.

The reporting system consists of four main parts:

1. Report UI and `ReportsViewModel`.
2. `WorkReportService`.
3. `WorkReportPresetService` and preset persistence.
4. `ClosedXmlWorkReportExporter`.

### 13.1 Report Data

`WorkReportService` retrieves calendar data for the selected date range through `TaskService`.

It constructs report rows from planning periods and work entries.

Actual work is grouped by task and work date to prevent the same work entry from being counted repeatedly across multiple planning periods.

### 13.2 Report Presets

Report presets are represented by `WorkReportPreset` and related column models.

The preset service supports creating, updating, selecting, deleting, and resetting presets.

Presets are persisted through `IWorkReportPresetRepository`.

The built-in Default preset is protected from deletion.

Its standard report layout contains four columns:

1. Date.
2. Task.
3. Work performed.
4. Time.

Custom presets can modify the selected columns, their order, and their headings.

### 13.3 Excel Export

The export flow is:

```text
ReportsViewModel
       |
       v
WorkReportService
       |
       v
TaskService / CalendarData
       |
       v
WorkReport
       |
       v
IWorkReportExporter
       |
       v
ClosedXmlWorkReportExporter
       |
       v
      XLSX
```

ClosedXML creates the workbook and applies report layout, formatting, and totals.

Durations are exported using Excel-compatible time representations, including formats suitable for totals exceeding 24 hours.

The selected language is captured at the beginning of the export.

Localized default labels are resolved for that language, while user-defined headers are preserved.

## 14. Application Settings

Application settings are represented by the immutable `AppSettings` model.

Settings include:

- `Language`
- `Theme`
- `MinutesPerDay`

They are stored in the `AppSettings` SQLite table.

`IAppSettingsRepository` and `SqliteAppSettingsRepository` provide persistence.

`IAppSettingsService` and `AppSettingsService` maintain the current loaded settings and publish changes after successful saving.

The settings service is initialized after database migration and pending restoration processing.

### 14.1 Language

Supported languages are defined through `AppLanguage` and associated language-code mappings.

Version 0.1.0 supports English and Russian.

`LocalizationService` retrieves localized strings through RESX resources.

The application can update localized UI properties without restarting the process.

### 14.2 Appearance

Appearance uses Avalonia's `ThemeVariant` mechanism.

Supported values are:

- `System`
- `Light`
- `Dark`

System appearance delegates theme selection to the operating system.

### 14.3 Duration of One Day

`MinutesPerDay` controls the conversion of day-based duration input into minutes.

Valid values are between 1 and 1,440 minutes.

The default is 1,440 minutes.

Changing this setting does not modify previously stored planned or actual durations.

## 15. Backup and Recovery

Daycord uses SQLite snapshots for user backups and a deferred restoration process.

The principal components are:

- `UserBackupService`
- `SqliteSnapshotIO`
- `SqliteSnapshotValidator`
- `PendingRestoreCoordinator`
- `PendingRestoreState`

`AppDataPaths` defines internal backup and restoration directories.

### 15.1 Create Backup

The backup service creates a consistent snapshot of the working database.

It validates generated snapshots rather than assuming a copied file is valid.

A live SQLite database must not be backed up through an ordinary file copy that ignores active journal state.

### 15.2 Prepare Restore

The user selects an existing SQLite backup.

The service validates the selected database and prepares a staged copy.

The pending restoration is recorded in a manifest.

The current working database is not replaced immediately.

### 15.3 Apply Restore

The pending restoration is processed during the next application startup.

The application first acquires its database lifetime lock.

The restore coordinator validates the staged snapshot and creates a safety backup of the existing database.

The staged database is then installed before normal application services open SQLite.

The standard database initialization and settings loading sequence follows.

A successful restoration is finalized only after the restored database completes initialization.

### 15.4 Failure Handling

If the restored database fails initialization, the application attempts to roll back to the safety backup.

The restoration mechanism also maintains state for handling interrupted operations.

Safety backups are retained for recovery.

Further details are documented in `backup-and-recovery.md`.

## 16. SQLite Persistence

SQLite is the single local application database.

Database paths are resolved through `AppDataPaths`.

The default database filename is:

`daycord.db`

The database contains task information, time records, report configuration, application settings, and migration history.

### 16.1 Data Access

Repositories use Dapper together with Microsoft.Data.Sqlite.

`SqliteConnectionFactory` creates database connections and enables foreign-key enforcement.

Dapper type handlers provide explicit persistence conventions for application types.

### 16.2 Schema Migrations

`MigrationRunner` loads embedded SQL migrations.

The current migrations are:

- `001_InitialSchema.sql`
- `002_ApplicationSettings.sql`

Migration checksums are used to detect modifications of already applied migrations.

Previously released migrations must remain immutable.

New schema changes should be introduced through new, sequentially numbered migrations.

### 16.3 WAL

The initializer attempts to configure SQLite WAL mode where supported.

Correctness must not depend on WAL being available.

Backup and recovery routines must account for SQLite journal state.

## 17. Navigation

Navigation is coordinated by:

`INavigationService` and `NavigationService`.

The service tracks the current page and task editor state.

`MainWindowViewModel` selects the appropriate ViewModel for the active screen.

Navigation is event-driven through observable state rather than direct View-to-View dependencies.

The application supports navigation among Home, Calendar, Tasks, New Task, Reports, and Settings.

Task Details is presented within the active task editing context.

## 18. Localization

Localization resources are located at:

```text
src/Daycord.Desktop/Localization/
├── Strings.resx
└── Strings.ru.resx
```

The application uses resource keys to resolve interface strings.

English is the default language.

Russian resources provide the alternative locale.

ViewModels subscribe to localization changes and refresh relevant properties.

New user-facing strings should be added to the resource files rather than embedded directly into AXAML or ViewModels.

New language support should be implemented through the application language model and localization resources, without automatically introducing a database migration.

## 19. Release Architecture

Daycord uses centralized version properties and MSBuild-based release automation.

The application version is defined in:

`Directory.Build.props`

Release targets are defined in:

`Directory.Build.targets`

The release automation provides:

- macOS application bundle generation.
- macOS DMG creation.
- Windows NSIS installer generation.
- Portable ZIP archives.
- SHA-256 checksums.
- Release artifact validation.

The supported runtime identifiers are:

- `osx-arm64`
- `win-x64`

The current packaging targets execute on macOS.

Code signing and notarization are not configured in version 0.1.0.

## 20. Architectural Constraints

Future development must preserve the following constraints:

1. Do not introduce business logic directly into Avalonia Views.
2. Do not bypass `TaskService` for standard task operations.
3. Do not duplicate the shared duration parser.
4. Do not duplicate `DaycordCalendarView` unnecessarily.
5. Do not merge `TaskDate`, `PlannedDate`, and `WorkDate`.
6. Do not edit previously applied SQL migrations.
7. Do not silently delete or recreate the user's database.
8. Do not replace an active SQLite database while normal application connections are open.
9. Do not bypass the migration checksum validation.
10. Do not remove compatibility mechanisms without analyzing existing persisted data.
11. Do not introduce an independent reporting implementation when the existing report services can be extended.
12. Do not add unnecessary registrations or duplicate services to the DI container.

## 21. Further Documentation

- [Database](database.md)
- [Development](development.md)
- [User Guide](user-guide.md)
- [Releasing](releasing.md)
- [Backup and Recovery](backup-and-recovery.md)
- [Testing](testing.md)

This document describes the architecture of Daycord 0.1.0. Future architectural changes should be documented alongside their implementation.