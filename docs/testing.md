# Daycord Testing

**Target version:** 0.1.1

## 1. Overview

This document describes how to build, verify, and manually test Daycord.

It contains executable commands and verification guidance only.

Historical test results, acceptance reports, and defect registers are not maintained in this document.

Daycord targets:

- macOS ARM64.
- Windows x64.
- .NET 10.

## 2. Prerequisites

Run commands from the repository root.

Use a .NET SDK compatible with `global.json`.

Verify the SDK:

```bash
dotnet --info
```

The repository contains three application projects and three automated verification projects.

## 3. Build Verification

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

The solution includes the application and automated verification projects.

A successful build confirms compilation, not correct runtime behavior.

## 4. Automated Verification

Automated checks are implemented as executable .NET console projects.

They are not xUnit, NUnit, or MSTest projects.

Run them with `dotnet run`.

### 4.1 Automatic Backup Checks

Project:

```text
tests/Daycord.AutomaticBackupChecks/
```

Run:

```bash
dotnet run \
  --project tests/Daycord.AutomaticBackupChecks/Daycord.AutomaticBackupChecks.csproj \
  --configuration Release \
  --no-build
```

This suite covers:

- Initial automatic snapshot creation.
- Skipping backup work when the database is unchanged.
- Coalescing multiple changes into a snapshot.
- Preserving changes committed during snapshot creation.
- Retaining up to three automatic copies.
- Preserving previous backups after snapshot failure.
- Avoiding unnecessary validation of older backups at startup.

### 4.2 Emergency Retry Checks

Project:

```text
tests/Daycord.EmergencyRetryChecks/
```

Run:

```bash
dotnet run \
  --project tests/Daycord.EmergencyRetryChecks/Daycord.EmergencyRetryChecks.csproj \
  --configuration Release \
  --no-build
```

This suite covers:

- Recovery behavior without a restore journal.
- Rejecting unsupported journal states.
- Protecting a healthy active database.
- Rejecting modified or missing staged backups.
- Rejecting ambiguous installation states.
- Preserving the original failed journal.
- Safely rearming eligible pre-installation failures.
- Completing an eligible rearmed restoration.

### 4.3 Recovery Checks

Project:

```text
tests/Daycord.RecoveryChecks/
```

Run:

```bash
dotnet run \
  --project tests/Daycord.RecoveryChecks/Daycord.RecoveryChecks.csproj \
  --configuration Release \
  --no-build
```

This suite covers:

- Fresh database initialization.
- SQLite snapshots containing committed WAL data.
- Normal backup and restoration.
- Preservation of user records across restoration.
- Rejection of incompatible migration history.
- Backup-source path restrictions.
- Emergency restoration from corrupted databases.
- Preservation of damaged original files.
- Tampered and incomplete recovery artifacts.
- Existing restore-journal protection.
- Interrupted installation and rollback handling.

The recovery suite includes focused compatibility checks for the current backup and restoration implementation.

These checks do not substitute for installing and upgrading actual published application binaries.

### 4.4 Running All Checks

After a successful Release build:

```bash
dotnet run \
  --project tests/Daycord.AutomaticBackupChecks/Daycord.AutomaticBackupChecks.csproj \
  --configuration Release \
  --no-build

dotnet run \
  --project tests/Daycord.EmergencyRetryChecks/Daycord.EmergencyRetryChecks.csproj \
  --configuration Release \
  --no-build

dotnet run \
  --project tests/Daycord.RecoveryChecks/Daycord.RecoveryChecks.csproj \
  --configuration Release \
  --no-build
```

Each project reports its checks and returns a nonzero exit code when a check fails.

The `--no-build` option requires previously built output for the specified configuration.

Omit `--no-build` when running the project without an earlier build.

## 5. Database Isolation

Automated database and recovery checks use isolated temporary directories.

Do not run destructive database tests against the user's application profile.

The Desktop application separates normal Debug and Release data:

| Configuration | Data directory |
|---|---|
| Debug | `Daycord-test` |
| Release | `Daycord` |

This separation does not make Release data suitable for destructive tests.

Never delete or reset a real `daycord.db` to prepare a test.

Preserve WAL, SHM, journal, and restore-staging files when investigating failures.

## 6. Manual Functional Verification

Automated infrastructure checks do not cover every desktop interaction.

Verify the following workflows in a running application.

### Tasks and Navigation

- Create, edit, open, and delete tasks.
- Search, filter, and sort the task list.
- Open selected tasks by keyboard and mouse.
- Verify confirmation dialogs and unsaved-change handling.
- Navigate from Home to Today's Tasks.
- Confirm that the Today filter uses `TaskDate`.
- Confirm that old rows are not shown during filtered navigation.

### Planning, Actual Time, and Calendar

- Create, edit, and delete planning periods.
- Create, edit, and delete actual work entries.
- Switch between independent unsaved work-entry drafts.
- Verify duration input using hours, minutes, and days.
- Verify that TaskDate, PlannedDate, and WorkDate remain independent.
- Check date selection and calendar navigation.

### Reports

- Generate XLSX files for different date ranges.
- Verify planned and actual time totals.
- Verify tasks with TaskDate but no time records.
- Verify that report statistics are independent of Excel presets.
- Verify that presets can affect exported data rows.
- Create, edit, select, and delete custom presets.
- Verify English and Russian report headings.

### Settings and Appearance

- Switch between English and Russian.
- Verify layout stability after switching languages.
- Switch between System, Light, and Dark themes.
- Change the configured length of one day.
- Verify settings persistence after restarting.
- Check shared styles, overlays, and animations.

### Backup and Restore

- Create a user backup.
- Restore a valid backup after restarting.
- Verify rejection of incompatible backup files.
- Verify preservation of original data when restoration fails.
- Verify automatic backup scheduling and rotation.
- Verify recovery behavior using isolated corrupted test data.

Do not simulate corruption against the normal application database.

## 7. Cross-Version Upgrade Verification

A real upgrade check must use actual application binaries.

For the transition from 0.1.0 to 0.1.1:

1. Install the published 0.1.0 application in an isolated environment.
2. Create representative tasks, planning periods, and work entries.
3. Configure report presets and application settings.
4. Create an independent database backup.
5. Close Daycord.
6. Install the 0.1.1 release candidate without deleting application data.
7. Start the updated application.
8. Verify the existing records and settings.
9. Verify report generation and backup creation.
10. Restart and confirm continued data persistence.

Do not manually edit migration history or remove the database to make an upgrade succeed.

A successful isolated RecoveryChecks run is not equivalent to testing a real binary upgrade.

## 8. Platform Verification

### macOS ARM64

Verify:

- DMG mounting and application installation.
- Application bundle and icon.
- Startup from Applications.
- Database persistence across restarts.
- Representative task and reporting workflows.
- Backup creation and restoration.

### Windows x64

Verify:

- Installer execution and application startup.
- Start Menu shortcuts.
- Representative task and reporting workflows.
- Database persistence.
- Reinstallation without user-data loss.
- Uninstallation without deleting the LocalAppData profile.
- Portable ZIP startup.

Platform behavior must be checked on the corresponding operating system.

## 9. Repository Checks

Before integrating changes, review the working tree:

```bash
git diff --check
git status --short
git diff --stat
```

For documentation changes, also verify:

- Relative Markdown links.
- File and directory names.
- Commands against the current repository.
- Version references.
- Consistency with the implemented behavior.

## 10. Related Documentation

- [Development](development.md)
- [Architecture](architecture.md)
- [Database](database.md)
- [Backup and Recovery](backup-and-recovery.md)
- [Releasing](releasing.md)
- [User Guide](user-guide.md)