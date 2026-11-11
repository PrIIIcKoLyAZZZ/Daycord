using System.Security.Cryptography;
using System.Text.Json;

namespace Daycord.Infrastructure.Persistence;

// Serialized as integers. Values 0–5 were already introduced and must not move.
internal enum PendingRestorePhase
{
    Prepared = 0,
    SafetyCreated = 1,
    Installing = 2,
    Installed = 3,
    RollingBack = 4,
    RawCaptured = 5,
    EmergencyInstalling = 6,
    EmergencyInstalled = 7,
    EmergencyNeedsAttention = 8
}

// Older journals omit this property; the default preserves normal Restore.
internal enum PendingRestoreMode
{
    Normal = 0,
    Emergency = 1
}

internal sealed record PendingRestoreState(
    string OperationId,
    string StagedFileName,
    string StagedSha256,
    PendingRestorePhase Phase,
    string? SafetyBackupFileName = null,
    string? SafetyBackupSha256 = null,
    string? RawArchiveDirectoryName = null,
    string? RawArchiveManifestSha256 = null,
    PendingRestoreMode Mode = PendingRestoreMode.Normal);

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

        if (overwrite)
        {
            var previous = Read(manifestPath);
            ValidateTransition(previous, state);
        }
        else if (File.Exists(manifestPath))
        {
            throw new IOException("A restore journal already exists.");
        }

        var directory = Path.GetDirectoryName(manifestPath)
            ?? throw new ArgumentException(
                "Invalid restore journal path.", nameof(manifestPath));
        Directory.CreateDirectory(directory);

        var temporary = Path.Combine(
            directory, $".pending-restore-{Guid.NewGuid():N}.tmp");
        try
        {
            var contents = JsonSerializer.SerializeToUtf8Bytes(state);
            using (var stream = new FileStream(
                       temporary, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(contents);
                stream.Flush(flushToDisk: true);
            }

            // Same-directory rename. Power-loss durability still depends on FS.
            File.Move(temporary, manifestPath, overwrite);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void Validate(PendingRestoreState state)
    {
        if (!IsGuidN(state.OperationId) ||
            state.StagedFileName != $"pending-{state.OperationId}.db" ||
            !IsSha256(state.StagedSha256) ||
            !Enum.IsDefined(state.Phase) ||
            !Enum.IsDefined(state.Mode))
        {
            throw new InvalidDataException("The restore journal is invalid.");
        }

        var hasSafety = state.SafetyBackupFileName is not null;
        var hasRaw = state.RawArchiveDirectoryName is not null;
        if (hasSafety != (state.SafetyBackupSha256 is not null) ||
            hasRaw != (state.RawArchiveManifestSha256 is not null))
        {
            throw new InvalidDataException("Incomplete recovery-artifact reference.");
        }

        if (hasSafety)
        {
            var name = state.SafetyBackupFileName!;
            if (name.Length > 160 ||
                !name.StartsWith("daycord-before-restore-", StringComparison.Ordinal) ||
                !name.EndsWith(".db", StringComparison.Ordinal) ||
                name != Path.GetFileName(name) ||
                name.Contains('/') || name.Contains('\\') ||
                !IsSha256(state.SafetyBackupSha256))
            {
                throw new InvalidDataException("Invalid safety-backup reference.");
            }
        }

        if (hasRaw)
        {
            var prefix = $"raw-before-restore-{state.OperationId}-";
            var name = state.RawArchiveDirectoryName!;
            if (!name.StartsWith(prefix, StringComparison.Ordinal) ||
                !IsGuidN(name[prefix.Length..]) ||
                name != Path.GetFileName(name) ||
                name.Contains('/') || name.Contains('\\') ||
                !IsSha256(state.RawArchiveManifestSha256))
            {
                throw new InvalidDataException("Invalid raw-archive reference.");
            }
        }

        if (state.Mode == PendingRestoreMode.Normal)
        {
            var valid = state.Phase switch
            {
                PendingRestorePhase.Prepared => !hasSafety && !hasRaw,
                PendingRestorePhase.RawCaptured => !hasSafety && hasRaw,
                PendingRestorePhase.SafetyCreated or
                    PendingRestorePhase.Installing or
                    PendingRestorePhase.Installed or
                    PendingRestorePhase.RollingBack => hasSafety,
                _ => false
            };

            if (!valid)
                throw new InvalidDataException("Invalid normal-restore journal state.");
        }
        else
        {
            // Emergency Restore never pretends the damaged original is a
            // validated safety snapshot. It requires raw preservation before
            // any installation. NeedsAttention can arise before capture.
            var valid = !hasSafety && (state.Phase switch
            {
                PendingRestorePhase.Prepared => !hasRaw,
                PendingRestorePhase.RawCaptured or
                    PendingRestorePhase.EmergencyInstalling or
                    PendingRestorePhase.EmergencyInstalled => hasRaw,
                PendingRestorePhase.EmergencyNeedsAttention => true,
                _ => false
            });

            if (!valid)
                throw new InvalidDataException("Invalid emergency-restore journal state.");
        }
    }

    private static void ValidateTransition(
        PendingRestoreState previous, PendingRestoreState next)
    {
        if (previous.OperationId != next.OperationId ||
            previous.StagedFileName != next.StagedFileName ||
            previous.Mode != next.Mode ||
            !string.Equals(previous.StagedSha256, next.StagedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Restore journal identity changed.");
        }

        if ((previous.SafetyBackupFileName is not null &&
             (previous.SafetyBackupFileName != next.SafetyBackupFileName ||
              !string.Equals(previous.SafetyBackupSha256,
                  next.SafetyBackupSha256, StringComparison.OrdinalIgnoreCase))) ||
            (previous.RawArchiveDirectoryName is not null &&
             (previous.RawArchiveDirectoryName != next.RawArchiveDirectoryName ||
              !string.Equals(previous.RawArchiveManifestSha256,
                  next.RawArchiveManifestSha256, StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidDataException(
                "A published recovery-artifact reference cannot be changed.");
        }

        var allowed = previous.Mode switch
        {
            PendingRestoreMode.Normal => (previous.Phase, next.Phase) switch
            {
                (PendingRestorePhase.Prepared, PendingRestorePhase.RawCaptured) => true,
                (PendingRestorePhase.Prepared, PendingRestorePhase.SafetyCreated) => true,
                (PendingRestorePhase.RawCaptured, PendingRestorePhase.SafetyCreated) => true,
                (PendingRestorePhase.SafetyCreated, PendingRestorePhase.Installing) => true,
                (PendingRestorePhase.Installing, PendingRestorePhase.Installed) => true,
                (PendingRestorePhase.Installing, PendingRestorePhase.RollingBack) => true,
                (PendingRestorePhase.Installed, PendingRestorePhase.RollingBack) => true,
                (PendingRestorePhase.RollingBack, PendingRestorePhase.RollingBack) => true,
                _ => false
            },
            PendingRestoreMode.Emergency => (previous.Phase, next.Phase) switch
            {
                (PendingRestorePhase.Prepared, PendingRestorePhase.RawCaptured) => true,
                (PendingRestorePhase.Prepared, PendingRestorePhase.EmergencyNeedsAttention) => true,
                (PendingRestorePhase.RawCaptured, PendingRestorePhase.EmergencyInstalling) => true,
                (PendingRestorePhase.RawCaptured, PendingRestorePhase.EmergencyNeedsAttention) => true,
                (PendingRestorePhase.EmergencyInstalling, PendingRestorePhase.EmergencyInstalled) => true,
                (PendingRestorePhase.EmergencyInstalling, PendingRestorePhase.EmergencyNeedsAttention) => true,
                (PendingRestorePhase.EmergencyInstalled, PendingRestorePhase.EmergencyNeedsAttention) => true,
                _ => false
            },
            _ => false
        };

        if (!allowed)
            throw new InvalidDataException("Unsafe restore-journal phase transition.");
    }

    private static bool IsGuidN(string? value) =>
        value is { Length: 32 } &&
        Guid.TryParseExact(value, "N", out var guid) &&
        string.Equals(value, guid.ToString("N"), StringComparison.Ordinal);

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);
}