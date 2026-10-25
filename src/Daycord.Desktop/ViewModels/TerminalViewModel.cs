using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daycord.Core.Application.Models;
using Daycord.Core.Application.Services;
using Daycord.Desktop.Localization;
using Daycord.Desktop.Navigation;

namespace Daycord.Desktop.ViewModels;

public sealed partial class TerminalViewModel : ObservableObject, IDisposable
{
    private static readonly string[] DateFormats =
    [
        "dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd", "yyyy/MM/dd",
        "MM/dd/yyyy", "M/d/yyyy"
    ];

    private readonly TaskService _taskService;
    private readonly ILocalizationService _localization;
    private readonly INavigationService _navigation;
    private readonly CancellationTokenSource _sessionCancellation = new();

    private string? _formErrorKey;
    private string? _formErrorDetails;
    private bool _disposed;

    // An empty input means today.
    [ObservableProperty]
    private string _dateInputText = string.Empty;

    [ObservableProperty]
    private string _taskNumber = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _plannedTimeInput = string.Empty;

    [ObservableProperty]
    private bool _isCreating;

    [ObservableProperty]
    private bool _wasTaskCreated;

    public TerminalViewModel(
        TaskService taskService,
        ILocalizationService localization,
        INavigationService navigation)
    {
        _taskService = taskService
            ?? throw new ArgumentNullException(nameof(taskService));

        _localization = localization
            ?? throw new ArgumentNullException(nameof(localization));

        _navigation = navigation
            ?? throw new ArgumentNullException(nameof(navigation));

        _localization.PropertyChanged += OnLocalizationChanged;
    }

    public ObservableCollection<SessionTaskItemViewModel>
        SessionTasks { get; } = new();

    public event EventHandler? TaskCreated;

    public bool IsSessionEmpty => SessionTasks.Count == 0;

    public bool CanEditForm => !_disposed && !IsCreating;

    public CultureInfo CalendarCulture =>
        CultureInfo.GetCultureInfo(_localization.CurrentLanguage);

    public string RecentHeading => Text("QuickTaskRecentHeading");
    public string OpenDetailsLabel => Text("QuickTaskOpenDetails");
    public string EmptySessionText => Text("QuickTaskEmptySession");
    public string DateLabel => Text("QuickTaskDateLabel");
    public string NumberLabel => Text("QuickTaskNumberLabel");
    public string TitleLabel => Text("QuickTaskTitleLabel");
    public string TimeLabel => Text("QuickTaskTimeLabel");
    public string DatePlaceholder => Text("QuickTaskToday");
    public string NumberPlaceholder => Text("QuickTaskNumberPlaceholder");
    public string TitlePlaceholder => Text("QuickTaskTitlePlaceholder");
    public string TimePlaceholder => Text("QuickTaskTimePlaceholder");
    public string EnterHint => Text("QuickTaskEnterHint");

    public string CreateButtonText => Text(
        IsCreating ? "QuickTaskCreating" : "QuickTaskCreateAction");

    public string CreatedMessage => WasTaskCreated
        ? Text("QuickTaskCreated")
        : string.Empty;

    public string FormErrorMessage
    {
        get
        {
            if (_formErrorKey is null)
                return string.Empty;

            var message = Text(_formErrorKey);

            return _formErrorDetails is null
                ? message
                : string.Format(
                    CalendarCulture,
                    message,
                    _formErrorDetails);
        }
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public DateTime CalendarAnchorDate =>
        TryResolveDate(out var date)
            ? date
            : DateTime.Today;

    public bool TryResolveDate(out DateTime date)
    {
        var value = DateInputText.Trim();

        if (value.Length == 0 ||
            value.Equals(
                "today",
                StringComparison.OrdinalIgnoreCase) ||
            value.Equals(
                "сегодня",
                StringComparison.OrdinalIgnoreCase))
        {
            date = DateTime.Today;
            return true;
        }

        if (value.Equals(
                "tomorrow",
                StringComparison.OrdinalIgnoreCase) ||
            value.Equals(
                "завтра",
                StringComparison.OrdinalIgnoreCase))
        {
            date = DateTime.Today.AddDays(1);
            return true;
        }

        if (DateTime.TryParseExact(
                value,
                DateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed) ||
            DateTime.TryParse(
                value,
                CalendarCulture,
                DateTimeStyles.None,
                out parsed))
        {
            date = parsed.Date;
            return true;
        }

        date = default;
        return false;
    }

    public void SelectCalendarDate(DateTime date)
    {
        DateInputText = date.Date == DateTime.Today
            ? string.Empty
            : date.ToString(
                "dd.MM.yyyy",
                CultureInfo.InvariantCulture);
    }

    [RelayCommand(CanExecute = nameof(CanCreateTask))]
    private async Task CreateTaskAsync()
    {
        if (!CanCreateTask())
            return;

        SetFormError(null);
        WasTaskCreated = false;

        if (!TryResolveDate(out var localDate))
        {
            SetFormError("QuickTaskDateRequired");
            return;
        }

        if (string.IsNullOrWhiteSpace(Title))
        {
            SetFormError("QuickTaskTitleRequired");
            return;
        }

        if (!PlannedDuration.TryParse(
                PlannedTimeInput,
                out var plannedMinutes))
        {
            SetFormError("QuickTaskInvalidTime");
            return;
        }

        var request = new CreateTaskRequest(
            TaskDate: DateOnly.FromDateTime(localDate),
            Title: Title,
            ShortDescription: null,
            DescriptionMarkdown: null,
            StatusId: null,
            TaskNumber: TaskNumber,
            PlannedMinutes: plannedMinutes);

        IsCreating = true;
        var succeeded = false;

        try
        {
            var created = await _taskService.CreateAsync(
                request,
                _sessionCancellation.Token);

            if (_disposed)
                return;

            SessionTasks.Add(
                new SessionTaskItemViewModel(
                    created,
                    plannedMinutes,
                    _localization));

            OnPropertyChanged(nameof(IsSessionEmpty));

            // Preserve the date for successive tasks.
            TaskNumber = string.Empty;
            Title = string.Empty;
            PlannedTimeInput = string.Empty;

            WasTaskCreated = true;
            succeeded = true;
        }
        catch (OperationCanceledException)
            when (_sessionCancellation.IsCancellationRequested)
        {
            // The user left Quick Task.
        }
        catch (Exception ex) when (!_disposed)
        {
            SetFormError(
                "QuickTaskCreateError",
                ex.Message);
        }
        finally
        {
            if (!_disposed)
            {
                IsCreating = false;

                if (succeeded)
                    TaskCreated?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    // Task Details integration

    [RelayCommand]
    private void OpenSessionTask(
        SessionTaskItemViewModel? item)
    {
        if (_disposed ||
            IsCreating ||
            item is null ||
            _navigation.CurrentPage != PageKind.NewTask ||
            _navigation.IsTaskOpen ||
            !SessionTasks.Contains(item))
        {
            return;
        }

        // Do not change the main section.
        // MainWindow keeps the existing TerminalViewModel alive.
        _navigation.OpenTask(item.Id);
    }

    private bool CanCreateTask() =>
        !_disposed && !IsCreating;

    private string Text(string key) =>
        _localization.GetString(key);

    private void SetFormError(
        string? key,
        string? details = null)
    {
        _formErrorKey = key;
        _formErrorDetails = details;

        OnPropertyChanged(nameof(FormErrorMessage));
    }

    private void ClearFeedback()
    {
        SetFormError(null);
        WasTaskCreated = false;
    }

    partial void OnDateInputTextChanged(string value) =>
        ClearFeedback();

    partial void OnTaskNumberChanged(string value) =>
        ClearFeedback();

    partial void OnTitleChanged(string value) =>
        ClearFeedback();

    partial void OnPlannedTimeInputChanged(string value) =>
        ClearFeedback();

    partial void OnIsCreatingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditForm));
        OnPropertyChanged(nameof(CreateButtonText));

        CreateTaskCommand.NotifyCanExecuteChanged();
    }

    partial void OnWasTaskCreatedChanged(bool value) =>
        OnPropertyChanged(nameof(CreatedMessage));

    private void OnLocalizationChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.PropertyName) &&
            e.PropertyName !=
                nameof(ILocalizationService.CurrentLanguage))
        {
            return;
        }

        OnPropertyChanged(nameof(CalendarCulture));
        OnPropertyChanged(nameof(RecentHeading));
        OnPropertyChanged(nameof(OpenDetailsLabel));
        OnPropertyChanged(nameof(EmptySessionText));
        OnPropertyChanged(nameof(DateLabel));
        OnPropertyChanged(nameof(NumberLabel));
        OnPropertyChanged(nameof(TitleLabel));
        OnPropertyChanged(nameof(TimeLabel));
        OnPropertyChanged(nameof(DatePlaceholder));
        OnPropertyChanged(nameof(NumberPlaceholder));
        OnPropertyChanged(nameof(TitlePlaceholder));
        OnPropertyChanged(nameof(TimePlaceholder));
        OnPropertyChanged(nameof(EnterHint));
        OnPropertyChanged(nameof(CreateButtonText));
        OnPropertyChanged(nameof(CreatedMessage));
        OnPropertyChanged(nameof(FormErrorMessage));

        foreach (var task in SessionTasks)
            task.RefreshLocalization();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _localization.PropertyChanged -= OnLocalizationChanged;

        _sessionCancellation.Cancel();
        _sessionCancellation.Dispose();

        SessionTasks.Clear();
        TaskCreated = null;
    }
}