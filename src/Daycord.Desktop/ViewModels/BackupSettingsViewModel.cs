using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Daycord.Core.Application.Contracts;
using Daycord.Desktop.Localization;
using Daycord.Infrastructure.Persistence;

namespace Daycord.Desktop.ViewModels;

public sealed class BackupSettingsViewModel : ObservableObject, IDisposable
{
    private readonly IUserBackupService _backups;
    private readonly AppDataPaths _paths;
    private readonly ILocalizationService _localization;
    private readonly ReportsViewModel _reports;

    private string? _restoreCandidate;
    private string? _statusKey;
    private string? _statusArgument;

    private bool _isBusy;
    private bool _hasRestoreConfirmation;
    private bool _isRestartRequired;
    private bool _disposed;

    public BackupSettingsViewModel(
        IUserBackupService backups,
        AppDataPaths paths,
        ILocalizationService localization,
        ReportsViewModel reports)
    {
        _backups = backups;
        _paths = paths;
        _localization = localization;
        _reports = reports;

        _isRestartRequired =
            File.Exists(paths.PendingRestoreManifestPath);

        if (_isRestartRequired)
            SetStatus("BackupRestorePrepared");

        _localization.PropertyChanged += OnLocalizationChanged;
    }

    private string L(string key) => _localization.GetString(key);

    // Localized labels

    public string SectionTitle => L("BackupSectionTitle");
    public string SectionDescription => L("BackupSectionDescription");

    public string CreateTitle => L("BackupCreateTitle");
    public string CreateDescription => L("BackupCreateDescription");
    public string CreateButtonLabel => L("BackupCreateButton");

    public string RestoreTitle => L("BackupRestoreTitle");
    public string RestoreDescription => L("BackupRestoreDescription");
    public string ChooseButtonLabel => L("BackupChooseFile");

    public string ConfirmationTitle => L("BackupConfirmationTitle");
    public string ConfirmationWarning => L("BackupConfirmationWarning");
    public string SafetyWarning => L("BackupSafetyWarning");

    public string CancelLabel => L("BackupCancel");
    public string ConfirmLabel => L("BackupConfirmRestore");

    public string LastOperationLabel => L("BackupLastOperation");
    public string WorkingLabel => L("BackupWorking");
    public string RestartMessage => L("BackupRestartRequired");

    public string SaveDialogTitle => L("BackupSaveDialogTitle");
    public string OpenDialogTitle => L("BackupOpenDialogTitle");

    // State

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                RefreshActions();
        }
    }

    public bool HasRestoreConfirmation
    {
        get => _hasRestoreConfirmation;
        private set
        {
            if (SetProperty(ref _hasRestoreConfirmation, value))
                RefreshActions();
        }
    }

    public bool IsRestartRequired
    {
        get => _isRestartRequired;
        private set
        {
            if (SetProperty(ref _isRestartRequired, value))
                RefreshActions();
        }
    }

    public bool CanStartOperation =>
        !_disposed &&
        !IsBusy &&
        !IsRestartRequired &&
        !HasRestoreConfirmation;

    public bool CanConfirmRestore =>
        !_disposed &&
        !IsBusy &&
        !IsRestartRequired &&
        HasRestoreConfirmation;

    public bool HasStatus => _statusKey is not null;

    public string StatusMessage
    {
        get
        {
            if (_statusKey is null)
                return string.Empty;

            var template = L(_statusKey);

            return _statusArgument is null
                ? template
                : string.Format(
                    CultureInfo.CurrentCulture,
                    template,
                    _statusArgument);
        }
    }

    private void RefreshActions()
    {
        OnPropertyChanged(nameof(CanStartOperation));
        OnPropertyChanged(nameof(CanConfirmRestore));
    }

    public bool TryBeginOperation()
    {
        if (!CanStartOperation)
            return false;

        IsBusy = true;
        return true;
    }

    public void EndOperation()
    {
        if (!_disposed)
            IsBusy = false;
    }

    // Temporary files

    public string AllocateTemporaryFile(string purpose)
    {
        if (_disposed || !IsBusy)
        {
            throw new InvalidOperationException(
                "No backup operation is active.");
        }

        if (purpose is not ("export" or "import"))
            throw new ArgumentOutOfRangeException(nameof(purpose));

        _paths.EnsureRestoreStagingDirectory();

        return Path.Combine(
            _paths.RestoreStagingDirectory,
            $"dc014-{purpose}-{Guid.NewGuid():N}.db");
    }

    public void DeleteTemporaryFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var normalized = Path.GetFullPath(path);

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var directory = Path.GetDirectoryName(normalized);
        var name = Path.GetFileName(normalized);

        if (!string.Equals(
                directory,
                Path.GetFullPath(_paths.RestoreStagingDirectory),
                comparison) ||
            !(name.StartsWith("dc014-export-", StringComparison.Ordinal) ||
              name.StartsWith("dc014-import-", StringComparison.Ordinal)) ||
            !name.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            if (File.Exists(normalized))
                File.Delete(normalized);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // Destination safety

    /// <summary>
    /// Validates a local destination and resolves directory symlinks.
    /// All application-managed files are protected, not only daycord.db.
    /// Returns the canonical destination for the atomic file operation.
    /// </summary>
    public string EnsureSafeSaveDestination(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new IOException(
                "A local backup destination is required.");
        }

        var normalized = Path.GetFullPath(path);

        if (!string.Equals(
                Path.GetExtension(normalized),
                ".db",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException(
                "The destination must have a .db extension.");
        }

        // An existing symlink must not redirect the backup writer.
        if (File.Exists(normalized) &&
            new FileInfo(normalized).LinkTarget is not null)
        {
            throw new IOException(
                "A symbolic link cannot be used as a backup destination.");
        }

        var directory = Path.GetDirectoryName(normalized)
            ?? throw new IOException("Invalid destination directory.");

        var canonicalDirectory = ResolveDirectoryLinks(directory);

        var canonicalDestination = Path.Combine(
            canonicalDirectory,
            Path.GetFileName(normalized));

        var canonicalDataDirectory =
            ResolveDirectoryLinks(_paths.DataDirectory);

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var protectedPrefix = Path.TrimEndingDirectorySeparator(
                                  canonicalDataDirectory) +
                              Path.DirectorySeparatorChar;

        if (string.Equals(
                canonicalDestination,
                _paths.DataDirectory,
                comparison) ||
            canonicalDestination.StartsWith(
                protectedPrefix,
                comparison))
        {
            throw new IOException(
                "Daycord's internal data directory is protected.");
        }

        return canonicalDestination;
    }

    private static string ResolveDirectoryLinks(string directory)
    {
        var fullPath = Path.GetFullPath(directory);
        var root = Path.GetPathRoot(fullPath)
            ?? throw new IOException("Invalid directory root.");

        var relative = Path.GetRelativePath(root, fullPath);

        var current = root;

        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar,
                      Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
                continue;

            current = Path.Combine(current, segment);

            var info = new DirectoryInfo(current);

            if (!info.Exists)
            {
                throw new DirectoryNotFoundException(
                    "The backup destination directory is unavailable.");
            }

            var resolved = info.ResolveLinkTarget(
                returnFinalTarget: true);

            if (resolved is not null)
                current = Path.GetFullPath(resolved.FullName);
        }

        return Path.GetFullPath(current);
    }

    // Backup creation

    public Task CreateTemporaryBackupAsync(string path)
    {
        if (!IsBusy)
        {
            throw new InvalidOperationException(
                "No backup operation is active.");
        }

        return _backups.CreateBackupAsync(path);
    }

    public void ReportBackupCreated(string fileName)
    {
        SetStatus("BackupCreated", fileName);
    }

    // Restore validation and confirmation

    public async Task<bool> ValidateRestoreCandidateAsync(
        string localPath)
    {
        if (!IsBusy || HasRestoreConfirmation)
            return false;

        var result = await _backups.ValidateBackupAsync(localPath);

        if (!result.IsValid)
        {
            var messageKey = result.Status switch
            {
                BackupValidationStatus.Corrupted =>
                    "BackupCorrupted",

                BackupValidationStatus.IncompatibleSchema =>
                    "BackupIncompatible",

                _ => "BackupInvalid"
            };

            SetStatus(messageKey);
            return false;
        }

        _restoreCandidate = localPath;
        HasRestoreConfirmation = true;

        return true;
    }

    public void CancelRestore()
    {
        if (IsBusy || !HasRestoreConfirmation)
            return;

        ReleaseCandidate();
        HasRestoreConfirmation = false;
    }

    public async Task ConfirmRestoreAsync()
    {
        if (!CanConfirmRestore || _restoreCandidate is null)
            return;

        if (_reports.IsExporting ||
            _reports.IsPresetBusy ||
            _reports.HasUnsavedChanges)
        {
            SetStatus("BackupReportsBusy");
            return;
        }

        IsBusy = true;

        try
        {
            await _backups.PrepareRestoreAsync(_restoreCandidate);

            ReleaseCandidate();
            HasRestoreConfirmation = false;
            IsRestartRequired = true;

            SetStatus("BackupRestorePrepared");
        }
        catch (Exception error)
        {
            ReportFailure(error, "BackupRestoreFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ReleaseCandidate()
    {
        var candidate = _restoreCandidate;
        _restoreCandidate = null;

        DeleteTemporaryFile(candidate);
    }

    // User-facing errors

    public void ReportFailure(Exception error, string fallbackKey)
    {
        var key = error switch
        {
            UnauthorizedAccessException =>
                "BackupPermissionDenied",

            FileNotFoundException or DirectoryNotFoundException =>
                "BackupFileUnavailable",

            InvalidDataException =>
                "BackupInvalid",

            IOException =>
                "BackupStorageError",

            _ => fallbackKey
        };

        SetStatus(key);
    }

    private void SetStatus(string key, string? argument = null)
    {
        _statusKey = key;
        _statusArgument = argument;

        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(HasStatus));
    }

    private void OnLocalizationChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (_disposed ||
            (!string.IsNullOrEmpty(e.PropertyName) &&
             e.PropertyName !=
                 nameof(ILocalizationService.CurrentLanguage)))
        {
            return;
        }

        OnPropertyChanged(nameof(SectionTitle));
        OnPropertyChanged(nameof(SectionDescription));

        OnPropertyChanged(nameof(CreateTitle));
        OnPropertyChanged(nameof(CreateDescription));
        OnPropertyChanged(nameof(CreateButtonLabel));

        OnPropertyChanged(nameof(RestoreTitle));
        OnPropertyChanged(nameof(RestoreDescription));
        OnPropertyChanged(nameof(ChooseButtonLabel));

        OnPropertyChanged(nameof(ConfirmationTitle));
        OnPropertyChanged(nameof(ConfirmationWarning));
        OnPropertyChanged(nameof(SafetyWarning));

        OnPropertyChanged(nameof(CancelLabel));
        OnPropertyChanged(nameof(ConfirmLabel));

        OnPropertyChanged(nameof(LastOperationLabel));
        OnPropertyChanged(nameof(WorkingLabel));
        OnPropertyChanged(nameof(RestartMessage));

        OnPropertyChanged(nameof(SaveDialogTitle));
        OnPropertyChanged(nameof(OpenDialogTitle));
        OnPropertyChanged(nameof(StatusMessage));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _localization.PropertyChanged -= OnLocalizationChanged;
        ReleaseCandidate();

        _disposed = true;
    }
}