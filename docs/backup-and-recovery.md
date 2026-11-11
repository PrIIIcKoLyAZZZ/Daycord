# Daycord Backup and Recovery

**Target version:** 0.1.1

## 1. Overview

Daycord protects its local SQLite database through several independent mechanisms:

| Mechanism | Purpose |
|---|---|
| User Backup | Creates a database backup at a user-selected location |
| Automatic Backup | Maintains recent verified database snapshots |
| User Restore | Restores a backup selected through Settings |
| Emergency Recovery | Attempts recovery from supported SQLite corruption |
| Schema Upgrade Backup | Preserves the database before schema migrations |

These mechanisms have different responsibilities and must not be treated as interchangeable.

**Important:** Restoring a backup returns the database to the state captured in that backup. Changes made afterward may be lost.

No backup or recovery mechanism guarantees preservation of every recent operation.

## 2. Backup Storage

Application data paths are managed by `AppDataPaths`.

The default application directory is resolved using `Environment.SpecialFolder.LocalApplicationData`.

Typical locations:

**macOS:**

```text
~/Library/Application Support/Daycord/
```

**Windows:**

```text
%LOCALAPPDATA%\Daycord\
```

Internal storage:

```text
Daycord/
├── daycord.db
├── Backups/
│   ├── Automatic/
│   ├── SchemaUpgrades/
│   ├── BeforeRestore/
│   └── Recovery/
├── RestoreStaging/
│   └── pending-restore.json
└── Diagnostics/
    └── recovery-events.jsonl
```

Directories and files are created as needed.

| Directory | Purpose |
|---|---|
| `Automatic` | Rotating automatic database snapshots |
| `SchemaUpgrades` | Backups created before schema migrations |
| `BeforeRestore` | Safety backups for normal restoration |
| `Recovery` | Preserved original database files and quarantine artifacts |
| `RestoreStaging` | Staged snapshots and restore journal |
| `Diagnostics` | Startup and recovery failure diagnostics |

User-created backups are saved to a location selected through the system file picker.

Application-managed directories must not be manually modified while Daycord is running or a restore is pending.

## 3. SQLite Snapshot Creation

Daycord creates consistent database snapshots using the SQLite Online Backup API.

The shared implementation is:

`SqliteSnapshotIO.CreateStandaloneSnapshot()`

The snapshot process:

1. Opens the source database through SQLite.
2. Copies a consistent database state into a temporary destination.
3. Includes committed changes held in the source WAL.
4. Converts the destination to SQLite DELETE journal mode.
5. Requires the completed snapshot to have no WAL, SHM, or journal sidecars.
6. Flushes the standalone database file before publication.

A snapshot is not considered usable merely because its file was created successfully.

Its integrity and compatibility must also be verified.

### Snapshot Validation

`SqliteSnapshotValidator` performs read-only validation.

Checks include:

- SQLite `PRAGMA integrity_check`.
- SQLite `PRAGMA foreign_key_check`.
- Required tables, indexes, and columns.
- Recognized migration history.
- Migration names and SHA-256 checksums.
- Required built-in task statuses.
- Default report preset and report settings.
- Application settings compatibility.

The validator accepts supported older migration-history prefixes when they match the embedded migrations.

Missing migrations are applied later by `MigrationRunner` during normal initialization.

An invalid or incompatible backup cannot be installed as a valid recovery candidate.

## 4. User Backup

User backups are created through **Settings → Backup & Restore**.

### Creating a Backup

1. Open Settings.
2. Select Create backup.
3. Choose a local destination for the `.db` file.
4. Save the backup.
5. Check the operation status displayed by Daycord.

Daycord creates and validates the snapshot before publishing the selected destination file.

The Desktop layer:

- Uses a temporary, validated snapshot.
- Rejects destinations inside the protected application data directory.
- Rejects unsafe destination redirects.
- Copies the snapshot to a temporary file beside the destination.
- Verifies the copied file using SHA-256.
- Publishes the completed backup through a same-directory file move.

Existing backup files may be replaced after the user's save or overwrite decision.

The original destination is not intentionally replaced before the new file has been completely copied and verified.

### Recommendations

Keep independent backups outside Daycord's application data directory.

Store important copies on another device or in a trusted backup location.

User backups are especially recommended before application updates or database restoration.

## 5. Automatic Backup

`AutomaticBackupService` maintains up to **three published automatic SQLite snapshots**.

The backup directory is:

```text
Backups/Automatic/
```

Published files follow this naming pattern:

```text
daycord-auto-<UTC timestamp>-<identifier>.db
```

### Backup Scheduling

Automatic backup activity is coordinated by:

- `DatabaseChangeTracker`
- `AutomaticBackupScheduler`
- `AutomaticBackupService`

`DatabaseChangeTracker` records successfully committed application changes.

`AutomaticBackupScheduler` checks for pending changes:

- After application navigation between sections.
- Periodically during an active session.
- During normal application shutdown.

The periodic scheduler checks once per minute.

Ordinary automatic backup attempts have a minimum interval of two minutes after the latest published backup.

Startup backup initialization and the final shutdown attempt can bypass this interval.

These rules do not guarantee an immediate backup after every user action.

### Detecting Changes

Tracked repositories notify `DatabaseChangeTracker` after successfully completed write operations.

The tracker maintains an in-memory revision counter.

If the application has no unbacked changes, navigation checks do not need to open or inspect the database files.

The backup service captures the revision before creating a snapshot.

Changes committed after that captured revision remain pending, even if they occur while a snapshot is being created.

### Publication and Rotation

The automatic backup service:

1. Checks whether a new backup is required.
2. Creates a temporary standalone SQLite snapshot.
3. Validates the temporary snapshot.
4. Avoids publishing a duplicate when its content matches the latest available backup.
5. Publishes the verified snapshot.
6. Verifies the published snapshot.
7. Updates the tracked backup revision.
8. Removes older published backups beyond the retention limit.

The newest existing snapshot is inspected during backup-service initialization.

Older snapshots are not repeatedly revalidated during routine navigation. They are validated when considered for emergency recovery.

A failed new snapshot does not intentionally replace the previously published backups.

**Limitations:**

- Backups are scheduled, not synchronous with every write.
- Abrupt termination can prevent a pending snapshot from being created.
- A normal shutdown attempts to flush remaining changes, but failure is still possible.
- Automatic copies are stored within the same application data directory and do not protect against loss of that entire directory or device.

Automatic backups supplement, rather than replace, independent user backups.

## 6. User Restore

User Restore is initiated through Settings.

### Preparing Restoration

1. Open Settings → Backup & Restore.
2. Choose a backup file.
3. Allow Daycord to validate it.
4. Review the confirmation warning.
5. Confirm the operation.
6. Close and restart Daycord.

The selected source is copied into private staging for validation.

After confirmation, Daycord independently recreates and validates the staged snapshot.

A SHA-256 checksum and restore journal are recorded.

The running database is not replaced during the Settings operation.

### Startup Restoration

`PendingRestoreCoordinator` applies the prepared operation during the next application startup.

The application first obtains an exclusive instance lock.

For a normal restore, the coordinator:

1. Validates the staged snapshot and its checksum.
2. Preserves the original database file set in a raw archive.
3. Creates a verified safety backup of the current database.
4. Prepares the active database for replacement.
5. Installs the staged snapshot.
6. Validates the installed database.
7. Allows schema and application settings initialization.
8. Completes the restore only after successful initialization.

A safety backup is retained in:

```text
Backups/BeforeRestore/
```

The original raw files are preserved separately in:

```text
Backups/Recovery/
```

### Normal Restore Failure

If installation or subsequent initialization fails, Daycord uses the recorded safety backup for a protected rollback when the required recovery artifacts are valid.

The rollback process:

- Validates the safety backup and archived references.
- Records the rollback phase.
- Moves the current SQLite file set into quarantine.
- Reinstalls the verified safety snapshot.
- Validates the restored database before clearing the journal.

An interrupted or ambiguous restore must not silently continue normal startup.

## 7. Emergency Recovery

Emergency Recovery is designed for eligible physical corruption of the active SQLite database.

It is automatically considered during startup when schema initialization fails with a recognized SQLite corruption error.

Supported primary SQLite error codes include:

| Code | Meaning |
|---|---|
| `SQLITE_CORRUPT` (11) | Database corruption |
| `SQLITE_NOTADB` (26) | File is not a valid SQLite database |

This mechanism does not automatically handle every database failure.

For example, migration checksum mismatches, unknown migration histories, permission errors, and general filesystem failures are not automatically treated as recoverable corruption.

### Selecting a Recovery Backup

`AutomaticRecoveryCandidateSelector` searches the automatic backup directory.

Candidates are considered from newest to oldest.

Each candidate must:

- Match the expected automatic backup filename.
- Be an ordinary file rather than a redirected filesystem entry.
- Have no SQLite sidecar files.
- Pass database integrity and compatibility validation.

If the newest candidate is invalid, an older valid candidate may be selected.

If no suitable backup exists, recovery is not prepared.

### Recovery Process

An eligible recovery operation follows these steps:

1. Detect a supported SQLite corruption condition.
2. Find a valid automatic backup.
3. Stage and verify the selected snapshot.
4. Record an emergency restore journal.
5. Preserve the original database and existing SQLite sidecars.
6. Move the damaged active file set into quarantine.
7. Install the verified snapshot.
8. Validate the installed database.
9. Complete schema and application settings initialization.
10. Finalize the recovery journal.
11. Display the recovery-completed window.

The recovery-completed window provides a Restart action.

Successful recovery is reported only after the required initialization and finalization steps have completed.

### Preserving Damaged Data

`RawDatabaseRecoveryArchive` preserves the original SQLite files byte-for-byte.

Possible files include:

```text
daycord.db
daycord.db-wal
daycord.db-shm
daycord.db-journal
```

The archive includes metadata and SHA-256 checksums.

`RollbackFileQuarantine` moves the active file set aside before a replacement is installed.

Raw archives and quarantine files are retained for investigation.

**A raw archive is not necessarily a valid SQLite backup.**

It may contain corrupted data and must not be presented as a verified recovery point.

### Interrupted Recovery

Emergency recovery uses a dedicated installation path.

Unlike a normal Restore, it cannot assume that the damaged original database is a valid rollback destination.

An interrupted installation is not automatically repeated when the resulting file state is ambiguous.

A narrowly restricted automatic retry is supported only for an eligible failure that occurred before original-file preservation and installation.

Before such a retry, Daycord verifies:

- The previous emergency journal and its state.
- The absence of installation or preservation artifacts that would make the operation ambiguous.
- The active database is not already healthy.
- The staged backup is still valid.
- The staged backup checksum has not changed.

The previous failed journal is preserved before the operation is rearmed.

Other unresolved emergency states require intervention rather than an unsafe automatic replacement.

### Recovery Failure

Emergency recovery can fail when:

- No valid automatic backup is available.
- The original database files cannot be preserved.
- A required recovery file is missing or modified.
- Another process prevents exclusive access.
- Installation or validation fails.
- The restore journal is inconsistent.
- The failure is not an eligible SQLite corruption condition.

Daycord may prevent normal startup rather than replace the database with an unverified or incompatible snapshot.

The emergency recovery window does not offer a manual backup-selection list.

User-controlled Backup & Restore remains available through Settings when the application can start normally.

## 8. Restore Journal

Normal and emergency restoration use a persistent journal:

```text
RestoreStaging/pending-restore.json
```

The journal contains:

- Restore operation identifier.
- Staged snapshot filename.
- Staged snapshot SHA-256 checksum.
- Restore mode.
- Current restore phase.
- References to preserved safety or raw recovery artifacts where applicable.

`PendingRestoreStateStore` validates journal contents and permitted state transitions.

Journal updates are written to temporary files and published using same-directory moves.

The journal prevents an unfinished or ambiguous replacement from being mistaken for a completed restore.

It must not be manually deleted to bypass recovery failures.

## 9. Component Responsibilities

| Component | Responsibility |
|---|---|
| `SqliteSnapshotIO` | Creates standalone SQLite snapshots and durable copies |
| `SqliteSnapshotValidator` | Validates database integrity and compatibility |
| `UserBackupService` | Creates user backups and prepares user restoration |
| `DatabaseBackupService` | Creates verified pre-migration backups |
| `DatabaseChangeTracker` | Tracks committed changes requiring backup |
| `AutomaticBackupScheduler` | Triggers backup attempts during application use and shutdown |
| `AutomaticBackupService` | Publishes and rotates automatic snapshots |
| `AutomaticRecoveryCandidateSelector` | Selects the newest usable automatic recovery candidate |
| `AutomaticRecoveryPreparation` | Prepares eligible emergency recovery |
| `EmergencyRestorePreparation` | Stages and journals emergency recovery |
| `PendingRestoreCoordinator` | Coordinates deferred restoration under an application lock |
| `EmergencyRestoreInstaller` | Safely installs and validates emergency recovery snapshots |
| `EmergencyRestoreRetryPreparation` | Handles narrowly eligible retry preparation |
| `RawDatabaseRecoveryArchive` | Preserves original SQLite files for investigation |
| `RollbackFileQuarantine` | Moves the active database file set aside without overwriting it |
| `PendingRestoreStateStore` | Validates and persists restoration journal states |
| `DatabaseRecoveryDiagnostics` | Records sanitized startup and recovery diagnostics |
| `MigrationRunner` | Validates migration history and applies supported schema changes |

The fundamental boundaries are:

- **Backup** creates a verified copy.
- **Restore** installs a selected saved state.
- **Recovery** handles eligible failures using existing recovery points.
- **MigrationRunner** validates and updates the database schema.

These responsibilities must remain separate.

## 10. Diagnostics and Troubleshooting

Startup and recovery failures may be recorded in:

```text
Diagnostics/recovery-events.jsonl
```

Diagnostic records contain technical metadata such as:

- Application version.
- Platform and architecture.
- Startup or recovery stage.
- Exception type.
- SQLite error codes when applicable.

The diagnostic implementation uses sanitized error descriptions instead of recording raw exception messages that could expose SQL text, filesystem paths, or user data.

Diagnostic logging is best-effort and must not replace the original failure.

When investigating a database failure:

1. Preserve the full application data directory.
2. Do not delete the database or SQLite sidecar files.
3. Do not remove an unfinished restore journal.
4. Do not modify migration checksums.
5. Inspect the available diagnostics.
6. Reproduce destructive scenarios only with isolated test data.

For automated verification commands, see [Testing](testing.md).

For schema and migration details, see [Database](database.md).

For end-user instructions, see [User Guide](user-guide.md).