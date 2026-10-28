using Dapper;
using Daycord.Core.Application.Contracts;
using Daycord.Core.Application.Models;

namespace Daycord.Infrastructure.Persistence.Repositories;

/// <summary>
/// SQLite persistence for application-wide preferences.
/// The database must be initialized before this repository is used.
/// </summary>
public sealed class SqliteAppSettingsRepository
    : IAppSettingsRepository
{
    private readonly SqliteConnectionFactory _connections;

    public SqliteAppSettingsRepository(
        SqliteConnectionFactory connections)
    {
        _connections = connections
            ?? throw new ArgumentNullException(nameof(connections));
    }

    /// <summary>
    /// Loads the single application settings record.
    /// Invalid or missing settings are treated as database errors,
    /// rather than silently replaced with defaults.
    /// </summary>
    public async Task<AppSettings> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var connection =
            await _connections.OpenConnectionAsync(cancellationToken);

        var record =
            await connection.QuerySingleOrDefaultAsync<AppSettingsRecord>(
                new CommandDefinition(
                    """
                    SELECT
                        Language,
                        Theme,
                        MinutesPerDay
                    FROM AppSettings
                    WHERE SingletonId = 1;
                    """,
                    cancellationToken: cancellationToken));

        if (record is null)
        {
            throw new InvalidOperationException(
                "Application settings are missing from the database.");
        }

        // The domain model performs a second layer of validation.
        // Corrupted values must not be applied to the application.

        return new AppSettings(
            record.Language,
            record.Theme,
            record.MinutesPerDay);
    }

    /// <summary>
    /// Persists validated settings in a single atomic SQL statement.
    /// Does not create or replace the singleton record.
    /// </summary>
    public async Task SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        await using var connection =
            await _connections.OpenConnectionAsync(cancellationToken);

        var affectedRows = await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE AppSettings
                SET
                    Language = @Language,
                    Theme = @Theme,
                    MinutesPerDay = @MinutesPerDay
                WHERE SingletonId = 1;
                """,
                new
                {
                    settings.Language,
                    settings.Theme,
                    settings.MinutesPerDay
                },
                cancellationToken: cancellationToken));

        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                "Application settings could not be saved. " +
                "The settings record is missing or invalid.");
        }
    }

    private sealed class AppSettingsRecord
    {
        public string Language { get; set; } = string.Empty;

        public string Theme { get; set; } = string.Empty;

        public int MinutesPerDay { get; set; }
    }
}