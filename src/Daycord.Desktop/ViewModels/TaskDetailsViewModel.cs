using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daycord.Core.Application.Models;
using Daycord.Core.Application.Services;
using Daycord.Core.Domain.Entities;
using Daycord.Desktop.Localization;
using Daycord.Desktop.Navigation;

namespace Daycord.Desktop.ViewModels;

public sealed partial class TaskDetailsViewModel :
    ObservableObject,
    IDisposable
{
    private const int DefaultMinutesPerDay = 24 * 60;

    private static readonly string[] DateFormats =
    [
        "dd.MM.yyyy",
        "d.M.yyyy",
        "yyyy-MM-dd",
        "yyyy/MM/dd",
        "MM/dd/yyyy",
        "M/d/yyyy",
        "dd/MM/yyyy",
        "d/M/yyyy"
    ];

    private static readonly Regex DurationExpression = new(
        @"^\s*" +
        @"(?:(?<days>\d+)\s*(?:days?|d|д(?:ней|ня|ень|н)?))?\s*" +
        @"(?:(?<hours>\d+)\s*(?:hours?|hrs?|h|часов|часа|час|ч))?\s*" +
        @"(?:(?<minutes>\d+)\s*(?:minutes?|mins?|min|m|минуты|минута|минут|мин|м))?" +
        @"\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly TaskService _tasks;
    private readonly ILocalizationService _localization;
    private readonly INavigationService _navigation;
    private readonly CancellationTokenSource _lifetime = new();

    private Guid? _taskId;

    private TaskDetailsEditorMode _editorMode =
        TaskDetailsEditorMode.Uninitialized;

    private bool _initializationStarted;
    private bool _populating;
    private bool _disposed;

    private bool _hasMultiplePlanningPeriods;

    private EditorSnapshot _baseline = new(
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        Guid.Empty,
        string.Empty);

    private string? _errorKey;
    private string? _errorDetails;
    private Action? _pendingNavigation;

    [ObservableProperty]
    private string _taskNumber = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _dateInputText = string.Empty;

    [ObservableProperty]
    private string _shortDescription = string.Empty;

    [ObservableProperty]
    private string _descriptionMarkdown = string.Empty;

    [ObservableProperty]
    private string _plannedTimeInput = string.Empty;

    [ObservableProperty]
    private TaskDetailsStatusOption? _selectedStatus;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private bool _isTaskNotFound;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private bool _isPreviewMode;

    [ObservableProperty]
    private bool _isDiscardConfirmationOpen;

    [ObservableProperty]
    private bool _isSaveSuccessful;

    public TaskDetailsViewModel(
        TaskService tasks,
        ILocalizationService localization,
        INavigationService navigation)
    {
        _tasks = tasks
            ?? throw new ArgumentNullException(nameof(tasks));

        _localization = localization
            ?? throw new ArgumentNullException(nameof(localization));

        _navigation = navigation
            ?? throw new ArgumentNullException(nameof(navigation));

        _localization.PropertyChanged += OnLocalizationChanged;
    }

    public event EventHandler<TaskDetailsSavedEventArgs>? TaskSaved;

    public ObservableCollection<TaskDetailsStatusOption>
        StatusOptions { get; } = new();

    // Editor mode

    public TaskDetailsEditorMode EditorMode
    {
        get => _editorMode;

        private set
        {
            if (!SetProperty(ref _editorMode, value))
                return;

            OnPropertyChanged(nameof(IsCreateMode));
            OnPropertyChanged(nameof(Heading));
            OnPropertyChanged(nameof(SaveLabel));
            OnPropertyChanged(nameof(SavingMessage));
            OnPropertyChanged(nameof(SaveButtonText));
            OnPropertyChanged(nameof(SavedMessage));
        }
    }

    public bool IsCreateMode =>
        EditorMode == TaskDetailsEditorMode.Create;

    public Guid TaskId => _taskId ?? Guid.Empty;

    public CultureInfo DateCulture =>
        CultureInfo.GetCultureInfo(_localization.CurrentLanguage);

    // Planning state

    public bool HasMultiplePlanningPeriods
    {
        get => _hasMultiplePlanningPeriods;

        private set
        {
            if (SetProperty(ref _hasMultiplePlanningPeriods, value))
            {
                OnPropertyChanged(nameof(CanEditPlannedTime));
            }
        }
    }

    public bool CanEditPlannedTime =>
        CanEdit && !HasMultiplePlanningPeriods;

    public string MultiplePlanningPeriodsHint =>
        DateCulture.TwoLetterISOLanguageName == "ru"
            ? "У задачи несколько плановых периодов. " +
              "Чтобы сохранить распределение по датам, " +
              "изменение общей длительности здесь недоступно."
            : "This task has multiple planning periods. " +
              "The total cannot be edited here without " +
              "changing its distribution across dates.";

    // Localization

    private string Text(string key) =>
        _localization.GetString(key);

    public string Heading => Text(
        IsCreateMode
            ? "TaskDetailsCreateHeading"
            : "TaskDetailsHeading");

    public string BackLabel => Text("TaskDetailsBack");

    public string SaveLabel => Text(
        IsCreateMode
            ? "TaskDetailsCreateAction"
            : "TaskDetailsSave");

    public string CancelLabel => Text("TaskDetailsCancel");
    public string NumberLabel => Text("TaskDetailsNumber");
    public string TitleLabel => Text("TaskDetailsTitle");
    public string DateLabel => Text("TaskDetailsDate");
    public string StatusLabel => Text("TaskDetailsStatus");

    public string ShortDescriptionLabel =>
        Text("TaskDetailsShortDescription");

    public string MarkdownLabel =>
        Text("TaskDetailsMarkdown");

    public string PlannedTimeLabel =>
        Text("QuickTaskTimeLabel");

    public string PlannedTimePlaceholder =>
        Text("QuickTaskTimePlaceholder");

    public string PlannedTimeHint =>
        Text("QuickTaskInvalidTime");

    public string EditLabel => Text("TaskDetailsEdit");
    public string PreviewLabel => Text("TaskDetailsPreview");

    public string LoadingMessage =>
        Text("TaskDetailsLoading");

    public string SavingMessage => Text(
        IsCreateMode
            ? "TaskDetailsCreating"
            : "TaskDetailsSaving");

    public string SaveButtonText =>
        IsSaving ? SavingMessage : SaveLabel;

    public string SavedMessage => Text(
        IsCreateMode
            ? "TaskDetailsCreateSuccessful"
            : "TaskDetailsSaveSuccessful");

    public string NotFoundMessage =>
        Text("TaskDetailsTaskNotFound");

    public string DatePlaceholder =>
        Text("TaskDetailsDatePlaceholder");

    public string DiscardConfirmationTitle =>
        Text("TaskDetailsDiscardTitle");

    public string DiscardConfirmationMessage =>
        Text("TaskDetailsDiscardMessage");

    public string DiscardLabel =>
        Text("TaskDetailsDiscard");

    public string KeepEditingLabel =>
        Text("TaskDetailsKeepEditing");

    // State

    public bool CanEdit =>
        !_disposed &&
        IsLoaded &&
        !IsLoading &&
        !IsSaving &&
        !IsTaskNotFound &&
        !IsDiscardConfirmationOpen;

    public bool HasUnsavedChanges => IsDirty;

    public bool IsEditMode => !IsPreviewMode;

    public bool HasError => _errorKey is not null;

    public string ErrorMessage
    {
        get
        {
            if (_errorKey is null)
                return string.Empty;

            var message = Text(_errorKey);

            return _errorDetails is null
                ? message
                : string.Format(
                    DateCulture,
                    message,
                    _errorDetails);
        }
    }

    public bool IsDateInputInvalid =>
        !TryGetTaskDate(out _);

    public DateTime CalendarAnchorDate =>
        TryGetTaskDate(out var date) && date.HasValue
            ? date.Value.ToDateTime(TimeOnly.MinValue)
            : DateTime.Today;

    // Initialize an existing task

    public async Task InitializeAsync(Guid taskId)
    {
        if (_disposed)
            return;

        if (taskId == Guid.Empty)
        {
            throw new ArgumentException(
                "Task ID must not be empty.",
                nameof(taskId));
        }

        if (_initializationStarted)
        {
            if (EditorMode == TaskDetailsEditorMode.Edit &&
                _taskId == taskId)
            {
                return;
            }

            throw new InvalidOperationException(
                "The editor has already been initialized.");
        }

        BeginInitialization(TaskDetailsEditorMode.Edit, taskId);

        var token = _lifetime.Token;

        try
        {
            var task = await _tasks.GetByIdAsync(taskId, token);

            if (ShouldStop(token))
                return;

            if (task is null)
            {
                IsTaskNotFound = true;
                return;
            }

            await LoadStatusesAsync(token);

            if (ShouldStop(token))
                return;

            if (!StatusOptions.Any(
                    status => status.Id == task.StatusId))
            {
                throw new InvalidOperationException(
                    "The task's status was not found.");
            }

            // Load the actual planning periods.
            // Planned time is not stored in Tasks.
            var periods = await _tasks.ListPlanningPeriodsAsync(
                taskId,
                token);

            if (ShouldStop(token))
                return;

            HasMultiplePlanningPeriods = periods.Count > 1;

            // Display the total even when the planning
            // consists of multiple separate periods.
            long totalMinutes = 0;

            foreach (var period in periods)
            {
                totalMinutes = checked(
                    totalMinutes + period.PlannedMinutes);
            }

            PopulateFromTask(
                task,
                FormatPlannedTime(totalMinutes));

            IsLoaded = true;
        }
        catch (OperationCanceledException)
            when (token.IsCancellationRequested)
        {
            // The editor was closed during loading.
        }
        catch (Exception error)
        {
            if (!_disposed)
            {
                SetError(
                    "TaskDetailsLoadFailed",
                    error.Message);
            }
        }
        finally
        {
            if (!_disposed)
                IsLoading = false;
        }
    }

    // Initialize a new task

    public async Task InitializeForCreateAsync()
    {
        if (_disposed)
            return;

        if (_initializationStarted)
        {
            if (EditorMode == TaskDetailsEditorMode.Create)
                return;

            throw new InvalidOperationException(
                "The editor has already been initialized.");
        }

        BeginInitialization(TaskDetailsEditorMode.Create, null);

        var token = _lifetime.Token;

        try
        {
            await LoadStatusesAsync(token);

            if (ShouldStop(token))
                return;

            if (!StatusOptions.Any(
                    status => status.Id == TaskStatusDefinition.OpenId))
            {
                throw new InvalidOperationException(
                    "System Open status was not found.");
            }

            HasMultiplePlanningPeriods = false;

            PopulateDraft(
                taskNumber: null,
                title: string.Empty,
                taskDate: DateOnly.FromDateTime(DateTime.Today),
                shortDescription: null,
                markdown: null,
                statusId: TaskStatusDefinition.OpenId,
                plannedTime: string.Empty);

            IsLoaded = true;
        }
        catch (OperationCanceledException)
            when (token.IsCancellationRequested)
        {
            // The editor was closed during loading.
        }
        catch (Exception error)
        {
            if (!_disposed)
            {
                SetError(
                    "TaskDetailsLoadFailed",
                    error.Message);
            }
        }
        finally
        {
            if (!_disposed)
                IsLoading = false;
        }
    }

    private void BeginInitialization(
        TaskDetailsEditorMode mode,
        Guid? taskId)
    {
        _initializationStarted = true;
        _taskId = taskId;

        EditorMode = mode;
        IsLoaded = false;
        IsTaskNotFound = false;
        IsSaveSuccessful = false;
        IsLoading = true;

        SetError(null);
    }

    private async Task LoadStatusesAsync(CancellationToken token)
    {
        var statuses = await _tasks.ListStatusesAsync(token);

        if (ShouldStop(token))
            return;

        StatusOptions.Clear();

        foreach (var status in statuses)
        {
            StatusOptions.Add(
                new TaskDetailsStatusOption(
                    status,
                    _localization));
        }
    }

    private bool ShouldStop(CancellationToken token) =>
        _disposed || token.IsCancellationRequested;

    // Form population

    private void PopulateFromTask(
        TaskItem task,
        string plannedTime)
    {
        PopulateDraft(
            task.TaskNumber,
            task.Title,
            task.TaskDate,
            task.ShortDescription,
            task.DescriptionMarkdown,
            task.StatusId,
            plannedTime);
    }

    private void PopulateDraft(
        string? taskNumber,
        string title,
        DateOnly? taskDate,
        string? shortDescription,
        string? markdown,
        Guid statusId,
        string plannedTime)
    {
        _populating = true;

        try
        {
            TaskNumber = taskNumber ?? string.Empty;
            Title = title;

            DateInputText = taskDate.HasValue
                ? taskDate.Value.ToString(
                    "dd.MM.yyyy",
                    CultureInfo.InvariantCulture)
                : string.Empty;

            ShortDescription =
                shortDescription ?? string.Empty;

            DescriptionMarkdown =
                markdown ?? string.Empty;

            PlannedTimeInput = plannedTime;

            SelectedStatus = StatusOptions.FirstOrDefault(
                status => status.Id == statusId);

            CaptureSavedState();
            IsDirty = false;
        }
        finally
        {
            _populating = false;
        }

        OnPropertyChanged(nameof(IsDateInputInvalid));
        OnPropertyChanged(nameof(CalendarAnchorDate));
    }

    // Change tracking

    private readonly record struct EditorSnapshot(
        string Number,
        string Title,
        string Date,
        string ShortDescription,
        string Markdown,
        Guid StatusId,
        string PlannedTime);

    private EditorSnapshot CurrentSnapshot() => new(
        TaskNumber ?? string.Empty,
        Title ?? string.Empty,
        DateInputText ?? string.Empty,
        ShortDescription ?? string.Empty,
        DescriptionMarkdown ?? string.Empty,
        SelectedStatus?.Id ?? Guid.Empty,
        HasMultiplePlanningPeriods
            ? _baseline.PlannedTime
            : PlannedTimeInput ?? string.Empty);

    private void CaptureSavedState()
    {
        // During initial population this also captures
        // the planning duration loaded from SQLite.
        _baseline = new EditorSnapshot(
            TaskNumber ?? string.Empty,
            Title ?? string.Empty,
            DateInputText ?? string.Empty,
            ShortDescription ?? string.Empty,
            DescriptionMarkdown ?? string.Empty,
            SelectedStatus?.Id ?? Guid.Empty,
            PlannedTimeInput ?? string.Empty);
    }

    private void OnEdited()
    {
        if (_disposed || _populating || !IsLoaded)
            return;

        IsSaveSuccessful = false;
        SetError(null);

        IsDirty = CurrentSnapshot() != _baseline;
    }

    partial void OnTaskNumberChanged(string value) =>
        OnEdited();

    partial void OnTitleChanged(string value) =>
        OnEdited();

    partial void OnDateInputTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsDateInputInvalid));
        OnPropertyChanged(nameof(CalendarAnchorDate));
        OnEdited();
    }

    partial void OnShortDescriptionChanged(string value) =>
        OnEdited();

    partial void OnDescriptionMarkdownChanged(string value) =>
        OnEdited();

    partial void OnPlannedTimeInputChanged(string value) =>
        OnEdited();

    partial void OnSelectedStatusChanged(
        TaskDetailsStatusOption? value) =>
        OnEdited();

    partial void OnIsDirtyChanged(bool value)
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsLoadingChanged(bool value) =>
        NotifyEditState();

    partial void OnIsLoadedChanged(bool value) =>
        NotifyEditState();

    partial void OnIsSavingChanged(bool value)
    {
        NotifyEditState();
        OnPropertyChanged(nameof(SaveButtonText));
    }

    partial void OnIsTaskNotFoundChanged(bool value) =>
        NotifyEditState();

    partial void OnIsDiscardConfirmationOpenChanged(bool value) =>
        NotifyEditState();

    partial void OnIsPreviewModeChanged(bool value) =>
        OnPropertyChanged(nameof(IsEditMode));

    private void NotifyEditState()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanEditPlannedTime));

        SaveCommand.NotifyCanExecuteChanged();
    }

    // Date

    private bool TryGetTaskDate(out DateOnly? taskDate)
    {
        var input = DateInputText?.Trim() ?? string.Empty;

        if (input.Length == 0)
        {
            taskDate = null;
            return true;
        }

        if (DateOnly.TryParseExact(
                input,
                DateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed) ||
            DateOnly.TryParse(
                input,
                DateCulture,
                DateTimeStyles.None,
                out parsed))
        {
            taskDate = parsed;
            return true;
        }

        taskDate = null;
        return false;
    }

    public void SelectCalendarDate(DateTime date)
    {
        if (!CanEdit)
            return;

        DateInputText = date.ToString(
            "dd.MM.yyyy",
            CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    private void ClearDate()
    {
        if (CanEdit)
            DateInputText = string.Empty;
    }

    // Planned time

    private static string FormatPlannedTime(long minutes)
    {
        if (minutes <= 0)
            return string.Empty;

        var hours = minutes / 60;
        var remainingMinutes = minutes % 60;

        if (hours == 0)
            return $"{remainingMinutes}m";

        if (remainingMinutes == 0)
            return $"{hours}h";

        return $"{hours}h {remainingMinutes}m";
    }

    private static bool TryParsePlannedTime(
        string? input,
        out int? plannedMinutes)
    {
        plannedMinutes = null;

        if (string.IsNullOrWhiteSpace(input))
            return true;

        var value = input.Trim();

        // Plain "1" means 1h.
        if (int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var plainHours))
        {
            var total = (long)plainHours * 60;

            if (total <= 0 || total > int.MaxValue)
                return false;

            plannedMinutes = (int)total;
            return true;
        }

        var match = DurationExpression.Match(value);

        if (!match.Success)
            return false;

        var days = match.Groups["days"];
        var hours = match.Groups["hours"];
        var minutes = match.Groups["minutes"];

        if (!days.Success &&
            !hours.Success &&
            !minutes.Success)
        {
            return false;
        }

        if (!TryReadPart(days, out var d) ||
            !TryReadPart(hours, out var h) ||
            !TryReadPart(minutes, out var m))
        {
            return false;
        }

        var totalMinutes =
            (long)d * DefaultMinutesPerDay +
            (long)h * 60 +
            m;

        if (totalMinutes <= 0 || totalMinutes > int.MaxValue)
            return false;

        plannedMinutes = (int)totalMinutes;
        return true;
    }

    private static bool TryReadPart(Group group, out int value)
    {
        value = 0;

        return !group.Success ||
               int.TryParse(
                   group.Value,
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out value);
    }

    // Save

    private bool CanSave() =>
        CanEdit &&
        IsDirty &&
        !IsDiscardConfirmationOpen;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanSave())
            return;

        SetError(null);
        IsSaveSuccessful = false;

        if (string.IsNullOrWhiteSpace(Title))
        {
            SetError("TaskDetailsTitleRequired");
            return;
        }

        if (!TryGetTaskDate(out var taskDate))
        {
            SetError("TaskDetailsDateInvalid");
            return;
        }

        if (SelectedStatus is null)
        {
            SetError("TaskDetailsStatusRequired");
            return;
        }

        // Preserve all multi-period schedules unchanged.
        var updatePlannedTime =
            !HasMultiplePlanningPeriods &&
            !string.Equals(
                PlannedTimeInput,
                _baseline.PlannedTime,
                StringComparison.Ordinal);

        int? plannedMinutes = null;

        if (!HasMultiplePlanningPeriods)
        {
            if (!TryParsePlannedTime(
                    PlannedTimeInput,
                    out plannedMinutes))
            {
                SetError("QuickTaskInvalidTime");
                return;
            }

            // A new planning record, or an explicitly
            // changed duration, requires a task date.
            if ((IsCreateMode || updatePlannedTime) &&
                plannedMinutes.HasValue &&
                !taskDate.HasValue)
            {
                SetError("QuickTaskDateRequired");
                return;
            }
        }

        var number = TaskNumber;
        var title = Title;
        var shortDescription = ShortDescription;
        var markdown = DescriptionMarkdown;
        var statusId = SelectedStatus.Id;

        IsSaving = true;

        TaskItem? savedTask = null;
        var token = _lifetime.Token;

        try
        {
            if (IsCreateMode)
            {
                var request = new CreateTaskRequest(
                    TaskDate: taskDate,
                    Title: title,
                    ShortDescription: shortDescription,
                    DescriptionMarkdown: markdown,
                    StatusId: statusId,
                    TaskNumber: number,
                    PlannedMinutes: plannedMinutes);

                savedTask = await _tasks.CreateAsync(
                    request,
                    token);
            }
            else if (EditorMode == TaskDetailsEditorMode.Edit)
            {
                var request = new UpdateTaskRequest(
                    Id: TaskId,
                    TaskDate: taskDate,
                    Title: title,
                    ShortDescription: shortDescription,
                    DescriptionMarkdown: markdown,
                    StatusId: statusId,
                    TaskNumber: number,
                    UpdateTaskNumber: true);

                if (updatePlannedTime)
                {
                    // Updates Tasks and the single planning
                    // period in one SQLite transaction.
                    savedTask =
                        await _tasks.UpdateWithPlannedTimeAsync(
                            request,
                            plannedMinutes,
                            token);
                }
                else
                {
                    // Leave all PlanningPeriods untouched
                    // when planned time was not edited.
                    savedTask = await _tasks.UpdateAsync(
                        request,
                        token);
                }
            }
            else
            {
                throw new InvalidOperationException(
                    "The task editor is not initialized.");
            }

            if (ShouldStop(token))
                return;

            _taskId = savedTask.Id;
            OnPropertyChanged(nameof(TaskId));

            // Reflect the saved duration immediately.
            // The next opening will read SQLite again.
            var displayedPlannedTime =
                HasMultiplePlanningPeriods
                    ? PlannedTimeInput
                    : FormatPlannedTime(plannedMinutes ?? 0);

            PopulateFromTask(savedTask, displayedPlannedTime);

            IsSaveSuccessful = true;
        }
        catch (OperationCanceledException)
            when (token.IsCancellationRequested)
        {
            // The editor was disposed during saving.
        }
        catch (KeyNotFoundException)
        {
            if (!_disposed)
            {
                IsTaskNotFound = true;
                SetError("TaskDetailsTaskNotFound");
            }
        }
        catch (ArgumentException error)
        {
            if (!_disposed)
            {
                SetError(
                    "TaskDetailsValidationError",
                    error.Message);
            }
        }
        catch (Exception error)
        {
            if (!_disposed)
            {
                SetError(
                    IsCreateMode
                        ? "TaskDetailsCreateFailed"
                        : "TaskDetailsSaveFailed",
                    error.Message);
            }
        }
        finally
        {
            if (!_disposed)
                IsSaving = false;
        }

        if (savedTask is not null && !_disposed)
        {
            TaskSaved?.Invoke(
                this,
                new TaskDetailsSavedEventArgs(savedTask));

            if (!_disposed)
                _navigation.CloseTask();
        }
    }

    // Markdown modes

    [RelayCommand]
    private void ShowEdit()
    {
        if (!IsLoading && !IsSaving)
            IsPreviewMode = false;
    }

    [RelayCommand]
    private void ShowPreview()
    {
        if (!IsLoading && !IsSaving)
            IsPreviewMode = true;
    }

    // Navigation and unsaved changes

    [RelayCommand]
    private void Back() =>
        RequestLeave(_navigation.CloseTask);

    [RelayCommand]
    private void Cancel() =>
        RequestLeave(_navigation.CloseTask);

    public void RequestLeave(Action continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);

        if (_disposed || IsSaving)
            return;

        if (!IsDirty)
        {
            continuation();
            return;
        }

        _pendingNavigation = continuation;
        IsDiscardConfirmationOpen = true;
    }

    [RelayCommand]
    private void ConfirmDiscard()
    {
        if (_disposed || !IsDiscardConfirmationOpen)
            return;

        var continuation = _pendingNavigation;
        _pendingNavigation = null;

        IsDiscardConfirmationOpen = false;
        continuation?.Invoke();
    }

    [RelayCommand]
    private void KeepEditing()
    {
        if (_disposed)
            return;

        _pendingNavigation = null;
        IsDiscardConfirmationOpen = false;
    }

    // Error handling

    private void SetError(
        string? resourceKey,
        string? details = null)
    {
        _errorKey = resourceKey;
        _errorDetails = details;

        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ErrorMessage));
    }

    // Localization notifications

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

        foreach (var status in StatusOptions)
            status.RefreshLocalization();

        string[] properties =
        [
            nameof(DateCulture),
            nameof(Heading),
            nameof(BackLabel),
            nameof(SaveLabel),
            nameof(CancelLabel),
            nameof(NumberLabel),
            nameof(TitleLabel),
            nameof(DateLabel),
            nameof(StatusLabel),
            nameof(ShortDescriptionLabel),
            nameof(MarkdownLabel),
            nameof(PlannedTimeLabel),
            nameof(PlannedTimePlaceholder),
            nameof(PlannedTimeHint),
            nameof(MultiplePlanningPeriodsHint),
            nameof(EditLabel),
            nameof(PreviewLabel),
            nameof(LoadingMessage),
            nameof(SavingMessage),
            nameof(SaveButtonText),
            nameof(SavedMessage),
            nameof(NotFoundMessage),
            nameof(DatePlaceholder),
            nameof(DiscardConfirmationTitle),
            nameof(DiscardConfirmationMessage),
            nameof(DiscardLabel),
            nameof(KeepEditingLabel),
            nameof(ErrorMessage),
            nameof(IsDateInputInvalid)
        ];

        foreach (var property in properties)
            OnPropertyChanged(property);
    }

    // Cleanup

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _pendingNavigation = null;

        _localization.PropertyChanged -= OnLocalizationChanged;

        _lifetime.Cancel();
        _lifetime.Dispose();

        TaskSaved = null;
    }
}

public sealed class TaskDetailsSavedEventArgs : EventArgs
{
    public TaskDetailsSavedEventArgs(TaskItem task)
    {
        Task = task
            ?? throw new ArgumentNullException(nameof(task));
    }

    public TaskItem Task { get; }
}