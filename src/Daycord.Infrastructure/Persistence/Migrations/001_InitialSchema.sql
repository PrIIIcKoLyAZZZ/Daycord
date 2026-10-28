-- DAYCORD
-- Consolidated initial schema, pre-release.

-- ============================================
-- TASK STATUSES
-- ============================================

CREATE TABLE TaskStatuses (
                              Id TEXT NOT NULL PRIMARY KEY,

                              SystemKey TEXT NULL,

                              Name TEXT NOT NULL
                                  CHECK (length(trim(Name)) > 0),

                              IsSystem INTEGER NOT NULL
                                  CHECK (IsSystem IN (0, 1)),

                              IsCompleted INTEGER NOT NULL
                                  CHECK (IsCompleted IN (0, 1)),

                              SortOrder INTEGER NOT NULL,

                              CONSTRAINT CK_TaskStatuses_SystemKey CHECK (
                                  (
                                      IsSystem = 1
                                          AND SystemKey IS NOT NULL
                                          AND length(trim(SystemKey)) > 0
                                      )
                                      OR
                                  (
                                      IsSystem = 0
                                          AND SystemKey IS NULL
                                      )
                                  )
);

CREATE UNIQUE INDEX UX_TaskStatuses_SystemKey
    ON TaskStatuses(SystemKey)
    WHERE SystemKey IS NOT NULL;

-- ============================================
-- TASKS
-- ============================================

CREATE TABLE Tasks (
                       Id TEXT NOT NULL PRIMARY KEY,

                       TaskDate TEXT NULL,

                       TaskNumber TEXT NULL
        CHECK (
            TaskNumber IS NULL
            OR length(trim(TaskNumber)) > 0
        ),

                       Title TEXT NOT NULL
                           CHECK (length(trim(Title)) > 0),

                       ShortDescription TEXT NULL,

                       DescriptionMarkdown TEXT NULL,

                       StatusId TEXT NOT NULL,

                       CreatedAtUtc TEXT NOT NULL,

                       UpdatedAtUtc TEXT NOT NULL,

                       CompletedAtUtc TEXT NULL,

                       FOREIGN KEY (StatusId)
                           REFERENCES TaskStatuses(Id)
                           ON DELETE RESTRICT
);

CREATE INDEX IX_Tasks_TaskDate
    ON Tasks(TaskDate);

CREATE INDEX IX_Tasks_TaskNumber
    ON Tasks(TaskNumber)
    WHERE TaskNumber IS NOT NULL;

CREATE INDEX IX_Tasks_StatusId
    ON Tasks(StatusId);

-- ============================================
-- PLANNING PERIODS
-- ============================================

CREATE TABLE PlanningPeriods (
                                 Id TEXT NOT NULL PRIMARY KEY,

                                 TaskId TEXT NOT NULL,

                                 PlannedDate TEXT NOT NULL,

                                 PlannedMinutes INTEGER NOT NULL
                                     CHECK (PlannedMinutes > 0),

                                 FOREIGN KEY (TaskId)
                                     REFERENCES Tasks(Id)
                                     ON DELETE RESTRICT
);

CREATE INDEX IX_PlanningPeriods_TaskId
    ON PlanningPeriods(TaskId);

CREATE INDEX IX_PlanningPeriods_PlannedDate
    ON PlanningPeriods(PlannedDate);

-- ============================================
-- WORK ENTRIES
-- ============================================

CREATE TABLE WorkEntries (
                             Id TEXT NOT NULL PRIMARY KEY,

                             TaskId TEXT NOT NULL,

                             WorkDate TEXT NOT NULL,

                             ActualMinutes INTEGER NOT NULL
                                 CHECK (ActualMinutes > 0),

                             WorkDescription TEXT NULL,

                             Note TEXT NULL,

                             CreatedAtUtc TEXT NOT NULL,

                             UpdatedAtUtc TEXT NOT NULL,

                             FOREIGN KEY (TaskId)
                                 REFERENCES Tasks(Id)
                                 ON DELETE RESTRICT
);

CREATE INDEX IX_WorkEntries_TaskId
    ON WorkEntries(TaskId);

CREATE INDEX IX_WorkEntries_WorkDate
    ON WorkEntries(WorkDate);

-- ============================================
-- BUILT-IN TASK STATUSES
-- ============================================

INSERT INTO TaskStatuses (
    Id,
    SystemKey,
    Name,
    IsSystem,
    IsCompleted,
    SortOrder
)
VALUES
    (
        '{{OPEN_STATUS_ID}}',
        'open',
        'Open',
        1,
        0,
        0
    ),
    (
        '{{COMPLETED_STATUS_ID}}',
        'completed',
        'Completed',
        1,
        1,
        1
    );

-- ============================================
-- REPORT PRESETS
-- ============================================

CREATE TABLE ReportPresets (
                               Id TEXT NOT NULL PRIMARY KEY,

                               Name TEXT NOT NULL
                                   CHECK (length(trim(Name)) BETWEEN 1 AND 120),

                               IsBuiltIn INTEGER NOT NULL
                                   CHECK (IsBuiltIn IN (0, 1)),

                               ColumnsJson TEXT NOT NULL
                                   CHECK (json_valid(ColumnsJson)),

                               CreatedAtUtc TEXT NOT NULL,

                               UpdatedAtUtc TEXT NOT NULL,

                               CONSTRAINT CK_ReportPresets_DefaultIdentity CHECK (
                                   (
                                       Id = '7b9f3e18-6fc7-449e-9a12-e59063d58101'
                                           AND IsBuiltIn = 1
                                       )
                                       OR
                                   (
                                       Id <> '7b9f3e18-6fc7-449e-9a12-e59063d58101'
                                           AND IsBuiltIn = 0
                                       )
                                   )
);

CREATE UNIQUE INDEX UX_ReportPresets_Name
    ON ReportPresets(Name COLLATE NOCASE);

-- ============================================
-- ACTIVE REPORT SETTINGS
-- ============================================

CREATE TABLE ReportSettings (
                                SingletonId INTEGER NOT NULL PRIMARY KEY
                                    CHECK (SingletonId = 1),

                                ActivePresetId TEXT NOT NULL,

                                FOREIGN KEY (ActivePresetId)
                                    REFERENCES ReportPresets(Id)
                                    ON DELETE RESTRICT
);

-- ============================================
-- BUILT-IN DEFAULT REPORT PRESET
-- ============================================
--
-- Header tokens are language-independent.
-- The application resolves them to RU/EN labels.
-- Custom headers are stored as literal text.

INSERT INTO ReportPresets (
    Id,
    Name,
    IsBuiltIn,
    ColumnsJson,
    CreatedAtUtc,
    UpdatedAtUtc
)
VALUES (
           '7b9f3e18-6fc7-449e-9a12-e59063d58101',
           'Default',
           1,
           '[{"Field":1,"Header":"@daycord:Date"},{"Field":2,"Header":"@daycord:TaskNumber"},{"Field":3,"Header":"@daycord:TaskTitle"},{"Field":4,"Header":"@daycord:PlannedTime"}]',
           strftime('%Y-%m-%dT%H:%M:%fZ', 'now'),
           strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
       );

INSERT INTO ReportSettings (
    SingletonId,
    ActivePresetId
)
VALUES (
           1,
           '7b9f3e18-6fc7-449e-9a12-e59063d58101'
       );