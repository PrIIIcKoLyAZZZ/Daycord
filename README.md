# Daycord

Daycord — локальное кроссплатформенное desktop-приложение для планирования
задач, учёта фактически выполненной работы и формирования ежемесячных
Excel-отчётов.

Целевые платформы: macOS ARM64 и Windows x64.

Приложение работает локально, без отдельного сервера, обязательной
регистрации и постоянного интернет-соединения.

## Technology Stack

| Component | Technology |
|---|---|
| Language | C# |
| Runtime | .NET 10 |
| Solution | Daycord.slnx |
| UI | Avalonia 12 |
| MVVM | CommunityToolkit.Mvvm |
| DI | Microsoft.Extensions.DependencyInjection |
| Database | SQLite |
| Data access | Dapper 2.1.89 |
| SQLite provider | Microsoft.Data.Sqlite 10.0.13 |
| Migrations | Embedded versioned SQL scripts |
| Excel | ClosedXML (planned) |

Entity Framework Core is not used.

## Solution Architecture

Daycord consists of three projects.

### Daycord.Core

Contains domain entities and application contracts:

- TaskItem
- TaskStatusDefinition
- PlanningPeriod
- WorkEntry
- IDatabaseInitializer

Core does not depend on Infrastructure or Desktop.

### Daycord.Infrastructure

Contains:

- AppDataPaths
- SqliteConnectionFactory
- SqliteTypeHandlers
- DatabaseInitializer
- MigrationRunner
- DatabaseBackupService
- Embedded SQL migrations

Infrastructure depends on Core.

### Daycord.Desktop

Contains:

- Avalonia views
- MVVM view models
- Dependency injection configuration
- Application startup
- Graphite & Amber theme resources

Desktop depends on Core and Infrastructure.

## Build and Run

Requires .NET SDK 10.

From the repository root:

```bash
dotnet restore Daycord.slnx
dotnet build Daycord.slnx
dotnet run --project src/Daycord.Desktop/Daycord.Desktop.csproj
```

The solution uses Central Package Management through
`Directory.Packages.props`.

The application must not require a network connection after installation.

## Local Database

The database file is named:

`daycord.db`

Its location is determined through:

`Environment.SpecialFolder.LocalApplicationData`

Expected paths:

macOS:

`~/Library/Application Support/Daycord/daycord.db`

Windows:

`%LOCALAPPDATA%\Daycord\daycord.db`

The database is created automatically during the first application startup.

Existing databases are never deleted or silently recreated by the initializer.

### Tables

The initial schema contains:

- TaskStatuses
- Tasks
- PlanningPeriods
- WorkEntries
- __SchemaMigrations

PlanningPeriods and WorkEntries are independent records.

Durations are stored in integer minutes.

Foreign keys and CHECK constraints protect referential integrity.

Dependent records use RESTRICT deletion rules.

## Built-in Task Statuses

| SystemKey | Name | IsCompleted |
|---|---|---|
| open | Open | false |
| completed | Completed | true |

Permanent identifiers:

Open:

`5e8a148e-1a3b-4b17-bba6-251392a01d61`

Completed:

`b9436dc3-7247-4d9e-a032-2a9cb1b9c7a8`

Tasks reference statuses through `TaskItem.StatusId`.

The database supports custom statuses, but the status editor is not
implemented in DC-003.

## Data Serialization

The SQLite storage conventions are:

| C# type | SQLite type | Format |
|---|---|---|
| Guid | TEXT | D |
| DateOnly | TEXT | yyyy-MM-dd |
| DateTime UTC | TEXT | ISO 8601 UTC |
| bool | INTEGER | 0 or 1 |
| int | INTEGER | Integer |

All technical timestamps must represent UTC unambiguously.

Dapper type handlers are centralized in Infrastructure.

## Automatic Database Migrations

Migrations are embedded into the Infrastructure assembly.

Migration files:

```text
Persistence/
└── Migrations/
    ├── 001_InitialSchema.sql
    └── 002_NormalizeMigrationUtcTimestamps.sql
```

Migrations are applied automatically during application startup.

The `__SchemaMigrations` table contains:

- Version
- Name
- Checksum
- AppliedAtUtc

Checksums are calculated with SHA-256.

Previously applied migrations are validated before applying new versions.

An unknown migration version, modified checksum, or unsupported database
schema prevents further initialization.

Migration execution is transactional.

An error causes a rollback.

Published migration files must not be modified.

## Migration 001 — Initial Schema

Creates the domain tables, foreign keys, constraints, indexes,
and built-in statuses.

This migration is immutable.

Its original contents and checksum must remain unchanged.

## Migration 002 — UTC Timestamp Normalization

The original DC-003 migration runner inserted `DateTime.UtcNow`
into SQLite through Microsoft.Data.Sqlite.

The resulting historical representation did not include an explicit
timezone marker.

Example:

`2026-10-19 20:43:14.657501`

The reworked MigrationRunner writes new migration timestamps as
invariant ISO 8601 UTC strings with an explicit `Z`.

Example:

`2026-10-19T20:43:14.6575010Z`

Migration 002 converts the recognized historical representation:

Before:

`2026-10-19 20:43:14.657501`

After:

`2026-10-19T20:43:14.657501Z`

The historical fractional precision is preserved without rounding.

Conversion is limited to the recognized format associated with
the original UTC-writing code path.

Unrecognized formats are rejected rather than interpreted
using the current system timezone.

Migration 002 does not modify Tasks, PlanningPeriods, WorkEntries,
or TaskStatuses.

For a newly created database, both migrations are applied in order.

For an existing database at version 001, only version 002 is pending.

## Database Backups

Before upgrading an existing recognized database, Daycord creates
a protective backup using:

`SqliteConnection.BackupDatabase()`

Backups are stored under:

`<Daycord data directory>/Backups/SchemaUpgrades/`

SQLite Backup API is used instead of directly copying a potentially
open database file.

Backup files are retained after successful upgrades.

A database without pending migrations does not require a new backup.

The application does not automatically restore or overwrite an
existing database after a migration failure.

## Database Integrity

Initialization checks:

- Migration history structure
- Applied versions
- Migration checksums
- Required domain tables
- Built-in status identifiers
- Migration UTC timestamp formats
- PRAGMA integrity_check
- PRAGMA foreign_key_check

Foreign key enforcement is enabled for opened application connections.

WAL journaling is attempted when supported.

The application may retain another valid journal mode when WAL
is unavailable.

## Application Startup

The Desktop application uses asynchronous database initialization.

The MainWindowViewModel initially displays:

`Initializing local database...`

After successful initialization:

`Local database ready.`

On initialization failure:

`Database initialization failed: ...`

The application must not display a successful database state
before initialization completes.

Data-dependent features must not be activated after a failed initialization.

All user-visible messages introduced in DC-003 are in English.

Russian localization will be implemented in a later task.

## Safe Migration Verification

Never delete or manually modify the real user database to test
initialization or migrations.

A database snapshot can be created using SQLite Backup API.

On macOS:

```bash
DB="$HOME/Library/Application Support/Daycord/daycord.db"

TEST_DIR="$(mktemp -d)"

sqlite3 "$DB" ".backup '$TEST_DIR/daycord.db'"
```

This creates an isolated database snapshot.

The normal Desktop application still uses the real
LocalApplicationData directory.

Therefore, do not assume that launching the normal Desktop project
will use the test database.

For testing the C# initializer in isolation, construct
`AppDataPaths` with the test directory and resolve the initializer
using that instance.

An isolated diagnostic harness must remain outside the production
Solution and must not add application features or unit-test projects.

After running the initializer against the isolated database,
check the migration history:

```bash
sqlite3 "$TEST_DIR/daycord.db" \
"SELECT Version, Name, AppliedAtUtc
 FROM __SchemaMigrations
 ORDER BY Version;"
```

Expected migration versions:

- 1 — 001_InitialSchema.sql
- 2 — 002_NormalizeMigrationUtcTimestamps.sql

Check statuses:

```bash
sqlite3 "$TEST_DIR/daycord.db" \
"SELECT SystemKey, Name, IsCompleted
 FROM TaskStatuses
 ORDER BY SortOrder;"
```

Expected statuses:

- open / Open / 0
- completed / Completed / 1

Check database integrity:

```bash
sqlite3 "$TEST_DIR/daycord.db" \
"PRAGMA integrity_check;"
```

Expected result:

`ok`

Check foreign keys:

```bash
sqlite3 "$TEST_DIR/daycord.db" \
"PRAGMA foreign_key_check;"
```

Expected result:

No rows.

A complete verification must also include:

1. Repeated initialization without duplicate migrations.
2. Repeated initialization without duplicate system statuses.
3. Checksum mismatch detection.
4. Unknown migration version handling.
5. Protective backup before schema upgrade.
6. Rollback on migration failure.
7. Positive-duration CHECK constraints.
8. Foreign key enforcement.
9. Confirmation that user records remain unchanged.

All destructive or error-injection checks must use isolated databases.

## Current MVP Scope

DC-003 implements database infrastructure and startup initialization.

The following are intentionally excluded:

- Task CRUD
- Calendar
- Dashboard
- Custom status editor
- Full localization
- Excel reporting
- User backup/restore interface
- Cloud synchronization

## Documentation

Architecture decisions:

`docs/architecture/DC-001.md`

Further work requires a separate development task.