using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Daycord.Desktop.Views;

/// <summary>
/// Small, SQLite-independent startup surface and failure fallback.
/// Backup selection and recovery installation are handled by Bootstrap,
/// never by this window.
/// </summary>
public partial class DatabaseRecoveryWindow : Window
{
    private readonly bool _isRussian;
    private bool _startupInProgress = true;

    public DatabaseRecoveryWindow()
    {
        InitializeComponent();

        // Startup may fail before the saved app language can be read.
        // Use the OS language only as an early-startup fallback.
        _isRussian = string.Equals(
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
            "ru",
            StringComparison.OrdinalIgnoreCase);

        Required<TextBlock>("StatusHeading").Text = _isRussian
            ? "Запуск Daycord"
            : "Starting Daycord";

        Required<TextBlock>("StatusDescription").Text = _isRussian
            ? "Открываем данные приложения."
            : "Opening your app data.";

        Closing += OnWindowClosing;
    }

    public void CloseAfterSuccessfulStartup()
    {
        _startupInProgress = false;
        Close();
    }

    public void ShowFailure()
    {
        _startupInProgress = false;

        Required<TextBlock>("StatusHeading").Text = _isRussian
            ? "Не удалось открыть данные"
            : "Could not open your data";

        Required<TextBlock>("StatusDescription").Text = _isRussian
            ? "Автоматическое восстановление не завершилось."
            : "Automatic recovery could not be completed.";

        var closeButton = Required<Button>("CloseButton");
        closeButton.Content = _isRussian ? "Закрыть" : "Close";
        closeButton.IsVisible = true;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        // Prevent an ordinary close from interrupting active recovery.
        if (_startupInProgress)
            e.Cancel = true;
    }

    private T Required<T>(string name) where T : Control =>
        this.FindControl<T>(name)
        ?? throw new InvalidOperationException(
            $"The startup control '{name}' was not found.");
}