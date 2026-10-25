using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daycord.Core.Application.Models;
using Daycord.Core.Application.Services;
using Daycord.Core.Domain.Entities;
using Daycord.Desktop.Localization;
using Daycord.Desktop.Navigation;

namespace Daycord.Desktop.ViewModels;

public sealed partial class TaskDetailsViewModel : ObservableObject, IDisposable
{
    private static readonly string[] DateFormats =
    [
        "dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd", "yyyy/MM/dd",
        "MM/dd/yyyy", "M/d/yyyy", "dd/MM/yyyy", "d/M/yyyy"
    ];

    private readonly TaskService _tasks;
    private readonly ILocalizationService _localization;
    private readonly INavigationService _navigation;
    private readonly CancellationTokenSource _lifetime = new();

    private Guid? _taskId;
    private TaskDetailsEditorMode _editorMode = TaskDetailsEditorMode.Uninitialized;
    private bool _initializationStarted;
    private bool _populating;
    private bool _disposed;
    private Guid? _editingPeriodId;
    private Guid? _periodPendingDeletionId;
    private Action? _pendingNavigation;
    private string? _errorKey;
    private string? _errorDetails;

    private EditorSnapshot _baseline = new(
        string.Empty, string.Empty, string.Empty, string.Empty,
        string.Empty, Guid.Empty, string.Empty);

    private PeriodSnapshot _periodBaseline = new(string.Empty, string.Empty);

    [ObservableProperty] private string _taskNumber = string.Empty;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _dateInputText = string.Empty;
    [ObservableProperty] private string _shortDescription = string.Empty;
    [ObservableProperty] private string _descriptionMarkdown = string.Empty;
    [ObservableProperty] private string _plannedTimeInput = string.Empty;
    [ObservableProperty] private TaskDetailsStatusOption? _selectedStatus;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private bool _isTaskNotFound;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _isPreviewMode;
    [ObservableProperty] private bool _isDiscardConfirmationOpen;
    [ObservableProperty] private bool _isSaveSuccessful;
    [ObservableProperty] private bool _isPeriodEditorOpen;
    [ObservableProperty] private bool _isPeriodBusy;
    [ObservableProperty] private bool _isDeletePeriodConfirmationOpen;
    [ObservableProperty] private string _periodDateInputText = string.Empty;
    [ObservableProperty] private string _periodDurationInput = string.Empty;
    [ObservableProperty] private bool _isPeriodDraftDirty;

    public TaskDetailsViewModel(
        TaskService tasks,
        ILocalizationService localization,
        INavigationService navigation)
    {
        _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _localization.PropertyChanged += OnLocalizationChanged;
    }

    public event EventHandler<TaskDetailsSavedEventArgs>? TaskSaved;
    public ObservableCollection<TaskDetailsStatusOption> StatusOptions { get; } = new();
    public ObservableCollection<PlanningPeriodRowViewModel> PlanningPeriods { get; } = new();

    public TaskDetailsEditorMode EditorMode
    {
        get => _editorMode;
        private set
        {
            if (!SetProperty(ref _editorMode, value)) return;
            OnPropertyChanged(nameof(IsCreateMode));
            OnPropertyChanged(nameof(Heading));
            OnPropertyChanged(nameof(SaveLabel));
            OnPropertyChanged(nameof(SavingMessage));
            OnPropertyChanged(nameof(SaveButtonText));
            OnPropertyChanged(nameof(SavedMessage));
            NotifyEditState();
        }
    }

    public bool IsCreateMode => EditorMode == TaskDetailsEditorMode.Create;
    public Guid TaskId => _taskId ?? Guid.Empty;
    public CultureInfo DateCulture => CultureInfo.GetCultureInfo(_localization.CurrentLanguage);
    public bool IsEditMode => !IsPreviewMode;
    public bool HasUnsavedChanges => IsDirty || IsPeriodDraftDirty;
    public bool HasError => _errorKey is not null;
    public bool CanEdit => !_disposed && IsLoaded && !IsLoading && !IsSaving &&
                           !IsTaskNotFound && !IsPeriodBusy && !IsDiscardConfirmationOpen;
    public bool CanEditPlannedTime => CanEdit && IsCreateMode;
    public bool CanManagePlanningPeriods => CanEdit && !IsCreateMode &&
                                            !IsPeriodEditorOpen && !IsDeletePeriodConfirmationOpen;
    public bool CanSavePeriod => CanEdit && IsPeriodEditorOpen;
    public bool IsEditingExistingPeriod => _editingPeriodId.HasValue;
    public bool HasPlanningPeriods => PlanningPeriods.Count > 0;
    public bool HasNoPlanningPeriods => !HasPlanningPeriods;
    public bool HasMultiplePlanningPeriods => PlanningPeriods.Count > 1;
    public long TotalPlannedMinutes { get; private set; }
    public string TotalPlannedText => TotalPlannedMinutes == 0
        ? Text("PlanningNoPeriods") : PlannedDuration.Format(TotalPlannedMinutes);

    private string Text(string key) => _localization.GetString(key);
    public string Heading => Text(IsCreateMode ? "TaskDetailsCreateHeading" : "TaskDetailsHeading");
    public string BackLabel => Text("TaskDetailsBack");
    public string SaveLabel => Text(IsCreateMode ? "TaskDetailsCreateAction" : "TaskDetailsSave");
    public string CancelLabel => Text("TaskDetailsCancel");
    public string NumberLabel => Text("TaskDetailsNumber");
    public string TitleLabel => Text("TaskDetailsTitle");
    public string DateLabel => Text("TaskDetailsDate");
    public string StatusLabel => Text("TaskDetailsStatus");
    public string ShortDescriptionLabel => Text("TaskDetailsShortDescription");
    public string MarkdownLabel => Text("TaskDetailsMarkdown");
    public string PlannedTimeLabel => Text("QuickTaskTimeLabel");
    public string PlannedTimePlaceholder => Text("QuickTaskTimePlaceholder");
    public string PlannedTimeHint => Text("QuickTaskInvalidTime");
    public string EditLabel => Text("TaskDetailsEdit");
    public string PreviewLabel => Text("TaskDetailsPreview");
    public string LoadingMessage => Text("TaskDetailsLoading");
    public string SavingMessage => Text(IsCreateMode ? "TaskDetailsCreating" : "TaskDetailsSaving");
    public string SaveButtonText => IsSaving ? SavingMessage : SaveLabel;
    public string SavedMessage => Text(IsCreateMode ? "TaskDetailsCreateSuccessful" : "TaskDetailsSaveSuccessful");
    public string NotFoundMessage => Text("TaskDetailsTaskNotFound");
    public string DatePlaceholder => Text("TaskDetailsDatePlaceholder");
    public string DiscardConfirmationTitle => Text("TaskDetailsDiscardTitle");
    public string DiscardConfirmationMessage => Text("TaskDetailsDiscardMessage");
    public string DiscardLabel => Text("TaskDetailsDiscard");
    public string KeepEditingLabel => Text("TaskDetailsKeepEditing");
    public string PeriodsLabel => Text("PlanningPeriods");
    public string AddPeriodLabel => Text("PlanningAddPeriod");
    public string TotalPlannedLabel => Text("PlanningTotal");
    public string PeriodEditorHeading => Text(IsEditingExistingPeriod ? "PlanningEditPeriod" : "PlanningAddPeriod");
    public string PeriodDateLabel => Text("PlanningDate");
    public string PeriodDurationLabel => Text("PlanningDuration");
    public string SavePeriodLabel => Text("PlanningSave");
    public string DeletePeriodTitle => Text("PlanningDeleteTitle");
    public string DeletePeriodMessage => Text("PlanningDeleteMessage");
    public string DeletePeriodLabel => Text("PlanningDeletePeriod");

    public string ErrorMessage
    {
        get
        {
            if (_errorKey is null) return string.Empty;
            var message = Text(_errorKey);
            return _errorDetails is null ? message :
                string.Format(DateCulture, message, _errorDetails);
        }
    }

    public bool IsDateInputInvalid => !TryGetTaskDate(out _);
    public bool IsPeriodDateInputInvalid => !TryGetPeriodDate(out _);
    public DateTime CalendarAnchorDate => TryGetTaskDate(out var date) && date.HasValue
        ? date.Value.ToDateTime(TimeOnly.MinValue) : DateTime.Today;
    public DateTime PeriodCalendarAnchorDate => TryGetPeriodDate(out var date)
        ? date.ToDateTime(TimeOnly.MinValue) : DateTime.Today;

    public async Task InitializeAsync(Guid taskId)
    {
        if (_disposed) return;
        if (taskId == Guid.Empty) throw new ArgumentException("Task ID must not be empty.", nameof(taskId));
        if (_initializationStarted)
        {
            if (EditorMode == TaskDetailsEditorMode.Edit && _taskId == taskId) return;
            throw new InvalidOperationException("The editor has already been initialized.");
        }

        BeginInitialization(TaskDetailsEditorMode.Edit, taskId);
        var token = _lifetime.Token;
        try
        {
            var task = await _tasks.GetByIdAsync(taskId, token);
            if (ShouldStop(token)) return;
            if (task is null)
            {
                IsTaskNotFound = true;
                return;
            }
            await LoadStatusesAsync(token);
            if (ShouldStop(token)) return;
            if (!StatusOptions.Any(s => s.Id == task.StatusId))
                throw new InvalidOperationException("The task's status was not found.");
            var periods = await _tasks.ListPlanningPeriodsAsync(taskId, token);
            if (ShouldStop(token)) return;
            PopulatePlanningPeriods(periods);
            PopulateFromTask(task, PlannedDuration.Format(TotalPlannedMinutes));
            IsLoaded = true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { if (!_disposed) SetError("TaskDetailsLoadFailed", ex.Message); }
        finally { if (!_disposed) IsLoading = false; }
    }

    public async Task InitializeForCreateAsync()
    {
        if (_disposed) return;
        if (_initializationStarted)
        {
            if (EditorMode == TaskDetailsEditorMode.Create) return;
            throw new InvalidOperationException("The editor has already been initialized.");
        }

        BeginInitialization(TaskDetailsEditorMode.Create, null);
        var token = _lifetime.Token;
        try
        {
            await LoadStatusesAsync(token);
            if (ShouldStop(token)) return;
            if (!StatusOptions.Any(s => s.Id == TaskStatusDefinition.OpenId))
                throw new InvalidOperationException("System Open status was not found.");
            PopulatePlanningPeriods([]);
            PopulateDraft(null, string.Empty, DateOnly.FromDateTime(DateTime.Today),
                null, null, TaskStatusDefinition.OpenId, string.Empty);
            IsLoaded = true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { if (!_disposed) SetError("TaskDetailsLoadFailed", ex.Message); }
        finally { if (!_disposed) IsLoading = false; }
    }

    private void BeginInitialization(TaskDetailsEditorMode mode, Guid? id)
    {
        _initializationStarted = true;
        _taskId = id;
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
        if (ShouldStop(token)) return;
        StatusOptions.Clear();
        foreach (var status in statuses)
            StatusOptions.Add(new TaskDetailsStatusOption(status, _localization));
    }

    private bool ShouldStop(CancellationToken token) => _disposed || token.IsCancellationRequested;

    private void PopulateFromTask(TaskItem task, string plannedTime) =>
        PopulateDraft(task.TaskNumber, task.Title, task.TaskDate, task.ShortDescription,
            task.DescriptionMarkdown, task.StatusId, plannedTime);

    private void PopulateDraft(string? number, string title, DateOnly? date,
        string? description, string? markdown, Guid statusId, string plannedTime)
    {
        _populating = true;
        try
        {
            TaskNumber = number ?? string.Empty;
            Title = title;
            DateInputText = date?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? string.Empty;
            ShortDescription = description ?? string.Empty;
            DescriptionMarkdown = markdown ?? string.Empty;
            PlannedTimeInput = plannedTime;
            SelectedStatus = StatusOptions.FirstOrDefault(s => s.Id == statusId);
            CaptureSavedState();
            IsDirty = false;
        }
        finally { _populating = false; }
        OnPropertyChanged(nameof(IsDateInputInvalid));
        OnPropertyChanged(nameof(CalendarAnchorDate));
    }

    private readonly record struct EditorSnapshot(string Number, string Title, string Date,
        string ShortDescription, string Markdown, Guid StatusId, string PlannedTime);
    private readonly record struct PeriodSnapshot(string Date, string Duration);

    private EditorSnapshot CurrentSnapshot() => new(
        TaskNumber ?? string.Empty, Title ?? string.Empty, DateInputText ?? string.Empty,
        ShortDescription ?? string.Empty, DescriptionMarkdown ?? string.Empty,
        SelectedStatus?.Id ?? Guid.Empty,
        IsCreateMode ? PlannedTimeInput ?? string.Empty : _baseline.PlannedTime);

    private PeriodSnapshot CurrentPeriodSnapshot() => new(
        PeriodDateInputText ?? string.Empty, PeriodDurationInput ?? string.Empty);

    private void CaptureSavedState() => _baseline = new EditorSnapshot(
        TaskNumber ?? string.Empty, Title ?? string.Empty, DateInputText ?? string.Empty,
        ShortDescription ?? string.Empty, DescriptionMarkdown ?? string.Empty,
        SelectedStatus?.Id ?? Guid.Empty, PlannedTimeInput ?? string.Empty);

    private void OnEdited()
    {
        if (_disposed || _populating || !IsLoaded) return;
        IsSaveSuccessful = false;
        SetError(null);
        IsDirty = CurrentSnapshot() != _baseline;
    }

    partial void OnTaskNumberChanged(string value) => OnEdited();
    partial void OnTitleChanged(string value) => OnEdited();
    partial void OnShortDescriptionChanged(string value) => OnEdited();
    partial void OnDescriptionMarkdownChanged(string value) => OnEdited();
    partial void OnPlannedTimeInputChanged(string value) => OnEdited();
    partial void OnSelectedStatusChanged(TaskDetailsStatusOption? value) => OnEdited();
    partial void OnDateInputTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsDateInputInvalid));
        OnPropertyChanged(nameof(CalendarAnchorDate));
        OnEdited();
    }
    partial void OnIsDirtyChanged(bool value)
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        SaveCommand.NotifyCanExecuteChanged();
    }
    partial void OnIsLoadingChanged(bool value) => NotifyEditState();
    partial void OnIsLoadedChanged(bool value) => NotifyEditState();
    partial void OnIsTaskNotFoundChanged(bool value) => NotifyEditState();
    partial void OnIsDiscardConfirmationOpenChanged(bool value) => NotifyEditState();
    partial void OnIsPeriodEditorOpenChanged(bool value) => NotifyEditState();
    partial void OnIsDeletePeriodConfirmationOpenChanged(bool value) => NotifyEditState();
    partial void OnIsPeriodBusyChanged(bool value) => NotifyEditState();
    partial void OnIsPreviewModeChanged(bool value) => OnPropertyChanged(nameof(IsEditMode));
    partial void OnIsSavingChanged(bool value)
    {
        NotifyEditState();
        OnPropertyChanged(nameof(SaveButtonText));
    }
    partial void OnIsPeriodDraftDirtyChanged(bool value) =>
        OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnPeriodDateInputTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsPeriodDateInputInvalid));
        OnPropertyChanged(nameof(PeriodCalendarAnchorDate));
        OnPeriodDraftEdited();
    }
    partial void OnPeriodDurationInputChanged(string value) => OnPeriodDraftEdited();

    private void NotifyEditState()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanEditPlannedTime));
        OnPropertyChanged(nameof(CanManagePlanningPeriods));
        OnPropertyChanged(nameof(CanSavePeriod));
        SaveCommand.NotifyCanExecuteChanged();
        AddPeriodCommand.NotifyCanExecuteChanged();
        SavePeriodCommand.NotifyCanExecuteChanged();
        ConfirmDeletePeriodCommand.NotifyCanExecuteChanged();
    }

    private bool TryGetTaskDate(out DateOnly? date)
    {
        var value = DateInputText?.Trim() ?? string.Empty;
        date = null;
        if (value.Length == 0) return true;
        if (TryParseDate(value, out var parsed))
        {
            date = parsed;
            return true;
        }
        return false;
    }

    private bool TryGetPeriodDate(out DateOnly date) =>
        TryParseDate(PeriodDateInputText?.Trim(), out date);

    private bool TryParseDate(string? value, out DateOnly result)
    {
        result = default;
        return !string.IsNullOrWhiteSpace(value) &&
               (DateOnly.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out result) ||
                DateOnly.TryParse(value, DateCulture, DateTimeStyles.None, out result));
    }

    public void SelectCalendarDate(DateTime date)
    {
        if (CanEdit)
            DateInputText = date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    private void ClearDate()
    {
        if (CanEdit) DateInputText = string.Empty;
    }

    public void SelectPeriodCalendarDate(DateTime date)
    {
        if (CanSavePeriod)
            PeriodDateInputText = date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    }

    private void PopulatePlanningPeriods(IReadOnlyList<PlanningPeriod> periods)
    {
        long total = 0;
        foreach (var period in periods)
            total = checked(total + period.PlannedMinutes);

        PlanningPeriods.Clear();
        foreach (var period in periods.OrderBy(p => p.PlannedDate).ThenBy(p => p.Id))
            PlanningPeriods.Add(new PlanningPeriodRowViewModel(period, EditPeriod, AskDeletePeriod));
        TotalPlannedMinutes = total;
        OnPropertyChanged(nameof(TotalPlannedMinutes));
        OnPropertyChanged(nameof(TotalPlannedText));
        OnPropertyChanged(nameof(HasPlanningPeriods));
        OnPropertyChanged(nameof(HasNoPlanningPeriods));
        OnPropertyChanged(nameof(HasMultiplePlanningPeriods));
    }

    private async Task RefreshPlanningPeriodsAsync(CancellationToken token)
    {
        var periods = await _tasks.ListPlanningPeriodsAsync(TaskId, token);
        if (ShouldStop(token)) return;
        PopulatePlanningPeriods(periods);
        _populating = true;
        try { PlannedTimeInput = PlannedDuration.Format(TotalPlannedMinutes); }
        finally { _populating = false; }
    }

    private bool CanAddPeriod() => CanManagePlanningPeriods;

    [RelayCommand(CanExecute = nameof(CanAddPeriod))]
    private void AddPeriod()
    {
        if (!CanAddPeriod()) return;
        _editingPeriodId = null;
        PeriodDateInputText = TryGetTaskDate(out var taskDate) && taskDate.HasValue
            ? taskDate.Value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
            : DateOnly.FromDateTime(DateTime.Today).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        PeriodDurationInput = string.Empty;
        OpenPeriodEditor();
    }

    private void EditPeriod(PlanningPeriodRowViewModel period)
    {
        if (!CanManagePlanningPeriods || period.TaskId != TaskId) return;
        _editingPeriodId = period.Id;
        PeriodDateInputText = period.DateText;
        PeriodDurationInput = PlannedDuration.Format(period.PlannedMinutes);
        OpenPeriodEditor();
    }

    private void OpenPeriodEditor()
    {
        _periodBaseline = CurrentPeriodSnapshot();
        IsPeriodDraftDirty = false;
        SetError(null);
        IsPeriodEditorOpen = true;
        OnPropertyChanged(nameof(IsEditingExistingPeriod));
        OnPropertyChanged(nameof(PeriodEditorHeading));
    }

    private void OnPeriodDraftEdited()
    {
        if (!IsPeriodEditorOpen || _disposed) return;
        IsPeriodDraftDirty = CurrentPeriodSnapshot() != _periodBaseline;
        SetError(null);
    }

    private bool CanSavePeriodCommand() => CanSavePeriod;

    [RelayCommand(CanExecute = nameof(CanSavePeriodCommand))]
    private async Task SavePeriodAsync()
    {
        if (!CanSavePeriod) return;
        if (!TryGetPeriodDate(out var date) ||
            !PlannedDuration.TryParseRequired(PeriodDurationInput, out var minutes))
        {
            SetError("PlanningValidationError");
            return;
        }

        IsPeriodBusy = true;
        SetError(null);
        var token = _lifetime.Token;
        var saved = false;
        try
        {
            if (_editingPeriodId is Guid periodId)
                await _tasks.UpdatePlanningPeriodAsync(TaskId, periodId, date, minutes, token);
            else
                await _tasks.AddPlanningPeriodAsync(TaskId, date, minutes, token);
            saved = true;
            await RefreshPlanningPeriodsAsync(token);
            if (!ShouldStop(token)) ClosePeriodEditor();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                // A successful INSERT/UPDATE is not retried if refreshing fails.
                if (saved) ClosePeriodEditor();
                SetError(saved ? "TaskDetailsLoadFailed" : "PlanningSaveFailed", ex.Message);
            }
        }
        finally { if (!_disposed) IsPeriodBusy = false; }
    }

    [RelayCommand]
    private void CancelPeriod()
    {
        if (!_disposed && !IsPeriodBusy && IsPeriodEditorOpen)
            ClosePeriodEditor(); // Explicit Cancel discards only this period draft.
    }

    private void ClosePeriodEditor()
    {
        IsPeriodEditorOpen = false;
        _editingPeriodId = null;
        IsPeriodDraftDirty = false;
        OnPropertyChanged(nameof(IsEditingExistingPeriod));
        OnPropertyChanged(nameof(PeriodEditorHeading));
    }

    private void AskDeletePeriod(PlanningPeriodRowViewModel period)
    {
        if (!CanManagePlanningPeriods || period.TaskId != TaskId) return;
        _periodPendingDeletionId = period.Id;
        IsDeletePeriodConfirmationOpen = true;
        NotifyEditState();
    }

    [RelayCommand]
    private void CancelDeletePeriod()
    {
        if (_disposed || IsPeriodBusy) return;
        _periodPendingDeletionId = null;
        IsDeletePeriodConfirmationOpen = false;
        NotifyEditState();
    }

    private bool CanConfirmDeletePeriod() => CanEdit && IsDeletePeriodConfirmationOpen &&
                                               _periodPendingDeletionId.HasValue;

    [RelayCommand(CanExecute = nameof(CanConfirmDeletePeriod))]
    private async Task ConfirmDeletePeriodAsync()
    {
        if (!CanConfirmDeletePeriod()) return;
        var id = _periodPendingDeletionId!.Value;
        IsPeriodBusy = true;
        SetError(null);
        var token = _lifetime.Token;
        var deleted = false;
        try
        {
            await _tasks.DeletePlanningPeriodAsync(TaskId, id, token);
            deleted = true;
            _periodPendingDeletionId = null;
            IsDeletePeriodConfirmationOpen = false;
            await RefreshPlanningPeriodsAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_disposed)
                SetError(deleted ? "TaskDetailsLoadFailed" : "PlanningDeleteFailed", ex.Message);
        }
        finally { if (!_disposed) IsPeriodBusy = false; }
    }

    private bool CanSave() => CanEdit && IsDirty && !IsPeriodEditorOpen &&
                              !IsDeletePeriodConfirmationOpen;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanSave()) return;
        SetError(null);
        IsSaveSuccessful = false;
        if (string.IsNullOrWhiteSpace(Title)) { SetError("TaskDetailsTitleRequired"); return; }
        if (!TryGetTaskDate(out var date)) { SetError("TaskDetailsDateInvalid"); return; }
        if (SelectedStatus is null) { SetError("TaskDetailsStatusRequired"); return; }

        int? minutes = null;
        if (IsCreateMode)
        {
            if (!PlannedDuration.TryParse(PlannedTimeInput, out minutes))
            { SetError("QuickTaskInvalidTime"); return; }
            if (minutes.HasValue && !date.HasValue)
            { SetError("QuickTaskDateRequired"); return; }
        }

        var token = _lifetime.Token;
        IsSaving = true;
        TaskItem? saved = null;
        try
        {
            if (IsCreateMode)
            {
                var request = new CreateTaskRequest(
                    TaskDate: date, Title: Title, ShortDescription: ShortDescription,
                    DescriptionMarkdown: DescriptionMarkdown, StatusId: SelectedStatus.Id,
                    TaskNumber: TaskNumber, PlannedMinutes: minutes);
                saved = await _tasks.CreateAsync(request, token);
            }
            else
            {
                var request = new UpdateTaskRequest(
                    Id: TaskId, TaskDate: date, Title: Title,
                    ShortDescription: ShortDescription,
                    DescriptionMarkdown: DescriptionMarkdown,
                    StatusId: SelectedStatus.Id, TaskNumber: TaskNumber,
                    UpdateTaskNumber: true);
                // Main Save never writes PlanningPeriods.
                saved = await _tasks.UpdateAsync(request, token);
            }

            if (ShouldStop(token)) return;
            _taskId = saved.Id;
            OnPropertyChanged(nameof(TaskId));
            PopulateFromTask(saved, IsCreateMode
                ? PlannedDuration.Format(minutes ?? 0)
                : PlannedDuration.Format(TotalPlannedMinutes));
            IsSaveSuccessful = true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (KeyNotFoundException)
        {
            if (!_disposed)
            {
                IsTaskNotFound = true;
                SetError("TaskDetailsTaskNotFound");
            }
        }
        catch (ArgumentException ex)
        {
            if (!_disposed) SetError("TaskDetailsValidationError", ex.Message);
        }
        catch (Exception ex)
        {
            if (!_disposed)
                SetError(IsCreateMode ? "TaskDetailsCreateFailed" : "TaskDetailsSaveFailed", ex.Message);
        }
        finally { if (!_disposed) IsSaving = false; }

        if (saved is not null && !_disposed)
        {
            TaskSaved?.Invoke(this, new TaskDetailsSavedEventArgs(saved));
            if (!_disposed) _navigation.CloseTask();
        }
    }

    [RelayCommand] private void ShowEdit()
    {
        if (!IsLoading && !IsSaving) IsPreviewMode = false;
    }

    [RelayCommand] private void ShowPreview()
    {
        if (!IsLoading && !IsSaving) IsPreviewMode = true;
    }

    [RelayCommand] private void Back() => RequestLeave(_navigation.CloseTask);
    [RelayCommand] private void Cancel() => RequestLeave(_navigation.CloseTask);

    public void RequestLeave(Action continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        if (_disposed || IsSaving || IsPeriodBusy) return;
        if (!HasUnsavedChanges)
        {
            continuation();
            return;
        }
        _pendingNavigation = continuation;
        IsDiscardConfirmationOpen = true;
    }

    [RelayCommand] private void ConfirmDiscard()
    {
        if (_disposed || !IsDiscardConfirmationOpen) return;
        var continuation = _pendingNavigation;
        _pendingNavigation = null;
        IsDiscardConfirmationOpen = false;
        continuation?.Invoke();
    }

    [RelayCommand] private void KeepEditing()
    {
        if (_disposed) return;
        _pendingNavigation = null;
        IsDiscardConfirmationOpen = false;
    }

    private void SetError(string? key, string? details = null)
    {
        _errorKey = key;
        _errorDetails = details;
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ErrorMessage));
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed || (!string.IsNullOrEmpty(e.PropertyName) &&
                          e.PropertyName != nameof(ILocalizationService.CurrentLanguage)))
            return;
        foreach (var status in StatusOptions) status.RefreshLocalization();
        string[] properties =
        [
            nameof(DateCulture), nameof(Heading), nameof(BackLabel),
            nameof(SaveLabel), nameof(CancelLabel), nameof(NumberLabel),
            nameof(TitleLabel), nameof(DateLabel), nameof(StatusLabel),
            nameof(ShortDescriptionLabel), nameof(MarkdownLabel),
            nameof(PlannedTimeLabel), nameof(PlannedTimePlaceholder),
            nameof(PlannedTimeHint), nameof(PeriodsLabel), nameof(AddPeriodLabel),
            nameof(TotalPlannedLabel), nameof(TotalPlannedText),
            nameof(PeriodEditorHeading), nameof(PeriodDateLabel),
            nameof(PeriodDurationLabel), nameof(SavePeriodLabel),
            nameof(DeletePeriodTitle), nameof(DeletePeriodMessage),
            nameof(DeletePeriodLabel), nameof(EditLabel), nameof(PreviewLabel),
            nameof(LoadingMessage), nameof(SavingMessage),
            nameof(SaveButtonText), nameof(SavedMessage),
            nameof(NotFoundMessage), nameof(DatePlaceholder),
            nameof(DiscardConfirmationTitle), nameof(DiscardConfirmationMessage),
            nameof(DiscardLabel), nameof(KeepEditingLabel),
            nameof(ErrorMessage), nameof(IsDateInputInvalid),
            nameof(IsPeriodDateInputInvalid)
        ];
        foreach (var property in properties) OnPropertyChanged(property);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pendingNavigation = null;
        _periodPendingDeletionId = null;
        _localization.PropertyChanged -= OnLocalizationChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
        TaskSaved = null;
    }
}

public sealed class TaskDetailsSavedEventArgs : EventArgs
{
    public TaskDetailsSavedEventArgs(TaskItem task) =>
        Task = task ?? throw new ArgumentNullException(nameof(task));
    public TaskItem Task { get; }
}