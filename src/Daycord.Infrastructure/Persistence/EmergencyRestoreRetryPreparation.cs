using System.Security.Cryptography;
using System.Text.Json;

namespace Daycord.Infrastructure.Persistence;

/// <summary>
/// Safely rearms an emergency restore that failed before raw
/// preservation and before installation began.
///
/// The general restore state machine remains monotonic.
/// This class performs one narrowly controlled recovery transition
/// after preserving and verifying the original failure journal.
/// </summary>
public sealed class EmergencyRestoreRetryPreparation
{
    private readonly AppDataPaths _paths;
    private readonly PendingRestoreCoordinator _coordinator;
    private readonly SqliteSnapshotValidator _validator = new();

    public EmergencyRestoreRetryPreparation(
        AppDataPaths paths,
        PendingRestoreCoordinator coordinator)
    {
        _paths = paths ??
                 throw new ArgumentNullException(nameof(paths));

        _coordinator = coordinator ??
                       throw new ArgumentNullException(nameof(coordinator));
    }

    public Task<bool> TryRearmAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => TryRearm(cancellationToken),
            cancellationToken);
    }

    private bool TryRearm(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The coordinator owns the application-wide recovery lock.
        _coordinator.AcquireApplicationLock();

        var journalPath = _paths.PendingRestoreManifestPath;

        if (!File.Exists(journalPath))
            return false;

        RequireOrdinaryFile(journalPath);
        RequireOrdinaryDirectory(
            _paths.RestoreStagingDirectory);

        var state = PendingRestoreStateStore.Read(journalPath);

        if (state.Mode != PendingRestoreMode.Emergency ||
            state.Phase !=
                PendingRestorePhase.EmergencyNeedsAttention)
        {
            return false;
        }

        // Any reference to a safety snapshot or raw archive
        // makes the previous installation state ambiguous.
        // Such operations must never be automatically rewound.
        if (state.RawArchiveDirectoryName is not null ||
            state.RawArchiveManifestSha256 is not null ||
            state.SafetyBackupFileName is not null ||
            state.SafetyBackupSha256 is not null)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(_paths.DatabasePath))
            return false;

        RequireOrdinaryFile(_paths.DatabasePath);

        // Never replace an already healthy active database.
        if (_validator.Validate(_paths.DatabasePath).IsValid)
            return false;

        var stagedPath = Path.Combine(
            _paths.RestoreStagingDirectory,
            state.StagedFileName);

        if (!File.Exists(stagedPath))
        {
            throw new InvalidDataException(
                "The staged emergency backup is missing.");
        }

        RequireOrdinaryFile(stagedPath);

        SqliteSnapshotIO.RequireNoSidecars(stagedPath);

        if (!_validator.Validate(stagedPath).IsValid)
        {
            throw new InvalidDataException(
                "The staged emergency backup is invalid.");
        }

        var actualHash =
            PendingRestoreStateStore.GetSha256(stagedPath);

        if (!string.Equals(
                actualHash,
                state.StagedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The staged emergency backup hash does not match.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Record the exact original bytes before any state change.
        var originalJournal = File.ReadAllBytes(journalPath);

        var archivedPath = PreserveFailedJournal(
            journalPath,
            state.OperationId,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        // Use a dedicated atomic transition. The ordinary
        // PendingRestoreStateStore.Update intentionally forbids
        // EmergencyNeedsAttention -> Prepared.
        RearmJournalAtomically(
            journalPath,
            archivedPath,
            state,
            originalJournal,
            cancellationToken);

        return true;
    }

    private string PreserveFailedJournal(
        string sourcePath,
        string operationId,
        CancellationToken cancellationToken)
    {
        var archivePath = Path.Combine(
            _paths.RestoreStagingDirectory,
            $"retry-origin-{operationId}.json");

        if (Directory.Exists(archivePath))
        {
            throw new IOException(
                "The saved recovery journal path is a directory.");
        }

        if (File.Exists(archivePath))
        {
            RequireOrdinaryFile(archivePath);

            var saved =
                PendingRestoreStateStore.Read(archivePath);

            if (saved.OperationId != operationId ||
                saved.Mode != PendingRestoreMode.Emergency ||
                saved.Phase !=
                    PendingRestorePhase.EmergencyNeedsAttention)
            {
                throw new InvalidDataException(
                    "The saved recovery journal is inconsistent.");
            }

            return archivePath;
        }

        var temporaryPath = Path.Combine(
            _paths.RestoreStagingDirectory,
            $".retry-origin-{Guid.NewGuid():N}.tmp");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            SqliteSnapshotIO.CopyDurably(
                sourcePath,
                temporaryPath);

            if (!SameHash(sourcePath, temporaryPath))
            {
                throw new IOException(
                    "The recovery journal copy was not verified.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            File.Move(
                temporaryPath,
                archivePath,
                overwrite: false);

            return archivePath;
        }
        finally
        {
            TryDeleteTemporary(temporaryPath);
        }
    }

    /// <summary>
    /// Performs the only permitted backward recovery transition:
    ///
    /// EmergencyNeedsAttention -> Prepared
    ///
    /// All pre-installation checks must have succeeded before
    /// this method is called. The previous journal is retained
    /// byte-for-byte in a separate file.
    /// </summary>
    private static void RearmJournalAtomically(
        string journalPath,
        string archivedPath,
        PendingRestoreState expectedState,
        byte[] expectedOriginalBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (expectedState.Mode != PendingRestoreMode.Emergency ||
            expectedState.Phase !=
                PendingRestorePhase.EmergencyNeedsAttention ||
            expectedState.RawArchiveDirectoryName is not null ||
            expectedState.RawArchiveManifestSha256 is not null ||
            expectedState.SafetyBackupFileName is not null ||
            expectedState.SafetyBackupSha256 is not null)
        {
            throw new InvalidOperationException(
                "The emergency restore is not eligible for rearming.");
        }

        RequireOrdinaryFile(journalPath);
        RequireOrdinaryFile(archivedPath);

        // The archived journal must exactly match what we read
        // before starting the retry operation.
        var archivedBytes = File.ReadAllBytes(archivedPath);

        if (!archivedBytes.AsSpan().SequenceEqual(
                expectedOriginalBytes))
        {
            throw new InvalidDataException(
                "The preserved recovery journal differs from the original.");
        }

        var currentState =
            PendingRestoreStateStore.Read(journalPath);

        if (currentState != expectedState ||
            !File.ReadAllBytes(journalPath)
                .AsSpan()
                .SequenceEqual(expectedOriginalBytes))
        {
            throw new InvalidDataException(
                "The recovery journal changed during retry preparation.");
        }

        var preparedState = expectedState with
        {
            Phase = PendingRestorePhase.Prepared
        };

        var directory = Path.GetDirectoryName(journalPath)
            ?? throw new InvalidOperationException(
                "The recovery journal directory is unavailable.");

        var temporaryPath = Path.Combine(
            directory,
            $".emergency-rearm-{Guid.NewGuid():N}.tmp");

        try
        {
            var contents =
                JsonSerializer.SerializeToUtf8Bytes(preparedState);

            // Write the new journal completely before publication.
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

            cancellationToken.ThrowIfCancellationRequested();

            // Check again immediately before the atomic replacement.
            // The application lock prevents another Daycord instance
            // from participating in this recovery operation.
            if (!File.ReadAllBytes(journalPath)
                    .AsSpan()
                    .SequenceEqual(expectedOriginalBytes))
            {
                throw new InvalidDataException(
                    "The recovery journal changed before publication.");
            }

            File.Move(
                temporaryPath,
                journalPath,
                overwrite: true);

            // The existing state-store validator must accept the
            // newly prepared journal.
            var result = PendingRestoreStateStore.Read(journalPath);

            if (result != preparedState)
            {
                throw new InvalidDataException(
                    "The rearmed recovery journal is inconsistent.");
            }
        }
        finally
        {
            TryDeleteTemporary(temporaryPath);
        }
    }

    private static bool SameHash(
        string first,
        string second)
    {
        static byte[] Compute(string path)
        {
            using var stream = File.OpenRead(path);
            return SHA256.HashData(stream);
        }

        return Compute(first)
            .AsSpan()
            .SequenceEqual(Compute(second));
    }

    private static void RequireOrdinaryFile(string path)
    {
        var attributes = File.GetAttributes(path);

        if ((attributes & FileAttributes.ReparsePoint) != 0 ||
            (attributes & FileAttributes.Directory) != 0)
        {
            throw new IOException(
                "A recovery file is redirected or invalid.");
        }
    }

    private static void RequireOrdinaryDirectory(string path)
    {
        if (!Directory.Exists(path) ||
            (File.GetAttributes(path) &
             FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                "The recovery staging directory is invalid.");
        }
    }

    private static void TryDeleteTemporary(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}