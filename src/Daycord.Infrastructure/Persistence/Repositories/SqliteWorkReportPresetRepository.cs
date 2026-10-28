using System.Globalization;
using System.Text.Json;
using Dapper;
using Daycord.Core.Application.Contracts;
using Daycord.Core.Application.Models;

namespace Daycord.Infrastructure.Persistence.Repositories;

/// <summary>
/// SQLite persistence for configurable report presets.
/// </summary>
public sealed class SqliteWorkReportPresetRepository
    : IWorkReportPresetRepository
{
    private const string LegacyDefaultPresetName =
        "Стандартный отчёт ГПХ";

    private readonly SqliteConnectionFactory _connections;

    public SqliteWorkReportPresetRepository(
        SqliteConnectionFactory connections)
    {
        _connections = connections
            ?? throw new ArgumentNullException(nameof(connections));
    }

    public async Task<WorkReportPresetState> GetStateAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var connection =
            await _connections.OpenConnectionAsync(cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        // Pre-release compatibility update.
        //
        // The existing 001 migration seeded the built-in preset
        // with its original Russian name.
        //
        // Rename only that exact legacy record and only when
        // no other preset already uses the name "Default".
        //
        // This does not alter preset columns, selected preset,
        // user-created presets or the migration checksum.
        //
        // Once renamed, this UPDATE becomes a no-op.

        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE ReportPresets
                SET
                    Name = @DefaultName,
                    UpdatedAtUtc = @UpdatedAtUtc
                WHERE Id = @Id
                  AND IsBuiltIn = 1
                  AND Name = @LegacyName
                  AND NOT EXISTS (
                      SELECT 1
                      FROM ReportPresets AS OtherPreset
                      WHERE OtherPreset.Id <> @Id
                        AND OtherPreset.Name =
                            @DefaultName COLLATE NOCASE
                  );
                """,
                new
                {
                    Id = FormatGuid(
                        WorkReportPreset.DefaultPresetId),
                    LegacyName = LegacyDefaultPresetName,
                    DefaultName =
                        WorkReportPreset.DefaultPresetName,
                    UpdatedAtUtc = UtcNow()
                },
                transaction: transaction,
                cancellationToken: cancellationToken));

        var records = (await connection.QueryAsync<PresetRecord>(
            new CommandDefinition(
                """
                SELECT
                    Id,
                    Name,
                    IsBuiltIn,
                    ColumnsJson
                FROM ReportPresets
                ORDER BY
                    IsBuiltIn DESC,
                    Name COLLATE NOCASE,
                    Id;
                """,
                transaction: transaction,
                cancellationToken: cancellationToken))).ToArray();

        var activeId = await connection.ExecuteScalarAsync<string?>(
            new CommandDefinition(
                """
                SELECT ActivePresetId
                FROM ReportSettings
                WHERE SingletonId = 1;
                """,
                transaction: transaction,
                cancellationToken: cancellationToken));

        if (string.IsNullOrWhiteSpace(activeId))
        {
            throw new InvalidOperationException(
                "The active report preset is missing.");
        }

        var presets = records
            .Select(ToDomainPreset)
            .ToArray();

        var state = new WorkReportPresetState(
            Guid.Parse(activeId),
            presets);

        await transaction.CommitAsync(cancellationToken);

        return state;
    }

    public async Task SavePresetAsync(
        WorkReportPreset preset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        cancellationToken.ThrowIfCancellationRequested();

        await using var connection =
            await _connections.OpenConnectionAsync(cancellationToken);

        await using var transaction =
            connection.BeginTransaction(deferred: false);

        var id = FormatGuid(preset.Id);

        var existingFlag = await connection.ExecuteScalarAsync<int?>(
            new CommandDefinition(
                """
                SELECT IsBuiltIn
                FROM ReportPresets
                WHERE Id = @Id;
                """,
                new { Id = id },
                transaction: transaction,
                cancellationToken: cancellationToken));

        var columnsJson = SerializeColumns(preset.Columns);
        var now = UtcNow();

        if (existingFlag.HasValue)
        {
            if ((existingFlag.Value == 1) != preset.IsBuiltIn)
            {
                throw new InvalidOperationException(
                    "The built-in identity of a preset cannot change.");
            }

            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    UPDATE ReportPresets
                    SET
                        Name = @Name,
                        ColumnsJson = @ColumnsJson,
                        UpdatedAtUtc = @UpdatedAtUtc
                    WHERE Id = @Id;
                    """,
                    new
                    {
                        Id = id,
                        preset.Name,
                        ColumnsJson = columnsJson,
                        UpdatedAtUtc = now
                    },
                    transaction: transaction,
                    cancellationToken: cancellationToken));
        }
        else
        {
            if (preset.IsBuiltIn)
            {
                throw new InvalidOperationException(
                    "The built-in report preset is missing " +
                    "from the database.");
            }

            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO ReportPresets (
                        Id,
                        Name,
                        IsBuiltIn,
                        ColumnsJson,
                        CreatedAtUtc,
                        UpdatedAtUtc
                    )
                    VALUES (
                        @Id,
                        @Name,
                        @IsBuiltIn,
                        @ColumnsJson,
                        @CreatedAtUtc,
                        @UpdatedAtUtc
                    );
                    """,
                    new
                    {
                        Id = id,
                        preset.Name,
                        IsBuiltIn = 0,
                        ColumnsJson = columnsJson,
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    },
                    transaction: transaction,
                    cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SetActivePresetAsync(
        Guid presetId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(presetId);
        cancellationToken.ThrowIfCancellationRequested();

        await using var connection =
            await _connections.OpenConnectionAsync(cancellationToken);

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE ReportSettings
                SET ActivePresetId = @Id
                WHERE SingletonId = 1
                  AND EXISTS (
                      SELECT 1
                      FROM ReportPresets
                      WHERE Id = @Id
                  );
                """,
                new { Id = FormatGuid(presetId) },
                cancellationToken: cancellationToken));

        if (affected != 1)
        {
            throw new KeyNotFoundException(
                $"Report preset '{presetId}' was not found " +
                "or report settings are missing.");
        }
    }

    public async Task DeletePresetAsync(
        Guid presetId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(presetId);

        if (presetId == WorkReportPreset.DefaultPresetId)
        {
            throw new InvalidOperationException(
                "The built-in report preset cannot be deleted.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        await using var connection =
            await _connections.OpenConnectionAsync(cancellationToken);

        await using var transaction =
            connection.BeginTransaction(deferred: false);

        var id = FormatGuid(presetId);

        var existingFlag = await connection.ExecuteScalarAsync<int?>(
            new CommandDefinition(
                """
                SELECT IsBuiltIn
                FROM ReportPresets
                WHERE Id = @Id;
                """,
                new { Id = id },
                transaction: transaction,
                cancellationToken: cancellationToken));

        if (!existingFlag.HasValue)
        {
            throw new KeyNotFoundException(
                $"Report preset '{presetId}' was not found.");
        }

        if (existingFlag.Value == 1)
        {
            throw new InvalidOperationException(
                "Built-in report presets cannot be deleted.");
        }

        // If the deleted preset is active, select Default
        // in the same transaction.

        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE ReportSettings
                SET ActivePresetId = @DefaultId
                WHERE SingletonId = 1
                  AND ActivePresetId = @Id;
                """,
                new
                {
                    Id = id,
                    DefaultId = FormatGuid(
                        WorkReportPreset.DefaultPresetId)
                },
                transaction: transaction,
                cancellationToken: cancellationToken));

        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                DELETE FROM ReportPresets
                WHERE Id = @Id
                  AND IsBuiltIn = 0;
                """,
                new { Id = id },
                transaction: transaction,
                cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ResetToDefaultAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var defaultPreset = WorkReportPreset.CreateDefault();

        await using var connection =
            await _connections.OpenConnectionAsync(cancellationToken);

        await using var transaction =
            connection.BeginTransaction(deferred: false);

        var id = FormatGuid(defaultPreset.Id);

        // Never overwrite or rename a custom preset if it
        // already uses the reserved built-in display name.
        var conflictingName = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                """
                SELECT COUNT(*)
                FROM ReportPresets
                WHERE Id <> @Id
                  AND Name = @Name COLLATE NOCASE;
                """,
                new
                {
                    Id = id,
                    defaultPreset.Name
                },
                transaction: transaction,
                cancellationToken: cancellationToken));

        if (conflictingName != 0)
        {
            throw new InvalidOperationException(
                "A custom preset is already named 'Default'. " +
                "Rename that preset before resetting.");
        }

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE ReportPresets
                SET
                    Name = @Name,
                    ColumnsJson = @ColumnsJson,
                    UpdatedAtUtc = @UpdatedAtUtc
                WHERE Id = @Id
                  AND IsBuiltIn = 1;
                """,
                new
                {
                    Id = id,
                    defaultPreset.Name,
                    ColumnsJson = SerializeColumns(
                        defaultPreset.Columns),
                    UpdatedAtUtc = UtcNow()
                },
                transaction: transaction,
                cancellationToken: cancellationToken));

        if (affected != 1)
        {
            throw new InvalidOperationException(
                "The built-in report preset is missing.");
        }

        var settingsAffected = await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE ReportSettings
                SET ActivePresetId = @Id
                WHERE SingletonId = 1;
                """,
                new { Id = id },
                transaction: transaction,
                cancellationToken: cancellationToken));

        if (settingsAffected != 1)
        {
            throw new InvalidOperationException(
                "Report settings are missing.");
        }

        // Custom presets remain untouched.
        await transaction.CommitAsync(cancellationToken);
    }

    private static WorkReportPreset ToDomainPreset(
        PresetRecord record)
    {
        var columns = JsonSerializer.Deserialize<
            WorkReportColumn[]>(record.ColumnsJson);

        if (columns is null)
        {
            throw new InvalidOperationException(
                $"Preset '{record.Id}' has invalid column data.");
        }

        return new WorkReportPreset(
            Guid.Parse(record.Id),
            record.Name,
            record.IsBuiltIn == 1,
            columns);
    }

    private static string SerializeColumns(
        IReadOnlyList<WorkReportColumn> columns)
    {
        return JsonSerializer.Serialize(columns);
    }

    private static string FormatGuid(Guid id) =>
        id.ToString("D");

    private static string UtcNow() =>
        DateTime.UtcNow.ToString(
            "O",
            CultureInfo.InvariantCulture);

    private static void ValidateId(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Preset ID cannot be empty.",
                nameof(id));
        }
    }

    private sealed class PresetRecord
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public int IsBuiltIn { get; set; }

        public string ColumnsJson { get; set; } = string.Empty;
    }
}