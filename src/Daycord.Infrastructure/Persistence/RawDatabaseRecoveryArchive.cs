using System.Security.Cryptography;
using System.Text.Json;

namespace Daycord.Infrastructure.Persistence;

/// <summary>
/// Preserves the source SQLite files verbatim before a pending restore touches
/// them. This is forensic preservation, NOT a validated SQLite backup.
/// </summary>
internal sealed class RawDatabaseRecoveryArchive
{
    private static readonly string[] Suffixes = ["", "-wal", "-shm", "-journal"];
    private readonly AppDataPaths _paths;

    public RawDatabaseRecoveryArchive(AppDataPaths paths)
    {
        _paths = paths;
    }

    /// <summary>
    /// Returns the existing verified archive for an operation or captures it
    /// once. A crash between directory publication and journal update can be
    /// resumed without creating a second archive.
    /// </summary>
    public RawRecoveryArchiveReference EnsureCaptured(
        string operationId,
        CancellationToken cancellationToken)
    {
        RequireOperationId(operationId);
        cancellationToken.ThrowIfCancellationRequested();

        var root = _paths.RawRecoveryBackupDirectory;
        if (Directory.Exists(root))
        {
            var incomplete = Directory.EnumerateDirectories(
                root, $".raw-before-restore-{operationId}-*.incomplete")
                .ToArray();
            if (incomplete.Length != 0)
            {
                throw new IOException(
                    "An unfinished raw recovery archive exists; restore is blocked.");
            }

            var published = Directory.EnumerateDirectories(
                    root, $"raw-before-restore-{operationId}-*")
                .ToArray();

            if (published.Length > 1)
            {
                throw new IOException(
                    "Multiple raw recovery archives exist for this operation.");
            }

            if (published.Length == 1)
                return ValidatePublished(published[0], operationId);
        }

        var path = Capture(operationId, cancellationToken);
        return ValidatePublished(path, operationId);
    }

    /// <summary>
    /// Verifies the journal reference and every preserved file. Raw archives
    /// are forensic artifacts; this is NOT an SQLite integrity validation.
    /// </summary>
    public void ValidateReference(
        string operationId,
        string directoryName,
        string manifestSha256)
    {
        RequireOperationId(operationId);
        var directory = Path.Combine(
            _paths.RawRecoveryBackupDirectory, directoryName);
        var current = ValidatePublished(directory, operationId);

        if (!string.Equals(current.DirectoryName, directoryName,
                StringComparison.Ordinal) ||
            !string.Equals(current.ManifestSha256, manifestSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The raw recovery archive no longer matches its journal.");
        }
    }

    private RawRecoveryArchiveReference ValidatePublished(
        string directory,
        string operationId)
    {
        var directoryName = Path.GetFileName(
            Path.TrimEndingDirectorySeparator(directory));
        var prefix = $"raw-before-restore-{operationId}-";

        if (!directoryName.StartsWith(prefix, StringComparison.Ordinal) ||
            !IsGuidN(directoryName[prefix.Length..]) ||
            !Directory.Exists(directory) ||
            (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Invalid raw recovery directory.");
        }

        var manifestPath = Path.Combine(directory, "archive.json");
        if (!File.Exists(manifestPath) ||
            (File.GetAttributes(manifestPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Raw recovery manifest is missing or redirected.");
        }

        var manifest = JsonSerializer.Deserialize<ArchiveManifest>(
            File.ReadAllBytes(manifestPath))
            ?? throw new InvalidDataException("Raw recovery manifest is empty.");
        var profile = Path.GetFileName(
            Path.TrimEndingDirectorySeparator(_paths.DataDirectory));

        if (manifest.FormatVersion != 1 ||
            manifest.Purpose != "raw-before-restore" ||
            manifest.OperationId != operationId ||
            manifest.Profile != profile ||
            manifest.Files is not { Count: >= 1 and <= 4 })
        {
            throw new InvalidDataException(
                "Raw recovery manifest does not match this operation/profile.");
        }

        var allowedNames = Suffixes.Select(x => "daycord.db" + x)
            .ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in manifest.Files)
        {
            if (file is null ||
                !allowedNames.Contains(file.FileName) ||
                !seen.Add(file.FileName) ||
                file.Length < 0 ||
                file.Sha256 is not { Length: 64 } ||
                !file.Sha256.All(Uri.IsHexDigit))
            {
                throw new InvalidDataException("Raw recovery file metadata is invalid.");
            }

            var path = Path.Combine(directory, file.FileName);
            if (!File.Exists(path) ||
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
                new FileInfo(path).Length != file.Length ||
                !string.Equals(PendingRestoreStateStore.GetSha256(path),
                    file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Raw recovery file verification failed.");
            }
        }

        if (!seen.Contains("daycord.db") ||
            Directory.EnumerateFileSystemEntries(directory).Any(path =>
                !seen.Contains(Path.GetFileName(path)) &&
                Path.GetFileName(path) != "archive.json"))
        {
            throw new InvalidDataException("Raw recovery archive has unexpected contents.");
        }

        return new RawRecoveryArchiveReference(
            directoryName,
            PendingRestoreStateStore.GetSha256(manifestPath));
    }

    private static void RequireOperationId(string operationId)
    {
        if (!IsGuidN(operationId))
            throw new ArgumentException("Invalid restore operation ID.", nameof(operationId));
    }

    private static bool IsGuidN(string? value) =>
        value is { Length: 32 } &&
        Guid.TryParseExact(value, "N", out var guid) &&
        string.Equals(value, guid.ToString("N"), StringComparison.Ordinal);

    public string Capture(string operationId, CancellationToken cancellationToken)
    {
        RequireOperationId(operationId);

        var sourceFiles = Suffixes
            .Select(suffix => (Name: "daycord.db" + suffix,
                               Path: _paths.DatabasePath + suffix))
            .Where(file => File.Exists(file.Path))
            .ToArray();

        if (sourceFiles.All(file => file.Name != "daycord.db"))
        {
            throw new FileNotFoundException(
                "The original database file is missing; restore is blocked.",
                _paths.DatabasePath);
        }

        // The source database must not be held open by Daycord at this point.
        // Open all existing components exclusively before reading any of them.
        // On Unix, external SQLite writers may not respect .NET sharing rules;
        // therefore this raw archive is never presented as a valid snapshot.
        var handles = new List<(string Name, FileStream Stream)>();
        try
        {
            foreach (var source in sourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if ((File.GetAttributes(source.Path) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException(
                        "A database component is a symbolic link or reparse point.");
                }

                handles.Add((source.Name, new FileStream(
                    source.Path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.None)));
            }

            // If sidecar membership changes while handles are opened, the
            // captured set cannot be trusted even as a fixed file set.
            foreach (var suffix in Suffixes)
            {
                var name = "daycord.db" + suffix;
                var currentlyExists = File.Exists(_paths.DatabasePath + suffix);
                if (currentlyExists != handles.Any(handle => handle.Name == name))
                {
                    throw new IOException(
                        "SQLite source file membership changed during preservation.");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            _paths.EnsureRawRecoveryBackupDirectory();

            var identifier = Guid.NewGuid().ToString("N");
            var prefix = $"raw-before-restore-{operationId}-{identifier}";
            var incomplete = Path.Combine(
                _paths.RawRecoveryBackupDirectory, "." + prefix + ".incomplete");
            var published = Path.Combine(_paths.RawRecoveryBackupDirectory, prefix);

            // A failed capture intentionally keeps its .incomplete directory
            // for investigation; it is never mistaken for a completed archive.
            Directory.CreateDirectory(incomplete);

            var entries = new List<ArchivedFile>();
            foreach (var (name, input) in handles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var target = Path.Combine(incomplete, name);
                using (var output = new FileStream(
                           target,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           bufferSize: 81920,
                           FileOptions.WriteThrough))
                {
                    input.CopyTo(output);
                    output.Flush(flushToDisk: true);
                }

                var length = new FileInfo(target).Length;
                var hash = PendingRestoreStateStore.GetSha256(target);

                // The destination must match a fresh read of the held source
                // handle. In case an external writer changed its contents,
                // abort before modifying the active database.
                input.Position = 0;
                var sourceHash = Convert.ToHexString(SHA256.HashData(input));
                if (input.Length != length ||
                    !string.Equals(sourceHash, hash, StringComparison.Ordinal))
                {
                    throw new IOException(
                        "A database component changed during preservation.");
                }

                entries.Add(new ArchivedFile(name, length, hash));
            }

            // Re-check exact byte counts and hashes of the durable copies.
            foreach (var entry in entries)
            {
                var file = Path.Combine(incomplete, entry.FileName);
                if (new FileInfo(file).Length != entry.Length ||
                    !string.Equals(
                        PendingRestoreStateStore.GetSha256(file),
                        entry.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("The raw recovery archive failed verification.");
                }
            }

            var manifest = new ArchiveManifest(
                FormatVersion: 1,
                Purpose: "raw-before-restore",
                OperationId: operationId,
                Profile: Path.GetFileName(Path.TrimEndingDirectorySeparator(_paths.DataDirectory)),
                CreatedAtUtc: DateTimeOffset.UtcNow,
                Files: entries);

            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
            using (var output = new FileStream(
                       Path.Combine(incomplete, "archive.json"),
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                output.Write(manifestBytes);
                output.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Publish only a fully copied and checked directory. This is a
            // same-directory rename, not an atomic durability guarantee for
            // every file system / unexpected power-loss combination.
            Directory.Move(incomplete, published);
            return published;
        }
        finally
        {
            foreach (var (_, stream) in handles)
                stream.Dispose();
        }
    }

    private sealed record ArchivedFile(string FileName, long Length, string Sha256);

    private sealed record ArchiveManifest(
        int FormatVersion,
        string Purpose,
        string OperationId,
        string Profile,
        DateTimeOffset CreatedAtUtc,
        List<ArchivedFile> Files);
}

/// <summary>Reference to an immutable, checked raw-preservation directory.</summary>
internal sealed record RawRecoveryArchiveReference(
    string DirectoryName,
    string ManifestSha256);