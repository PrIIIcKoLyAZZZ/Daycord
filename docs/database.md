# Daycord Database

**Target version:** 0.1.1

## 1. Overview

Daycord uses SQLite for local data persistence.

The database stores:

- Tasks and task statuses.
- Planning periods.
- Actual work entries.
- Report presets and settings.
- Application preferences.
- Database migration history.

The persistence layer uses:

- `Microsoft.Data.Sqlite` for database connections.
- `Dapper` for SQL queries and object mapping.
- Embedded SQL migrations for schema management.
- Explicit type handlers for GUIDs, dates, and UTC timestamps.

Entity Framework Core is not used.

Database initialization is performed during application startup.

## 2. Database Location

Database paths are managed by:

```text
src/Daycord.Infrastructure/Persistence/AppDataPaths.cs
```

The database filename is:

```text
daycord.db
```

### Release Locations

**macOS:**

```text
~/Library/Application Support/Daycord/daycord.db
```

**Windows:**

```text
%LOCALAPPDATA%\Daycord\daycord.db
```

The application resolves its data directory using `Environment.SpecialFolder.LocalApplicationData`.

### Debug Environment

The Desktop application's Debug configuration uses a separate data directory:

```text
Daycord-test/
```

The Release configuration uses:

```text
Daycord/
```

This separation helps prevent normal development activity from modifying the Release database.

Tests that perform destructive operations must use their own isolated databases and must not rely on the user's application data directory.

### Related Storage

The application data directory also contains internal backup, recovery, and diagnostic files.

Their purposes and lifecycle are documented in [Backup and Recovery](backup-and-recovery.md).

Do not remove database files or internal recovery artifacts to resolve startup failures.

## 3. Database Connections

SQLite connections are created through `SqliteConnectionFactory`.

Normal application connections use:

| Setting | Value |
|---|---|
| Mode | `ReadWriteCreate` |
| ForeignKeys | `true` |
| Pooling | `false` |
| DefaultTimeout | 15 seconds |

Foreign key enforcement must be enabled for normal operations.

The migration runner verifies foreign key enforcement before applying migrations.

Connections should be disposed after their operations complete.

Connection pooling is disabled to keep connection lifetimes explicit, particularly during restoration.

## 4. Database Schema

The current database contains seven application tables and one migration history table.

| Table | Responsibility |
|---|---|
| `TaskStatuses` | Task status definitions |
| `Tasks` | Primary task records |
| `PlanningPeriods` | Planned work durations |
| `WorkEntries` | Actual work records |
| `ReportPresets` | Excel report configurations |
| `ReportSettings` | Active report preset |
| `AppSettings` | Application preferences |
| `__SchemaMigrations` | Applied schema migration history |

### 4.1 TaskStatuses

Stores task status definitions.

Important columns:

- `Id` — GUID primary key.
- `SystemKey` — stable key for built-in statuses.
- `Name` — non-empty display name.
- `IsSystem` — built-in status flag.
- `IsCompleted` — completion flag.
- `SortOrder` — display order.

Built-in statuses:

| SystemKey | Default name | IsCompleted |
|---|---|---|
| `open` | Open | `0` |
| `completed` | Completed | `1` |

The application uses stable system identifiers and the `IsCompleted` property.

Completion logic must not depend on localized status names.

### 4.2 Tasks

Stores task information.

Important columns:

| Column | Description |
|---|---|
| `Id` | GUID primary key |
| `TaskDate` | Optional task date |
| `TaskNumber` | Optional user-defined number |
| `Title` | Required title |
| `ShortDescription` | Optional short description |
| `DescriptionMarkdown` | Optional Markdown content |
| `StatusId` | Reference to TaskStatuses |
| `CreatedAtUtc` | Creation timestamp |
| `UpdatedAtUtc` | Modification timestamp |
| `CompletedAtUtc` | Optional completion timestamp |

`TaskDate` is a calendar date, not a timestamp.

The task's planning and actual work records have their own independent dates.

### 4.3 PlanningPeriods

Stores planned work associated with tasks.

| Column | Description |
|---|---|
| `Id` | GUID primary key |
| `TaskId` | Reference to Tasks |
| `PlannedDate` | Planned work date |
| `PlannedMinutes` | Planned duration in minutes |

`PlannedMinutes` must be greater than zero.

A task can have multiple planning periods.

`PlannedDate` does not have to equal the task's `TaskDate`.

### 4.4 WorkEntries

Stores actual work performed on tasks.

| Column | Description |
|---|---|
| `Id` | GUID primary key |
| `TaskId` | Reference to Tasks |
| `WorkDate` | Date of performed work |
| `ActualMinutes` | Actual duration in minutes |
| `WorkDescription` | Optional work description |
| `Note` | Optional note |
| `CreatedAtUtc` | Creation timestamp |
| `UpdatedAtUtc` | Modification timestamp |

`ActualMinutes` must be greater than zero.

A task can contain multiple work entries.

`WorkDate` is independent of TaskDate and PlannedDate.

### 4.5 ReportPresets

Stores configurable Excel report layouts.

Important columns:

- `Id` — GUID primary key.
- `Name` — preset name.
- `IsBuiltIn` — built-in preset flag.
- `ColumnsJson` — JSON column configuration.
- `CreatedAtUtc` — creation timestamp.
- `UpdatedAtUtc` — modification timestamp.

The database contains a built-in Default preset with a stable identifier.

Preset names are unique using case-insensitive comparison.

`ColumnsJson` must contain syntactically valid JSON.

Application-level validation additionally controls supported fields, column configurations, and headings.

Built-in report headings use language-independent tokens, resolved by the application.

Custom headings are stored as literal text.

### 4.6 ReportSettings

Stores the active report preset.

The table contains a singleton record:

```text
SingletonId = 1
```

`ActivePresetId` references `ReportPresets`.

The active preset must exist.

When the active custom preset is deleted through the repository, the application selects the Default preset within the same transaction.

### 4.7 AppSettings

Stores application preferences.

| Column | Description |
|---|---|
| `SingletonId` | Singleton primary key |
| `Language` | Application language code |
| `Theme` | Appearance setting |
| `MinutesPerDay` | Minutes represented by one day |

Defaults:

| Setting | Value |
|---|---|
| Language | `en` |
| Theme | `System` |
| MinutesPerDay | `1440` |

Allowed theme values:

- `System`
- `Light`
- `Dark`

`MinutesPerDay` must be an integer between 1 and 1,440.

The SQL schema does not restrict language codes to a fixed set.

Supported languages are validated through the application's language model.

### 4.8 __SchemaMigrations

Stores the history of applied schema migrations.

| Column | Description |
|---|---|
| `Version` | Migration version |
| `Name` | Migration filename |
| `Checksum` | SHA-256 checksum |
| `AppliedAtUtc` | UTC application timestamp |

This table is managed by `MigrationRunner`.

Applied migration entries must match the corresponding embedded SQL resources.

## 5. Relationships and Deletion

Main relationships:

```text
TaskStatuses
    |
    +-- Tasks
          |
          +-- PlanningPeriods
          |
          +-- WorkEntries

ReportPresets
    |
    +-- ReportSettings

AppSettings
    (singleton)

__SchemaMigrations
    (migration history)
```

Foreign keys use `ON DELETE RESTRICT`.

The application does not rely on SQLite cascading deletion for tasks.

When deleting a task, `SqliteTaskRepository` explicitly removes:

1. Associated WorkEntries.
2. Associated PlanningPeriods.
3. The Task record.

The operations execute within a transaction.

Do not change foreign key deletion behavior without reviewing the repository implementation.

## 6. Data Types and Serialization

### GUIDs

GUID values are stored as SQLite TEXT using the standard `D` format.

Example:

```text
5e8a148e-1a3b-4b17-bba6-251392a01d61
```

Dapper uses the registered GUID type handler.

### Calendar Dates

Date-only values are stored as:

```text
yyyy-MM-dd
```

Example:

```text
2026-11-11
```

Date-only fields include:

- `Tasks.TaskDate`
- `PlanningPeriods.PlannedDate`
- `WorkEntries.WorkDate`

These values do not contain a time or timezone.

### UTC Timestamps

Application-generated .NET `DateTime` values are stored using the round-trip `"O"` format.

Example:

```text
2026-11-11T12:34:56.0000000Z
```

The Dapper UTC DateTime handler requires a UTC value.

Other SQLite-generated timestamps and migration-history timestamps may use compatible ISO 8601 UTC representations according to their respective validation rules.

Do not replace UTC timestamps with local or timezone-less values.

### Durations

Planned and actual durations are stored as integer minutes.

For example:

```text
1 hour 30 minutes = 90 minutes
```

The configurable day length affects duration input and presentation.

Changing `MinutesPerDay` does not modify previously saved durations.

## 7. Schema Migrations

Database migrations are managed by:

```text
src/Daycord.Infrastructure/Persistence/MigrationRunner.cs
```

Migration resources are embedded in the Infrastructure assembly.

Current migrations:

| Version | File | Purpose |
|---|---|---|
| 001 | `001_InitialSchema.sql` | Core schema, tasks, time records, and reports |
| 002 | `002_ApplicationSettings.sql` | Application preferences |

No additional migration is required for the backup and recovery mechanisms introduced in version 0.1.1.

### Initialization

On application startup, `MigrationRunner`:

1. Loads the embedded SQL migrations.
2. Opens the SQLite database.
3. Starts an immediate transaction.
4. Verifies foreign key enforcement.
5. Checks existing migration history.
6. Determines which migrations are pending.
7. Creates a safety backup when upgrading an existing recognized database.
8. Applies pending migrations in order.
9. Records migration names, checksums, and timestamps.
10. Verifies database integrity and required system data.
11. Commits the transaction.
12. Attempts to enable WAL journal mode.

If an initialization operation fails, the migration transaction is rolled back.

### Migration History Validation

Applied migrations must have:

- Recognized sequential versions.
- Expected filenames.
- Matching SHA-256 checksums.
- Valid UTC application timestamps.

Applied migrations are checked against the embedded SQL resources.

An existing database without recognized migration history is not silently recreated or overwritten.

A database containing unknown or newer migrations is not automatically downgraded.

### Migration Development Rules

When changing the schema:

1. Create a new sequentially numbered migration.
2. Preserve existing migration files unchanged.
3. Maintain compatibility with existing user data.
4. Respect foreign keys and application invariants.
5. Test upgrades using an isolated copy of an existing database.

**Never edit an already published migration.**

Changing its SQL text changes its checksum and can prevent existing databases from initializing.

Migration checksum errors are not automatically repaired by Emergency Recovery.

## 8. Schema Upgrade Backups

Before applying pending schema migrations to a recognized existing database, `DatabaseBackupService` creates a verified SQLite snapshot.

The backup is stored in:

```text
Backups/SchemaUpgrades/
```

The backup is created through the SQLite Online Backup API and validated before publication.

If the pre-upgrade backup cannot be created or validated, the migration does not proceed.

A schema-upgrade backup is separate from:

- User backups.
- Automatic rotating backups.
- Pre-restore safety backups.

Schema upgrades do not require deleting or recreating the user's database.

## 9. SQLite WAL and Sidecar Files

Daycord attempts to use SQLite Write-Ahead Logging (WAL) after migration initialization.

SQLite may create:

```text
daycord.db-wal
daycord.db-shm
```

Depending on the active journal mode and transaction state, a journal file may also exist.

These files can contain information required for correct database operation.

**Do not copy or delete the active database file independently of its SQLite sidecars.**

Daycord creates consistent backups through the SQLite Online Backup API rather than copying the active `.db` file directly.

Published snapshots are normalized into standalone database files.

### Restore Handling

Normal restoration coordinates WAL checkpointing and journal-mode transition before replacing the active database.

Emergency recovery preserves the original SQLite file set without attempting to open the corrupted database through SQLite.

The original files are archived and moved into quarantine before a verified replacement is installed.

Recovery does not treat an arbitrary copied database file as a valid snapshot.

See [Backup and Recovery](backup-and-recovery.md).

## 10. Database Integrity and Safety

Database safety relies on several independent checks.

### MigrationRunner

Responsible for:

- Migration history verification.
- Schema updates.
- Required built-in data validation.
- SQLite integrity checks.
- Foreign key checks.

### SqliteSnapshotValidator

Responsible for validating standalone backup and recovery candidates.

Validation includes:

- Database integrity.
- Foreign keys.
- Required database structure.
- Migration compatibility.
- Required application configuration.

The validator does not repair invalid data.

### PendingRestoreCoordinator

Responsible for installing a previously prepared database state during startup.

It does not define or apply schema migrations.

### Emergency Recovery

Responsible for attempting restoration from a verified automatic backup after eligible corruption.

It does not repair arbitrary schema or migration errors.

## 11. Developer Guidelines

### Working with Test Data

Use isolated test databases.

Do not perform destructive checks against the user's Release database.

When inserting test data through SQL:

- Use correctly formatted GUIDs.
- Store date-only values as `yyyy-MM-dd`.
- Use valid UTC timestamp formats.
- Respect foreign key relationships.
- Respect positive-minute constraints.
- Preserve required singleton records and system statuses.

### Read-Only Inspection

Useful SQLite integrity commands:

```sql
PRAGMA integrity_check;
PRAGMA foreign_key_check;
```

Migration history can be inspected with:

```sql
SELECT Version, Name, Checksum, AppliedAtUtc
FROM __SchemaMigrations
ORDER BY Version;
```

Run diagnostics against an isolated copy when possible.

Do not edit migration history to bypass validation.

### Troubleshooting

If database initialization fails:

1. Preserve the complete application data directory.
2. Do not delete `daycord.db`.
3. Do not remove WAL, SHM, or restore journal files.
4. Check the application's diagnostic information.
5. Verify the failure using an isolated copy of the data.
6. Review backup and recovery state before attempting restoration.

Never delete user data merely to force the application to start.

## 12. Related Documentation

- [Architecture](architecture.md) — Application layers and responsibilities.
- [Backup and Recovery](backup-and-recovery.md) — SQLite backup, restore, and emergency recovery.
- [Development](development.md) — Development conventions and project setup.
- [Testing](testing.md) — Automated database checks.
- [User Guide](user-guide.md) — User-facing backup and restore instructions.