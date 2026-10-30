# Daycord Database

## 1. Overview

Daycord 0.1.0 uses SQLite as its local persistent data store.

The database contains tasks, task statuses, planned time, actual work, report presets, report settings, application settings, and migration history.

The implementation uses:

- Microsoft.Data.Sqlite for SQLite connections.
- Dapper for query execution and object mapping.
- Versioned embedded SQL migrations.
- Explicit type handlers for GUIDs, dates, and UTC timestamps.
- SQLite transactions and foreign key constraints.

Entity Framework Core is not used.

The database is initialized automatically when the application starts.

## 2. Database Location

Database paths are managed by:

`src/Daycord.Infrastructure/Persistence/AppDataPaths.cs`

The application uses:

`Environment.SpecialFolder.LocalApplicationData`

and creates a `Daycord` subdirectory.

The main database file is named:

`daycord.db`

Typical locations are:

### macOS

```text
~/Library/Application Support/Daycord/daycord.db
```

### Windows

```text
%LOCALAPPDATA%\Daycord\daycord.db
```

The exact location depends on the operating system and the current user profile.

### 2.1 Internal Directories

`AppDataPaths` also defines:

```text
Daycord/
├── daycord.db
├── Backups/
│   ├── SchemaUpgrades/
│   └── BeforeRestore/
└── RestoreStaging/
    └── pending-restore.json
```

These directories serve different purposes:

- `SchemaUpgrades` stores recovery copies associated with database schema upgrades.
- `BeforeRestore` stores safety copies created before database restoration.
- `RestoreStaging` holds files associated with pending restoration.
- `pending-restore.json` records the pending restore operation.

User-selected backup destinations may be outside these internal directories.

Do not remove the internal directories while Daycord is running or while a restoration is pending.

## 3. Database Connection Management

SQLite connections are created through:

`SqliteConnectionFactory`

The factory configures the following connection properties:

| Property | Value |
|---|---|
| Mode | ReadWriteCreate |
| ForeignKeys | true |
| Pooling | false |
| DefaultTimeout | 15 seconds |

Each database operation should properly dispose of its connection.

`Pooling = false` helps make connection lifetimes explicit, especially during database restoration.

Foreign key enforcement is enabled when opening normal application connections.

The migration runner verifies that foreign key enforcement is active before proceeding.

## 4. Database Schema

The Daycord 0.1.0 database contains eight application tables and one migration history table.

| Table | Purpose |
|---|---|
| `TaskStatuses` | Task status definitions |
| `Tasks` | Task records |
| `PlanningPeriods` | Planned work durations |
| `WorkEntries` | Actual work records |
| `ReportPresets` | Configurable report layouts |
| `ReportSettings` | Active report preset |
| `AppSettings` | Application preferences |
| `__SchemaMigrations` | Applied migration history |

`__SchemaMigrations` is created by `MigrationRunner`, not directly by `001_InitialSchema.sql`.

### 4.1 TaskStatuses

Defines available task statuses.

| Column | Type | Description |
|---|---|---|
| `Id` | TEXT | Primary key, GUID |
| `SystemKey` | TEXT NULL | Stable identifier for system statuses |
| `Name` | TEXT | Non-empty status name |
| `IsSystem` | INTEGER | System status flag, 0 or 1 |
| `IsCompleted` | INTEGER | Completion flag, 0 or 1 |
| `SortOrder` | INTEGER | Display order |

Constraints:

- `Id` is the primary key.
- `Name` cannot be empty or whitespace-only.
- `IsSystem` and `IsCompleted` must be 0 or 1.
- System statuses must have a non-empty `SystemKey`.
- Custom statuses must have a NULL `SystemKey`.
- Non-NULL system keys are unique.

The initial migration creates two built-in statuses:

| SystemKey | Name | IsCompleted |
|---|---|---|
| `open` | Open | 0 |
| `completed` | Completed | 1 |

Their identifiers are stable constants defined in `TaskStatusDefinition`.

Application logic must use `IsCompleted` to determine whether a status represents completion.

Do not compare a status's display name to a localized string.

### 4.2 Tasks

Stores the primary task records.

| Column | Type | Description |
|---|---|---|
| `Id` | TEXT | Primary key, GUID |
| `TaskDate` | TEXT NULL | Optional task date |
| `TaskNumber` | TEXT NULL | Optional identifier supplied by the user |
| `Title` | TEXT | Required task title |
| `ShortDescription` | TEXT NULL | Short description |
| `DescriptionMarkdown` | TEXT NULL | Markdown description |
| `StatusId` | TEXT | References TaskStatuses |
| `CreatedAtUtc` | TEXT | Creation timestamp |
| `UpdatedAtUtc` | TEXT | Last modification timestamp |
| `CompletedAtUtc` | TEXT NULL | Completion timestamp |

Constraints:

- `Title` cannot be empty or whitespace-only.
- `TaskNumber` may be NULL but cannot be whitespace-only.
- `StatusId` references `TaskStatuses(Id)`.
- Status deletion is restricted when tasks reference that status.

Indexes:

- `IX_Tasks_TaskDate`
- `IX_Tasks_TaskNumber`
- `IX_Tasks_StatusId`

Task dates and timestamps represent different concepts.

`TaskDate` is a calendar date without a timezone.

The timestamp columns record moments in UTC.

### 4.3 PlanningPeriods

Stores planned work for tasks.

| Column | Type | Description |
|---|---|---|
| `Id` | TEXT | Primary key, GUID |
| `TaskId` | TEXT | References Tasks |
| `PlannedDate` | TEXT | Date of planned work |
| `PlannedMinutes` | INTEGER | Planned duration |

Constraints:

- `PlannedMinutes` must be greater than zero.
- `TaskId` references `Tasks(Id)`.
- Foreign key deletion behavior is RESTRICT.

Indexes:

- `IX_PlanningPeriods_TaskId`
- `IX_PlanningPeriods_PlannedDate`

A task can have multiple planning periods.

`PlannedDate` does not have to equal the task's `TaskDate`.

Planned durations are stored in integer minutes.

### 4.4 WorkEntries

Stores actual work performed.

| Column | Type | Description |
|---|---|---|
| `Id` | TEXT | Primary key, GUID |
| `TaskId` | TEXT | References Tasks |
| `WorkDate` | TEXT | Date of actual work |
| `ActualMinutes` | INTEGER | Actual duration |
| `WorkDescription` | TEXT NULL | Work description |
| `Note` | TEXT NULL | Optional note |
| `CreatedAtUtc` | TEXT | Creation timestamp |
| `UpdatedAtUtc` | TEXT | Last modification timestamp |

Constraints:

- `ActualMinutes` must be greater than zero.
- `TaskId` references `Tasks(Id)`.
- Foreign key deletion behavior is RESTRICT.

Indexes:

- `IX_WorkEntries_TaskId`
- `IX_WorkEntries_WorkDate`

A task may have multiple work entries on the same date.

`WorkDate` is independent of `TaskDate` and `PlannedDate`.

### 4.5 ReportPresets

Stores report layout configurations.

| Column | Type | Description |
|---|---|---|
| `Id` | TEXT | Primary key, GUID |
| `Name` | TEXT | Preset name |
| `IsBuiltIn` | INTEGER | Built-in flag |
| `ColumnsJson` | TEXT | JSON column configuration |
| `CreatedAtUtc` | TEXT | Creation timestamp |
| `UpdatedAtUtc` | TEXT | Modification timestamp |

Constraints:

- `Name` length must be between 1 and 120 characters after trimming.
- `IsBuiltIn` must be 0 or 1.
- `ColumnsJson` must contain valid JSON.
- Names are unique using `COLLATE NOCASE`.
- Only the known Default preset ID may have `IsBuiltIn = 1`.
- The Default preset ID must have `IsBuiltIn = 1`.

The Default preset ID is:

```text
7b9f3e18-6fc7-449e-9a12-e59063d58101
```

The initial migration creates the built-in preset using language-independent column header tokens.

For example:

```text
@daycord:Date
@daycord:TaskNumber
@daycord:TaskTitle
@daycord:PlannedTime
```

These tokens are resolved to localized column headers by the reporting implementation.

Custom user-defined headers are stored as literal text.

The application validates report fields, header lengths, and duplicate fields through `WorkReportPreset`.

The database's `json_valid` constraint verifies JSON syntax, not the complete application-level report schema.

### 4.6 ReportSettings

Stores the selected report preset.

| Column | Type | Description |
|---|---|---|
| `SingletonId` | INTEGER | Primary key, always 1 |
| `ActivePresetId` | TEXT | References ReportPresets |

The singleton constraint requires:

```sql
SingletonId = 1
```

The foreign key uses `ON DELETE RESTRICT`.

When an active custom preset is deleted through the repository, the application first selects the Default preset within the same transaction.

Report configuration must not be modified by direct SQL without respecting this relationship.

### 4.7 AppSettings

Introduced by migration `002_ApplicationSettings.sql`.

| Column | Type | Description |
|---|---|---|
| `SingletonId` | INTEGER | Primary key, always 1 |
| `Language` | TEXT | Persisted language code |
| `Theme` | TEXT | Appearance preference |
| `MinutesPerDay` | INTEGER | Duration represented by one day |

Default values:

| Setting | Default |
|---|---|
| Language | `en` |
| Theme | `System` |
| MinutesPerDay | `1440` |

Constraints:

- `SingletonId` must equal 1.
- `Language` must be 2 to 35 characters and contain no surrounding whitespace.
- `Theme` must be `System`, `Light`, or `Dark`.
- `MinutesPerDay` must be an integer between 1 and 1,440.

The database deliberately does not restrict `Language` to a fixed enumeration.

Supported language codes are validated by `AppLanguageCodes` in Core.

A new supported language does not require changing the SQL constraint.

### 4.8 __SchemaMigrations

Tracks applied database migrations.

| Column | Type | Description |
|---|---|---|
| `Version` | INTEGER | Primary key, migration number |
| `Name` | TEXT | Migration filename |
| `Checksum` | TEXT | SHA-256 checksum |
| `AppliedAtUtc` | TEXT | UTC application timestamp |

All columns are required.

Migration versions must be consecutive and begin at `001`.

The checksum represents the embedded SQL migration text.

The migration runner rejects an applied migration if its version, filename, or checksum differs from the corresponding embedded migration.

## 5. Entity Relationships

```text
TaskStatuses
    |
    +----< Tasks
              |
              +----< PlanningPeriods
              |
              +----< WorkEntries

ReportPresets
    |
    +----< ReportSettings

AppSettings
    (singleton)

__SchemaMigrations
    (migration history)
```

The task relationship is one-to-many for both planning periods and work entries.

Task status relationships are many-to-one.

Report settings reference one active report preset.

AppSettings and ReportSettings use singleton records.

## 6. Foreign Keys and Deletion

Foreign keys use RESTRICT rather than relying on automatic cascading deletion.

The `SqliteTaskRepository` explicitly deletes dependent work entries and planning periods before deleting a task.

These operations occur in a transaction.

The deletion sequence is:

1. Delete the task's WorkEntries.
2. Delete the task's PlanningPeriods.
3. Delete the Task record.
4. Commit the transaction.

Do not modify the database schema to use cascading deletion without reviewing repository behavior.

## 7. Date and Time Storage

Daycord distinguishes date-only values from UTC timestamps.

### 7.1 DateOnly

`DateOnly` values are stored as ISO-style date strings:

```text
yyyy-MM-dd
```

Example:

```text
2026-10-30
```

Fields include:

- `Tasks.TaskDate`
- `PlanningPeriods.PlannedDate`
- `WorkEntries.WorkDate`

These fields represent calendar dates, not UTC instants.

### 7.2 UTC DateTime

Application timestamp fields use the .NET round-trip format:

```csharp
DateTime.UtcNow.ToString(
    "O",
    CultureInfo.InvariantCulture)
```

An example serialized value is:

```text
2026-10-30T12:34:56.0000000Z
```

This format preserves an explicit UTC representation.

`SqliteTypeHandlers.UtcDateTimeHandler` requires parsed timestamps to have `DateTimeKind.Utc`.

The handler expects .NET's exact `"O"` format when mapping database values to DateTime.

Do not use local timestamps or timezone-less values in application timestamp columns.

### 7.3 GUIDs

GUIDs are stored as TEXT using the standard `D` format.

Example:

```text
5e8a148e-1a3b-4b17-bba6-251392a01d61
```

`SqliteTypeHandlers.GuidHandler` uses the corresponding exact parsing convention.

### 7.4 Direct SQL and DataGrip

Direct SQL insertion can bypass application-level validation.

In particular, SQLite itself does not enforce the .NET timestamp format for ordinary TEXT columns.

A timestamp such as:

```text
2026-10-30 12:34:56
```

may be stored in SQLite but fail when Dapper maps it to a UTC DateTime.

For test data, prefer application services.

If direct SQL insertion is required:

- Work on an isolated test database.
- Use valid foreign keys.
- Use the correct GUID format.
- Use `yyyy-MM-dd` for dates.
- Use explicitly formatted UTC timestamps.
- Respect positive-minute constraints.
- Validate the resulting database before opening it in the application.

Never populate or modify the production user database merely to test reports or application features.

## 8. Migration System

Migrations are implemented by:

`src/Daycord.Infrastructure/Persistence/MigrationRunner.cs`

They are embedded into the Infrastructure assembly through:

```xml
<EmbeddedResource Include="Persistence/Migrations/*.sql" />
```

The migration runner discovers the embedded resources at runtime.

### 8.1 Current Migrations

| Version | File | Purpose |
|---|---|---|
| 001 | `001_InitialSchema.sql` | Tasks, statuses, planning, work entries, reports |
| 002 | `002_ApplicationSettings.sql` | Application preferences |

There has never been a separate UTC migration numbered 002 in the current migration history.

Do not add references to such a migration.

### 8.2 First Run

For a new database, the initializer:

1. Creates the application data directory.
2. Opens the SQLite database.
3. Starts an immediate transaction.
4. Checks foreign key enforcement.
5. Creates `__SchemaMigrations`.
6. Applies the available migrations in order.
7. Records migration names and checksums.
8. Validates database integrity.
9. Commits the transaction.
10. Attempts to enable WAL mode.

The runner substitutes the stable built-in status IDs into migration 001 before executing it.

The checksum is calculated from the original embedded SQL content.

### 8.3 Existing Databases

When a database already has migration history, the runner checks that:

- The history table has the expected structure.
- Applied versions are known and consecutive.
- Migration filenames match.
- Applied checksums match.
- Migration timestamps contain explicit UTC information.
- The expected task tables exist.
- Built-in task statuses have valid identities and completion flags.

An unknown existing database without recognized migration history is not automatically overwritten or recreated.

### 8.4 Upgrading from 001 to 002

A database containing migration 001 can be upgraded by applying migration 002.

Before upgrading an existing recognized database, the application creates a schema-upgrade backup.

The migration is executed transactionally.

The migration creates AppSettings without dropping existing task or reporting tables.

### 8.5 Migration Checksums

Migration checksums are calculated using SHA-256 over the embedded SQL text.

Once a migration has been applied and distributed, its contents must remain unchanged.

Even a harmless edit to the migration file changes its checksum and can cause initialization to fail on existing databases.

Correct schema evolution requires a new migration.

Example naming convention:

```text
003_DescriptiveSchemaChange.sql
004_AnotherSchemaChange.sql
```

These are illustrative future filenames, not existing migrations.

Never add a migration file without a corresponding schema-change requirement.

## 9. Migration Development Rules

When introducing a new migration:

1. Create a new sequentially numbered SQL file.
2. Preserve all existing migrations unchanged.
3. Keep migration operations compatible with existing user data.
4. Avoid destructive schema replacements.
5. Consider foreign key references and indexes.
6. Verify migration checksum behavior.
7. Test on a fresh isolated database.
8. Test on an isolated copy representing the previous schema.
9. Test repeated startup and migration idempotence.
10. Validate the upgraded database before release.

Do not reset the user's database to apply schema changes.

## 10. Transactions

Daycord uses explicit transactions for multi-step database operations.

Examples include:

- Task creation with an initial planning period.
- Task deletion with dependent records.
- Report preset changes requiring related settings updates.
- Schema migrations.

A multi-step operation must either complete successfully or roll back.

Repository changes must preserve transactional consistency.

## 11. WAL Mode

After successful migration processing, Daycord attempts:

```sql
PRAGMA journal_mode = WAL;
```

WAL may be unavailable or fail under some filesystem conditions.

The application tolerates selected SQLite errors during this optional configuration step.

Correctness must not depend on WAL being successfully enabled.

When WAL is active, SQLite may create additional files such as:

```text
daycord.db-wal
daycord.db-shm
```

These files are part of SQLite's runtime persistence behavior.

Do not copy an active database using a simple filesystem copy and assume the result is a consistent backup.

Use the application's SQLite backup mechanism.

## 12. Database Integrity

The migration runner validates the database with:

```sql
PRAGMA integrity_check;
```

The expected result is:

```text
ok
```

It also executes:

```sql
PRAGMA foreign_key_check;
```

The expected result is no rows.

These checks are also relevant when validating backups and recovery copies.

### 12.1 Manual Inspection

After creating an isolated database copy using a SQLite-consistent backup mechanism, you can inspect it using the SQLite CLI:

```bash
sqlite3 /path/to/test-daycord.db \
  "PRAGMA integrity_check;"
```

```bash
sqlite3 /path/to/test-daycord.db \
  "PRAGMA foreign_key_check;"
```

Migration history can be inspected with:

```bash
sqlite3 /path/to/test-daycord.db \
  "SELECT Version, Name, Checksum, AppliedAtUtc
   FROM __SchemaMigrations
   ORDER BY Version;"
```

These commands are examples for a test database.

Do not perform destructive operations on the working user database.

## 13. Backup and Recovery

The backup and restoration subsystem uses SQLite-aware snapshot operations.

Key components are:

- `DatabaseBackupService`
- `UserBackupService`
- `SqliteSnapshotIO`
- `SqliteSnapshotValidator`
- `PendingRestoreCoordinator`

Schema upgrade backups and user-created backups serve separate purposes.

Restoration is deferred until the next application startup so the database can be replaced without active application connections.

Before installing a restored database, Daycord creates a safety backup.

The restored database must pass initialization before restoration is finalized.

See [Backup and Recovery](backup-and-recovery.md).

## 14. Report Preset Compatibility

The report preset repository contains a compatibility mechanism for an older built-in preset name.

It changes only the known legacy built-in record when appropriate and avoids overwriting user-created presets.

This behavior is intentionally separate from SQL migration 001.

Do not remove this compatibility mechanism without reviewing existing databases and release compatibility requirements.

Do not rewrite migration 001 merely to change the historical default preset seed.

## 15. Data Safety Rules

The following rules apply to all future development:

- Never delete the user's database as a routine troubleshooting action.
- Never modify already applied migrations.
- Never disable migration checksum checks.
- Never replace an active database while normal application connections are open.
- Never use ordinary file copying as a substitute for SQLite-aware backup.
- Never assume an unknown SQLite schema belongs to Daycord.
- Never silently discard tasks, time entries, settings, or report presets.
- Always test destructive and recovery scenarios in an isolated environment.
- Preserve internal safety backups until the user or an explicit retention policy handles them.

## 16. Related Documentation

- [Architecture](architecture.md)
- [Development](development.md)
- [Backup and Recovery](backup-and-recovery.md)
- [Testing](testing.md)

This document describes the database implementation of Daycord 0.1.0.