using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform;
using Daycord.Desktop.ViewModels;

namespace Daycord.Desktop.Views;

public partial class MainWindow : Window
{
    private ReportsViewModel? _reportsViewModel;
    private Func<BackupSettingsViewModel>? _createBackupSettingsViewModel;
    private BackupSettingsViewModel? _backupSettingsViewModel;

    private bool _isSavingReportsBeforeClose;
    private bool _allowCloseAfterSaving;

    public MainWindow()
    {
        InitializeComponent();

        // Select the appropriate Avalonia window icon.
        // macOS application icon itself comes from the .app bundle.
        var iconFile = OperatingSystem.IsWindows()
            ? "icon.ico"
            : "icon.png";

        var iconUri = new Uri(
            $"avares://Daycord.Desktop/Assets/{iconFile}");

        Icon = new WindowIcon(AssetLoader.Open(iconUri));

        Closing += OnWindowClosing;
    }

    public MainWindow(
        MainWindowViewModel viewModel,
        ReportsViewModel reportsViewModel,
        Func<BackupSettingsViewModel> createBackupSettingsViewModel)
        : this()
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _reportsViewModel = reportsViewModel
            ?? throw new ArgumentNullException(nameof(reportsViewModel));
        _createBackupSettingsViewModel = createBackupSettingsViewModel
            ?? throw new ArgumentNullException(nameof(createBackupSettingsViewModel));

        // Lazy construction: on a restart with PendingRestore, the VM must
        // read the manifest AFTER the startup coordinator has finalized it.
        viewModel.PropertyChanged += OnMainViewModelPropertyChanged;
        Closed += (_, _) =>
            viewModel.PropertyChanged -= OnMainViewModelPropertyChanged;
    }

    private void OnMainViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) ||
            e.PropertyName is nameof(MainWindowViewModel.IsSettingsSelected)
                              or nameof(MainWindowViewModel.IsDatabaseReady))
        {
            EnsureBackupSettingsViewModel();
        }
    }

    private void EnsureBackupSettingsViewModel()
    {
        if (_backupSettingsViewModel is not null ||
            DataContext is not MainWindowViewModel vm ||
            !vm.IsDatabaseReady ||
            !vm.IsSettingsSelected ||
            _createBackupSettingsViewModel is null)
        {
            return;
        }

        _backupSettingsViewModel = _createBackupSettingsViewModel();

        var page = this.FindControl<BackupSettingsView>("BackupSettingsPage")
            ?? throw new InvalidOperationException(
                "The Backup & Restore view was not found.");

        page.DataContext = _backupSettingsViewModel;
    }

    /// <summary>
    /// Prevents closing the main window while a report
    /// export or preset modification is in progress.
    ///
    /// Pending report column edits are saved before closing.
    /// If validation or persistence fails, closing is cancelled.
    /// </summary>
    private async void OnWindowClosing(
        object? sender,
        WindowClosingEventArgs e)
    {
        // Do not dispose the database services while a backup or restore
        // preparation is still writing files.
        if (_backupSettingsViewModel?.IsBusy == true)
        {
            e.Cancel = true;
            return;
        }

        if (_allowCloseAfterSaving)
            return;

        var reports = _reportsViewModel;

        // Design-time instances do not have a ReportsViewModel.
        if (reports is null)
            return;

        // Do not interrupt a write or start competing saves.
        if (_isSavingReportsBeforeClose ||
            reports.IsExporting ||
            reports.IsPresetBusy)
        {
            e.Cancel = true;
            return;
        }

        // No pending changes: follow the normal Avalonia
        // window closing lifecycle.
        if (!reports.HasUnsavedChanges)
            return;

        // Avalonia closing events are synchronous.
        // Cancel first, save asynchronously, then close again
        // after a successful persistence operation.
        e.Cancel = true;

        if (!reports.SaveColumnsCommand.CanExecute(null))
        {
            Trace.TraceWarning(
                "Daycord: report settings cannot be saved " +
                "in the current application state.");

            return;
        }

        _isSavingReportsBeforeClose = true;

        try
        {
            await reports.SaveColumnsCommand.ExecuteAsync(null);

            // The command reports validation and SQLite errors
            // through the ReportsViewModel. Do not close
            // when any edited configuration remains unsaved.
            if (reports.HasUnsavedChanges ||
                reports.HasPresetError ||
                reports.IsPresetBusy ||
                reports.IsExporting)
            {
                return;
            }

            // Bypass this guard on the second close request.
            // Otherwise Closing would restart the same cycle.
            _allowCloseAfterSaving = true;

            Close();
        }
        catch (Exception error)
        {
            // Unexpected errors must not terminate the process
            // or discard the user's pending configuration.
            Trace.TraceError(
                "Daycord: unable to save report settings " +
                "before closing: " + error);
        }
        finally
        {
            _isSavingReportsBeforeClose = false;
        }
    }
}