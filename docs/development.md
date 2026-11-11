# Daycord Developer Guide

**Target version:** 0.1.1

## 1. Introduction

This guide describes how to build, run, debug, and extend Daycord.

Daycord is an offline-first desktop application using:

- C# and .NET 10.
- Avalonia UI.
- CommunityToolkit.Mvvm.
- SQLite and Dapper.
- ClosedXML.
- Microsoft.Extensions.DependencyInjection.

The application supports macOS ARM64 and Windows x64.

For architectural responsibilities, see [Architecture](architecture.md).

## 2. Development Environment

### Prerequisites

Required:

- .NET SDK compatible with `global.json`.
- Git.
- macOS or Windows.

Recommended:

- JetBrains Rider.
- SQLite CLI or DataGrip.

The repository specifies:

```json
{
  "sdk": {
    "version": "10.0.201",
    "rollForward": "latestPatch",
    "allowPrerelease": false
  }
}
```

Verify the installed SDK:

```bash
dotnet --info
```

### JetBrains Rider

Open:

```text
Daycord.slnx
```

Use Debug for everyday development and debugging.

Use Release when validating production configuration
or preparing distribution artifacts.

## 3. Repository Structure

The solution contains three application projects:

| Project | Responsibility |
|---|---|
| `Daycord.Core` | Domain models, contracts, application services |
| `Daycord.Infrastructure` | SQLite persistence, migrations, backup, reporting |
| `Daycord.Desktop` | Avalonia UI, navigation, localization, startup |

Infrastructure depends on Core.

Desktop depends on Core and Infrastructure.

Core must not reference Infrastructure or Desktop.

Automated verification projects are located in `tests/`.

## 4. Building and Running

All commands below are executed from the repository root.

Restore dependencies:

```bash
dotnet restore Daycord.slnx
```

Build Debug:

```bash
dotnet build Daycord.slnx --configuration Debug
```

Build Release:

```bash
dotnet build Daycord.slnx --configuration Release
```

Run the Desktop application in Debug:

```bash
dotnet run \
  --project src/Daycord.Desktop/Daycord.Desktop.csproj \
  --configuration Debug
```

On macOS, the Debug build creates an application bundle
in the configured Debug output directory.

### Debug and Release Data

Daycord uses separate application data directories.

| Configuration | Directory |
|---|---|
| Debug | `Daycord-test` |
| Release | `Daycord` |

This separation is configured in `ServiceRegistration`.

It must not be removed without reviewing the consequences
for existing user data.

Destructive testing must use independently isolated
temporary databases, not either normal application profile.

## 5. NuGet Package Management

Dependencies use Central Package Management.

Versions are maintained in:

```text
Directory.Packages.props
```

To add a dependency:

1. Declare its version centrally.
2. Add a PackageReference to the appropriate project.
3. Do not duplicate the package version in the project file.
4. Restore dependencies.
5. Build the solution.
6. Verify compatibility with supported platforms.

Do not introduce a new dependency for functionality that
already exists in the solution without a clear reason.

## 6. Application Architecture

The normal application flow is:

```text
Avalonia View
     |
     v
ViewModel
     |
     v
Application Service
     |
     v
Core Contract
     |
     v
Infrastructure Implementation
     |
     v
SQLite / XLSX
```

### Core

Core owns application rules and contracts.

Examples:

- `TaskService`
- `WorkReportService`
- `WorkReportPresetService`

Business validation belongs in application services or
appropriate domain/application models.

### Infrastructure

Infrastructure owns database queries, repository
implementations, and external file generation.

Views and ViewModels must not issue direct persistence
queries for normal application operations.

### Desktop

Desktop owns presentation, user interaction, navigation,
localization, and application lifecycle.

Use the MVVM pattern.

Keep UI-specific code in the presentation layer.

## 7. Dependency Injection

Service registrations are centralized in:

```text
src/Daycord.Desktop/Bootstrap/ServiceRegistration.cs
```

Use constructor injection.

Choose service lifetimes according to their dependencies
and the state they maintain.

### Tracked Repositories

The application registers tracked repository decorators:

- `TrackedTaskRepository`
- `TrackedAppSettingsRepository`
- `TrackedWorkReportPresetRepository`

They delegate persistence to SQLite repositories and notify
`DatabaseChangeTracker` after successful writes.

When introducing a new database mutation:

1. Use the established repository.
2. Preserve its transaction boundaries.
3. Ensure successful committed changes are tracked.
4. Do not mark failed operations as committed.
5. Do not mark read-only operations as database changes.

Missing change tracking can leave committed data outside
the automatic backup scheduler's pending revision state.

### Editor Factories

Task Details and Quick Task use ViewModel factories:

```csharp
Func<TaskDetailsViewModel>
Func<TerminalViewModel>
```

These factories provide independent editing sessions.

Do not replace them with shared editor instances that
accidentally carry unsaved state between unrelated tasks.

### Maintaining Registrations

Before removing a dependency:

- Search constructor injections.
- Check `ServiceRegistration`.
- Check startup and navigation consumers.
- Check ViewModel factories.
- Check backup and recovery dependencies.

Do not remove a component merely because it is not referenced
directly by an AXAML view.

## 8. MVVM and UI Development

ViewModels should:

- Expose observable state.
- Coordinate application services.
- Validate presentation input.
- Provide commands and navigation requests.
- React to localization and settings changes.
- Preserve appropriate unsaved editing state.

Views and code-behind may handle:

- Keyboard focus.
- Control-specific events.
- Popup placement.
- Native file dialogs.
- Window lifecycle.
- Visual interactions.

Avoid SQL queries, persistence orchestration, and domain
business rules in view code-behind.

### Asynchronous Operations

Long-running database, backup, and export operations should
not block the UI thread.

Use asynchronous APIs and cancellation when supported.

Prevent overlapping operations where they would conflict.

Update busy and error states consistently.

Do not assume a successful operation until the underlying
service has completed.

## 9. Tasks and Navigation

Task operations use `TaskService`.

Creation and editing use application request models such as:

- `CreateTaskRequest`
- `UpdateTaskRequest`

Task completion depends on `TaskStatuses.IsCompleted`,
not the status's localized display name.

Deleting a task must preserve referential integrity.

The existing repository deletes dependent work and planning
records within a transaction.

### Task Details

`TaskDetailsViewModel` supports both creation and editing.

It maintains independent in-memory drafts for actual work
entries.

Switching between drafts does not automatically save changes.

New editing behavior must preserve draft isolation and
unsaved-change handling.

### Keyboard Navigation

Task opening and keyboard interaction are coordinated by
the Tasks View and ViewModel.

The list supports:

- Selecting rows.
- Opening a task through the supported keyboard action.
- Opening a task through mouse interaction.
- Using the dedicated Open Task action.

Keyboard handlers must respect focus ownership.

They must not activate underlying task actions while
confirmation dialogs or incompatible loading states are active.

### Navigation Service

Navigation is managed through `INavigationService`.

Use navigation requests rather than direct View-to-View
dependencies.

For date-specific navigation, use:

```csharp
NavigateToTasksForDate(DateOnly date)
```

The Tasks screen consumes this request and applies the
appropriate `TaskDate` filter.

Do not implement Home-to-Tasks navigation by directly
modifying unrelated ViewModel instances.

## 10. Dates and Calendar

Daycord distinguishes:

| Field | Meaning |
|---|---|
| `TaskDate` | Date assigned to a task |
| `PlannedDate` | Date of planned work |
| `WorkDate` | Date of actual work |

These dates must remain independent.

### Shared Calendar

The reusable date-selection control is:

```text
Views/DaycordCalendarView.axaml
Views/DaycordCalendarView.axaml.cs
```

Use it for new date-selection interfaces instead of
duplicating calendar generation and popup behavior.

The parent view owns its date input and coordinates
opening, selection, and closing.

The shared control provides:

- Month navigation.
- Date selection.
- Selected-date highlighting.
- Today selection.
- Localized month and weekday presentation.

Do not replace the shared calendar with an unrelated
platform DatePicker without an explicit requirement.

### Calendar Data

Calendar data is retrieved through:

```csharp
TaskService.GetCalendarDataAsync(...)
```

The returned model separates tasks, planning periods,
and actual work entries.

Use the correct date field for each collection.

## 11. Duration Handling

Duration parsing is centralized in:

```text
src/Daycord.Desktop/ViewModels/PlannedDuration.cs
```

Examples:

| Input | Meaning |
|---|---|
| `1` | 1 hour |
| `30m` | 30 minutes |
| `1h 30m` | 90 minutes |
| `1d` | 1 configured day |
| `2d 3h` | 2 configured days plus 3 hours |

The parser also supports localized duration expressions.

Use the existing parsing and formatting methods.

Do not introduce separate duration parsers for Tasks,
Quick Task, Reports, or Settings.

### MinutesPerDay

The setting determines how a day-based duration is converted
to integer minutes.

The default value is 1,440 minutes.

Stored durations remain integer minutes.

Changing `MinutesPerDay` does not recalculate saved data.

The day-length setting itself must not accept recursive
day-based expressions.

Its accepted value must be between 1 and 1,440 minutes.

## 12. Localization

Daycord supports English and Russian.

Localization resources:

```text
src/Daycord.Desktop/Localization/
├── Strings.resx
└── Strings.ru.resx
```

Localization is provided by:

- `ILocalizationService`
- `LocalizationService`
- `AppLanguage`
- `AppLanguageCodes`

### Adding User-Facing Text

When introducing a localized string:

1. Add a key to `Strings.resx`.
2. Add the corresponding translation to `Strings.ru.resx`.
3. Resolve the key through the localization service.
4. Refresh affected ViewModel properties on language changes.
5. Verify English and Russian presentation.

Avoid hard-coded language-dependent strings in AXAML and
ViewModels.

### Dynamic Language Changes

Language changes take effect without an application restart.

Existing editing state must be preserved.

Do not discard:

- Unsaved task changes.
- Work-entry drafts.
- Report preset edits.
- Current navigation context.

UI layout must remain stable when switching between
different-length localized strings.

## 13. Themes, Styles, and Animations

Daycord supports:

- System.
- Light.
- Dark.

Use the existing theme resources.

Avoid hard-coded foreground, background, and border colors
when an appropriate dynamic resource exists.

### Shared AXAML Resources

Reusable styles, animations, effects, and icon geometries
should be placed in appropriate shared resource dictionaries.

Existing examples:

```text
Styles/Sidebar.axaml
Styles/OverlayEffects.axaml
Themes/NavigationIcons.axaml
```

When developing new UI elements:

1. Reuse existing shared resources where appropriate.
2. Centralize newly introduced reusable behavior.
3. Avoid duplicating animations and icon geometries.
4. Preserve light and dark theme compatibility.
5. Keep screen-specific styling local when reuse is unnecessary.

Do not assume all existing UI resources are already
centralized.

Broader refactoring should be handled separately from
unrelated feature or bugfix work.

### UI Stability

When changing layouts:

- Test both interface languages.
- Verify window resizing.
- Prevent localized content from unexpectedly changing
  the width of stable parent containers.
- Check overlay sizing, clipping, and focus behavior.
- Preserve smooth transitions between UI states.

## 14. Excel Reporting

Reporting uses:

- `WorkReportService`
- `WorkReportPresetService`
- `ReportsViewModel`
- `IWorkReportExporter`
- `ClosedXmlWorkReportExporter`

### Report Data

`WorkReportService` builds the complete reporting model.

It includes:

- Planning-period rows.
- Actual-work rows without corresponding planning.
- TaskDate-only rows for eligible tasks without time records.

Do not create fictional duration values for tasks that
have no planned or actual work.

Actual work must not be counted multiple times when
several planning periods share a task and date.

### Statistics and Presets

Report statistics must use the complete report model.

The selected Excel preset controls export configuration,
not the underlying report statistics.

A TaskDate-only row can be omitted from an export when
the selected preset lacks meaningful identifying columns.

The exported row count can therefore differ from the
complete report row count.

Do not treat report row count as unique task count.

### Exporting XLSX

Excel workbooks are generated using ClosedXML.

The exporter captures the selected language for each
export operation.

Built-in headers are localized through resource-backed
header tokens.

Custom user headings remain unchanged.

Time formatting must support totals exceeding 24 hours.

## 15. SQLite Development

SQLite persistence is implemented in Infrastructure.

Use:

- `SqliteConnectionFactory`
- Dapper repositories.
- Existing type handlers.
- Explicit transactions where required.

### Data Types

GUID values are stored as standard `D` strings.

Date-only values use:

```text
yyyy-MM-dd
```

Application UTC DateTime values use the .NET round-trip
`"O"` representation.

Do not introduce local or timezone-less timestamps
into fields requiring UTC.

### Migrations

Current migrations:

```text
001_InitialSchema.sql
002_ApplicationSettings.sql
```

Migration history includes version, filename, checksum,
and application timestamp.

Previously applied migration SQL must remain immutable.

For schema changes:

1. Add a new sequential migration.
2. Preserve existing migration files.
3. Keep existing user data compatible.
4. Respect foreign keys and required system records.
5. Verify initialization from an older schema.

Do not edit migration checksums or bypass validation
to resolve startup failures.

### Direct Database Access

Use isolated test databases for direct SQL experiments.

Do not manually modify the normal Release database.

Be aware that direct SQL can bypass application-level
validation even when SQLite constraints are satisfied.

See [Database](database.md) for schema details.

## 16. Backup and Recovery Development

The backup system includes:

- `UserBackupService`
- `DatabaseBackupService`
- `AutomaticBackupService`
- `DatabaseChangeTracker`
- `AutomaticBackupScheduler`
- `SqliteSnapshotIO`
- `SqliteSnapshotValidator`
- `PendingRestoreCoordinator`
- `AutomaticRecoveryCandidateSelector`
- `EmergencyRestoreInstaller`

### Backup Rules

Use SQLite-aware snapshot creation.

Do not copy a live SQLite `.db` file as a substitute for
the Online Backup API.

Validate snapshots before publishing them.

Preserve previous backups when a new backup fails.

Do not bypass the change tracker when adding new
database-writing operations.

### Restore Rules

Normal user restoration is deferred to application startup.

A restored database must pass validation and initialization
before the operation is completed.

Maintain:

- The application instance lock.
- The restore journal.
- Staged snapshot verification.
- Raw original-file preservation.
- Safety backups for normal restoration.
- Quarantine and recovery failure handling.

Do not replace the active database while normal application
SQLite connections are open.

### Emergency Recovery Rules

Emergency recovery applies only to eligible corruption.

It must not be used to bypass migration validation.

An emergency recovery operation must not:

- Install an unverified backup.
- Overwrite an unresolved restore journal.
- Treat damaged source files as a valid safety snapshot.
- Automatically repeat an ambiguous installation.
- Delete original damaged files before preservation.

Changes to recovery behavior require verification with
isolated data and the relevant automated checks.

For implementation details, see
[Backup and Recovery](backup-and-recovery.md).

## 17. Debugging and Diagnostics

Useful debugging areas include:

- Database startup and migrations.
- Repository transactions.
- Navigation state and keyboard focus.
- Date filtering.
- Task editing drafts.
- Localization updates.
- Report aggregation and export.
- Backup scheduling.
- Restore and recovery state transitions.

Startup and recovery diagnostics are recorded on a
best-effort basis in:

```text
Diagnostics/recovery-events.jsonl
```

The diagnostic implementation avoids persisting raw
exception messages that may contain private data.

When investigating a database failure:

1. Preserve the affected data directory.
2. Do not delete database or restore files.
3. Inspect available diagnostics.
4. Reproduce the problem using isolated data.
5. Review the relevant initialization or recovery stage.

Do not include real user database contents in issue
reports, documentation, or shared logs.

## 18. Release Configuration

Application version properties are defined in:

```text
Directory.Build.props
```

Packaging targets are configured through:

```text
Directory.Build.targets
```

Supported runtime identifiers:

```text
osx-arm64
win-x64
```

Release publishing uses platform-specific packaging
workflows.

Do not modify packaging configuration or application
versioning as part of an unrelated development task.

See [Releasing](releasing.md) for platform-specific
requirements, commands, and artifact descriptions.

## 19. Validation Before Integration

Before submitting implementation changes:

1. Confirm the correct architectural layer.
2. Check modified dependencies and DI registrations.
3. Build the solution.
4. Run relevant automated verification suites.
5. Verify affected UI behavior on supported platforms
   when required.
6. Review localization and theme compatibility.
7. Check SQLite and migration compatibility if affected.
8. Review the Git diff.

Useful commands:

```bash
dotnet restore Daycord.slnx

dotnet build Daycord.slnx --configuration Debug

dotnet build Daycord.slnx --configuration Release

git diff --check
git status --short
git diff --stat
```

Detailed automated verification commands are documented
in [Testing](testing.md).

Compilation and automated verification do not replace
manual platform acceptance when it is required.

## 20. Development Safety Rules

All contributors must preserve these constraints:

1. Keep domain logic out of Views.
2. Preserve the established project dependency structure.
3. Use existing application services and repositories.
4. Track successful database changes for automatic backup.
5. Preserve independent task, planning, and work dates.
6. Reuse calendar and duration components.
7. Preserve unsaved editing drafts.
8. Keep localization and theme switching functional.
9. Centralize reusable visual resources.
10. Keep report statistics independent of Excel presets.
11. Preserve database migration history.
12. Never silently delete user data.
13. Do not bypass backup validation or restore journals.
14. Use isolated data for destructive testing.
15. Keep release documentation aligned with actual
    application behavior.

## 21. Related Documentation

- [Architecture](architecture.md)
- [Database](database.md)
- [Backup and Recovery](backup-and-recovery.md)
- [Testing](testing.md)
- [Releasing](releasing.md)
- [User Guide](user-guide.md)