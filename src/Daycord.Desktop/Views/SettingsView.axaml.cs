using Avalonia.Controls;
using Daycord.Desktop.ViewModels;

namespace Daycord.Desktop.Views;

/// <summary>
/// Full application settings page.
///
/// General settings use SettingsViewModel.
/// Backup & Restore uses its own existing ViewModel,
/// initialized lazily after database restoration.
/// </summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Connects the existing Backup & Restore component.
    ///
    /// Must be called only after the database is ready
    /// and any pending restoration has completed.
    /// </summary>
    public void SetBackupViewModel(
        BackupSettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        var backupView =
            this.FindControl<BackupSettingsView>(
                "BackupSettingsPage");

        if (backupView is null)
        {
            throw new InvalidOperationException(
                "Backup & Restore component was not found " +
                "inside SettingsView.");
        }

        if (ReferenceEquals(backupView.DataContext, viewModel))
        {
            return;
        }

        if (backupView.DataContext is BackupSettingsViewModel)
        {
            throw new InvalidOperationException(
                "Backup & Restore is already connected " +
                "to a different ViewModel.");
        }

        backupView.DataContext = viewModel;
    }
}