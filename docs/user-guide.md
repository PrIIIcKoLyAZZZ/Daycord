# Daycord User Guide

**Documentation target:** Daycord 0.1.1  
**Platforms:** macOS ARM64 and Windows x64

## 1. Introduction

Daycord is an offline-first desktop application for managing tasks, planning time, recording actual work, and creating Excel reports.

All application data is stored locally in SQLite.

Daycord does not require an account, server, or internet connection for everyday use.

### Main Sections

| Section | Purpose |
|---|---|
| Home | Today's tasks and activity |
| Calendar | Tasks and time records by date |
| Tasks | Task management, search, and filters |
| New Task | Quick Task creation |
| Reports | Excel reporting and presets |
| Settings | Language, appearance, durations, and backups |

## 2. Getting Started

On first launch, Daycord creates its local database and initializes the application.

Default settings:

| Setting | Default |
|---|---|
| Language | English |
| Appearance | System |
| Duration of one day | 24 hours |

To get started:

1. Create a task using New Task or Tasks.
2. Assign a task date if needed.
3. Add planned time.
4. Record actual work.
5. Review your activity on Home or Calendar.
6. Export an Excel report.
7. Create a backup in Settings.

## 3. Tasks

### 3.1 Quick Task

Quick Task is intended for creating tasks with minimal input.

1. Open **New Task**.
2. Select or enter a task date.
3. Optionally enter a task number.
4. Enter a title.
5. Optionally enter planned time.
6. Press Enter or use the create action.

If planned time is provided, Daycord creates a corresponding planning record.

Recently created tasks are displayed during the current session.

Use the available task-details action to open a recently created task in the full editor.

### 3.2 Full Task Editor

To create a detailed task:

1. Open **Tasks**.
2. Select the create-task action.
3. Enter the task title.
4. Optionally enter a task number and date.
5. Select a status.
6. Enter a short description or Markdown description.
7. Select **Create Task**.

The full editor supports:

- Task number.
- Task title.
- Optional TaskDate.
- Task status.
- Short description.
- Markdown description.
- Planned time.
- Actual work records after task creation.

TaskDate is optional in the full editor.

### 3.3 Editing Tasks

Open an existing task to modify its information.

Select **Save Changes** to persist the changes.

Task Details includes an Edit/Preview mode for Markdown descriptions.

If you attempt to leave with unsaved changes, Daycord can request confirmation before discarding them.

### 3.4 Task Statuses

Daycord includes two built-in statuses:

- Open.
- Completed.

Completion is determined by the status's completion property, not only its displayed name.

Changing a status may update completion indicators and timestamps.

### 3.5 Deleting Tasks

To delete a task:

1. Open Tasks.
2. Select the task.
3. Choose the delete action.
4. Review the confirmation dialog.
5. Confirm or cancel.

The confirmation dialog uses a background blur effect.

Deleting a task also removes its associated planning periods and actual work entries.

**Warning:** Deletion is permanent. Maintain backups of important data.

## 4. Task Navigation

### 4.1 Opening Tasks

In the Tasks list, you can open a task using:

- A double-click on its row.
- Enter when the task row is selected and the task list has focus.
- The dedicated Open Task button.

A single click selects a task without opening its details.

### 4.2 Keyboard Navigation

The task list supports keyboard-based selection and navigation.

- Use Tab to navigate between focusable controls.
- Use the arrow keys to navigate the focused task list.
- Press Enter to open the selected task.
- Press Escape to clear the task selection when the list is focused.

Enter does not open a task when focus is in the search field, filters, or date inputs.

Within Task Details, Escape can close the calendar, cancel a confirmation, or initiate navigation back, depending on the active context.

If unsaved changes are present, Daycord can require confirmation before leaving the editor.

### 4.3 Confirmation Dialogs

When a delete confirmation is open:

- Escape cancels the confirmation.
- Tab and Shift+Tab move between the available confirmation buttons.
- Task-list navigation cannot activate an underlying task.

## 5. Searching and Filtering Tasks

Open **Tasks** to access searching, filtering, and sorting.

### 5.1 Search

Use the search field to find tasks by title or short description.

### 5.2 Filters

Select **Filters** to display the available controls.

Filters include:

- Task status.
- Start date.
- End date.
- Sorting field.
- Sorting direction.

The date range filters tasks by TaskDate.

Use **Clear filters** to reset the filtering state.

### 5.3 Home to Today's Tasks

On Home, select the **Today's Tasks** card or heading.

Daycord opens Tasks and applies a date filter for the current local calendar day.

The filter uses **TaskDate**.

It does not use the dates of planning periods or actual work entries.

For example:

- TaskDate: November 11.
- PlannedDate: November 12.
- WorkDate: November 13.

The task belongs to the November 11 task list.

After navigation:

- The date filter is visible.
- The date range is set to today.
- Search and status filters are reset.

You can change or clear these filters afterward.

The Today filter is not permanently enforced.

## 6. Planning Time

Planning periods represent intended work.

Each planning period has:

- A planned date.
- A duration.

A task can contain multiple planning periods.

### 6.1 Add Planned Time

1. Open an existing task.
2. Find the Planned Time section.
3. Select **Add period**.
4. Choose a date.
5. Enter a duration.
6. Save the planning period.

You can edit or delete individual periods without deleting the task.

### 6.2 Duration Input

Supported duration expressions include:

| Input | Duration |
|---|---|
| `1` | 1 hour |
| `30m` | 30 minutes |
| `1h 30m` | 1 hour 30 minutes |
| `2h` | 2 hours |
| `1d` | 1 configured day |
| `2d 3h` | 2 configured days and 3 hours |

A plain number represents hours.

The duration of one day can be configured in Settings.

Planned time does not automatically create actual work records.

## 7. Actual Time

Actual time represents work performed on a task.

Each work entry contains:

- WorkDate.
- Actual duration.
- Work description.
- Optional note.

A task can contain multiple work entries.

### 7.1 Add a Work Entry

1. Open an existing task.
2. Find the Actual Time section.
3. Select **Add work entry**.
4. Choose the work date.
5. Enter the actual duration.
6. Enter a description or note if needed.
7. Save the entry.

### 7.2 Edit and Delete

Select an existing work entry to edit its date, duration, description, or note.

Save the entry to persist the changes.

Individual work entries can also be deleted.

### 7.3 Switching Between Drafts

Daycord preserves separate unsaved editing drafts during a work-entry editing session.

For example:

1. Edit work entry A.
2. Select work entry B without saving A.
3. Edit work entry B.
4. Return to A.

The draft changes to A remain available during the editing session.

Switching between drafts does not automatically save them to the database.

Save the entries you want to keep before leaving the editor.

### 7.4 Planned vs. Actual Time

Planned and actual time are calculated independently.

A task may have:

- 5 hours planned.
- 3 hours of actual work.

The difference does not automatically change either record.

## 8. Calendar and Dates

Daycord uses a shared calendar component across its date-selection interfaces.

### 8.1 Calendar Page

Open **Calendar** to view tasks and activity by month.

Use the previous and next month controls to navigate.

Use **Today** to return to the current date.

Select a day to inspect:

- Tasks.
- Planned work.
- Actual work.

Available entries can open their associated task details.

### 8.2 Date Selection

The shared Daycord calendar is available in:

- Quick Task.
- Tasks filters.
- Task Details.
- Planning periods.
- Actual work entries.
- Reports date ranges.

It provides consistent month navigation and date selection.

Dates can also be entered manually in supported date fields.

Use the date format indicated by the input field.

Task Details provides a way to clear the optional task date.

Not every date field supports an empty value.

### 8.3 Independent Dates

Daycord uses three distinct dates:

| Field | Meaning |
|---|---|
| TaskDate | Date assigned to a task |
| PlannedDate | Date assigned to a planning period |
| WorkDate | Date assigned to an actual work entry |

For example:

| Activity | Date |
|---|---|
| TaskDate | November 11 |
| PlannedDate | November 12 |
| WorkDate | November 13 |

These dates are independent.

Changing TaskDate does not automatically move existing planning periods or actual work entries.

The calendar displays each type of activity on its corresponding date.

## 9. Reports

The **Reports** section generates Excel work reports in XLSX format.

### 9.1 Reporting Period

1. Open Reports.
2. Select the start date using **From**.
3. Select the end date using **To**.
4. Review the report statistics.
5. Select an Excel preset.
6. Export the workbook.

The reporting period can span multiple months.

The start date cannot be later than the end date.

### 9.2 Report Data

The reporting model includes:

- Planning periods within the selected date range.
- Actual work entries within the selected date range.
- Eligible tasks with TaskDate but no planned or actual time records.

Planning periods and actual work entries use their own dates.

TaskDate-only rows use the task's date.

### 9.3 Tasks Without Time Records

A task with TaskDate but no planning periods or work entries can appear in the reporting model.

For example:

- Task: Prepare documentation.
- TaskDate: November 11.
- Planned time: Not recorded.
- Actual time: Not recorded.

This task contributes an informational row to the full report model when its TaskDate falls within the selected period.

It does not create fictional planned or actual duration.

TaskDate-only rows apply to tasks without time records, not to tasks whose time records merely fall outside the selected period.

### 9.4 Report Statistics

Reports displays statistics for the selected period.

| Statistic | Meaning |
|---|---|
| Report rows | Number of rows in the complete report model |
| Planned total | Total planned duration |
| Actual total | Total actual duration |
| Work entries | Number of included actual work entries |

**Report statistics do not depend on the selected Excel preset.**

A preset determines the content of the exported workbook, not the underlying statistics.

Report rows is not a count of unique tasks.

One task may contribute multiple rows.

### 9.5 Excel Row Filtering

The number of exported Excel rows may differ from Report rows.

The selected preset determines which types of rows are meaningful in the exported file.

For example:

- Planning rows are included.
- Actual-only rows require an actual-time or work-description column.
- TaskDate-only rows require a visible task identifier, such as a task title.

A preset containing only a date column cannot provide a useful identity for TaskDate-only rows.

Consequently, the exported XLSX may contain fewer data rows than the full reporting model.

### 9.6 Report Presets

Reports includes a built-in **Default** preset.

Custom presets allow you to:

- Choose columns.
- Change their order.
- Customize column headings.
- Save column settings.
- Rename presets.
- Create additional presets.
- Delete custom presets.
- Reset the configuration to the default.

The built-in Default preset cannot be deleted.

### 9.7 Exporting XLSX

To export a report:

1. Select the reporting period.
2. Choose a preset.
3. Configure the columns if necessary.
4. Save the preset settings.
5. Select **Export to Excel**.
6. Choose the destination.
7. Save the workbook.

Built-in report headings and totals use the language selected in Settings.

Custom column headings are preserved as entered.

Reports can also generate workbooks for periods without report rows.

## 10. Settings

### 10.1 Language

Open **Settings → Language**.

Supported languages:

- English.
- Russian.

Changing the language updates the interface without restarting Daycord.

The selected language is saved automatically.

### 10.2 Appearance

Open **Settings → Appearance**.

Available modes:

| Mode | Behavior |
|---|---|
| System | Follows the operating system |
| Light | Uses the light theme |
| Dark | Uses the dark theme |

Changes take effect without restarting.

### 10.3 Duration of One Day

Open Settings and locate **Duration of one day**.

The default is:

```text
1d = 24h
```

For example, set the day length to:

```text
8h
```

After saving, the preview reflects the new setting:

```text
1d = 8h
```

With an eight-hour day:

| Input | Duration |
|---|---|
| `1d` | 8 hours |
| `2d` | 16 hours |
| `2d 3h` | 19 hours |

The allowed day length is between 1 minute and 24 hours.

Changing this setting does not recalculate existing saved durations.

## 11. Backup and Restore

Daycord provides user-controlled backups, automatic backups, and emergency recovery.

These mechanisms have different purposes.

### 11.1 Creating a User Backup

To create a backup:

1. Open **Settings**.
2. Find **Backup & Restore**.
3. Select **Create backup**.
4. Choose a destination.
5. Save the `.db` file.
6. Check the success message.

Daycord creates and validates a consistent SQLite snapshot before publishing the backup file.

A backup contains your application database, including tasks, time records, report presets, and settings.

Store important backups outside the application data directory.

An external drive or another trusted storage location is recommended.

### 11.2 Automatic Backups

Daycord automatically creates SQLite database snapshots.

Automatic backups:

- Use SQLite's Online Backup API.
- Are validated before publication.
- Are created after changes according to the backup scheduler.
- Are not created unnecessarily when the database is unchanged.
- Retain up to three published automatic copies.
- Preserve previous backups if a new snapshot cannot be created successfully.

Automatic backups are stored in:

```text
Backups/Automatic
```

This directory is located inside Daycord's application data directory.

**Automatic backups are not guaranteed to run immediately after every operation.**

The latest user changes may not yet be included in the newest available snapshot.

Maintain independent user backups for important data.

### 11.3 Restoring a User Backup

Restoring a backup replaces the current database state with the state contained in the selected backup.

To prepare restoration:

1. Open **Settings → Backup & Restore**.
2. Select **Choose backup file**.
3. Choose a Daycord SQLite backup.
4. Allow Daycord to validate it.
5. Review the confirmation.
6. Confirm restoration.

Daycord prepares the operation without immediately replacing the running database.

After confirming:

1. Close Daycord.
2. Start Daycord again.
3. Allow the restoration to complete.
4. Verify your tasks, time records, and settings.

Daycord creates a safety backup before replacing the existing database.

**Warning:** Changes made after the selected backup was created may no longer be present.

Create a fresh backup of your current data before restoring an older copy.

Invalid or incompatible backup files are rejected.

## 12. Emergency Database Recovery

Daycord includes automatic emergency recovery for eligible SQLite database corruption.

This mechanism is separate from the user-controlled Restore feature.

### 12.1 When Recovery Is Used

Automatic recovery is intended for supported physical database corruption scenarios, including:

- `SQLITE_CORRUPT`
- `SQLITE_NOTADB`

Not every database initialization failure qualifies for automatic recovery.

For example, recovery is not a general-purpose solution for migration checksum errors.

### 12.2 Recovery Process

When eligible corruption is detected during startup, Daycord can:

1. Identify an applicable database corruption condition.
2. Search for a suitable automatic backup.
3. Validate the recovery candidate.
4. Preserve the original damaged database files.
5. Install the selected backup through a protected restore process.
6. Validate database initialization after restoration.
7. Display a recovery-completed message.
8. Offer to restart the application.

The success message is displayed only after the recovery process has completed its required initialization checks.

### 12.3 After Recovery

When the recovery-completed window appears, select **Restart**.

After Daycord restarts:

1. Open Tasks.
2. Verify the available tasks.
3. Check planned and actual time records.
4. Review report presets and Settings.
5. Create a new user backup if the restored data is correct.

**Important:** Emergency recovery returns the database to a previously backed-up state.

Changes made after that backup may be missing.

The damaged original files are preserved for possible investigation, but recovering the missing changes from them is not guaranteed.

### 12.4 When Recovery Is Not Possible

Daycord may be unable to recover automatically if:

- No suitable automatic backup exists.
- Available backups are invalid or incompatible.
- Database or filesystem operations fail.
- The failure is outside the supported recovery conditions.

In these situations, Daycord can stop startup and display an error.

The emergency recovery window does not provide a manual list of backups to choose from.

The normal user-controlled Backup & Restore function remains available through Settings when the application can start.

Do not delete or manually replace database files to force startup.

Preserve the application data and seek assistance if necessary.

## 13. Updating Daycord

Daycord stores user data separately from the application executable.

An ordinary update from 0.1.0 to 0.1.1 does not require deleting the database.

Before updating:

1. Open Settings.
2. Create a user backup.
3. Store the backup in a safe location.
4. Close Daycord.
5. Install the newer application version.
6. Start Daycord.
7. Verify that your existing data is available.

Daycord performs supported database initialization and migrations during startup.

Do not delete `daycord.db` or the application data directory as part of the update process.

If an update fails, preserve the database and any related files before attempting troubleshooting.

## 14. Troubleshooting

### Daycord Does Not Start

Check that you are using a supported operating system and architecture.

If the operating system displays a security warning, verify that the application was obtained from a trusted source.

If a database error appears, preserve the application data directory.

Do not remove the database to bypass initialization.

### A Task Is Missing

Open Tasks and inspect the search field and filters.

Clear the filters and check the complete task list.

If the task was deleted or lost during restoration, recovering it may require an older backup.

### Today's Tasks Does Not Show a Task

Check the task's TaskDate.

Today's Tasks uses TaskDate, not the dates of planned or actual work.

### Planned and Actual Time Differ

Planned time and actual work are independent records.

Check the saved dates and durations of their individual entries.

### A Report Contains Unexpected Rows

Check the reporting period and the dates of the underlying records.

Remember that:

- TaskDate-only tasks can contribute informational rows.
- One task can contribute multiple report rows.
- Report statistics are independent of Excel presets.
- Excel column selection can affect exported rows.

### A Backup Cannot Be Created

Check that the selected destination is accessible and writable.

User backups must be saved to a supported local filesystem destination outside Daycord's protected application data directory.

### A Backup Cannot Be Restored

The backup may be invalid, corrupted, incompatible, or inaccessible.

Try another known-good backup if available.

Do not modify backup files or migration records to bypass validation.

### Emergency Recovery Failed

Preserve the original application data directory.

Keep any available automatic backups and diagnostic files.

Do not delete restore staging files or manually overwrite the database.

## 15. Data Storage and Privacy

Daycord stores application data locally in SQLite.

Typical database locations:

**macOS:**

```text
~/Library/Application Support/Daycord/daycord.db
```

**Windows:**

```text
%LOCALAPPDATA%\Daycord\daycord.db
```

Daycord does not provide built-in cloud synchronization.

The database and its backups may contain private work information.

Treat backup files as sensitive data and store them securely.

Maintain independent backups to protect against device loss, filesystem damage, and other failures.

## 16. Additional Documentation

For technical information, see:

- [Architecture](architecture.md)
- [Database](database.md)
- [Backup and Recovery](backup-and-recovery.md)
- [Development](development.md)
- [Testing](testing.md)
- [Releasing](releasing.md)