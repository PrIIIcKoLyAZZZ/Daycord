# Daycord User Guide

**Version:** 0.1.0  
**Supported platforms:** macOS ARM64 and Windows x64

## 1. Introduction

Daycord is an offline-first desktop application for managing tasks, planning time, recording completed work, and generating Excel reports.

Daycord keeps application data locally on your computer. You do not need to create an account or connect to an online service.

The main sections are:

- **Home** — an overview of today's tasks and activity.
- **Calendar** — tasks, planned work, and actual work organized by date.
- **Tasks** — your task list, search, filters, and task management.
- **New Task** — Quick Task creation.
- **Reports** — configurable Excel reports.
- **Settings** — language, appearance, day length, backup, and restoration.

## 2. First Launch

Open Daycord from Applications on macOS or from the Start menu on Windows.

On the first launch, Daycord creates its local database and initializes the application.

The default settings are:

| Setting | Default |
|---|---|
| Language | English |
| Appearance | System |
| Duration of one day | 24 hours |

Application settings and task information are saved automatically to the local database when you complete the corresponding operation.

You do not need to configure a server or create an account.

### 2.1 First Steps

A recommended first-time workflow is:

1. Create a task.
2. Assign planned time.
3. Record actual work.
4. Review your activity on Home or Calendar.
5. Generate an Excel report.
6. Create a backup of your data.

## 3. Home Dashboard

Home summarizes your activity for the current day.

The dashboard contains four main indicators:

| Indicator | Description |
|---|---|
| Today's Tasks | Number of tasks scheduled for today |
| Completed | Completed tasks in today's task summary |
| Planned Today | Planned time assigned to today |
| Actual Today | Actual time recorded for today |

Below the indicators, Daycord displays sections for tasks, planned work, and actual work.

### 3.1 Open a Task

Select a task in the dashboard to open its details.

You can inspect its descriptions, planned time, and work records.

### 3.2 Empty States

If you have no activity for today, the dashboard displays an empty-state message.

Tasks and work assigned to other dates remain stored in the database.

### 3.3 Refreshing Data

The dashboard loads information when it is activated and refreshes after relevant navigation and data changes.

If loading fails, use the available Retry action.

## 4. Creating a Quick Task

Quick Task is designed for creating tasks with minimal input.

Open the **New Task** section.

The Quick Task form includes:

- Date.
- Issue number (optional).
- Title.
- Planned time (optional).

### 4.1 Create a Task

1. Open New Task.
2. Select or enter the task date.
3. Optionally enter a task number, such as `DC-017`.
4. Enter a task title.
5. Optionally specify planned time.
6. Press Enter or use the create action.

Daycord saves the new task to SQLite.

If planned time was entered, the corresponding planning record is created with the task.

### 4.2 Date Selection

Use the calendar button to open Daycord's date picker.

You can also enter a date using the date field.

The date picker is part of the application's custom interface rather than a native operating-system calendar.

### 4.3 Planned Time Examples

Valid expressions include:

| Input | Result with the default day length |
|---|---|
| `1` | 1 hour |
| `30m` | 30 minutes |
| `1h` | 1 hour |
| `1h 30m` | 1 hour 30 minutes |
| `1d` | 24 hours |
| `2d 3h` | 51 hours |

A plain number represents hours, not minutes.

The meaning of `d` depends on the **Duration of one day** setting.

### 4.4 Recently Created Tasks

Quick Task displays tasks created during the current session.

Use the task-details action beside a recent task to open its full editor.

The recent-task list is session state, not a permanent replacement for the Tasks section.

## 5. Creating a Full Task

For detailed task creation, open **Tasks** and use the create-task action.

This opens the full Task Details editor in creation mode.

The editor supports:

- Task number.
- Title.
- Optional task date.
- Task status.
- Short description.
- Markdown description.
- Planned time.
- Actual work, after the task exists.

### 5.1 Create a Detailed Task

1. Open Tasks.
2. Select the action for creating a task.
3. Enter a title.
4. Optionally enter a task number.
5. Select a date or leave it empty.
6. Select the task status.
7. Enter a short description.
8. Add Markdown content if needed.
9. Use **Create Task** to save.

Additional planned and actual time records can be managed through Task Details.

### 5.2 Tasks Without Dates

The task date is optional in the full task editor.

A task without a task date can still have planned or actual work recorded on specific dates.

These dates are stored independently.

## 6. Editing Tasks

Open the **Tasks** section and select the task you want to edit.

The Task Details editor allows changes to the task's information.

### 6.1 Edit a Task

1. Open an existing task.
2. Modify the required fields.
3. Select **Save Changes**.
4. Return to the task list when finished.

You can change or clear an optional task number or task date.

### 6.2 Markdown Descriptions

Task Details includes a Markdown editor and preview.

Use **Edit** to modify the Markdown source.

Use **Preview** to view the rendered description.

Markdown can be used for:

- Headings.
- Lists.
- Emphasis.
- Links.
- Code snippets.
- Other supported Markdown formatting.

The short description and Markdown description are separate fields.

### 6.3 Unsaved Changes

If you attempt to leave an editing session with unsaved changes, Daycord may ask whether to discard those changes.

Choose **Keep Editing** to continue editing, or **Discard Changes** to leave without saving.

### 6.4 Deleting Tasks

Tasks can be deleted from the Tasks section.

1. Select the task.
2. Choose Delete.
3. Review the confirmation.
4. Confirm the deletion.

Deleting a task also removes its associated planned periods and actual work entries.

**Warning:** Task deletion is permanent. Create a backup before making large or destructive changes.

## 7. Finding and Filtering Tasks

The Tasks page provides searching, filtering, and sorting.

### 7.1 Search

Use the search field to find tasks by title or short description.

### 7.2 Filters

Select **Filters** to expand the filter controls.

Available filters include:

- Task status.
- Start date.
- End date.
- Sort field.
- Sort direction.

Date fields support keyboard entry and Daycord's shared calendar picker.

### 7.3 Sorting

You can sort tasks by supported fields such as:

- Date.
- Title.
- Status.

Change the sorting direction between ascending and descending.

### 7.4 Clearing Filters

Use **Clear filters** to restore the default filtering state.

If no tasks match the selected criteria, Daycord displays an appropriate empty-state message.

## 8. Task Statuses

Each task has a status.

Daycord includes the built-in statuses:

- Open.
- Completed.

The underlying status model also supports custom statuses.

A status has a completion property that determines whether the task is treated as completed.

Changing a task's status may affect:

- Completion indicators.
- Completed task counts.
- Completion timestamps.

Status names are not used as the technical definition of completion.

## 9. Planned Time

Planned time is managed using planning periods.

A planning period records:

- A planned date.
- A positive duration.

A task can have more than one planning period.

### 9.1 Add a Planning Period

1. Open an existing task.
2. Find the Planned Time section.
3. Select **Add period**.
4. Choose the planned date.
5. Enter a duration.
6. Select **Save period**.

### 9.2 Edit a Planning Period

Select an existing period, update its date or duration, and save the changes.

### 9.3 Delete a Planning Period

Select the period's delete action and confirm.

Deleting one planning period does not delete the task or other time records.

### 9.4 Planned Totals

Daycord calculates planned totals from the task's planning periods.

For example:

| Date | Planned |
|---|---|
| November 2 | 2h |
| November 3 | 1h 30m |
| November 5 | 3h |

Total planned time: **6h 30m**.

Planned time represents intended effort. It does not automatically create actual work entries.

## 10. Actual Time and Work Log

Actual time is recorded using work entries.

A work entry contains:

- Work date.
- Actual duration.
- Work description.
- Optional note.

A task can have multiple work entries, including multiple entries on the same date.

### 10.1 Add a Work Entry

1. Open an existing task.
2. Find the Actual Time or Work Log section.
3. Select **Add work entry**.
4. Choose the work date.
5. Enter the actual duration.
6. Enter a work description or note as needed.
7. Select **Save entry**.

Work entries are saved independently from the main task fields.

### 10.2 Edit a Work Entry

1. Select an existing work entry.
2. Change its date, duration, description, or note.
3. Save the entry.

### 10.3 Switching Between Drafts

Daycord supports separate in-memory drafts for work-entry editing.

For example:

1. Select work entry A.
2. Modify its description.
3. Select work entry B without saving A.
4. Modify work entry B.
5. Return to A.

The unsaved draft for A remains available in the editing session.

**Important:** Switching between drafts does not save them to SQLite.

Save the entries you want to keep before leaving the editor.

### 10.4 Delete a Work Entry

Select the delete action for a work entry and confirm.

Other work entries and planning periods are not deleted.

### 10.5 Actual Totals

Actual totals are calculated from saved work entries.

For example:

| Date | Actual |
|---|---|
| November 2 | 1h 15m |
| November 3 | 2h |
| November 3 | 30m |

Total actual time: **3h 45m**.

Actual and planned time are always tracked independently.

## 11. Calendar

The Calendar page provides a monthly view of tasks and time records.

### 11.1 Navigating the Calendar

Use the previous and next month controls to move between months.

Use **Today** to return to the current date.

The calendar supports navigation across years.

### 11.2 Daily Indicators

Calendar cells can display indicators for:

- Tasks.
- Planned work.
- Actual work.

These indicators represent separate kinds of activity.

### 11.3 Selected Day

Select a date in the calendar.

The selected-day section displays the available tasks, planned work, and actual work for that date.

You can open an associated task directly from the displayed entries.

### 11.4 Important Date Behavior

Daycord uses three independent dates:

| Date | Meaning |
|---|---|
| Task Date | The date assigned to a task |
| Planned Date | The date assigned to planned work |
| Work Date | The date when work was recorded |

For example, a task may be dated November 2, have planned work on November 3, and actual work on November 4.

The Calendar represents each activity on its corresponding date.

## 12. Excel Reports

The Reports section generates Excel work reports in XLSX format.

Reports are generated locally and can include activity across multiple months.

### 12.1 Choose a Reporting Period

1. Open Reports.
2. Select the start date in **From**.
3. Select the end date in **To**.
4. Use the calendar controls or enter the dates manually.
5. Review the updated report summary.

The start date must not be later than the end date.

### 12.2 Report Summary

Reports can display:

- Number of report rows.
- Number of work entries.
- Total planned time.
- Total actual time.

The data comes from saved planning periods and work entries.

### 12.3 Select a Preset

Use **Report preset** to select the report layout.

Daycord includes a built-in Default preset.

The standard report contains four columns:

1. Date.
2. Task.
3. Work performed.
4. Time.

Custom presets can change the selected fields, headers, and column order.

### 12.4 Export to Excel

1. Choose the reporting period.
2. Select the desired preset.
3. Save any pending preset changes.
4. Select **Export to Excel**.
5. Choose a destination and filename.
6. Save the workbook.

The exported XLSX file can be opened in compatible spreadsheet applications such as Microsoft Excel.

### 12.5 Duration Formatting

Excel reports use time formats that can display totals exceeding 24 hours.

For example, a total of 27 hours is represented as 27 hours rather than wrapping around to 3 hours.

### 12.6 Empty Reporting Periods

Daycord supports exporting a period without report rows.

This may be useful for producing a report template for a period without recorded work.

## 13. Report Presets

Report presets define which fields appear in the exported workbook and how they are presented.

### 13.1 Default Preset

The Default preset is built into Daycord.

It provides the standard reporting layout and cannot be deleted.

### 13.2 Create a Custom Preset

1. Open Reports.
2. Find the preset controls.
3. Enter a new preset name.
4. Select the create-preset action.
5. Select the new preset.

### 13.3 Customize Columns

Under **Columns and order**, you can:

- Enable or disable supported fields.
- Rename column headers.
- Move columns up.
- Move columns down.

Select **Save column settings** to persist your changes.

### 13.4 Rename a Preset

Select the appropriate custom preset and use the preset rename action.

Enter the new name and confirm the change.

### 13.5 Delete a Preset

Select a custom preset and use its delete action.

Confirm the deletion.

The built-in Default preset cannot be deleted.

### 13.6 Reset Configuration

Use the available reset-to-default action to restore the standard column configuration where supported.

Review any unsaved changes before switching presets or leaving Reports.

### 13.7 Localized Headers

Built-in column headings and report totals follow the language selected in Daycord Settings.

Custom headers entered by users are preserved exactly as entered and are not translated automatically.

## 14. Language Settings

Open **Settings** and find the **Language** section.

Supported languages:

- English.
- Russian.

Select the desired language.

The interface updates without restarting Daycord.

The selected language is saved and restored the next time the application starts.

Excel reports use the selected language for their built-in headings and totals.

## 15. Appearance Settings

Open Settings and find **Appearance**.

Available options:

| Option | Behavior |
|---|---|
| System | Follow the operating system |
| Light | Use the light appearance |
| Dark | Use the dark appearance |

Changes are applied without restarting the application.

The appearance preference is saved to the local database.

## 16. Duration of One Day

Open Settings and find **Duration of one day**.

By default:

```text
1d = 24h
```

You can configure a shorter day.

For example, enter:

```text
8h
```

Select Save.

The preview then displays the equivalent of:

```text
1d = 8h
```

Examples with an eight-hour day:

| Input | Result |
|---|---|
| `1d` | 8h |
| `2d` | 16h |
| `2d 3h` | 19h |

The allowed day length is between 1 minute and 24 hours.

The setting affects the interpretation of day-based duration inputs. It does not recalculate existing saved records.

## 17. Creating a Backup

Daycord can create a backup containing your application data.

A backup includes:

- Tasks.
- Task statuses.
- Planning periods.
- Actual work entries.
- Report presets.
- Report settings.
- Application settings.
- Database migration history.

### 17.1 Create a Backup File

1. Open Settings.
2. Find **Backup & Restore**.
3. Select **Create backup**.
4. Choose a destination using the file picker.
5. Save the `.db` backup file.
6. Check the success message.

Daycord creates and validates a consistent SQLite snapshot before publishing the backup file.

The default suggested filename follows a pattern such as:

```text
Daycord-Backup-2026-10-30-221500.db
```

The filename includes the local date and time.

### 17.2 Backup Storage Recommendations

Keep at least one backup outside your normal computer storage.

Suitable destinations include:

- An external drive.
- A dedicated backup folder.
- A trusted storage service.

Daycord does not upload backups to a cloud service automatically.

A backup is a copy of your complete application database. Protect it appropriately if it contains sensitive work information.

## 18. Restoring a Backup

**Warning:** Restoration replaces the current Daycord application data with the contents of the selected backup.

Create a fresh backup of your current data before restoring an older backup.

### 18.1 Prepare a Restoration

1. Open Settings.
2. Find **Backup & Restore**.
3. Select **Choose backup file**.
4. Choose a Daycord `.db` backup.
5. Allow Daycord to validate the file.
6. Review the confirmation message.
7. Select **Restore** to prepare the operation.

The currently running database is not replaced immediately.

### 18.2 Apply the Restoration

After confirmation:

1. Close Daycord normally.
2. Launch Daycord again.
3. Allow database initialization to finish.
4. Verify that your restored tasks and work records are present.
5. Verify your report presets and settings.

Daycord applies the pending restoration during startup, before normal application features access the database.

### 18.3 Safety Backup

Before replacing the active database, Daycord creates a safety backup of its previous state.

The safety backup is stored under the application's internal `Backups/BeforeRestore` directory.

These files are retained for recovery.

Do not delete them while investigating a failed restoration.

### 18.4 Invalid Backups

Daycord checks whether the selected SQLite file belongs to a compatible Daycord schema.

Invalid, corrupted, or incompatible backups are rejected.

A file with the `.db` extension is not necessarily a valid Daycord backup.

### 18.5 Interrupted Restoration

Daycord includes recovery logic for interrupted restoration operations.

However, no software can guarantee recovery from every possible storage or hardware failure.

Maintain independent backups.

If startup reports a restoration error, avoid deleting or manually replacing database files until the situation has been investigated.

## 19. Troubleshooting

### 19.1 Daycord Does Not Start

Check that you are using a supported operating system and architecture.

The official release targets are:

- macOS ARM64.
- Windows x64.

Check whether an operating-system security warning is blocking the application.

Version 0.1.0 distribution packages are not digitally signed.

### 19.2 Database Initialization Failed

Do not delete `daycord.db`.

If the error occurred after a restoration, keep the safety backup and any restoration staging files.

Record the displayed error and the circumstances under which it occurred.

### 19.3 A Task Is Missing

Check the Tasks search field and active filters.

Clear filters and inspect the full task list.

If the task was deleted, recovery may require restoring an earlier backup.

### 19.4 Planned and Actual Time Differ

This is normal when planned work and recorded work do not match.

Planned time and actual time are separate records.

Check the dates and durations of individual entries.

### 19.5 The Calendar Does Not Show a Task

Check whether the task has a Task Date.

Planning and actual work appear according to their own dates, even when the task date is empty or different.

### 19.6 An Excel Report Is Empty

Check the selected reporting period.

Confirm that relevant saved work entries or planning periods exist in that range.

Check the selected report preset and enabled columns.

### 19.7 A Backup Cannot Be Saved

Check that the destination folder exists and is writable.

Use a local filesystem destination.

Daycord protects its internal application data directory from being used as an ordinary backup export destination.

### 19.8 A Backup Cannot Be Restored

The file may be invalid, corrupted, incompatible, or inaccessible.

Try validating another known-good backup.

Do not manually edit the backup's migration history or bypass its validation.

### 19.9 Language or Appearance Does Not Persist

Confirm that Daycord completed initialization and that the setting change succeeded.

If database persistence has failed, investigate the application database rather than resetting it.

## 20. Data Storage and Privacy

Daycord stores its data locally.

Typical database locations:

**macOS**

```text
~/Library/Application Support/Daycord/daycord.db
```

**Windows**

```text
%LOCALAPPDATA%\Daycord\daycord.db
```

There is no built-in account synchronization or cloud collaboration in version 0.1.0.

The user is responsible for keeping independent backups.

Do not share your database or backup files unless you intend to share the information they contain.

## 21. Known Limitations

Daycord 0.1.0 is an MVP.

Current limitations include:

- Official distribution is limited to macOS ARM64 and Windows x64.
- There is no built-in cloud synchronization.
- The macOS build is not Developer ID signed or notarized.
- The Windows installer is not Authenticode signed.
- Upgrade paths between different application versions have not been fully verified.
- Some recovery failure scenarios remain unverified.
- Additional defects identified during MVP testing may be documented separately.

See [Testing](testing.md) for the verification register.

## 22. Additional Documentation

- [Architecture](architecture.md)
- [Database](database.md)
- [Development](development.md)
- [Releasing](releasing.md)
- [Backup and Recovery](backup-and-recovery.md)
- [Testing](testing.md)

This guide describes the features available in Daycord 0.1.0.