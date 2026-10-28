using System.Security.Cryptography;
using System.Text.Json;

namespace Daycord.Infrastructure.Persistence;

internal enum PendingRestorePhase
{
    Prepared,
    SafetyCreated,
    Installing,
    Installed,
    RollingBack
}

/// <summary>
/// Durable journal for one deferred restore. Paths in this record are file names,
/// never user-supplied absolute paths.
/// </summary>
internal sealed record PendingRestoreState(
    string OperationId,
    string StagedFileName,
    string StagedSha256,
    PendingRestorePhase Phase,
    string? SafetyBackupFileName = null,
    string? SafetyBackupSha256 = null);

internal static class PendingRestoreStateStore
{
    public static PendingRestoreState Read(string manifestPath)
    {
        var state = JsonSerializer.Deserialize<PendingRestoreState>(
            File.ReadAllText(manifestPath))
            ?? throw new InvalidDataException("The restore journal is empty.");

        Validate(state);
        return state;
    }

    public static void Create(string manifestPath, PendingRestoreState state) =>
        WriteAtomically(manifestPath, state, overwrite: false);

    public static void Update(string manifestPath, PendingRestoreState state) =>
        WriteAtomically(manifestPath, state, overwrite: true);

    public static string GetSha256(string filePath)
    {
        using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void WriteAtomically(
        string manifestPath, PendingRestoreState state, bool overwrite)
    {
        Validate(state);

        var directory = Path.GetDirectoryName(manifestPath)
            ?? throw new ArgumentException(
                "Invalid restore journal path.", nameof(manifestPath));

        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(directory,
            $".pending-restore-{Guid.NewGuid():N}.tmp");

        try
        {
            var contents = JsonSerializer.SerializeToUtf8Bytes(state);

            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(contents);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, manifestPath, overwrite);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void Validate(PendingRestoreState state)
    {
        if (!Guid.TryParseExact(state.OperationId, "N", out var operationId) ||
            !string.Equals(state.OperationId, operationId.ToString("N"),
                StringComparison.Ordinal) ||
            state.StagedFileName != $"pending-{state.OperationId}.db" ||
            !IsSha256(state.StagedSha256) ||
            !Enum.IsDefined(state.Phase))
        {
            throw new InvalidDataException("The restore journal is invalid.");
        }

        var hasSafetyName = state.SafetyBackupFileName is not null;
        var hasSafetyHash = state.SafetyBackupSha256 is not null;

        if (hasSafetyName != hasSafetyHash ||
            (state.Phase != PendingRestorePhase.Prepared && !hasSafetyName))
        {
            throw new InvalidDataException(
                "The restore journal has no valid safety backup.");
        }

        if (state.SafetyBackupFileName is { } name &&
            (name.Length > 160 ||
             !name.StartsWith(
                 "daycord-before-restore-", StringComparison.Ordinal) ||
             !name.EndsWith(".db", StringComparison.Ordinal) ||
             name != Path.GetFileName(name) ||
             name.Contains('\\') ||
             name.Contains('/') ||
             !IsSha256(state.SafetyBackupSha256)))
        {
            throw new InvalidDataException(
                "The restore journal contains an invalid safety-backup reference.");
        }
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);
}