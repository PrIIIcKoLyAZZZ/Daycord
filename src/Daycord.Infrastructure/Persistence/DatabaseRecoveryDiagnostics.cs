using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Daycord.Infrastructure.Persistence;

/// <summary>
/// File-only diagnostics for startup and recovery failures.
/// Does not open SQLite or persist raw exception messages.
/// </summary>
public sealed class DatabaseRecoveryDiagnostics
{
    private readonly AppDataPaths _paths;
    private readonly object _writeGate = new();
    private readonly string _sessionId = Guid.NewGuid().ToString("N");

    public DatabaseRecoveryDiagnostics(AppDataPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public string DirectoryPath =>
        Path.Combine(_paths.DataDirectory, "Diagnostics");

    public string LogPath =>
        Path.Combine(DirectoryPath, "recovery-events.jsonl");

    /// <summary>
    /// Best-effort logging. Failure to write diagnostics must never
    /// replace the original startup or recovery exception.
    /// </summary>
    public bool TryRecordFailure(
        DatabaseRecoveryStage stage,
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        try
        {
            var entry = new DiagnosticEvent(
                TimestampUtc: DateTimeOffset.UtcNow,
                SessionId: _sessionId,
                ApplicationVersion: Assembly.GetEntryAssembly()
                    ?.GetName().Version?.ToString() ?? "unknown",
                Platform: RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "Windows"
                    : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                        ? "macOS"
                        : "Other",
                Architecture: RuntimeInformation.OSArchitecture.ToString(),
                Profile: GetProfile(),
                Stage: stage.ToString(),
                MigrationName: null,
                SnapshotValidation: null,
                Exceptions: DescribeExceptionChain(exception));

            var line = JsonSerializer.Serialize(entry);

            lock (_writeGate)
            {
                Directory.CreateDirectory(DirectoryPath);

                using var file = new FileStream(
                    LogPath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read,
                    bufferSize: 4096,
                    options: FileOptions.WriteThrough);

                using var writer = new StreamWriter(
                    file,
                    new UTF8Encoding(
                        encoderShouldEmitUTF8Identifier: false));

                writer.WriteLine(line);
                writer.Flush();
                file.Flush(flushToDisk: true);
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private string GetProfile()
    {
        var name = Path.GetFileName(
            Path.TrimEndingDirectorySeparator(_paths.DataDirectory));

        return name switch
        {
            "Daycord-test" => "Debug",
            "Daycord" => "Release",
            _ => "Custom"
        };
    }

    private static List<DiagnosticException> DescribeExceptionChain(
        Exception exception)
    {
        var exceptions = new List<DiagnosticException>();

        for (Exception? current = exception;
             current is not null && exceptions.Count < 6;
             current = current.InnerException)
        {
            var sqlite = current as SqliteException;

            exceptions.Add(new DiagnosticException(
                Type: current.GetType().FullName ??
                      current.GetType().Name,
                Message: GetSafeMessage(current),
                HResult: $"0x{current.HResult:X8}",
                SqliteCode: sqlite?.SqliteErrorCode,
                SqliteExtendedCode: sqlite?.SqliteExtendedErrorCode));
        }

        return exceptions;
    }

    private static string GetSafeMessage(Exception exception)
    {
        // Never persist user data, SQL text or filesystem paths
        // from raw exception messages.
        return exception switch
        {
            SqliteException sqlite => sqlite.SqliteErrorCode switch
            {
                5 or 6 => "SQLite database is busy or locked.",
                8 => "SQLite database is read-only.",
                10 => "SQLite I/O operation failed.",
                11 or 26 => "SQLite database is corrupt or invalid.",
                13 => "SQLite reported insufficient disk space.",
                19 => "SQLite constraint validation failed.",
                _ => "SQLite operation failed."
            },
            UnauthorizedAccessException =>
                "Access to a required file was denied.",
            FileNotFoundException =>
                "A required recovery file is missing.",
            DirectoryNotFoundException =>
                "A required recovery directory is missing.",
            IOException =>
                "File or disk I/O operation failed.",
            InvalidDataException =>
                "Database or recovery data validation failed.",
            InvalidOperationException =>
                "Database startup or recovery state is invalid.",
            ArgumentException =>
                "Invalid database startup or recovery argument.",
            _ =>
                "An unexpected startup or recovery error occurred."
        };
    }

    private sealed record DiagnosticEvent(
        DateTimeOffset TimestampUtc,
        string SessionId,
        string ApplicationVersion,
        string Platform,
        string Architecture,
        string Profile,
        string Stage,
        string? MigrationName,
        string? SnapshotValidation,
        List<DiagnosticException> Exceptions);

    private sealed record DiagnosticException(
        string Type,
        string Message,
        string HResult,
        int? SqliteCode,
        int? SqliteExtendedCode);
}

public enum DatabaseRecoveryStage
{
    Bootstrap,
    AcquireApplicationLock,
    ApplyPendingRestore,
    InitializeSchema,
    LoadSettings,
    CompleteRestore,
    RollbackRestore,
    ApplyPreferences
}