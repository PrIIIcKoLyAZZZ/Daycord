# Daycord Architecture

**Target version:** 0.1.1

## 1. Overview

Daycord is an offline-first desktop application built with C#,
.NET 10, Avalonia UI, and SQLite.

Its main capabilities include:

- Task management.
- Planned and actual time tracking.
- Calendar and daily activity views.
- Configurable Excel reporting.
- Application preferences and localization.
- SQLite backup, restore, and emergency recovery.

Daycord operates locally without requiring a backend server,
account, or continuous internet connection.

## 2. Architectural Principles

The application follows these principles:

1. **Local-first:** User data belongs to the local SQLite database.
2. **Separation of concerns:** Business logic, persistence, and UI
   belong to different projects.
3. **Dependency direction:** Core is independent; Infrastructure
   depends on Core; Desktop depends on both.
4. **MVVM:** Views handle presentation while ViewModels coordinate
   user interactions and application services.
5. **Dependency injection:** Services are composed through
   Microsoft.Extensions.DependencyInjection.
6. **Explicit persistence:** SQLite operations use Dapper and SQL.
7. **Data preservation:** Initialization, migrations, and recovery
   must not silently destroy user data.
8. **Reusable UI:** Shared controls and visual resources should not
   be duplicated unnecessarily.

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
│   ├── Daycord.Infrastructure/
│   │   ├── Persistence/
│   │   │   ├── Migrations/
│   │   │   └── Repositories/
│   │   └── Reports/
│   └── Daycord.Desktop/
│       ├── Bootstrap/
│       ├── Localization/
│       ├── Navigation/
│       ├── Settings/
│       ├── Styles/
│       ├── Themes/
│       ├── ViewModels/
│       └── Views/
├── tests/
├── scripts/
└── docs/
```

### 3.1 Daycord.Core

Core contains:

- Domain entities.
- Application contracts.
- Application models.
- Business services.

Important services:

- `TaskService`
- `WorkReportService`
- `WorkReportPresetService`

Core does not reference Avalonia, SQLite implementations,
or Desktop.

### 3.2 Daycord.Infrastructure

Infrastructure implements:

- SQLite repositories and connection management.
- Database initialization and migrations.
- Database change tracking.
- Backup creation and validation.
- Deferred restoration and emergency recovery.
- Excel generation through ClosedXML.

Infrastructure references Core.

### 3.3 Daycord.Desktop

Desktop contains:

- Avalonia Views and ViewModels.
- Application startup and lifecycle management.
- Navigation and keyboard interaction.
- Localization and appearance management.
- Shared calendar controls.
- Visual styles and animations.
- User-facing backup and restore workflows.
- Dependency injection registration.

Desktop references Core and Infrastructure.

## 4. Dependency Architecture

Project dependencies:

```text
              Daycord.Desktop
                /        \
               v          v
       Daycord.Core <-- Daycord.Infrastructure
```

Application services use contracts defined in Core.

Infrastructure provides implementations of those contracts.

Desktop coordinates application behavior without directly
owning persistence logic.

The dependency direction must remain unchanged.

## 5. Application Startup

The entry point is:

```text
src/Daycord.Desktop/Program.cs
```

Application startup is coordinated by `App.axaml.cs`.

Daycord uses a dedicated bootstrap window while database
initialization and recovery processing are performed.

### 5.1 Startup Sequence

```text
Program
   |
   v
Avalonia App
   |
   v
DatabaseRecoveryWindow (bootstrap)
   |
   v
ServiceRegistration
   |
   v
Acquire application instance lock
   |
   v
Check pending restore / eligible retry
   |
   v
Apply pending restore if present
   |
   v
DatabaseInitializer
   |
   v
MigrationRunner
   |
   v
Load application settings
   |
   v
Complete pending restore if required
   |
   v
Initialize automatic backup state
   |
   v
Apply language and appearance
   |
   v
MainWindow + AutomaticBackupScheduler
```

The main application window is opened only after the required
database and settings initialization succeeds.

If emergency recovery has completed, Daycord displays the
recovery-completed window with a Restart action instead of
immediately opening the main workspace.

### 5.2 Database Initialization

`DatabaseInitializer` coordinates schema initialization.

`MigrationRunner`:

- Loads embedded SQL migrations.
- Validates migration history.
- Creates a pre-upgrade backup when required.
- Applies pending migrations.
- Checks database integrity and required system data.

The currently embedded migrations are:

```text
001_InitialSchema.sql
002_ApplicationSettings.sql
```

After successful initialization, Daycord attempts to ensure
that a recent verified automatic backup exists.

A failure to create an automatic backup does not automatically
make an otherwise healthy database initialization fail.

### 5.3 Initialization Failure

Eligible SQLite corruption errors can initiate automatic
emergency recovery.

The supported primary error codes are:

- `SQLITE_CORRUPT` (11)
- `SQLITE_NOTADB` (26)

The recovery path selects a valid automatic snapshot and
attempts a protected replacement.

Recovery is not used as a universal solution for migration
checksum errors, unknown schemas, or filesystem problems.

If the database cannot be initialized or safely recovered,
normal application startup remains blocked.

Diagnostics are recorded on a best-effort basis.

## 6. Dependency Injection

Dependency injection is configured in:

```text
src/Daycord.Desktop/Bootstrap/ServiceRegistration.cs
```

The container uses `Microsoft.Extensions.DependencyInjection`.

Important registrations include:

| Service | Implementation |
|---|---|
| `IDatabaseInitializer` | `DatabaseInitializer` |
| `IUserBackupService` | `UserBackupService` |
| `ITaskRepository` | `TrackedTaskRepository` |
| `IAppSettingsRepository` | `TrackedAppSettingsRepository` |
| `IWorkReportPresetRepository` | `TrackedWorkReportPresetRepository` |
| `IWorkReportExporter` | `ClosedXmlWorkReportExporter` |
| `INavigationService` | `NavigationService` |
| `ILocalizationService` | `LocalizationService` |

The tracked repositories delegate persistence to their
underlying SQLite repositories.

They notify `DatabaseChangeTracker` after successfully
completed write operations.

The underlying implementations remain responsible for SQL
execution and transaction boundaries.

### ViewModel Factories

Task Details and Quick Task use factories:

```csharp
Func<TaskDetailsViewModel>
Func<TerminalViewModel>
```

These factories provide separate editing sessions.

Editor ViewModels must not accidentally share unsaved
editing state between unrelated tasks.

## 7. Task Management

Task operations are coordinated by `TaskService`.

The normal data flow is:

```text
View
  |
  v
ViewModel
  |
  v
TaskService
  |
  v
ITaskRepository
  |
  v
TrackedTaskRepository
  |
  v
SqliteTaskRepository
  |
  v
SQLite
```

Tasks are represented by `TaskItem`.

Important task properties include:

- Identifier and optional task number.
- Title and descriptions.
- Optional `TaskDate`.
- Status.
- Creation and modification timestamps.
- Optional completion timestamp.

### Task Statuses

Completion behavior is determined by the status's
`IsCompleted` property.

Business logic must not compare localized status names.

### Task Deletion

The repository explicitly removes related WorkEntries and
PlanningPeriods before deleting the task.

The deletion is transactional and preserves foreign key
integrity.

### Task Details

`TaskDetailsViewModel` coordinates the full task editor.

It supports task creation, editing, Markdown descriptions,
planning periods, and actual work entries.

Work-entry editing uses independent in-memory drafts.

Switching between drafts does not automatically persist them.

## 8. Planned and Actual Time

Planning periods and work entries are independent records.

| Entity | Date | Duration |
|---|---|---|
| `TaskItem` | `TaskDate` | None |
| `PlanningPeriod` | `PlannedDate` | `PlannedMinutes` |
| `WorkEntry` | `WorkDate` | `ActualMinutes` |

All three dates represent independent calendar concepts.

Planned and actual durations are stored as integer minutes.

Duration input is parsed by the shared Desktop component:

```text
ViewModels/PlannedDuration.cs
```

`MinutesPerDay` controls the interpretation of day-based
duration expressions.

Changing the setting does not recalculate saved durations.

## 9. Navigation

Navigation is coordinated through:

- `INavigationService`
- `NavigationService`
- `MainWindowViewModel`

The navigation service tracks:

- Current page.
- Currently opened task.
- Task creation state.
- One-time date-filter requests.

Task Details is opened within the task editing context.

### Task List Navigation

`TaskListViewModel` manages task selection and requests.

Opening a task can be initiated through:

- Double-click.
- Enter when the task list has appropriate focus.
- The dedicated Open Task action.

The list does not open tasks while deletion confirmation,
loading, or other blocking states make navigation unsafe.

Focus handling and keyboard interactions belong to the
presentation layer.

### Home to Today's Tasks

`NavigationService.NavigateToTasksForDate()` passes a
one-time date-filter request to the Tasks screen.

The request is consumed when the destination activates.

The task list:

- Clears its search text.
- Resets the status filter.
- Sets the date range to the requested date.
- Expands the filter controls.
- Invalidates outdated loading state.

The filter uses `TaskDate`, not planning or actual work dates.

Users can change or clear the filter afterward.

## 10. Calendar Architecture

Calendar data is retrieved through:

```text
TaskService.GetCalendarDataAsync()
```

The returned `CalendarData` contains separate collections:

- `Tasks`
- `PlannedWork`
- `ActualWork`

Each collection uses its corresponding date field.

`CalendarPageViewModel` prepares the monthly calendar page.

### Shared Date Selection

The reusable date-selection component is:

```text
Views/DaycordCalendarView.axaml
Views/DaycordCalendarView.axaml.cs
```

It provides consistent calendar presentation and date
selection across supported UI contexts.

Views that require date selection should reuse this control
rather than implement independent calendar popups.

The parent view owns the date field and integration with
the popup.

## 11. Reports Architecture

The reporting subsystem consists of:

- `ReportsViewModel`
- `WorkReportService`
- `WorkReportPresetService`
- `IWorkReportPresetRepository`
- `IWorkReportExporter`
- `ClosedXmlWorkReportExporter`

### Report Data

`WorkReportService` constructs the complete reporting model
from application data.

The model can contain:

1. Rows for planning periods.
2. Rows for actual work without matching planning.
3. Informational rows for tasks having `TaskDate` but no
   planning or actual work records.

Actual work associated with a planned task/date is aggregated
without counting the same work entry repeatedly.

TaskDate-only rows do not create fictitious time values.

### Statistics and Presets

The Reports statistics are calculated from the complete
report model.

They do not depend on the selected Excel preset.

A preset determines the columns and applicable row content
of the exported workbook.

The XLSX data-row count may therefore differ from the full
report row count.

Report rows must not be interpreted as unique task counts.

### Excel Export

The export flow is:

```text
ReportsViewModel
       |
       v
WorkReportService
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

ClosedXML creates the workbook and formats duration totals.

Built-in column headers use language-independent tokens.

Custom headings remain literal.

The exporter's selected language is captured for the
operation to avoid mixed-language report labels.

## 12. Application Settings

Application preferences are represented by `AppSettings`.

Persisted properties include:

- `Language`
- `Theme`
- `MinutesPerDay`

Persistence is provided through `IAppSettingsRepository`.

`AppSettingsService` manages the loaded settings and publishes
changes after successful persistence.

Settings are loaded after database initialization and any
pending restore processing required before initialization.

### Localization

Localization uses:

```text
Localization/Strings.resx
Localization/Strings.ru.resx
```

Supported interface languages are English and Russian.

`LocalizationService` publishes language changes.

ViewModels refresh localized properties without restarting
the application.

### Appearance

Appearance uses Avalonia's theme system.

Supported modes:

- System.
- Light.
- Dark.

Shared UI colors should use application theme resources
rather than arbitrary hard-coded values.

## 13. Shared Styles and Animations

Reusable visual resources belong in the appropriate AXAML
resource dictionaries.

Existing examples include:

```text
Styles/Sidebar.axaml
Styles/OverlayEffects.axaml
Themes/NavigationIcons.axaml
```

Their responsibilities include:

- Sidebar presentation and interactions.
- Shared overlay effects.
- Reusable navigation icon geometry.

When implementing reusable visual behavior:

1. Prefer an existing resource.
2. Keep shared styles and effects in appropriate dictionaries.
3. Avoid copying reusable animations into several Views.
4. Use theme resources for appearance-dependent values.
5. Keep view-specific behavior local when it is not reusable.

These rules describe the intended development convention.

They do not imply that every historical AXAML duplication
has already been removed.

## 14. Database Persistence and Safety

SQLite persistence is owned by Infrastructure.

`SqliteConnectionFactory` creates application connections.

Repositories use Dapper and explicit SQL.

Migration history is validated using stored SHA-256 checksums.

Existing migrations must remain immutable.

### Backup and Recovery Components

| Component | Responsibility |
|---|---|
| `DatabaseBackupService` | Pre-migration safety snapshots |
| `AutomaticBackupService` | Automatic snapshot publication and rotation |
| `DatabaseChangeTracker` | Tracking committed writes |
| `AutomaticBackupScheduler` | Scheduling backup attempts |
| `SqliteSnapshotValidator` | Snapshot integrity and compatibility |
| `AutomaticRecoveryCandidateSelector` | Recovery candidate selection |
| `PendingRestoreCoordinator` | Deferred restore coordination |
| `EmergencyRestoreInstaller` | Installation of verified emergency snapshots |
| `MigrationRunner` | Schema initialization and migration validation |

The automatic backup service retains up to three published
snapshots.

Snapshots are created through the SQLite Online Backup API.

The automatic scheduler checks for changes on navigation,
periodically during sessions, and during normal shutdown.

Backups are not guaranteed to happen immediately after every
database write.

### Restore Protection

User restoration is prepared while the application is running
and installed during a subsequent startup.

The restore coordinator uses:

- An application instance lock.
- A persistent restore journal.
- Snapshot checksums and validation.
- Original-file preservation.
- Pre-restore safety backups for normal restoration.
- Quarantine when files must be moved aside.

Emergency recovery has additional restrictions because the
original database may be physically corrupted.

It must not assume the damaged database is a valid rollback
destination.

The exact restore state machine and recovery artifacts are
documented in [Backup and Recovery](backup-and-recovery.md).

## 15. Release Architecture

Application version properties are centralized in:

```text
Directory.Build.props
```

Release packaging is configured through:

```text
Directory.Build.targets
```

Supported release targets:

- `osx-arm64`
- `win-x64`

The project provides macOS application packages, Windows
distribution packages, portable archives, and checksums
through its release workflow.

See [Releasing](releasing.md) for actual packaging commands
and platform requirements.

## 16. Architectural Constraints

When extending Daycord:

1. Preserve the Core → Infrastructure/Desktop dependency
   separation.
2. Do not place database or domain business logic in Views.
3. Reuse established application services and repositories.
4. Preserve independent task, planning, and actual work dates.
5. Reuse the shared calendar and duration parser.
6. Preserve task-editing draft behavior.
7. Keep report statistics independent of Excel presets.
8. Do not duplicate report aggregation logic.
9. Preserve localization and dynamic theme behavior.
10. Use shared AXAML resources for reusable visual elements.
11. Never modify applied SQL migrations.
12. Never delete or recreate user data as error handling.
13. Never replace the active database while ordinary
    application connections are active.
14. Preserve restore journals and recovery artifacts until
    their lifecycle permits completion.
15. Validate backup and recovery candidates before use.

## 17. Related Documentation

- [Database](database.md)
- [Backup and Recovery](backup-and-recovery.md)
- [Development](development.md)
- [User Guide](user-guide.md)
- [Testing](testing.md)
- [Releasing](releasing.md)