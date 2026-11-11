using System.ComponentModel;
using System.Diagnostics;
using Daycord.Desktop.Navigation;
using Daycord.Infrastructure.Persistence;

namespace Daycord.Desktop.Bootstrap;

/// <summary>
/// Triggers automatic backups after section changes and periodically
/// during long sessions. Never executes SQLite work in navigation handlers.
/// </summary>
internal sealed class AutomaticBackupScheduler : IDisposable
{
    private static readonly TimeSpan PollInterval =
        TimeSpan.FromMinutes(1);

    private readonly INavigationService _navigation;
    private readonly AutomaticBackupService _backups;
    private readonly DatabaseChangeTracker _changes;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    private Task? _periodicTask;
    private bool _started;
    private int _stopping;

    public AutomaticBackupScheduler(
        INavigationService navigation,
        AutomaticBackupService backups,
        DatabaseChangeTracker changes)
    {
        _navigation = navigation;
        _backups = backups;
        _changes = changes;
    }

    public void Start()
    {
        if (_started)
            throw new InvalidOperationException(
                "Automatic backup scheduler is already running.");

        _started = true;

        _navigation.PropertyChanged += OnNavigationChanged;
        _periodicTask = RunPeriodicAsync(_lifetime.Token);
    }

    private void OnNavigationChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (Volatile.Read(ref _stopping) != 0 ||
            e.PropertyName != nameof(INavigationService.CurrentPage) ||
            !_changes.HasUnbackedChanges)
        {
            return;
        }

        // The event handler does not wait for SQLite.
        // Backup failures are caught inside TryBackgroundBackupAsync.
        _ = TryBackgroundBackupAsync(_lifetime.Token);
    }

    private async Task RunPeriodicAsync(
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(
                       cancellationToken))
            {
                if (_changes.HasUnbackedChanges)
                {
                    await TryBackgroundBackupAsync(
                        cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
    }

    private async Task TryBackgroundBackupAsync(
        CancellationToken cancellationToken)
    {
        var entered = false;

        try
        {
            if (Volatile.Read(ref _stopping) != 0 ||
                !_changes.HasUnbackedChanges)
            {
                return;
            }

            // Skip overlapping triggers. The timer will retry if
            // changes still need a backup.
            entered = await _operationGate.WaitAsync(
                0, cancellationToken);

            if (!entered)
                return;

            await _backups.BackupIfChangedAsync(
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // A closing application cancels background attempts.
        }
        catch (Exception error)
        {
            // A failed backup must not interrupt section navigation.
            Trace.TraceWarning(
                "Daycord background backup failed: {0}",
                error.GetType().Name);
        }
        finally
        {
            if (entered)
                _operationGate.Release();
        }
    }

    /// <summary>
    /// Stops new work, waits for an active attempt, then creates a final
    /// verified snapshot if committed changes remain. Called before the
    /// application disposes its database services.
    /// </summary>
    public async Task StopAndFlushAsync()
    {
        if (!_started ||
            Interlocked.Exchange(ref _stopping, 1) != 0)
        {
            return;
        }

        _navigation.PropertyChanged -= OnNavigationChanged;
        _lifetime.Cancel();

        if (_periodicTask is not null)
        {
            await _periodicTask;
        }

        await _operationGate.WaitAsync();

        try
        {
            if (_changes.HasUnbackedChanges)
            {
                // Shutdown bypasses the two-minute cooldown.
                await _backups.BackupIfChangedAsync(
                    bypassCooldown: true);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public void Dispose()
    {
        _navigation.PropertyChanged -= OnNavigationChanged;

        if (Interlocked.Exchange(ref _stopping, 1) == 0)
        {
            _lifetime.Cancel();
        }

        // Do not dispose the gate here: asynchronous operations may
        // still be unwinding after an abnormal process shutdown.
        _lifetime.Dispose();
    }
}