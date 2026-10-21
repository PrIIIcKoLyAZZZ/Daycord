using System.Globalization;
using Dapper;
using Daycord.Core.Application.Contracts;
using Daycord.Core.Application.Models;
using Daycord.Core.Domain.Entities;

using TaskQueryModel = Daycord.Core.Application.Models.TaskQuery;

namespace Daycord.Infrastructure.Persistence.Repositories;

public sealed class SqliteTaskRepository : ITaskRepository
{
    private readonly SqliteConnectionFactory _connections;

    private const string InsertTaskSql = """
        INSERT INTO Tasks
        (
            Id,
            TaskDate,
            TaskNumber,
            Title,
            ShortDescription,
            DescriptionMarkdown,
            StatusId,
            CreatedAtUtc,
            UpdatedAtUtc,
            CompletedAtUtc
        )
        VALUES
        (
            @Id,
            @TaskDate,
            @TaskNumber,
            @Title,
            @ShortDescription,
            @DescriptionMarkdown,
            @StatusId,
            @CreatedAtUtc,
            @UpdatedAtUtc,
            @CompletedAtUtc
        );
        """;

    private const string InsertPlanningPeriodSql = """
        INSERT INTO PlanningPeriods
        (
            Id,
            TaskId,
            PlannedDate,
            PlannedMinutes
        )
        VALUES
        (
            @Id,
            @TaskId,
            @PlannedDate,
            @PlannedMinutes
        );
        """;

    public SqliteTaskRepository(
        SqliteConnectionFactory connections)
    {
        _connections = connections
            ?? throw new ArgumentNullException(nameof(connections));
    }

    public async Task CreateAsync(
        TaskItem task,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        await using var connection =
            await _connections.OpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(
            new CommandDefinition(
                InsertTaskSql,
                GetTaskParameters(task),
                cancellationToken: cancellationToken));
    }

    public async Task CreateWithPlanningAsync(
        TaskItem task,
        PlanningPeriod planningPeriod,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(planningPeriod);

        if (planningPeriod.TaskId != task.Id)
        {
            throw new ArgumentException(
                "Planning period must reference the task being created.",
                nameof(planningPeriod));
        }

        if (planningPeriod.PlannedDate != task.TaskDate)
        {
            throw new ArgumentException(
                "Initial planning date must match the task date.",
                nameof(planningPeriod));
        }

        if (planningPeriod.PlannedMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(planningPeriod),
                "Planned minutes must be greater than zero.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        await using var connection =
            await _connections.OpenConnectionAsync(cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(
            new CommandDefinition(
                InsertTaskSql,
                GetTaskParameters(task),
                transaction: transaction,
                cancellationToken: cancellationToken));

        await connection.ExecuteAsync(
            new CommandDefinition(
                InsertPlanningPeriodSql,
                new
                {
                    Id = FormatGuid(planningPeriod.Id),
                    TaskId = FormatGuid(planningPeriod.TaskId),
                    PlannedDate = FormatDate(planningPeriod.PlannedDate),
                    planningPeriod.PlannedMinutes
                },
                transaction: transaction,
                cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<TaskItem?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                Id,
                TaskDate,
                TaskNumber,
                Title,
                ShortDescription,
                DescriptionMarkdown,
                StatusId,
                CreatedAtUtc,
                UpdatedAtUtc,
                CompletedAtUtc
            FROM Tasks
            WHERE Id = @Id
            LIMIT 1;
            """;

        await using var connection =
            await _connections.OpenConnectionAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<TaskItem>(
            new CommandDefinition(
                sql,
                new { Id = FormatGuid(id) },
                cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateAsync(
        TaskItem task,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        const string sql = """
            UPDATE Tasks
            SET
                TaskDate = @TaskDate,
                TaskNumber = @TaskNumber,
                Title = @Title,
                ShortDescription = @ShortDescription,
                DescriptionMarkdown = @DescriptionMarkdown,
                StatusId = @StatusId,
                UpdatedAtUtc = @UpdatedAtUtc,
                CompletedAtUtc = @CompletedAtUtc
            WHERE Id = @Id;
            """;

        await using var connection =
            await _connections.OpenConnectionAsync(cancellationToken);

        var affectedRows = await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                GetTaskParameters(task),
                cancellationToken: cancellationToken));

        return affectedRows > 0;
    }

    public async Task<bool> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Task ID must not be empty.",
                nameof(id));
        }

        cancellationToken.ThrowIfCancellationRequested();

        await using var connection =
            await _connections.OpenConnectionAsync(cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        var parameters = new { TaskId = FormatGuid(id) };

        // Foreign keys are RESTRICT, not CASCADE.
        // Delete dependent records and the task atomically.
        await connection.ExecuteAsync(
            new CommandDefinition(
                "DELETE FROM WorkEntries WHERE TaskId = @TaskId;",
                parameters,
                transaction: transaction,
                cancellationToken: cancellationToken));

        await connection.ExecuteAsync(
            new CommandDefinition(
                "DELETE FROM PlanningPeriods WHERE TaskId = @TaskId;",
                parameters,
                transaction: transaction,
                cancellationToken: cancellationToken));

        var affectedRows = await connection.ExecuteAsync(
            new CommandDefinition(
                "DELETE FROM Tasks WHERE Id = @TaskId;",
                parameters,
                transaction: transaction,
                cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);

        return affectedRows == 1;
    }

    public async Task<IReadOnlyList<TaskListItem>> ListAsync(
        TaskQueryModel query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var direction = query.Direction switch
        {
            TaskSortDirection.Ascending => "ASC",
            TaskSortDirection.Descending => "DESC",
            _ => throw new ArgumentOutOfRangeException(
                nameof(query),
                "Unsupported sort direction.")
        };

        var orderBy = query.SortBy switch
        {
            TaskSortField.TaskDate =>
                $"CASE WHEN t.TaskDate IS NULL THEN 1 ELSE 0 END ASC, " +
                $"t.TaskDate {direction}",

            TaskSortField.Title =>
                $"t.Title COLLATE NOCASE {direction}",

            TaskSortField.Status =>
                $"s.SortOrder {direction}, " +
                $"s.Name COLLATE NOCASE {direction}",

            _ => throw new ArgumentOutOfRangeException(
                nameof(query),
                "Unsupported sort field.")
        };

        string? searchPattern = null;

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var escaped = EscapeLikePattern(
                query.SearchText.Trim());

            searchPattern = $"%{escaped}%";
        }

        var sql = $"""
            SELECT
                t.Id,
                t.TaskDate,
                t.TaskNumber,
                t.Title,
                t.ShortDescription,
                t.StatusId,
                s.Name AS StatusName,
                CASE WHEN s.IsSystem = 1
                    THEN s.SystemKey
                    ELSE NULL
                END AS StatusSystemKey,
                s.IsCompleted,
                planned.TotalPlannedMinutes,
                actual.TotalActualMinutes
            FROM Tasks AS t
            INNER JOIN TaskStatuses AS s
                ON s.Id = t.StatusId
            LEFT JOIN
            (
                SELECT
                    TaskId,
                    SUM(PlannedMinutes) AS TotalPlannedMinutes
                FROM PlanningPeriods
                GROUP BY TaskId
            ) AS planned
                ON planned.TaskId = t.Id
            LEFT JOIN
            (
                SELECT
                    TaskId,
                    SUM(ActualMinutes) AS TotalActualMinutes
                FROM WorkEntries
                GROUP BY TaskId
            ) AS actual
                ON actual.TaskId = t.Id
            WHERE
                (
                    @SearchPattern IS NULL
                    OR t.TaskNumber LIKE @SearchPattern ESCAPE '~'
                    OR t.Title LIKE @SearchPattern ESCAPE '~'
                    OR t.ShortDescription LIKE @SearchPattern ESCAPE '~'
                )
                AND (
                    @StatusId IS NULL
                    OR t.StatusId = @StatusId
                )
                AND (
                    @TaskDate IS NULL
                    OR t.TaskDate = @TaskDate
                )
                AND (
                    @DateFrom IS NULL
                    OR t.TaskDate >= @DateFrom
                )
                AND (
                    @DateTo IS NULL
                    OR t.TaskDate <= @DateTo
                )
            ORDER BY {orderBy}, t.Id ASC;
            """;

        var parameters = new
        {
            SearchPattern = searchPattern,
            StatusId = query.StatusId.HasValue
                ? FormatGuid(query.StatusId.Value)
                : null,
            TaskDate = FormatNullableDate(query.TaskDate),
            DateFrom = FormatNullableDate(query.DateFrom),
            DateTo = FormatNullableDate(query.DateTo)
        };

        await using var connection =
            await _connections.OpenConnectionAsync(cancellationToken);

        var items = await connection.QueryAsync<TaskListItem>(
            new CommandDefinition(
                sql,
                parameters,
                cancellationToken: cancellationToken));

        return items.ToList();
    }

    public async Task<IReadOnlyList<TaskStatusDefinition>> ListStatusesAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                Id,
                SystemKey,
                Name,
                IsSystem,
                IsCompleted,
                SortOrder
            FROM TaskStatuses
            ORDER BY
                SortOrder ASC,
                Name COLLATE NOCASE ASC,
                Id ASC;
            """;

        await using var connection =
            await _connections.OpenConnectionAsync(cancellationToken);

        var statuses =
            await connection.QueryAsync<TaskStatusDefinition>(
                new CommandDefinition(
                    sql,
                    cancellationToken: cancellationToken));

        return statuses.ToList();
    }

    public async Task<TaskStatusDefinition?> GetStatusByIdAsync(
        Guid statusId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                Id,
                SystemKey,
                Name,
                IsSystem,
                IsCompleted,
                SortOrder
            FROM TaskStatuses
            WHERE Id = @StatusId
            LIMIT 1;
            """;

        await using var connection =
            await _connections.OpenConnectionAsync(cancellationToken);

        return await connection
            .QuerySingleOrDefaultAsync<TaskStatusDefinition>(
                new CommandDefinition(
                    sql,
                    new { StatusId = FormatGuid(statusId) },
                    cancellationToken: cancellationToken));
    }

    public async Task<TaskStatusDefinition?> GetOpenStatusAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                Id,
                SystemKey,
                Name,
                IsSystem,
                IsCompleted,
                SortOrder
            FROM TaskStatuses
            WHERE SystemKey = 'open'
              AND IsSystem = 1
            LIMIT 1;
            """;

        await using var connection =
            await _connections.OpenConnectionAsync(cancellationToken);

        return await connection
            .QuerySingleOrDefaultAsync<TaskStatusDefinition>(
                new CommandDefinition(
                    sql,
                    cancellationToken: cancellationToken));
    }

    private static object GetTaskParameters(TaskItem task)
    {
        return new
        {
            Id = FormatGuid(task.Id),
            TaskDate = FormatNullableDate(task.TaskDate),
            task.TaskNumber,
            task.Title,
            task.ShortDescription,
            task.DescriptionMarkdown,
            StatusId = FormatGuid(task.StatusId),
            CreatedAtUtc = FormatUtc(task.CreatedAtUtc),
            UpdatedAtUtc = FormatUtc(task.UpdatedAtUtc),
            CompletedAtUtc = task.CompletedAtUtc.HasValue
                ? FormatUtc(task.CompletedAtUtc.Value)
                : null
        };
    }

    private static string FormatGuid(Guid value)
    {
        return value.ToString("D");
    }

    private static string FormatDate(DateOnly value)
    {
        return value.ToString(
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture);
    }

    private static string? FormatNullableDate(DateOnly? value)
    {
        return value.HasValue
            ? FormatDate(value.Value)
            : null;
    }

    private static string FormatUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Database timestamps must be UTC.",
                nameof(value));
        }

        return value.ToString(
            "O",
            CultureInfo.InvariantCulture);
    }

    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace("~", "~~")
            .Replace("%", "~%")
            .Replace("_", "~_");
    }
}