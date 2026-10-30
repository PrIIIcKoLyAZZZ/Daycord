# Daycord Backup and Recovery

**Version:** 0.1.0

## 1. Purpose

This document describes the technical implementation of SQLite backup and restoration in Daycord.

The backup subsystem is designed to preserve user data while allowing:

- User-created database backups.
- Validation of selected backup files.
- Deferred restoration after application restart.
- Automatic safety backups before restoration.
- Recovery from certain interrupted operations.
- Compatibility with supported older database migrations.

This document describes implemented mechanisms, not a guarantee that every possible filesystem or hardware failure can be recovered from.

## 2. Core Principles

The implementation follows these rules:

1. Never replace the active SQLite database during normal application use.
2. Create database backups through SQLite-aware snapshot operations.
3. Validate snapshots before making them available for restoration.
4. Do not modify the user-selected source backup.
5. Stage a restoration before restarting the application.
6. Create a safety backup before changing the working database.
7. Track restoration progress in a durable journal.
8. Validate the restored database before enabling normal application features.
9. Attempt rollback if installation or initialization fails.
10. Retain safety backups rather than deleting them automatically.

Destructive testing must use an isolated test environment.

## 3. Components

### 3.1 Application Contract

```text
src/Daycord.Core/Application/Contracts/IUserBackupService.cs
```

`IUserBackupService` defines:

```csharp
Task CreateBackupAsync(
    string destinationPath,
    CancellationToken cancellationToken = default);

Task<BackupValidationResult> ValidateBackupAsync(
    string sourcePath,
    CancellationToken cancellationToken = default);

Task PrepareRestoreAsync(
    string sourcePath,
    CancellationToken cancellationToken = default);
```

The validation result uses the statuses:

- `Valid`
- `InvalidFile`
- `Corrupted`
- `IncompatibleSchema`

### 3.2 Infrastructure Components

| Component | Responsibility |
|---|---|
| `UserBackupService` | User backup creation, validation, and restore preparation |
| `SqliteSnapshotIO` | SQLite snapshot creation and durable file copying |
| `SqliteSnapshotValidator` | Integrity, schema, and migration compatibility checks |
| `PendingRestoreState` | Durable restore journal model |
| `PendingRestoreCoordinator` | Startup restoration and rollback |
| `DatabaseBackupService` | Safety backup for schema upgrades |
| `AppDataPaths` | Application database and internal directories |

### 3.3 Desktop Components

The user-facing workflow is implemented through:

```text
src/Daycord.Desktop/ViewModels/BackupSettingsViewModel.cs
src/Daycord.Desktop/Views/BackupSettingsView.axaml
src/Daycord.Desktop/Views/BackupSettingsView.axaml.cs
```

The startup restoration sequence is coordinated by:

```text
src/Daycord.Desktop/App.axaml.cs
```

The backup controls are displayed under application Settings.

## 4. Application Data Paths

`AppDataPaths` determines the local application data directory.

The main database is:

```text
Daycord/daycord.db
```

The internal backup layout is:

```text
Daycord/
├── daycord.db
├── .daycord-instance.lock
├── Backups/
│   ├── SchemaUpgrades/
│   └── BeforeRestore/
└── RestoreStaging/
    ├── pending-restore.json
    └── pending-<operation-id>.db
```

The instance lock file and restoration staging files are internal implementation details.

The application may also create temporary files during backup, validation, or installation.

Do not manually remove these files while the application is running.

### 4.1 User Backups

User-created backups are saved to the destination selected in the file picker.

They do not have to be stored in Daycord's internal data directory.

The normal filename suggestion is:

```text
Daycord-Backup-yyyy-MM-dd-HHmmss.db
```

### 4.2 Schema Upgrade Backups

Schema upgrade backups are placed under:

```text
Backups/SchemaUpgrades/
```

They are created by `DatabaseBackupService` for recognized schema upgrades.

### 4.3 Pre-Restore Safety Backups

Pre-restore safety backups are placed under:

```text
Backups/BeforeRestore/
```

Their filenames follow the pattern:

```text
daycord-before-restore-<UTC timestamp>-<GUID>.db
```

These copies are retained after successful restoration and rollback.

## 5. SQLite Backup API

SQLite databases may use write-ahead logging (WAL).

When WAL is active, some recent transactions may reside in a WAL file rather than the main database file.

Therefore, directly copying a running `daycord.db` file is not a reliable backup strategy.

Daycord uses the SQLite online backup API through:

```csharp
SqliteConnection.BackupDatabase(...)
```

The implementation is centralized in:

```text
SqliteSnapshotIO.CreateStandaloneSnapshot
```

### 5.1 Snapshot Creation

The snapshot operation:

1. Opens the source SQLite database using a read-only connection.
2. Creates a destination SQLite database.
3. Copies the database through the SQLite backup API.
4. Converts the destination journal mode to `DELETE`.
5. Closes the database connections.
6. Confirms that no SQLite sidecar files remain.

A complete snapshot is represented by a standalone SQLite `.db` file.

### 5.2 Sidecar Validation

The snapshot implementation checks for:

```text
-wal
-shm
-journal
```

If sidecar files remain when a standalone snapshot is required, the operation fails rather than publishing an uncertain copy.

### 5.3 Copying Completed Snapshots

After a database snapshot has been finalized and validated, Daycord may copy the completed file through a durable file-copy operation.

This must not be confused with copying the active SQLite database.

`SqliteSnapshotIO.CopyDurably` is used for copying previously created standalone snapshots during recovery.

## 6. Creating a User Backup

The user-facing operation begins in Settings.

The sequence is:

```text
Settings
   |
   v
Create backup
   |
   v
Create temporary SQLite snapshot
   |
   v
Validate snapshot
   |
   v
Open Save File Picker
   |
   v
Write temporary output at destination
   |
   v
Compare SHA-256 checksums
   |
   v
Publish final backup file
```

### 6.1 Temporary Snapshot

The Desktop layer first requests a validated backup in the application's private staging directory.

This ensures that the destination file picker is not used to write an incomplete SQLite database.

### 6.2 Destination Validation

Before saving, the Desktop layer checks that:

- The destination is a local filesystem path.
- The filename has the `.db` extension.
- The directory is available.
- The destination is not a symbolic link.
- The destination is outside Daycord's protected internal data directory.

The code resolves directory symlinks to prevent a destination from indirectly pointing into protected application storage.

### 6.3 Atomic Publication

The Desktop layer writes the snapshot into a temporary file in the destination directory.

It calculates and compares SHA-256 hashes before publishing the final file.

Publication uses a same-directory file move, allowing the final name to appear only after the output contents have been written and verified.

If the user has approved overwriting an existing backup, the final move can replace it.

### 6.4 Cancellation

If the user cancels the file picker, no final backup file is published.

Temporary files are cleaned up as part of the operation.

### 6.5 Concurrent Operations

`UserBackupService` uses a semaphore to reject overlapping backup or restore operations.

The UI also prevents conflicting backup actions while an operation is active.

## 7. Validating a Backup

A backup must pass validation before it can be prepared for restoration.

The service does not run validation or migrations directly against the user-selected backup.

Instead, it creates a separate snapshot in private staging.

The validator works against that snapshot.

### 7.1 File Integrity

Validation runs:

```sql
PRAGMA integrity_check;
```

The expected result is:

```text
ok
```

It also runs:

```sql
PRAGMA foreign_key_check;
```

The expected result is no rows.

A failure indicates a corrupted or inconsistent snapshot.

### 7.2 Required Schema

The validator checks for the expected Daycord schema.

Required core tables include:

- `__SchemaMigrations`
- `TaskStatuses`
- `Tasks`
- `PlanningPeriods`
- `WorkEntries`
- `ReportPresets`
- `ReportSettings`

For migration 002 databases, AppSettings is also required.

The validator checks required indexes and columns.

A generic SQLite file containing unrelated tables is not accepted as a Daycord backup.

### 7.3 Migration History

Validation verifies the migration history against embedded migration resources.

Checks include:

- Known migration versions.
- Correct migration order.
- Exact migration filenames.
- SHA-256 checksums.
- Valid UTC migration timestamps.

A backup with modified or unknown migration history is not accepted.

### 7.4 System Data

Validation checks the identity and completion semantics of the built-in task statuses.

It also validates the built-in report preset and the active report settings relationship.

For databases containing migration 002, it checks the AppSettings structure and persisted values.

### 7.5 Validation Results

| Status | Meaning |
|---|---|
| `Valid` | Supported and internally consistent Daycord database |
| `InvalidFile` | Missing or invalid file |
| `Corrupted` | SQLite integrity or foreign key validation failure |
| `IncompatibleSchema` | Unsupported or inconsistent Daycord schema |

A validation result is meaningful only for the actual file contents examined at that time.

The service therefore validates the source again during restore preparation.

## 8. Backup Version Compatibility

Daycord 0.1.0 contains two migrations:

```text
001_InitialSchema.sql
002_ApplicationSettings.sql
```

The validator supports a compatible prefix of the known migration history.

Therefore, a valid database containing migration 001 can be accepted even though it does not yet contain AppSettings.

After restoration, normal database initialization can apply migration 002.

### 8.1 Migration 001 Backup

A migration 001 backup:

- Contains the original application schema.
- Does not contain the AppSettings table.
- Must have valid migration history.
- Must pass integrity and schema checks.

After installation, the migration runner applies the missing AppSettings migration.

### 8.2 Migration 002 Backup

A migration 002 backup must contain the application settings schema and valid persisted settings.

Application settings are loaded from the restored database.

### 8.3 Unsupported Versions

Backups with unknown or incompatible migration versions are rejected.

Do not manually edit migration history or disable SHA-256 verification to force compatibility.

## 9. Preparing a Restoration

Restoration preparation occurs while Daycord is running.

It does not replace the live database.

The sequence is:

```text
Choose backup file
        |
        v
Copy selected file to private staging
        |
        v
Validate snapshot
        |
        v
Display confirmation
        |
        v
User confirms restoration
        |
        v
Create new standalone staged snapshot
        |
        v
Revalidate snapshot
        |
        v
Calculate staged SHA-256
        |
        v
Create pending restore journal
        |
        v
Request normal application restart
```

### 9.1 User Confirmation

The UI warns that restoration will replace current data.

The user may cancel the operation before confirmation.

### 9.2 Staging

`UserBackupService.PrepareRestoreAsync` creates a new independently validated snapshot.

This matters because the selected source file may have changed since initial validation.

The staged snapshot is stored as:

```text
RestoreStaging/pending-<operation-id>.db
```

### 9.3 Pending Journal

The operation is recorded in:

```text
RestoreStaging/pending-restore.json
```

The journal contains:

- Operation identifier.
- Staged filename.
- SHA-256 hash of the staged database.
- Restoration phase.
- Safety backup filename, when created.
- Safety backup SHA-256, when created.

The journal uses internal filenames rather than arbitrary user-provided absolute paths.

Journal writes use a temporary file and atomic replacement.

### 9.4 Pending Restore Protection

Only one restore operation may be scheduled at a time.

If a pending restore journal already exists, another restoration cannot be prepared until the existing operation has been resolved.

## 10. Restoration State Machine

The journal uses the following phases:

| Phase | Description |
|---|---|
| `Prepared` | Validated backup is staged |
| `SafetyCreated` | A pre-restore safety backup has been created |
| `Installing` | Replacement of the working database is in progress |
| `Installed` | Staged database has been installed |
| `RollingBack` | Recovery to the previous database is in progress |

These phases allow startup logic to distinguish a prepared restore from an interrupted installation.

Do not treat the existence of a database file alone as proof that restoration succeeded.

## 11. Startup Restoration

The restoration coordinator is:

```text
PendingRestoreCoordinator
```

It is invoked during application startup before normal database initialization.

### 11.1 Application Lock

The coordinator acquires a lifetime lock:

```text
.daycord-instance.lock
```

The lock prevents another Daycord instance from simultaneously using the same application data directory through the normal startup path.

The lock remains held during the application lifetime.

### 11.2 Applying a Prepared Restore

When a pending journal exists:

1. Read and validate the journal.
2. Verify the staged database SHA-256 hash.
3. Validate the staged SQLite snapshot.
4. Create a new safety backup of the current database.
5. Validate the safety backup.
6. Record the safety backup filename and hash.
7. Create a standalone installation snapshot.
8. Checkpoint and leave WAL mode on the current database.
9. Record the `Installing` phase.
10. Replace the working database with the installation snapshot.
11. Validate the installed database.
12. Record the `Installed` phase.

### 11.3 WAL Handling

Before replacing the working database, the coordinator checks its journal mode.

If WAL is active, the implementation attempts:

```sql
PRAGMA wal_checkpoint(TRUNCATE);
```

It then switches to:

```sql
PRAGMA journal_mode = DELETE;
```

The coordinator verifies that no SQLite sidecar files remain before file replacement.

Failure to complete these steps blocks the replacement.

### 11.4 Database Initialization

After installation, Daycord performs normal database initialization.

This includes applying supported pending migrations.

Application settings are then loaded from the resulting database.

### 11.5 Completing Restoration

A restoration is finalized only after:

- Database initialization succeeds.
- Pending migrations succeed.
- Application settings load.
- Final installed database validation succeeds.

On success, the pending journal is deleted and the staged backup is cleaned up.

The pre-restore safety backup is retained.

## 12. Rollback and Failure Handling

The coordinator includes rollback logic for failures during installation or initialization.

### 12.1 Failed Initialization

If a restored database cannot complete initialization, Daycord attempts rollback using the validated safety backup.

The application blocks normal startup and reports that a restart is required after recovery.

### 12.2 Interrupted Installation

If startup finds an operation left in `Installing` or `RollingBack`, it treats the previous operation as interrupted.

It attempts rollback rather than assuming the installed database is correct.

The application requests another restart after recovery.

### 12.3 Rollback Procedure

Rollback:

1. Validates the safety backup.
2. Records `RollingBack` in the journal.
3. Creates a durable copy of the safety backup.
4. Validates the copy and its SHA-256 hash.
5. Prepares the current database for replacement.
6. Replaces the working database.
7. Verifies the resulting hash and snapshot.
8. Removes the pending journal after successful rollback.

The safety backup is preserved.

### 12.4 Rollback Failure

If the safety backup is missing, corrupted, or cannot be installed, recovery may fail.

Daycord must not continue into normal application operation with an uncertain database state.

Do not delete recovery files during investigation.

A qualified developer should inspect the database, journal, and available independent backups.

## 13. Application Settings Recovery

Application settings are stored inside SQLite.

Therefore, full database restoration also restores:

- Selected language.
- Appearance preference.
- Minutes per day.

Settings are loaded after the restored database has been installed and migrated.

This ordering prevents the application from applying settings belonging to the database that has just been replaced.

## 14. Report Configuration Recovery

Report configuration is also stored in SQLite.

A full backup contains:

- ReportPresets.
- ReportSettings.
- The active report preset.
- Custom column configuration.

Restoring a database restores these records to the state captured in that backup.

The validator checks report configuration consistency before accepting the snapshot.

## 15. Schema Upgrade Backups

`DatabaseBackupService` provides a separate backup mechanism for schema upgrades.

It creates a SQLite backup before a recognized existing database is migrated to a newer schema.

This is separate from user-initiated backup creation and pending restoration.

Schema upgrade backups are stored under:

```text
Backups/SchemaUpgrades/
```

The implementation validates their SQLite integrity before treating them as successful backups.

Do not remove this mechanism as duplicate legacy code.

## 16. Failure Scenarios

The following scenarios should be tested in isolation:

| Scenario | Expected behavior |
|---|---|
| Non-SQLite file | Rejected |
| Corrupted SQLite database | Rejected |
| Unknown migration | Rejected |
| Modified migration checksum | Rejected |
| Invalid foreign keys | Rejected |
| Invalid application settings | Rejected |
| Missing backup file | Error without replacing active data |
| Cancelled file picker | No restoration scheduled |
| Unwritable backup destination | Error without publishing incomplete output |
| Existing pending restoration | Second restoration rejected |
| Missing staged snapshot | Restoration blocked |
| Modified staged snapshot | SHA-256 mismatch; restoration blocked |
| Interrupted installation | Rollback attempted |
| Failed restored database initialization | Safety rollback attempted |
| Missing safety backup during rollback | Startup blocked rather than silently continuing |

These are expected behaviors derived from the implementation. They are not all confirmed runtime test results.

## 17. Manual Recovery Principles

Manual recovery should be performed only when the normal mechanism cannot restore the application.

Before attempting manual repair:

1. Completely close Daycord.
2. Preserve the current data directory.
3. Copy all relevant backup, journal, and staging files to a separate safe location.
4. Record the installed application version.
5. Record the observed error.
6. Inspect available backup files without modifying originals.
7. Validate candidate databases in an isolated environment.

Do not:

- Delete migration history.
- Disable foreign keys.
- Replace files while Daycord is running.
- Remove the pending journal to hide an unresolved installation state.
- Edit backup checksums.
- Overwrite the last available safety backup.

Manual recovery is not a normal end-user procedure.

## 18. Safe Verification Environment

Restore testing should use:

- A separate operating-system user.
- An isolated test environment.
- A test harness using an independent `AppDataPaths` directory, where supported.

Do not conduct crash simulations against the primary user database.

### 18.1 Representative Test Data

A full recovery test should include:

- Several tasks.
- Different task statuses.
- Multiple planning periods.
- Multiple work entries.
- Report presets.
- Custom report headers.
- Modified application language.
- Modified theme.
- Modified day length.

### 18.2 Successful Restoration Test

A complete test should:

1. Create representative data.
2. Create a backup.
3. Validate the backup.
4. Modify the original test database through the application.
5. Prepare restoration.
6. Close Daycord.
7. Restart Daycord.
8. Confirm that the backed-up data has returned.
9. Confirm that post-backup modifications are no longer present.
10. Confirm that report presets and application settings were restored.
11. Verify the safety backup.
12. Run integrity and foreign key checks.

Record actual test evidence and status in `testing.md`.

## 19. Technical Safety Checklist

Before changing backup or recovery code:

- [ ] Confirm correct application data paths.
- [ ] Preserve the application lifetime lock.
- [ ] Preserve SQLite snapshot validation.
- [ ] Preserve foreign key checks.
- [ ] Preserve migration checksum checks.
- [ ] Preserve staged backup hashing.
- [ ] Preserve atomic journal updates.
- [ ] Preserve safety backup creation.
- [ ] Preserve WAL checkpoint handling.
- [ ] Preserve startup rollback behavior.
- [ ] Verify migration 001 backup compatibility.
- [ ] Verify migration 002 backup compatibility.
- [ ] Test normal restoration in isolation.
- [ ] Test failures in isolation.
- [ ] Verify that original user data is never silently discarded.

## 20. Limitations

Daycord 0.1.0 has an implemented backup and recovery architecture.

However:

- Not every storage or hardware failure can be recovered automatically.
- Cross-version backup compatibility beyond the supported migration history is not guaranteed.
- Users must maintain independent backups.
- Some exceptional recovery scenarios have not been fully verified through runtime testing.
- A successful static review does not prove a complete backup-and-restore cycle.

## 21. Related Documentation

- [Architecture](architecture.md)
- [Database](database.md)
- [Development](development.md)
- [User Guide](user-guide.md)
- [Releasing](releasing.md)
- [Testing](testing.md)

This document describes the backup and recovery implementation of Daycord 0.1.0.