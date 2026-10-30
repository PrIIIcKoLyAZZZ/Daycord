# Daycord Developer Guide

## 1. Introduction

This document describes how to set up, build, run, debug, and extend Daycord 0.1.0.

Daycord is a cross-platform desktop application developed with C# and .NET 10.

Its user interface is implemented using Avalonia UI and the MVVM pattern.

Application data is persisted locally using SQLite and Dapper.

This guide reflects the implementation after the completion of the initial MVP development tasks.

## 2. Development Environment

### 2.1 Prerequisites

The recommended development environment includes:

- .NET SDK 10.
- JetBrains Rider.
- Git.
- macOS or Windows.
- SQLite CLI or a database tool such as DataGrip.

The repository includes `global.json`:

```json
{
  "sdk": {
    "version": "10.0.201",
    "rollForward": "latestPatch",
    "allowPrerelease": false
  }
}
```

Install an SDK compatible with this configuration.

Verify the installed SDK:

```bash
dotnet --info
```

### 2.2 JetBrains Rider

Open the solution:

```text
Daycord.slnx
```

Allow Rider to restore NuGet dependencies and index the projects.

The recommended configuration for normal development is Debug.

For release validation, use Release.

Avoid introducing platform-specific dependencies into Core or Infrastructure unless they are explicitly required by an approved architectural change.

## 3. Repository Structure

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

### 3.1 Core

`Daycord.Core` contains:

- Domain entities.
- Application contracts.
- Application models.
- Business services.

It must remain independent of Avalonia, SQLite implementations, and the desktop operating system.

### 3.2 Infrastructure

`Daycord.Infrastructure` contains:

- SQLite connection management.
- Dapper repositories.
- Schema migrations.
- Database initialization.
- SQLite backup and recovery.
- Excel generation through ClosedXML.

Infrastructure depends on Core.

### 3.3 Desktop

`Daycord.Desktop` contains:

- Application startup.
- Avalonia Views.
- ViewModels.
- Navigation.
- Localization.
- Themes.
- Application settings integration.
- Shared calendar controls.
- Dependency injection configuration.
- Release-specific desktop resources.

Desktop depends on Core and Infrastructure.

## 4. NuGet Package Management

Daycord uses Central Package Management.

Package versions are defined in:

`Directory.Packages.props`

The current major dependencies are:

| Package | Version |
|---|---|
| Avalonia | 12.1.4 |
| CommunityToolkit.Mvvm | 8.4.2 |
| Dapper | 2.1.89 |
| Microsoft.Data.Sqlite | 10.0.13 |
| Microsoft.Extensions.DependencyInjection | 10.0.13 |
| ClosedXML | 0.105.1 |

Additional Avalonia and development packages are also declared in the central package file.

### 4.1 Adding a Dependency

When a new package is required:

1. Add its version to `Directory.Packages.props`.
2. Add its package reference to the relevant project.
3. Do not specify the version again in the individual project file.
4. Restore dependencies.
5. Build Debug and Release.
6. Confirm that the dependency belongs in the appropriate architectural layer.

Avoid introducing duplicate libraries for functionality that already exists.

### 4.2 Updating Dependencies

Dependency updates should be intentional.

Before updating a package:

- Review breaking changes.
- Check .NET 10 compatibility.
- Check Avalonia compatibility.
- Review licensing implications.
- Verify both supported release platforms.
- Test affected functionality.

Do not upgrade dependencies solely because a newer version is available during release stabilization.

## 5. Building the Solution

From the repository root, restore dependencies:

```bash
dotnet restore Daycord.slnx
```

Build Debug:

```bash
dotnet build Daycord.slnx -c Debug
```

Build Release:

```bash
dotnet build Daycord.slnx -c Release
```

The solution should compile all three projects:

- Daycord.Core
- Daycord.Infrastructure
- Daycord.Desktop

The MVP legacy-cleanup stage completed with successful Debug and Release builds on macOS.

These build results do not replace runtime testing on both supported platforms.

## 6. Running Daycord

Run the Desktop project:

```bash
dotnet run \
  --project src/Daycord.Desktop/Daycord.Desktop.csproj
```

The application initializes the SQLite database during startup.

On macOS, the Debug configuration also prepares a `Daycord.app` bundle under the configured Debug output directory.

The macOS bundle contains the application executable, application metadata, and icon resources.

### 6.1 First Launch

During startup, Daycord:

1. Creates the dependency injection container.
2. Creates the main application window.
3. Acquires the application database lock.
4. Checks for a pending database restoration.
5. Initializes and migrates SQLite.
6. Loads application settings.
7. Applies language and appearance preferences.
8. Enables normal application screens.

Do not access database-dependent features before initialization completes.

### 6.2 Database Initialization Errors

When database initialization fails, the application displays a failure state rather than silently recreating the database.

Investigate the underlying exception before attempting recovery.

Do not delete `daycord.db` to resolve an initialization error.

## 7. Debugging

JetBrains Rider can be used for normal debugging.

Recommended debugging targets include:

- Application startup.
- TaskService.
- Repository operations.
- ViewModel commands.
- Navigation transitions.
- Localization updates.
- Database migrations.
- Report generation.
- Backup and recovery.

### 7.1 Debugging Persistence

Inspect database operations at the repository layer.

Relevant classes include:

- `SqliteConnectionFactory`
- `SqliteTaskRepository`
- `SqliteWorkReportPresetRepository`
- `SqliteAppSettingsRepository`
- `MigrationRunner`

Use an isolated database when investigating SQL writes or migrations.

Avoid changing the real user's data during debugging.

### 7.2 Release Diagnostics

Debug and Release builds are not identical.

The application includes Avalonia diagnostic support as a development dependency, with production asset inclusion controlled by the project configuration.

Do not add diagnostics-only dependencies to Release behavior without a specific requirement.

Release builds should be tested independently of Debug.

## 8. MVVM Conventions

Daycord uses CommunityToolkit.Mvvm.

ViewModels manage presentation state and coordinate application operations.

Views should remain focused on visual presentation and UI-specific interaction.

### 8.1 ViewModel Responsibilities

ViewModels may:

- Expose observable properties.
- Provide commands.
- Validate UI input.
- Translate application results into presentation models.
- Request navigation.
- React to localization or settings changes.

They should not duplicate repository implementations or domain business rules.

### 8.2 Views

Avalonia views are implemented using AXAML and associated C# code-behind where necessary.

Code-behind is appropriate for:

- Popup placement.
- Keyboard focus.
- Native window integration.
- Visual lifecycle handling.
- Control-specific events.

Avoid placing database queries and application business logic directly in code-behind.

### 8.3 Async Operations

Database and report operations should be asynchronous where supported.

Use cancellation tokens when appropriate.

Do not block the UI thread with long-running persistence or export operations.

Commands must handle errors and update UI state consistently.

## 9. Dependency Injection

Dependency injection registration is centralized in:

```text
src/Daycord.Desktop/Bootstrap/ServiceRegistration.cs
```

The container is built using `Microsoft.Extensions.DependencyInjection`.

### 9.1 Important Registrations

| Service | Implementation | Lifetime |
|---|---|---|
| `IDatabaseInitializer` | `DatabaseInitializer` | Singleton |
| `ITaskRepository` | `SqliteTaskRepository` | Transient |
| `TaskService` | `TaskService` | Transient |
| `IAppSettingsRepository` | `SqliteAppSettingsRepository` | Singleton |
| `IAppSettingsService` | `AppSettingsService` | Singleton |
| `IUserBackupService` | `UserBackupService` | Singleton |
| `INavigationService` | `NavigationService` | Singleton |
| `ILocalizationService` | `LocalizationService` | Singleton |
| `IWorkReportPresetRepository` | `SqliteWorkReportPresetRepository` | Transient |
| `WorkReportService` | `WorkReportService` | Singleton |
| `WorkReportPresetService` | `WorkReportPresetService` | Singleton |
| `IWorkReportExporter` | `ClosedXmlWorkReportExporter` | Singleton |

Additional services and ViewModels are registered in the same file.

### 9.2 ViewModel Factories

Quick Task and Task Details use factories:

```csharp
Func<TerminalViewModel>
Func<TaskDetailsViewModel>
```

These factories allow the application to create separate editing sessions.

Do not replace these factories with application-wide singleton editor instances.

Doing so may introduce unexpected state sharing.

### 9.3 Settings Initialization

Settings-related ViewModels are constructed lazily.

The application must initialize SQLite and load persisted settings before constructing components that require current settings.

`MainWindow` coordinates lazy initialization when the Settings page is activated.

### 9.4 DI Maintenance

When removing or replacing a component:

1. Search for constructor injections.
2. Search `ServiceRegistration.cs`.
3. Check navigation factories.
4. Check ViewModel factories.
5. Check startup dependencies.
6. Remove only obsolete registrations.
7. Rebuild Debug and Release.

Do not assume that a class is unused simply because it has no direct references in Views.

## 10. Task Management

Application task operations are coordinated by:

`Daycord.Core.Application.Services.TaskService`

Persistence is implemented through:

`ITaskRepository`

The SQLite implementation is:

`SqliteTaskRepository`

### 10.1 Task Creation

A new task should be created through `TaskService`.

The application uses `CreateTaskRequest` to transfer validated task data.

### 10.2 Task Updates

Task modifications use `UpdateTaskRequest` and corresponding service operations.

Task creation timestamps must remain unchanged during editing.

Completion timestamps must reflect completion-state changes.

### 10.3 Task Statuses

Task completion semantics are stored in `TaskStatuses.IsCompleted`.

Do not hard-code comparisons against status names such as `Open` or `Completed`.

Status names are presentation data.

### 10.4 Task Deletion

Task deletion must preserve referential integrity.

`SqliteTaskRepository` removes dependent work entries and planning periods before deleting the task, using a transaction.

Do not introduce SQL deletion paths that bypass this behavior.

## 11. Task Details Editor

`TaskDetailsViewModel` implements the full task editing experience.

The editor supports task creation and editing.

It also coordinates:

- Task properties.
- Markdown descriptions.
- Planned time.
- Actual work entries.
- Work-entry drafts.

### 11.1 Create and Edit Modes

Task Details supports explicit editor modes.

Do not implement a separate full-task editor for task creation when the existing editor supports the required operation.

### 11.2 Work-Entry Drafts

Each work entry may have an independent in-memory editing draft.

The user can switch between work entries without saving every intermediate edit.

A draft is not persisted automatically.

Database changes occur only when the corresponding save operation succeeds.

Unsaved state must be handled when leaving the editing context.

Future changes to Task Details must preserve this behavior.

## 12. Duration Parsing

Duration parsing is centralized in:

```text
src/Daycord.Desktop/ViewModels/PlannedDuration.cs
```

The class is internal to the Desktop assembly.

It supports input such as:

```text
1
30m
1h
1h 30m
1d
2d 3h
```

Russian duration abbreviations are also supported.

### 12.1 Plain Numbers

A plain numeric value represents hours.

For example:

```text
1 = 60 minutes
```

A plain `1` does not represent one minute.

### 12.2 Days

The `d` suffix represents one configured day.

By default:

```text
1d = 1440 minutes
```

If `MinutesPerDay` is 480:

```text
1d = 480 minutes
2d 3h = 1140 minutes
```

### 12.3 Stored Durations

Durations are stored as integer minutes.

Changing `MinutesPerDay` changes the interpretation of new day-based input, not historical values.

### 12.4 Parsing APIs

The existing parser exposes:

```csharp
TryParse(...)
TryParseRequired(...)
TryParseDayLength(...)
Format(...)
```

Use the existing methods within the Desktop layer.

Do not implement separate parsers for Quick Task, Task Details, Settings, or other duration fields.

### 12.5 Day-Length Setting

The day-length setting accepts a duration but does not allow a `d` component.

This avoids defining the length of one day in terms of itself.

The accepted result must be between 1 and 1,440 minutes.

## 13. Date Selection

Daycord uses a shared calendar control:

```text
src/Daycord.Desktop/Views/DaycordCalendarView.axaml
src/Daycord.Desktop/Views/DaycordCalendarView.axaml.cs
```

This control provides the custom Daycord calendar interface.

It supports:

- Month navigation.
- Date selection.
- Localized month and weekday labels.
- Popup positioning.
- Today selection.
- Selected-date highlighting.

### 13.1 Reusing the Calendar

New UI features requiring date selection should reuse `DaycordCalendarView`.

Do not copy its AXAML or calendar-generation logic into another view.

The parent view owns its date input and integrates the calendar popup.

The calendar exposes date-selection events and methods for opening, refreshing, and closing the popup.

### 13.2 Calendar Data

The application-level calendar data API is provided through:

`TaskService.GetCalendarDataAsync`

The resulting model separates:

- Tasks by `TaskDate`.
- Planning periods by `PlannedDate`.
- Actual work entries by `WorkDate`.

Do not substitute one date field for another.

A task can have planned or actual work on dates different from its task date.

## 14. Home Dashboard

The Home dashboard uses:

`HomeDashboardViewModel`

The dashboard displays task and time-related information for the current local date.

New dashboard features should reuse existing application services rather than issuing SQL directly from the ViewModel.

Completion calculations must use status metadata, not localized status names.

## 15. Localization

Daycord supports English and Russian.

Localization is implemented using:

- `AppLanguage`
- `AppLanguageCodes`
- `ILocalizationService`
- `LocalizationService`
- RESX resources

Resource files:

```text
src/Daycord.Desktop/Localization/
├── Strings.resx
└── Strings.ru.resx
```

English is the default language.

### 15.1 Adding a Localized String

When adding user-facing text:

1. Add a unique resource key to `Strings.resx`.
2. Add the corresponding translation to `Strings.ru.resx`.
3. Resolve the key through the localization service.
4. Ensure that open views update when the language changes.
5. Verify the text in both supported languages.

Avoid hard-coding localized strings in ViewModels or AXAML when the text should respond to the application language setting.

### 15.2 Dynamic Language Switching

`LocalizationService` publishes changes when the active language changes.

ViewModels that display localized content should react to these changes.

Changing language must not discard user input, editing drafts, or unsaved report configuration.

### 15.3 Adding Another Language

To add a new language:

1. Extend `AppLanguage`.
2. Add its language code to `AppLanguageCodes`.
3. Add the appropriate RESX resource file.
4. Update the localization service and UI selection as needed.
5. Test dynamic language switching.
6. Test reporting labels and Excel export.
7. Test persistence across application restarts.

The AppSettings database schema does not restrict the Language column to only English and Russian.

A new language normally does not require a database migration.

## 16. Application Settings

Application settings are represented by:

`Daycord.Core.Application.Models.AppSettings`

Persistence is implemented through:

`IAppSettingsRepository`

The desktop-facing service is:

`IAppSettingsService`

The current implementation is:

`AppSettingsService`

Settings include:

- Language.
- Theme.
- MinutesPerDay.

### 16.1 Saving Settings

Settings changes are persisted through the settings service.

The service publishes updated settings only after the repository successfully saves them.

Do not update the in-memory settings before successful persistence.

### 16.2 Theme Handling

Themes use Avalonia's native `ThemeVariant` mechanism.

Available modes are:

```text
System
Light
Dark
```

System mode follows the operating system's appearance.

Use the existing theme resources rather than adding unrelated colors directly to controls.

### 16.3 Settings Feedback

Language and appearance changes are immediately visible.

The duration setting provides a preview of its current meaning.

Do not reintroduce an automatic "Settings saved" notification solely for confirmation.

This behavior was intentionally excluded from the accepted MVP design.

## 17. Excel Reporting

Daycord uses the current report implementation:

- `WorkReportService`
- `WorkReportPresetService`
- `WorkReportColumnLabels`
- `IWorkReportExporter`
- `ClosedXmlWorkReportExporter`

The previous `MonthlyWorkReportService` is not part of the current architecture.

### 17.1 Report Data

Report data is constructed through `WorkReportService`.

The service retrieves task-related calendar data and prepares rows for the exporter.

Do not write a second report data aggregation implementation for new exports.

### 17.2 Presets

Report presets are persisted through:

`IWorkReportPresetRepository`

The implementation is:

`SqliteWorkReportPresetRepository`

The built-in Default preset has a stable identifier.

It cannot be deleted.

Custom presets can store different columns and user-defined headers.

### 17.3 Column Headers

Automatic headers use language-independent tokens.

Example:

```text
@daycord:Date
```

The reporting system resolves these tokens using the selected language.

User-entered headers remain literal.

Do not translate user-entered text automatically.

### 17.4 Exporting

XLSX generation is implemented through ClosedXML.

The exporter captures the current application language at the beginning of an export.

A language change during export must not produce mixed-language report headings and totals.

Excel duration values must support totals greater than 24 hours.

### 17.5 Report Compatibility

The repository contains compatibility logic for an earlier built-in preset name.

This logic is intentional and must not be removed without reviewing persisted user data.

## 18. SQLite Development Rules

SQLite persistence belongs in Infrastructure.

Repositories should use the shared connection factory and Dapper.

### 18.1 Database Connections

Use `SqliteConnectionFactory`.

Dispose of opened connections correctly.

Maintain foreign key enforcement.

### 18.2 Type Handlers

Daycord uses explicit type handlers for:

- GUID.
- DateOnly.
- UTC DateTime.

Do not assume arbitrary SQLite TEXT timestamp formats can be mapped safely.

DateOnly values use:

```text
yyyy-MM-dd
```

UTC DateTime values use .NET's round-trip `"O"` format.

### 18.3 Migrations

Current migrations:

```text
001_InitialSchema.sql
002_ApplicationSettings.sql
```

Migrations are embedded in the Infrastructure assembly.

Do not edit a migration that has already been applied.

New schema changes require a new migration with the next sequential version.

### 18.4 Migration Checksums

Migration checksums are verified during initialization.

Changing a historical migration can prevent existing databases from opening.

Do not disable or bypass checksum validation.

### 18.5 Testing Database Changes

Use isolated database environments.

Test:

- Fresh database initialization.
- Upgrade from an existing schema.
- Repeated initialization.
- Foreign key integrity.
- Schema checksum compatibility.
- Preservation of existing user data.

Do not modify the production `daycord.db` during tests.

## 19. Backup and Recovery Development

The backup subsystem includes:

- `UserBackupService`
- `SqliteSnapshotIO`
- `SqliteSnapshotValidator`
- `PendingRestoreCoordinator`
- `PendingRestoreState`

The database must not be replaced while normal application connections are active.

### 19.1 Backups

Use SQLite-aware snapshot operations.

Do not use an ordinary file copy of an active SQLite database as a substitute for the backup API.

Validate generated snapshots.

### 19.2 Restoration

Restoration is deferred until a subsequent application startup.

The restore coordinator processes staged files before normal database initialization.

A safety backup is created before the working database is replaced.

The restored database must pass initialization before restoration is finalized.

### 19.3 Testing

Restoration testing must use an isolated environment.

Do not simulate corrupt backups or interrupted restoration using the main user database.

See [Backup and Recovery](backup-and-recovery.md).

## 20. Release Configuration

Application version properties are centralized in:

`Directory.Build.props`

Packaging targets are centralized in:

`Directory.Build.targets`

The current application version is:

```text
0.1.0
```

The release targets are:

```text
osx-arm64
win-x64
```

### 20.1 Release Builds

The project configures Release builds without:

- Trimming.
- Native AOT.
- Single-file publishing.
- ReadyToRun publishing.

Do not enable these optimizations without validating compatibility with Avalonia, Dapper, localization, and report generation.

### 20.2 macOS

macOS Release publishing creates a self-contained application bundle.

The existing automation also prepares DMG and ZIP distribution artifacts.

### 20.3 Windows

Windows publishing produces a self-contained executable distribution.

The existing macOS-hosted packaging workflow uses NSIS to generate Setup.exe and a portable ZIP.

### 20.4 Release Signing

Version 0.1.0 distributions are unsigned.

Apple Developer ID signing, Apple notarization, and Windows Authenticode signing are not configured.

Do not describe unsigned artifacts as signed or notarized.

### 20.5 Future Mac App Store Distribution

The existing DMG workflow is not a Mac App Store submission workflow.

Future Mac App Store support requires separate work, including App Sandbox compatibility, signing, entitlements, and an appropriate App Store distribution process.

These capabilities are not part of the accepted 0.1.0 release configuration.

See [Releasing](releasing.md).

## 21. Validation Checklist

Before submitting a code change:

- Confirm that the change belongs in the correct project.
- Check architecture and DI registrations.
- Restore dependencies.
- Build Debug.
- Build Release.
- Check localization changes.
- Check compatibility with existing SQLite data.
- Check migration integrity if persistence changed.
- Check affected user workflows.
- Review `git diff`.
- Run `git diff --check`.
- Record the checks actually performed.

Recommended commands:

```bash
dotnet restore Daycord.slnx
dotnet build Daycord.slnx -c Debug
dotnet build Daycord.slnx -c Release

git diff --check
git status --short
git diff --stat
```

A successful build is not evidence that all runtime scenarios have passed.

The current solution does not contain a dedicated automated test project.

Functional verification relies on available manual and technical test procedures, documented separately.

## 22. Development Safety Rules

All contributors should follow these rules:

1. Preserve the three-project dependency structure.
2. Keep persistence operations out of Views.
3. Reuse existing application services.
4. Keep task, planned, and actual work dates independent.
5. Reuse `DaycordCalendarView`.
6. Reuse `PlannedDuration`.
7. Preserve independent WorkEntry drafts.
8. Preserve existing database migration history.
9. Do not delete or reset user data during development.
10. Do not modify a live SQLite database for destructive testing.
11. Keep translations consistent between supported languages.
12. Do not introduce duplicate report implementations.
13. Do not remove backward compatibility code without checking existing persisted data.
14. Verify DI registrations when changing service lifetimes.
15. Keep release packaging aligned with centralized project versioning.
16. Report unverified behavior as unverified rather than assuming it works.

## 23. Related Documentation

- [Architecture](architecture.md)
- [Database](database.md)
- [User Guide](user-guide.md)
- [Releasing](releasing.md)
- [Backup and Recovery](backup-and-recovery.md)
- [Testing](testing.md)

This guide describes the development conventions and implementation constraints of Daycord 0.1.0.