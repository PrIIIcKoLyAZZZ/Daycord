CREATE TABLE TaskStatuses (
    Id TEXT NOT NULL PRIMARY KEY,
    SystemKey TEXT NULL,
    Name TEXT NOT NULL CHECK (length(trim(Name)) > 0),
    IsSystem INTEGER NOT NULL CHECK (IsSystem IN (0, 1)),
    IsCompleted INTEGER NOT NULL CHECK (IsCompleted IN (0, 1)),
    SortOrder INTEGER NOT NULL,
    CONSTRAINT CK_TaskStatuses_SystemKey CHECK (
        (IsSystem = 1 AND SystemKey IS NOT NULL AND length(trim(SystemKey)) > 0)
        OR (IsSystem = 0 AND SystemKey IS NULL)
    )
);

CREATE UNIQUE INDEX UX_TaskStatuses_SystemKey
    ON TaskStatuses(SystemKey) WHERE SystemKey IS NOT NULL;

CREATE TABLE Tasks (
    Id TEXT NOT NULL PRIMARY KEY,
    TaskDate TEXT NOT NULL,
    Title TEXT NOT NULL,
    ShortDescription TEXT NULL,
    DescriptionMarkdown TEXT NULL,
    StatusId TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL,
    CompletedAtUtc TEXT NULL,
    FOREIGN KEY (StatusId) REFERENCES TaskStatuses(Id) ON DELETE RESTRICT
);

CREATE INDEX IX_Tasks_TaskDate ON Tasks(TaskDate);
CREATE INDEX IX_Tasks_StatusId ON Tasks(StatusId);

CREATE TABLE PlanningPeriods (
    Id TEXT NOT NULL PRIMARY KEY,
    TaskId TEXT NOT NULL,
    PlannedDate TEXT NOT NULL,
    PlannedMinutes INTEGER NOT NULL CHECK (PlannedMinutes > 0),
    FOREIGN KEY (TaskId) REFERENCES Tasks(Id) ON DELETE RESTRICT
);

CREATE INDEX IX_PlanningPeriods_TaskId ON PlanningPeriods(TaskId);
CREATE INDEX IX_PlanningPeriods_PlannedDate ON PlanningPeriods(PlannedDate);

CREATE TABLE WorkEntries (
    Id TEXT NOT NULL PRIMARY KEY,
    TaskId TEXT NOT NULL,
    WorkDate TEXT NOT NULL,
    ActualMinutes INTEGER NOT NULL CHECK (ActualMinutes > 0),
    WorkDescription TEXT NULL,
    Note TEXT NULL,
    CreatedAtUtc TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL,
    FOREIGN KEY (TaskId) REFERENCES Tasks(Id) ON DELETE RESTRICT
);

CREATE INDEX IX_WorkEntries_TaskId ON WorkEntries(TaskId);
CREATE INDEX IX_WorkEntries_WorkDate ON WorkEntries(WorkDate);

INSERT INTO TaskStatuses (Id, SystemKey, Name, IsSystem, IsCompleted, SortOrder)
VALUES
    ('{{OPEN_STATUS_ID}}', 'open', 'Open', 1, 0, 0),
    ('{{COMPLETED_STATUS_ID}}', 'completed', 'Completed', 1, 1, 1);