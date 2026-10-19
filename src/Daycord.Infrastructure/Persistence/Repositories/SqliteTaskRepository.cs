using Dapper;
using Daycord.Core.Application.Contracts;
using Daycord.Core.Application.Models;
using Daycord.Core.Domain.Entities;
using Daycord.Infrastructure.Persistence;

using TaskQueryModel = Daycord.Core.Application.Models.TaskQuery;

namespace Daycord.Infrastructure.Persistence.Repositories;

public sealed class SqliteTaskRepository : ITaskRepository
{
    private readonly SqliteConnectionFactory _connections;

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

        const string sql = """
            INSERT INTO Tasks
            (
                Id,
                TaskDate,
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
                @Title,
                @ShortDescription,
                @DescriptionMarkdown,
                @StatusId,
                @CreatedAtUtc,
                @UpdatedAtUtc,
                @CompletedAtUtc
            );
            """;

        await using var connection =
            await _connections.OpenConnectionAsync(
                cancellationToken);

        await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                task,
                cancellationToken: cancellationToken));
    }

    public async Task<TaskItem?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                Id,
                TaskDate,
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
            await _connections.OpenConnectionAsync(
                cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<TaskItem>(
            new CommandDefinition(
                sql,
                new { Id = id },
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
                Title = @Title,
                ShortDescription = @ShortDescription,
                DescriptionMarkdown = @DescriptionMarkdown,
                StatusId = @StatusId,
                UpdatedAtUtc = @UpdatedAtUtc,
                CompletedAtUtc = @CompletedAtUtc
            WHERE Id = @Id;
            """;

        await using var connection =
            await _connections.OpenConnectionAsync(
                cancellationToken);

        var affectedRows = await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                task,
                cancellationToken: cancellationToken));

        return affectedRows > 0;
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
                t.Title,
                t.ShortDescription,
                t.StatusId,
                s.Name AS StatusName,
                s.IsCompleted
            FROM Tasks AS t
            INNER JOIN TaskStatuses AS s
                ON s.Id = t.StatusId
            WHERE
                (
                    @SearchPattern IS NULL
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
            query.StatusId,
            query.TaskDate,
            query.DateFrom,
            query.DateTo
        };

        await using var connection =
            await _connections.OpenConnectionAsync(
                cancellationToken);

        var items = await connection.QueryAsync<TaskListItem>(
            new CommandDefinition(
                sql,
                parameters,
                cancellationToken: cancellationToken));

        return items.ToList();
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
            await _connections.OpenConnectionAsync(
                cancellationToken);

        return await connection
            .QuerySingleOrDefaultAsync<TaskStatusDefinition>(
                new CommandDefinition(
                    sql,
                    new { StatusId = statusId },
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
            WHERE
                Id = @OpenId
                AND IsSystem = 1
                AND SystemKey = 'open'
            LIMIT 1;
            """;

        await using var connection =
            await _connections.OpenConnectionAsync(
                cancellationToken);

        return await connection
            .QuerySingleOrDefaultAsync<TaskStatusDefinition>(
                new CommandDefinition(
                    sql,
                    new
                    {
                        OpenId = TaskStatusDefinition.OpenId
                    },
                    cancellationToken: cancellationToken));
    }

    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace("~", "~~")
            .Replace("%", "~%")
            .Replace("_", "~_");
    }
}