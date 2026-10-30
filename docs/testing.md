# Daycord Testing and QA

**Application version:** 0.1.0  
**Work item:** DC-017 — Final MVP Acceptance & Documentation  
**Last documentation update:** 2026-10-30

## 1. Purpose

This document provides a reproducible QA checklist and evidence register for Daycord 0.1.0.

It covers:

- Compilation.
- Task management.
- Planned and actual time.
- Calendar.
- Home dashboard.
- Excel reporting.
- Application settings.
- Localization.
- SQLite integrity and migrations.
- Backup and restoration.
- macOS distribution.
- Windows distribution.
- Installation and uninstallation.
- Release artifacts and checksums.

The project owner and an independent tester have performed MVP testing and reported defects.

However, individual test-case results from that testing have not yet been transferred into this document.

The existence of previous testing must not be confused with a documented PASS result for every case.

## 2. Testing Policy

### 2.1 Scope

DC-017 focuses on:

1. Repository and legacy-code review.
2. Documentation of the implemented MVP.
3. Recording existing QA evidence.
4. Registering known defects.
5. Preparing the final technical acceptance report.

Known defects are not automatically fixed during this stage.

A separate decision is required before changing the agreed MVP functionality.

### 2.2 Result Statuses

Use only the following statuses for individual test cases:

| Status | Meaning |
|---|---|
| PASS | Test executed; expected result confirmed |
| FAIL | Test executed; observed result differs from expected result |
| NOT VERIFIED | No sufficient execution evidence has been recorded |
| BLOCKED | Test cannot be completed because of a documented blocker |
| NOT APPLICABLE | Test does not apply to the target environment |

A successful compilation does not automatically make runtime tests PASS.

Static source inspection does not establish runtime behavior.

### 2.3 Evidence Requirements

A PASS result should include enough information to reproduce or audit the test.

Recommended evidence:

- Application version.
- Git revision.
- Operating system.
- Architecture.
- Test environment.
- Test date.
- Tester.
- Steps performed.
- Actual result.
- Logs or screenshots where relevant.

Do not invent test results to fill gaps in the register.

## 3. Test Environment

### 3.1 Supported Platforms

| Platform | Architecture | Release target |
|---|---|---|
| macOS | ARM64 | `osx-arm64` |
| Windows | x64 | `win-x64` |

### 3.2 Development Environment

The repository targets:

- .NET 10.
- Avalonia UI 12.
- SQLite.
- Dapper.
- ClosedXML.

The solution contains:

```text
Daycord.Core
Daycord.Infrastructure
Daycord.Desktop
```

### 3.3 Database Isolation

All potentially destructive database tests must use an isolated test environment.

Acceptable approaches include:

- A separate operating-system account.
- A separate test environment.
- An isolated database path supplied to infrastructure components in a test harness.

Do not run restoration failure simulations against the owner's primary database.

Do not delete the production `daycord.db` as part of test setup.

## 4. Recorded Evidence

### 4.1 Build Evidence — October 30, 2026

The project owner supplied terminal output from macOS.

Commands:

```bash
dotnet restore Daycord.slnx
dotnet build Daycord.slnx -c Debug
dotnet build Daycord.slnx -c Release
git diff --check
git status --short
git diff --stat
```

Recorded results:

| Check | Actual result | Status |
|---|---|---|
| Restore | Completed successfully | PASS |
| Debug Build | All three projects succeeded | PASS |
| Release Build | All three projects succeeded | PASS |
| Git diff whitespace check | No errors reported | PASS |

The recorded Debug build completed successfully in 3.7 seconds.

The recorded Release build completed successfully in 2.0 seconds.

These figures are historical observations, not required performance targets.

### 4.2 Legacy Cleanup Evidence

The owner applied the approved legacy cleanup.

Observed working-tree changes included:

- Removal of `WorkReportDefaults`.
- Removal of an unused TerminalViewModel initialization method.
- Removal of the default Avalonia icon.

Unused localization resource keys were intentionally retained.

The builds passed after these modifications.

This confirms compilation compatibility, not complete functional regression coverage.

### 4.3 Previous MVP Acceptance

The project owner has reported successful testing on the supported platforms and additional testing by an independent tester.

Previous release work also recorded owner acceptance of installation and packaging scenarios.

Detailed individual QA records are not yet attached to this register.

The corresponding test cases remain NOT VERIFIED here until that evidence is recorded.

## 5. Test Data

For reproducible functional testing, create an isolated database with representative records.

Recommended fixture:

| Data | Minimum coverage |
|---|---|
| Tasks | Multiple tasks with and without dates |
| Statuses | Open, Completed, custom status |
| PlanningPeriods | Multiple periods across different dates |
| WorkEntries | Multiple entries, including same-day records |
| Markdown | Headings, lists, links, code |
| ReportPresets | Default and custom |
| AppSettings | English/Russian, Light/Dark/System, 1440/480 minutes |
| Reports | Single-month and multi-month periods |
| Backup | Valid migration 001 and 002 snapshots |

All test timestamps must use valid persistence formats.

Do not manually create malformed timestamps in the main user database.

## 6. Build and Repository Tests

### QA-BLD-001 — Restore Dependencies

**Platform:** macOS build environment  
**Preconditions:** Compatible SDK and repository available.

**Steps:**

1. Open the repository root.
2. Run `dotnet restore Daycord.slnx`.
3. Review the process exit code.

**Expected result:** Dependencies restore successfully.

**Actual result:** Owner-provided terminal output confirms successful restoration on October 30, 2026.

**Status:** PASS

**Known limitations:** Windows-hosted restore not separately evidenced here.

### QA-BLD-002 — Debug Build

**Platform:** macOS build environment  
**Preconditions:** Dependencies restored.

**Steps:**

1. Run `dotnet build Daycord.slnx -c Debug`.
2. Inspect the result for all projects.

**Expected result:** Core, Infrastructure, and Desktop build successfully.

**Actual result:** All three projects compiled successfully.

**Status:** PASS

**Known limitations:** No runtime UI behavior is established by the build result.

### QA-BLD-003 — Release Build

**Platform:** macOS build environment  
**Preconditions:** Dependencies restored.

**Steps:**

1. Run `dotnet build Daycord.slnx -c Release`.
2. Inspect the result.

**Expected result:** All three projects build successfully.

**Actual result:** All three projects compiled successfully.

**Status:** PASS

**Known limitations:** This is a build, not a complete publish-and-package verification.

### QA-BLD-004 — Repository Revision

**Platform:** Development host  
**Preconditions:** Git repository available.

**Steps:**

1. Run `git branch --show-current`.
2. Run `git rev-parse HEAD`.
3. Run `git status --short`.
4. Run `git diff --check`.

**Expected result:** Revision and working-tree state are recorded; diff check reports no errors.

**Actual result:** The diff check returned no errors. A complete branch name and HEAD revision have not been recorded in this document.

**Status:** NOT VERIFIED

**Known limitations:** Repository revision evidence is incomplete.

## 7. Task Management Tests

### QA-TASK-001 — Task Creation

**Platform:** macOS ARM64 / Windows x64  
**Preconditions:** Initialized isolated test database.

**Steps:**

1. Open Quick Task.
2. Create a task with a title, date, and optional number.
3. Open Tasks.
4. Locate the new task.
5. Restart Daycord.
6. Locate the task again.

**Expected result:** The task is saved once and persists across restart.

**Actual result:** Individual test evidence not recorded.

**Status:** NOT VERIFIED

**Known limitations:** Awaiting the existing tester's case results.

### QA-TASK-002 — Full Task Editor

**Platform:** macOS ARM64 / Windows x64  
**Preconditions:** Test database initialized.

**Steps:**

1. Create a task using the full editor.
2. Enter a task number, title, date, and descriptions.
3. Save it.
4. Reopen the task.
5. Modify several fields.
6. Save and reopen again.

**Expected result:** Changes are persisted correctly; the creation timestamp remains unchanged.

**Actual result:** Individual test evidence not recorded.

**Status:** NOT VERIFIED

**Known limitations:** Historical accepted implementation; current test evidence pending.

### QA-TASK-003 — Status and Completion

**Platform:** macOS ARM64 / Windows x64  
**Preconditions:** Task with Open status.

**Steps:**

1. Change status to Completed.
2. Save and reopen.
3. Change status back to Open.
4. Inspect completion-related state.
5. Repeat using a custom status where supported.

**Expected result:** Completion state and timestamp reflect the selected status's completion semantics.

**Actual result:** Individual test evidence not recorded.

**Status:** NOT VERIFIED

**Known limitations:** Custom status behavior should be recorded separately if necessary.

### QA-TASK-004 — Search, Filters, and Deletion

**Platform:** macOS ARM64 / Windows x64  
**Preconditions:** Multiple tasks with distinct dates, names, and statuses.

**Steps:**

1. Search by title.
2. Search by short description.
3. Search using `%` and `_`.
4. Apply date and status filters.
5. Change sorting direction.
6. Reset filters.
7. Delete a selected task.

**Expected result:** Search, filters, sorting, and confirmed deletion behave correctly. Related time records are deleted with the task.

**Actual result:** Individual test evidence not recorded.

**Status:** NOT VERIFIED

**Known limitations:** Destructive step must use test records.

## 8. Planned Time Tests

### QA-PLAN-001 — Planning Period CRUD

**Platform:** macOS ARM64 / Windows x64  
**Preconditions:** Existing test task.

**Steps:**

1. Add a planning period.
2. Add another period on a different date.
3. Modify one period's duration.
4. Modify its date.
5. Delete one period.
6. Reopen the task.

**Expected result:** Remaining periods and total planned time are correct; unrelated entries are unchanged.

**Actual result:** Individual test evidence not recorded.

**Status:** NOT VERIFIED

**Known limitations:** None beyond isolated fixture requirements.

### QA-PLAN-002 — Duration Interpretation

**Platform:** macOS ARM64 / Windows x64  
**Preconditions:** Default day length of 1440 minutes.

**Steps:**

1. Test `1`.
2. Test `30m`.
3. Test `1h 30m`.
4. Test `1d`.
5. Test `2d 3h`.
6. Change day length to 480 minutes.
7. Repeat day-based inputs.

**Expected result:** Durations follow the configured day length; previously stored minutes remain unchanged.

**Actual result:** Individual test evidence not recorded.

**Status:** NOT VERIFIED

**Known limitations:** Test localized duration expressions separately.

## 9. Actual Time Tests

### QA-WORK-001 — Work Entry CRUD

**Platform:** macOS ARM64 / Windows x64  
**Preconditions:** Existing test task.

**Steps:**

1. Add a work entry.
2. Add another entry for the same date.
3. Edit an entry's duration.
4. Edit its description and note.
5. Save.
6. Delete one entry.

**Expected result:** Correct persistence, totals, and independent entry management.

**Actual result:** Individual test evidence not recorded.

**Status:** NOT VERIFIED

**Known limitations:** Use positive durations and valid dates.

### QA-WORK-002 — Independent Editing Drafts

**Platform:** macOS ARM64 / Windows x64  
**Preconditions:** Task with at least two work entries.

**Steps:**

1. Begin editing entry A.
2. Modify its fields without saving.
3. Switch to entry B.
4. Modify entry B.
5. Return to entry A.
6. Check the unsaved draft.
7. Verify that unsaved modifications have not been persisted to SQLite.
8. Attempt to leave the editor.

**Expected result:** Drafts remain independent, unsaved changes are not automatically persisted, and exit confirmation operates correctly.

**Actual result:** Individual test evidence not recorded.

**Status:** NOT VERIFIED

**Known limitations:** Distinguish UI draft state from committed database state.

## 10. Home Dashboard Tests

### QA-HOME-001 — Daily Summary

**Platform:** macOS ARM64 / Windows x64  
**Preconditions:** Test data for the current local date.

**Steps:**

1. Create today's task.
2. Add planned time for today.
3. Record actual work for today.
4. Complete a task.
5. Return to Home.
6. Open a task from the dashboard.

**Expected result:** Counts, planned time, actual time, completion state, and navigation are correct.

**Actual result:** Individual test evidence not recorded.

**Status:** NOT VERIFIED

**Known limitations:** Use the local calendar date, not an assumed UTC date.

## 11. Calendar Tests

### QA-CAL-001 — Date Navigation

**Platform:** macOS ARM64 / Windows x64  
**Preconditions:** Calendar available.

**Steps:**

1. Open Calendar.
2. Navigate backward and forward.
3. Cross a year boundary.
4. Inspect February in a leap year.
5. Select another date.
6. Use Today.

**Expected result:** Valid calendar layout, month navigation, and selection.

**Actual result:** Individual test evidence not recorded.

**Status:** NOT VERIFIED

**Known limitations:** Visual behavior must be checked on both platforms.

### QA-CAL-002 — Independent Dates

**Platform:** macOS ARM64 / Windows x64  
**Preconditions:** Task with distinct TaskDate, PlannedDate, and WorkDate.

**Steps:**

1. Open Calendar.
2. Inspect each relevant date.
3. Verify task indicators.
4. Verify planning indicators.
5. Verify actual work indicators.
6. Open the associated task.

**Expected result:** Each activity appears on its own date and navigation opens the correct task.

**Actual result:** Individual test evidence not recorded.

**Status:** NOT VERIFIED

**Known limitations:** Include a task without TaskDate.

## 12. Excel Reporting Tests

### QA-REP-001 — XLSX Export

**Platform:** macOS ARM64 / Windows x64  
**Preconditions:** Tasks with planned and actual work across multiple dates.

**Steps:**

1. Open Reports.
2. Select a valid date range.
3. Select the Default preset.
4. Export XLSX.
5. Open the workbook in Excel or a compatible spreadsheet viewer.
6. Inspect rows and totals.

**Expected result:** Valid XLSX, correct data, correct totals, and usable formatting.

**Actual result:** Individual test evidence not recorded.

**Status:** NOT VERIFIED

**Known limitations:** Spreadsheet rendering may differ among applications.

### QA-REP-002 — Preset Management

**Platform:** macOS ARM64 / Windows x64  
**Preconditions:** Reports available.

**Steps:**

1. Create a custom preset.
2. Change column selection.
3. Change column order.
4. Rename headers.
5. Save the settings.
6. Restart Daycord.
7. Reopen the preset.
8. Rename and delete it.
9. Verify that Default cannot be deleted.

**Expected result:** Custom preset changes persist; built-in preset protections remain active.

**Actual result:** Individual test evidence not recorded.

**Status:** NOT VERIFIED

**Known limitations:** Confirm behavior when leaving with unsaved column changes.

### QA-REP-003 — Localization and Extended Durations

**Platform:** macOS ARM64 / Windows x64  
**Preconditions:** Report data containing more than 24 hours.

**Steps:**

1. Switch Daycord to English.
2. Export a report.
3. Inspect built-in headings and totals.
4. Switch to Russian.
5. Export again.
6. Inspect localized headings and totals.
7. Confirm that custom headers remain unchanged.
8. Confirm extended duration values.

**Expected result:** Localized built-in text, preserved custom text, and correct duration values.

**Actual result:** Individual test evidence not recorded.

**Status:** NOT VERIFIED

**Known limitations:** The export must use one captured language throughout its operation.

## 13. Application Settings Tests

### QA-SET-001 — Language and Appearance

**Platform:** macOS ARM64 / Windows x64  
**Preconditions:** Initialized application.

**Steps:**

1. Change language to Russian.
2. Navigate between screens.
3. Change language back to English.
4. Select Light.
5. Select Dark.
6. Select System.
7. Restart Daycord.

**Expected result:** Changes apply without restart and the selected preferences persist.

**Actual result:** Individual test evidence not recorded.

**Status:** NOT VERIFIED

**Known limitations:** Verify System appearance against the operating-system theme.

### QA-SET-002 — Minutes Per Day

**Platform:** macOS ARM64 / Windows x64  
**Preconditions:** Existing planned time records.

**Steps:**

1. Confirm default day length is 1440 minutes.
2. Change the day length to 480 minutes.
3. Confirm the preview displays `1d = 8h`.
4. Create a new `1d` planning entry.
5. Reopen older entries.
6. Restart Daycord.

**Expected result:** New day-based input uses eight hours; historical stored minutes do not change; the setting persists.

**Actual result:** Individual test evidence not recorded.

**Status:** NOT VERIFIED

**Known limitations:** Check work-entry draft behavior independently.

## 14. SQLite and Migration Tests

### QA-DB-001 — Fresh Database

**Platform:** Isolated test environment  
**Preconditions:** Empty test data directory.

**Steps:**

1. Start Daycord using the isolated environment.
2. Allow initialization.
3. Inspect the database.
4. Read migration history.
5. Restart the application.

**Expected result:** Migrations 001 and 002 are recorded once; required tables and system data exist; repeated startup does not duplicate them.

**Actual result:** Individual test evidence not recorded.

**Status:** NOT VERIFIED

**Known limitations:** Must not use the primary database.

### QA-DB-002 — Integrity and Foreign Keys

**Platform:** Isolated test environment  
**Preconditions:** Initialized test database.

**Steps:**

Run:

```sql
PRAGMA integrity_check;
```

Then:

```sql
PRAGMA foreign_key_check;
```

**Expected result:** Integrity check returns `ok`; foreign key check returns no rows.

**Actual result:** No actual database-check output has been included in this documentation phase.

**Status:** NOT VERIFIED

**Known limitations:** Static schema review does not replace execution of these commands.

### QA-DB-003 — Migration Upgrade

**Platform:** Isolated test environment  
**Preconditions:** Valid database with migration 001 only.

**Steps:**

1. Preserve a copy of the migration 001 fixture.
2. Start the current application against the fixture.
3. Allow migration 002 to run.
4. Inspect AppSettings and migration history.
5. Verify original task records.
6. Restart.

**Expected result:** Migration 002 is applied once and existing data is preserved.

**Actual result:** Individual test evidence not recorded.

**Status:** NOT VERIFIED

**Known limitations:** Never edit an applied migration or its checksum.

## 15. Backup and Restore Tests

### QA-BACKUP-001 — Create Backup

**Platform:** Isolated test environment  
**Preconditions:** Database containing representative test records.

**Steps:**

1. Open Settings.
2. Create a backup.
3. Save it to a separate local directory.
4. Verify that the file exists.
5. Open the snapshot with SQLite tooling.
6. Run integrity and foreign key checks.

**Expected result:** The backup is a valid standalone SQLite database containing the source data.

**Actual result:** Individual test evidence not recorded.

**Status:** NOT VERIFIED

**Known limitations:** File existence alone is insufficient proof of consistency.

### QA-RESTORE-001 — Successful Full Restore

**Platform:** Isolated test environment  
**Preconditions:** Test database with tasks, work records, presets, and settings.

**Steps:**

1. Create and validate a backup.
2. Modify the test database after backup.
3. Prepare restoration using the saved backup.
4. Close Daycord normally.
5. Restart Daycord.
6. Inspect restored tasks and time records.
7. Inspect report presets.
8. Inspect AppSettings.
9. Verify the safety backup.
10. Run integrity and foreign key checks.

**Expected result:** The backed-up state is restored, post-backup changes are replaced, the database remains consistent, and a safety copy of the previous database is retained.

**Actual result:** A complete per-step execution record has not been supplied for this documentation.

**Status:** NOT VERIFIED

**Known limitations:** This is a critical recovery scenario and must not be inferred from source inspection.

### QA-RESTORE-002 — Invalid Backup

**Platform:** Isolated test environment  
**Preconditions:** Disposable test database and invalid backup fixtures.

**Steps:**

1. Attempt to select a non-SQLite file.
2. Attempt to validate corrupted SQLite data.
3. Attempt to validate an incompatible schema.
4. Attempt to validate modified migration history.
5. Check the working database afterward.

**Expected result:** Invalid backups are rejected without replacing current data.

**Actual result:** Individual test evidence not recorded.

**Status:** NOT VERIFIED

**Known limitations:** Use disposable fixtures only.

### QA-RESTORE-003 — Interrupted Restoration

**Platform:** Isolated test environment  
**Preconditions:** Dedicated recovery test environment.

**Steps:**

1. Prepare a valid restoration.
2. Simulate an interruption at a controlled restoration phase using an approved test method.
3. Restart the application.
4. Inspect rollback behavior.
5. Verify database integrity and recovery artifacts.

**Expected result:** Recovery follows the journal phase, attempts rollback where required, and does not silently start with an uncertain database state.

**Actual result:** No approved interrupted-restoration execution record has been supplied.

**Status:** NOT VERIFIED

**Known limitations:** Do not simulate process crashes against user data.

## 16. macOS Release Tests

### QA-MAC-001 — Distribution Package

**Platform:** macOS ARM64  
**Preconditions:** Published 0.1.0 release artifacts.

**Steps:**

1. Inspect `Daycord.app`.
2. Verify Info.plist.
3. Verify the application icon.
4. Verify the DMG.
5. Verify the ZIP.
6. Install Daycord into Applications.
7. Launch from Finder and Spotlight.

**Expected result:** Valid release artifacts and successful application launch.

**Actual result:** Previous project acceptance reported successful packaging and installation, but the full test record is not included here.

**Status:** NOT VERIFIED

**Known limitations:** Current distributions are unsigned and not notarized.

### QA-MAC-002 — Installed Application Persistence

**Platform:** macOS ARM64  
**Preconditions:** Installed application and test data.

**Steps:**

1. Create a task.
2. Record planned and actual work.
3. Change application settings.
4. Close Daycord.
5. Launch it again.
6. Verify that data and settings remain available.

**Expected result:** Data persists in the user's local application data directory.

**Actual result:** Individual test evidence not recorded.

**Status:** NOT VERIFIED

**Known limitations:** Do not confuse a Debug build with a packaged Release test.

## 17. Windows Release Tests

### QA-WIN-001 — Installer

**Platform:** Windows x64  
**Preconditions:** Windows Setup.exe.

**Steps:**

1. Run the installer.
2. Confirm UAC behavior.
3. Complete installation.
4. Check Program Files.
5. Check the Start menu.
6. Check Installed Apps.
7. Launch as a normal user.

**Expected result:** Machine-wide installation succeeds and Daycord starts without requiring elevated application execution.

**Actual result:** Previous project acceptance reported a successful Windows installation, but the full case evidence is not included here.

**Status:** NOT VERIFIED

**Known limitations:** Installer is not Authenticode-signed.

### QA-WIN-002 — Reinstallation and Uninstallation

**Platform:** Windows x64  
**Preconditions:** Installed application containing test data.

**Steps:**

1. Save sample task data.
2. Reinstall the same version.
3. Launch Daycord.
4. Verify the data.
5. Uninstall Daycord.
6. Check the installation directory.
7. Check the LocalAppData directory.

**Expected result:** Application files are managed correctly and the user database is preserved.

**Actual result:** Same-version reinstallation and data preservation were previously owner-reported; complete uninstall evidence is not recorded here.

**Status:** NOT VERIFIED

**Known limitations:** Cross-version upgrade is a separate test.

### QA-WIN-003 — Portable ZIP

**Platform:** Windows x64  
**Preconditions:** Windows portable ZIP.

**Steps:**

1. Extract the ZIP.
2. Launch `Daycord.Desktop.exe`.
3. Create a task.
4. Restart the application.
5. Verify the task remains available.

**Expected result:** Portable distribution launches and persists data without the NSIS installer.

**Actual result:** Individual test evidence not recorded.

**Status:** NOT VERIFIED

**Known limitations:** Windows security warnings may appear for unsigned executables.

## 18. Release Upgrade Tests

### QA-UPGRADE-001 — Cross-Version Upgrade

**Platform:** macOS ARM64 / Windows x64  
**Preconditions:** Two distinct approved application builds and isolated persistent data.

**Steps:**

1. Install an older version.
2. Create representative data.
3. Back up the database.
4. Install the newer version.
5. Launch the application.
6. Verify data and migration compatibility.

**Expected result:** Existing supported data is preserved and any required migrations complete safely.

**Actual result:** Cross-version upgrades have not been confirmed in the available QA evidence.

**Status:** NOT VERIFIED

**Known limitations:** Same-version reinstallation is not equivalent to a cross-version upgrade.

## 19. Artifact and Checksum Tests

### QA-ART-001 — Release Artifacts

**Platform:** macOS packaging host  
**Preconditions:** Both runtime targets published.

**Steps:**

1. Inspect `artifacts/release/0.1.0`.
2. Verify that four expected artifacts exist.
3. Test both ZIP archives.
4. Verify the DMG.
5. Run checksum validation.

**Expected result:** Correct files, complete archives, valid disk image, and matching SHA-256 hashes.

**Actual result:** Individual artifact verification output is not included in this documentation phase.

**Status:** NOT VERIFIED

**Known limitations:** Checksums do not establish code-signing authenticity.

## 20. UI Regression Matrix

The following combinations should be checked for Home, Calendar, Tasks, Quick Task, Task Details, Reports, and Settings.

| Language | Theme | Status |
|---|---|---|
| English | Light | NOT VERIFIED |
| English | Dark | NOT VERIFIED |
| Russian | Light | NOT VERIFIED |
| Russian | Dark | NOT VERIFIED |
| English/Russian | System | NOT VERIFIED |

For each combination, inspect:

- Text contrast.
- Hover and selected states.
- Disabled controls.
- Date picker behavior.
- Modal confirmations.
- Button borders.
- Long task names.
- Multiline descriptions.
- Column resizing.
- Scrollbars.
- Keyboard focus.
- Navigation state.
- Localization consistency.

Previous visual testing was reported by the owner and independent tester.

Detailed defects must be transferred to the known-issues register rather than silently changing these statuses.

## 21. Known Issues Register

Known defects identified during MVP testing must be recorded without automatically fixing them.

The detailed findings have not yet been provided for inclusion in this document.

Use the following record format for each defect:

```text
Issue ID:
Title:
Discovered by:
Date:
Platform:
Application version:
Severity:
Affected component:
Preconditions:
Steps to reproduce:
Expected result:
Actual result:
Frequency:
Evidence:
Workaround:
Release impact:
Decision: Deferred / Release Blocker / Accepted Risk
Target follow-up version:
```

### 21.1 Severity Classification

**Release Blocker**

Examples:

- Confirmed data loss.
- Database corruption.
- Broken main task persistence.
- Failed startup on a supported platform.
- Unsafe database restoration.
- Installer deleting user data.

**Major**

Examples:

- Incorrect time totals.
- Incorrect report data.
- Broken important editing behavior.
- Lost application settings.

**Minor**

Examples:

- Visual misalignment.
- Small styling defects.
- Noncritical interface inconsistencies.

### 21.2 Defect Handling for MVP 0.1.0

The owner has chosen to document existing defects without automatically fixing them in DC-017.

This decision does not remove the need to identify confirmed risks to user data.

Any confirmed Release Blocker should be escalated explicitly for an informed release decision.

Defects deferred until after MVP should receive separate issue identifiers and target versions.

### 21.3 Current Register State

**Status:** Awaiting transfer of the owner's and independent tester's detailed findings.

No defect details are invented in this document.

## 22. Final Acceptance Summary

| Area | Documented result |
|---|---|
| Repository audit | Static review completed |
| Legacy cleanup | Applied; compilation confirmed |
| Dependency restoration | PASS |
| Debug build | PASS |
| Release build | PASS |
| Task management runtime | Detailed evidence pending |
| Planned time runtime | Detailed evidence pending |
| Actual time runtime | Detailed evidence pending |
| Home dashboard runtime | Detailed evidence pending |
| Calendar runtime | Detailed evidence pending |
| Reports and XLSX | Detailed evidence pending |
| Application settings | Detailed evidence pending |
| Localization and themes | Detailed evidence pending |
| SQLite integrity | Detailed evidence pending |
| Full backup and restore | Detailed evidence pending |
| macOS distribution | Owner-reported acceptance; evidence pending |
| Windows distribution | Owner-reported acceptance; evidence pending |
| Cross-version upgrade | NOT VERIFIED |
| Release checksums | Detailed evidence pending |
| Known defects | Awaiting structured issue records |

These are evidence-register statuses, not a statement that the owner or independent tester did not perform the corresponding tests.

## 23. Go / No-Go Considerations

Technical MVP acceptance and public distribution approval are separate decisions.

A release recommendation should consider:

- Successful supported-platform startup.
- Basic task persistence.
- Database integrity.
- Backup and recovery behavior.
- Correctness of Excel reports.
- Packaging integrity.
- Known defects.
- Outstanding data-loss risks.
- Unsigned-distribution limitations.

A project-owner decision to ship the MVP should be documented together with accepted risks.

Do not represent unverified tests as PASS to support a release decision.

## 24. Future QA Improvements

Potential improvements after MVP include:

- Automated Core service tests.
- Repository integration tests.
- Migration compatibility tests.
- Dedicated SQLite backup and recovery tests.
- Cross-version upgrade fixtures.
- Automated release artifact validation.
- Repeatable UI smoke tests.
- A persistent defect tracker.
- Automated build pipelines.

These are recommendations for future development.

They are not existing capabilities of the version 0.1.0 repository.

## 25. Related Documentation

- [Architecture](architecture.md)
- [Database](database.md)
- [Development](development.md)
- [User Guide](user-guide.md)
- [Releasing](releasing.md)
- [Backup and Recovery](backup-and-recovery.md)

This document records the available evidence and defines the reproducible QA process for Daycord 0.1.0.