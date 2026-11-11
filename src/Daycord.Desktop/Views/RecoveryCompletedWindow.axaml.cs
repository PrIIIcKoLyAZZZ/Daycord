using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;

namespace Daycord.Desktop.Views;

public partial class RecoveryCompletedWindow : Window
{
    private readonly bool _isRussian;

    // Required by the Avalonia runtime XAML loader.
    public RecoveryCompletedWindow()
        : this("en")
    {
    }

    public RecoveryCompletedWindow(string language)
    {
        InitializeComponent();

        _isRussian = language.StartsWith(
            "ru",
            StringComparison.OrdinalIgnoreCase);

        var title = this.FindControl<TextBlock>("RecoveryTitle")
            ?? throw new InvalidOperationException(
                "Recovery title was not found.");

        var subtitle = this.FindControl<TextBlock>("RecoverySubtitle")
            ?? throw new InvalidOperationException(
                "Recovery subtitle was not found.");

        var restart = this.FindControl<Button>("RestartButton")
            ?? throw new InvalidOperationException(
                "Restart button was not found.");

        title.Text = _isRussian
            ? "Данные были некорректно сохранены"
            : "Data was not saved correctly";

        subtitle.Text = _isRussian
            ? "Мы уже всё восстановили."
            : "We've already restored it.";

        restart.Content = _isRussian
            ? "Перезапустить"
            : "Restart";
    }

    private void OnRestartClick(
        object? sender,
        RoutedEventArgs e)
    {
        var button = this.FindControl<Button>("RestartButton")
            ?? throw new InvalidOperationException(
                "Restart button was not found.");

        button.IsEnabled = false;

        try
        {
            StartRestartLauncher();

            if (Application.Current?.ApplicationLifetime
                is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
            else
            {
                Close();
            }
        }
        catch (Exception error)
        {
            Trace.TraceWarning(
                "Daycord restart failed: {0}",
                error.GetType().Name);

            button.IsEnabled = true;

            var subtitle =
                this.FindControl<TextBlock>("RecoverySubtitle");

            if (subtitle is not null)
            {
                subtitle.Text = _isRussian
                    ? "Не удалось перезапустить Daycord. " +
                      "Попробуйте ещё раз."
                    : "Daycord could not restart. Please try again.";
            }
        }
    }

    private static void StartRestartLauncher()
    {
        var executable = Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new InvalidOperationException(
                "The application executable is unavailable.");
        }

        var arguments = new List<string>();
        var currentArguments = Environment.GetCommandLineArgs();

        if (string.Equals(
                Path.GetFileNameWithoutExtension(executable),
                "dotnet",
                StringComparison.OrdinalIgnoreCase))
        {
            var assemblyPath = Assembly
                .GetEntryAssembly()?.Location;

            if (string.IsNullOrWhiteSpace(assemblyPath))
            {
                throw new InvalidOperationException(
                    "The managed entry point is unavailable.");
            }

            arguments.Add(assemblyPath);
        }

        arguments.AddRange(currentArguments.Skip(1));

        ProcessStartInfo launcher;

        if (OperatingSystem.IsWindows())
        {
            launcher = CreateWindowsLauncher(
                executable,
                arguments);
        }
        else if (OperatingSystem.IsMacOS() ||
                 OperatingSystem.IsLinux())
        {
            launcher = CreateUnixLauncher(
                executable,
                arguments);
        }
        else
        {
            throw new PlatformNotSupportedException(
                "Automatic restart is not supported.");
        }

        using var process = Process.Start(launcher);

        if (process is null)
        {
            throw new IOException(
                "The application restart launcher did not start.");
        }
    }

    private static ProcessStartInfo CreateUnixLauncher(
        string executable,
        IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Wait until the current Daycord process releases its lock.
        // Paths are positional arguments, not shell interpolation.
        info.ArgumentList.Add("-c");

        info.ArgumentList.Add(
            "parent=\"$1\"; shift; " +
            "while kill -0 \"$parent\" 2>/dev/null; do " +
            "sleep 0.2; " +
            "done; " +
            "exec \"$@\"");

        info.ArgumentList.Add("daycord-restart");

        info.ArgumentList.Add(
            Environment.ProcessId.ToString(
                CultureInfo.InvariantCulture));

        info.ArgumentList.Add(executable);

        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        return info;
    }

    private static ProcessStartInfo CreateWindowsLauncher(
        string executable,
        IReadOnlyList<string> arguments)
    {
        static string QuotePowerShell(string value)
        {
            return "'" + value.Replace("'", "''") + "'";
        }

        var quotedArguments = arguments
            .Select(argument =>
                QuotePowerShell(
                    "\"" +
                    argument.Replace("\"", "\\\"") +
                    "\""))
            .ToArray();

        var argumentExpression = arguments.Count == 0
            ? string.Empty
            : " -ArgumentList @(" +
              string.Join(",", quotedArguments) +
              ")";

        var script =
            "$ErrorActionPreference = 'Stop'; " +
            "try { Wait-Process -Id " +
            Environment.ProcessId.ToString(
                CultureInfo.InvariantCulture) +
            " -ErrorAction SilentlyContinue } catch {} ; " +
            "Start-Process -FilePath " +
            QuotePowerShell(executable) +
            argumentExpression;

        var encoded = Convert.ToBase64String(
            Encoding.Unicode.GetBytes(script));

        var info = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };

        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-EncodedCommand");
        info.ArgumentList.Add(encoded);

        return info;
    }
}