namespace Daycord.Infrastructure.Persistence;

/// <summary>
/// Tracks changes after successfully committed application writes.
/// No SQLite schema changes or writes are made by this class.
/// </summary>
public sealed class DatabaseChangeTracker
{
    private long _committedRevision;
    private long _backedUpRevision;

    public bool HasUnbackedChanges =>
        Interlocked.Read(ref _committedRevision) >
        Interlocked.Read(ref _backedUpRevision);

    /// <summary>
    /// Call only after a successful statement or transaction commit.
    /// </summary>
    public void MarkCommittedChange() =>
        Interlocked.Increment(ref _committedRevision);

    /// <summary>
    /// Capture before starting an SQLite online backup.
    /// </summary>
    public long CaptureRevision() =>
        Interlocked.Read(ref _committedRevision);

    /// <summary>
    /// A backup may cover this revision. Later commits remain dirty even if
    /// they occurred while SQLite was creating the snapshot.
    /// </summary>
    public void MarkBackedUp(long revision)
    {
        while (true)
        {
            var previous = Interlocked.Read(ref _backedUpRevision);

            if (previous >= revision)
                return;

            if (Interlocked.CompareExchange(
                    ref _backedUpRevision,
                    revision,
                    previous) == previous)
            {
                return;
            }
        }
    }
}