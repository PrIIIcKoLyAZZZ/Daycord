using CommunityToolkit.Mvvm.ComponentModel;

namespace Daycord.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public string WelcomeMessage { get; } = "Welcome to Daycord";

    [ObservableProperty]
    private string _databaseStatusMessage = "Initializing local database...";

    [ObservableProperty]
    private bool _isDatabaseReady;

    public void MarkDatabaseReady()
    {
        DatabaseStatusMessage = "Local database ready.";
        IsDatabaseReady = true;
    }

    public void MarkDatabaseFailed(Exception error)
    {
        IsDatabaseReady = false;

        DatabaseStatusMessage =
            $"Database initialization failed: {error.Message}\n" +
            "Task data is unavailable. Close Daycord and check the database " +
            "or its backups.";
    }
}