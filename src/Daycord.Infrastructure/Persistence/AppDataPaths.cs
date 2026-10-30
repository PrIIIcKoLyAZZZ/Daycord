namespace Daycord.Infrastructure.Persistence;

public sealed class AppDataPaths
{
    public AppDataPaths(string? appDataDirectory = null)
    {
        if (appDataDirectory is null)
        {
            var localApplicationData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

            if (string.IsNullOrWhiteSpace(localApplicationData))
            {
                throw new IOException("The local application data folder is unavailable.");
            }

            appDataDirectory = Path.Combine(localApplicationData, "Daycord");
        }

        DataDirectory = Path.GetFullPath(appDataDirectory);
    }

    public string DataDirectory { get; }

    public string DatabasePath => Path.Combine(DataDirectory, "daycord.db");

    public string SchemaBackupDirectory =>
        Path.Combine(DataDirectory, "Backups", "SchemaUpgrades");

    public string BeforeRestoreBackupDirectory =>
        Path.Combine(DataDirectory, "Backups", "BeforeRestore");

    public string RestoreStagingDirectory =>
        Path.Combine(DataDirectory, "RestoreStaging");

    public string PendingRestoreManifestPath =>
        Path.Combine(RestoreStagingDirectory, "pending-restore.json");

    public void EnsureDataDirectory() => Directory.CreateDirectory(DataDirectory);

    public void EnsureSchemaBackupDirectory() =>
        Directory.CreateDirectory(SchemaBackupDirectory);

    public void EnsureBeforeRestoreBackupDirectory() =>
        Directory.CreateDirectory(BeforeRestoreBackupDirectory);

    public void EnsureRestoreStagingDirectory() =>
        Directory.CreateDirectory(RestoreStagingDirectory);
}