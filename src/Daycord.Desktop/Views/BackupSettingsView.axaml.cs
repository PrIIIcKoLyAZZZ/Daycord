using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Daycord.Desktop.ViewModels;

namespace Daycord.Desktop.Views;

public partial class BackupSettingsView : UserControl
{
    private static readonly FilePickerFileType SqliteFileType =
        new("SQLite Database (*.db)")
        {
            Patterns = ["*.db"],
            MimeTypes = ["application/vnd.sqlite3"]
        };

    public BackupSettingsView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private BackupSettingsViewModel? ViewModel =>
        DataContext as BackupSettingsViewModel;

    private async void OnCreateBackupClicked(
        object? sender,
        RoutedEventArgs e)
    {
        var vm = ViewModel;

        if (vm is null || !vm.TryBeginOperation())
            return;

        string? snapshotPath = null;

        try
        {
            var topLevel = TopLevel.GetTopLevel(this)
                ?? throw new InvalidOperationException(
                    "The backup view is not attached to a window.");

            // First create and validate the entire backup.
            // No user-selected file is modified at this stage.
            snapshotPath = vm.AllocateTemporaryFile("export");
            await vm.CreateTemporaryBackupAsync(snapshotPath);

            var suggestedName =
                $"Daycord-Backup-{DateTime.Now:yyyy-MM-dd-HHmmss}.db";

            var options = new FilePickerSaveOptions
            {
                Title = vm.SaveDialogTitle,
                SuggestedFileName = suggestedName,
                DefaultExtension = "db",
                FileTypeChoices = [SqliteFileType],
                SuggestedFileType = SqliteFileType,
                ShowOverwritePrompt = true
            };

            using var selectedFile = await topLevel.StorageProvider
                .SaveFilePickerAsync(options);

            if (selectedFile is null)
                return;

            // Only local paths are supported for atomic replacement.
            // A non-local storage provider cannot guarantee this.
            var destination = vm.EnsureSafeSaveDestination(
                selectedFile.TryGetLocalPath());

            await PublishBackupAtomicallyAsync(
                snapshotPath,
                destination);

            // Only after successful publication and verification.
            vm.ReportBackupCreated(selectedFile.Name);
        }
        catch (Exception error)
        {
            vm.ReportFailure(error, "BackupFailed");
        }
        finally
        {
            vm.DeleteTemporaryFile(snapshotPath);
            vm.EndOperation();
        }
    }

    private static async Task PublishBackupAtomicallyAsync(
        string validatedSnapshot,
        string destinationPath)
    {
        var destinationDirectory =
            Path.GetDirectoryName(destinationPath)
            ?? throw new IOException("Invalid backup destination.");

        if (!Directory.Exists(destinationDirectory))
        {
            throw new DirectoryNotFoundException(
                "The selected destination directory does not exist.");
        }

        // The temporary output must be on the same filesystem
        // as the user-selected final file.
        var temporaryOutput = Path.Combine(
            destinationDirectory,
            $".daycord-save-{Guid.NewGuid():N}.tmp");

        try
        {
            var expectedHash = await ComputeSha256Async(
                validatedSnapshot);

            // CreateNew prevents unexpected replacement of another
            // file while writing the temporary output.
            await using (var input = new FileStream(
                             validatedSnapshot,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             bufferSize: 81920,
                             FileOptions.Asynchronous))
            await using (var output = new FileStream(
                             temporaryOutput,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 81920,
                             FileOptions.Asynchronous |
                             FileOptions.WriteThrough))
            {
                await input.CopyToAsync(output);

                await output.FlushAsync();

                // Request a durable flush before publication.
                await Task.Run(() =>
                    output.Flush(flushToDisk: true));
            }

            // The original backup is still untouched.
            // Verify the entire output before replacing anything.
            var actualHash = await ComputeSha256Async(
                temporaryOutput);

            if (!string.Equals(
                    expectedHash,
                    actualHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Backup verification failed before publication.");
            }

            // The picker has already obtained the user's save /
            // overwrite decision. This move occurs only after all
            // file contents have been copied and verified.
            //
            // Same-directory rename avoids exposing a partially
            // written final file.
            File.Move(
                temporaryOutput,
                destinationPath,
                overwrite: true);
        }
        finally
        {
            TryDeleteTemporaryOutput(temporaryOutput);
        }
    }

    private static async Task<string> ComputeSha256Async(
        string filePath)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous);

        var hash = await SHA256.HashDataAsync(stream);

        return Convert.ToHexString(hash);
    }

    private static void TryDeleteTemporaryOutput(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ------------------------------------------------------------
    // Choose a backup for restoration
    // ------------------------------------------------------------

    private async void OnChooseBackupClicked(
        object? sender,
        RoutedEventArgs e)
    {
        var vm = ViewModel;

        if (vm is null || !vm.TryBeginOperation())
            return;

        string? temporaryPath = null;

        try
        {
            var topLevel = TopLevel.GetTopLevel(this)
                ?? throw new InvalidOperationException(
                    "The backup view is not attached to a window.");

            var options = new FilePickerOpenOptions
            {
                Title = vm.OpenDialogTitle,
                AllowMultiple = false,
                FileTypeFilter = [SqliteFileType]
            };

            var files = await topLevel.StorageProvider
                .OpenFilePickerAsync(options);

            if (files.Count == 0)
                return;

            using var sourceFile = files[0];

            // Copy the selected file into private staging.
            // Never run migrations against the original.
            temporaryPath = vm.AllocateTemporaryFile("import");

            await using (var source = await sourceFile.OpenReadAsync())
            await using (var destination = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 81920,
                             FileOptions.Asynchronous |
                             FileOptions.WriteThrough))
            {
                await source.CopyToAsync(destination);
                await destination.FlushAsync();
            }

            var accepted =
                await vm.ValidateRestoreCandidateAsync(
                    temporaryPath);

            if (accepted)
            {
                // The ViewModel retains this private file until
                // the user explicitly confirms or cancels.
                temporaryPath = null;
            }
        }
        catch (Exception error)
        {
            vm.ReportFailure(error, "BackupRestoreFailed");
        }
        finally
        {
            vm.DeleteTemporaryFile(temporaryPath);
            vm.EndOperation();
        }
    }

    private void OnCancelRestoreClicked(
        object? sender,
        RoutedEventArgs e)
    {
        ViewModel?.CancelRestore();
    }

    private async void OnConfirmRestoreClicked(
        object? sender,
        RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
            await vm.ConfirmRestoreAsync();
    }
}