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
using Daycord.Desktop.Settings;

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
    private readonly IAppSettingsService _appSettings;

    // Freeze the interpretation of "1d" for each active editor.
    // Changing preferences must never reinterpret an unsaved draft.
    private int _createInputMinutesPerDay = AppSettings.DefaultMinutesPerDay;
    private int _periodInputMinutesPerDay = AppSettings.DefaultMinutesPerDay;
    private int _workEntryInputMinutesPerDay = AppSettings.DefaultMinutesPerDay;
    private readonly CancellationTokenSource _lifetime = new();

    private Guid? _taskId;
    private TaskDetailsEditorMode _editorMode =
        TaskDetailsEditorMode.Uninitialized;

    private bool _initializationStarted;
    private bool _populating;
    private bool _disposed;
    private bool _switchingWorkEntryContext;

    private Guid? _editingPeriodId;
    private Guid? _periodPendingDeletionId;
    private Guid? _editingWorkEntryId;
    private Guid? _workEntryPendingDeletionId;

    private Action? _pendingNavigation;
    private string? _errorKey;
    private string? _errorDetails;

    // DC-010: Unsaved drafts are independent for each WorkEntry.
    private readonly Dictionary<Guid, WorkEntrySnapshot> _workEntryDrafts = new();
    private readonly Dictionary<Guid, int> _workEntryDraftDayLengths = new();

    // Separate draft for creating a new WorkEntry.
    private WorkEntrySnapshot? _newWorkEntryDraft;
    private int? _newWorkEntryDraftDayLength;
    private WorkEntrySnapshot _newWorkEntryBaseline = new(
        string.Empty, string.Empty, string.Empty, string.Empty);

    private EditorSnapshot _baseline = new(
        string.Empty, string.Empty, string.Empty, string.Empty,
        string.Empty, Guid.Empty, string.Empty);

    private PeriodSnapshot _periodBaseline = new(
        string.Empty, string.Empty);

    private WorkEntrySnapshot _workEntryBaseline = new(
        string.Empty, string.Empty, string.Empty, string.Empty);

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

    [ObservableProperty] private bool _isWorkEntryEditorOpen;
    [ObservableProperty] private bool _isWorkEntryBusy;
    [ObservableProperty] private bool _isDeleteWorkEntryConfirmationOpen;
    [ObservableProperty] private bool _isWorkEntryDraftDirty;
    [ObservableProperty] private string _workDateInputText = string.Empty;
    [ObservableProperty] private string _workDurationInput = string.Empty;
    [ObservableProperty] private string _workDescriptionInput = string.Empty;
    [ObservableProperty] private string _workNoteInput = string.Empty;

    public TaskDetailsViewModel(
        TaskService tasks,
        ILocalizationService localization,
        INavigationService navigation,
        IAppSettingsService appSettings)
    {
        _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        _localization = localization
            ?? throw new ArgumentNullException(nameof(localization));
        _navigation = navigation
            ?? throw new ArgumentNullException(nameof(navigation));
        _appSettings = appSettings
            ?? throw new ArgumentNullException(nameof(appSettings));

        _localization.PropertyChanged += OnLocalizationChanged;
    }

    public event EventHandler<TaskDetailsSavedEventArgs>? TaskSaved;

    public ObservableCollection<TaskDetailsStatusOption> StatusOptions { get; } = new();
    public ObservableCollection<PlanningPeriodRowViewModel> PlanningPeriods { get; } = new();
    public ObservableCollection<WorkEntryRowViewModel> WorkEntries { get; } = new();

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

            NotifyEditState();
        }
    }

    public bool IsCreateMode =>
        EditorMode == TaskDetailsEditorMode.Create;

    public Guid TaskId => _taskId ?? Guid.Empty;

    public CultureInfo DateCulture =>
        CultureInfo.GetCultureInfo(_localization.CurrentLanguage);

    public bool IsEditMode => !IsPreviewMode;

    public bool HasPendingWorkEntryDrafts =>
        IsWorkEntryDraftDirty ||
        _workEntryDrafts.Count > 0 ||
        _newWorkEntryDraft.HasValue;

    public bool HasUnsavedChanges =>
        IsDirty ||
        IsPeriodDraftDirty ||
        HasPendingWorkEntryDrafts;

    public bool HasError => _errorKey is not null;

    public bool CanEdit =>
        !_disposed &&
        IsLoaded &&
        !IsLoading &&
        !IsSaving &&
        !IsTaskNotFound &&
        !IsPeriodBusy &&
        !IsWorkEntryBusy &&
        !IsDiscardConfirmationOpen;

    public bool CanEditPlannedTime => CanEdit && IsCreateMode;

    public bool CanManagePlanningPeriods =>
        CanEdit &&
        !IsCreateMode &&
        !IsPeriodEditorOpen &&
        !IsDeletePeriodConfirmationOpen &&
        !IsWorkEntryEditorOpen &&
        !IsDeleteWorkEntryConfirmationOpen;

    public bool CanSavePeriod =>
        CanEdit && IsPeriodEditorOpen;

    public bool IsEditingExistingPeriod => _editingPeriodId.HasValue;
    public bool HasPlanningPeriods => PlanningPeriods.Count > 0;
    public bool HasNoPlanningPeriods => !HasPlanningPeriods;
    public bool HasMultiplePlanningPeriods => PlanningPeriods.Count > 1;

    public bool CanManageWorkEntries =>
        CanEdit &&
        !IsCreateMode &&
        !IsPeriodEditorOpen &&
        !IsDeletePeriodConfirmationOpen &&
        !IsWorkEntryEditorOpen &&
        !IsDeleteWorkEntryConfirmationOpen;

    // Switching to another existing entry is allowed while the
    // WorkEntry editor is open. Save/delete remain protected.
    private bool CanSwitchWorkEntry =>
        CanEdit &&
        !IsCreateMode &&
        !IsPeriodEditorOpen &&
        !IsDeletePeriodConfirmationOpen &&
        !IsDeleteWorkEntryConfirmationOpen;

    public bool CanSaveWorkEntry =>
        CanEdit && IsWorkEntryEditorOpen;

    public bool IsEditingExistingWorkEntry =>
        _editingWorkEntryId.HasValue;

    public bool HasWorkEntries => WorkEntries.Count > 0;
    public bool HasNoWorkEntries => !HasWorkEntries;

    public long TotalActualMinutes { get; private set; }

    public string TotalActualText =>
        TotalActualMinutes == 0
            ? "0h"
            : PlannedDuration.Format(TotalActualMinutes);

    public long TotalPlannedMinutes { get; private set; }

    public string TotalPlannedText =>
        TotalPlannedMinutes == 0
            ? Text("PlanningNoPeriods")
            : PlannedDuration.Format(TotalPlannedMinutes);

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

    public string WorkLogLabel => Text("WorkLogTitle");
    public string WorkEntriesLabel => Text("WorkEntriesLabel");

    public string AddWorkEntryLabel =>
        Text("WorkAddEntry") +
        (_newWorkEntryDraft.HasValue ? " ✎" : string.Empty);

    public string WorkEditorHeading => Text(IsEditingExistingWorkEntry ? "WorkEditEntry" : "WorkAddEntry");
    public string WorkDateLabel => Text("WorkDate");
    public string WorkDurationLabel => Text("WorkDuration");
    public string WorkDescriptionLabel => Text("WorkDescription");
    public string WorkNoteLabel => Text("WorkNote");
    public string TotalActualLabel => Text("WorkTotalActual");
    public string NoWorkEntriesLabel => Text("WorkNoEntries");
    public string SaveWorkEntryLabel => Text("WorkSaveEntry");
    public string WorkEntrySaveHint => Text("WorkSaveIndependentHint");
    public string DeleteWorkEntryTitle => Text("WorkDeleteTitle");
    public string DeleteWorkEntryMessage => Text("WorkDeleteMessage");
    public string DeleteWorkEntryLabel => Text("WorkDeleteEntry");

    public string ErrorMessage
    {
        get
        {
            if (_errorKey is null)
                return string.Empty;

            var message = Text(_errorKey);

            return _errorDetails is null
                ? message
                : string.Format(DateCulture, message, _errorDetails);
        }
    }

    public bool IsDateInputInvalid => !TryGetTaskDate(out _);
    public bool IsPeriodDateInputInvalid => !TryGetPeriodDate(out _);
    public bool IsWorkDateInputInvalid => !TryGetWorkDate(out _);

    public DateTime CalendarAnchorDate =>
        TryGetTaskDate(out var date) && date.HasValue
            ? date.Value.ToDateTime(TimeOnly.MinValue)
            : DateTime.Today;

    public DateTime PeriodCalendarAnchorDate =>
        TryGetPeriodDate(out var date)
            ? date.ToDateTime(TimeOnly.MinValue)
            : DateTime.Today;

    public DateTime WorkCalendarAnchorDate =>
        TryGetWorkDate(out var date)
            ? date.ToDateTime(TimeOnly.MinValue)
            : DateTime.Today;

    public async Task InitializeAsync(Guid taskId)
    {
        if (_disposed)
            return;

        if (taskId == Guid.Empty)
            throw new ArgumentException(
                "Task ID must not be empty.",
                nameof(taskId));

        if (_initializationStarted)
        {
            if (EditorMode == TaskDetailsEditorMode.Edit &&
                _taskId == taskId)
                return;

            throw new InvalidOperationException(
                "The editor has already been initialized.");
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
                throw new InvalidOperationException(
                    "The task's status was not found.");

            var periods = await _tasks.ListPlanningPeriodsAsync(
                taskId, token);

            if (ShouldStop(token)) return;

            PopulatePlanningPeriods(periods);

            var entries = await _tasks.ListWorkEntriesAsync(
                taskId, token);

            if (ShouldStop(token)) return;

            PopulateWorkEntries(entries);

            PopulateFromTask(
                task,
                PlannedDuration.Format(TotalPlannedMinutes));

            IsLoaded = true;
        }
        catch (OperationCanceledException)
            when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_disposed)
                SetError("TaskDetailsLoadFailed", ex.Message);
        }
        finally
        {
            if (!_disposed)
                IsLoading = false;
        }
    }

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
            if (ShouldStop(token)) return;

            if (!StatusOptions.Any(
                s => s.Id == TaskStatusDefinition.OpenId))
            {
                throw new InvalidOperationException(
                    "System Open status was not found.");
            }

            PopulatePlanningPeriods([]);
            PopulateWorkEntries([]);

            PopulateDraft(
                null,
                string.Empty,
                DateOnly.FromDateTime(DateTime.Today),
                null,
                null,
                TaskStatusDefinition.OpenId,
                string.Empty);

            IsLoaded = true;
        }
        catch (OperationCanceledException)
            when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_disposed)
                SetError("TaskDetailsLoadFailed", ex.Message);
        }
        finally
        {
            if (!_disposed)
                IsLoading = false;
        }
    }

    private void BeginInitialization(
        TaskDetailsEditorMode mode,
        Guid? id)
    {
        _initializationStarted = true;
        _taskId = id;
        if (mode == TaskDetailsEditorMode.Create)
        {
            _createInputMinutesPerDay = _appSettings.Current.MinutesPerDay;
        }
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
        {
            StatusOptions.Add(
                new TaskDetailsStatusOption(status, _localization));
        }
    }

    private bool ShouldStop(CancellationToken token) =>
        _disposed || token.IsCancellationRequested;

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
        string? number,
        string title,
        DateOnly? date,
        string? description,
        string? markdown,
        Guid statusId,
        string plannedTime)
    {
        _populating = true;

        try
        {
            TaskNumber = number ?? string.Empty;
            Title = title;

            DateInputText = date?.ToString(
                "dd.MM.yyyy",
                CultureInfo.InvariantCulture) ?? string.Empty;

            ShortDescription = description ?? string.Empty;
            DescriptionMarkdown = markdown ?? string.Empty;
            PlannedTimeInput = plannedTime;

            SelectedStatus = StatusOptions.FirstOrDefault(
                s => s.Id == statusId);

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

    private readonly record struct EditorSnapshot(
        string Number,
        string Title,
        string Date,
        string ShortDescription,
        string Markdown,
        Guid StatusId,
        string PlannedTime);

    private readonly record struct PeriodSnapshot(
        string Date,
        string Duration);

    private readonly record struct WorkEntrySnapshot(
        string Date,
        string Duration,
        string Description,
        string Note);

    private EditorSnapshot CurrentSnapshot() => new(
        TaskNumber ?? string.Empty,
        Title ?? string.Empty,
        DateInputText ?? string.Empty,
        ShortDescription ?? string.Empty,
        DescriptionMarkdown ?? string.Empty,
        SelectedStatus?.Id ?? Guid.Empty,
        IsCreateMode
            ? PlannedTimeInput ?? string.Empty
            : _baseline.PlannedTime);

    private PeriodSnapshot CurrentPeriodSnapshot() => new(
        PeriodDateInputText ?? string.Empty,
        PeriodDurationInput ?? string.Empty);

    private WorkEntrySnapshot CurrentWorkEntrySnapshot() => new(
        WorkDateInputText ?? string.Empty,
        WorkDurationInput ?? string.Empty,
        WorkDescriptionInput ?? string.Empty,
        WorkNoteInput ?? string.Empty);

    private static WorkEntrySnapshot SnapshotFromEntry(
        WorkEntryRowViewModel entry) => new(
            entry.WorkDate.ToString(
                "dd.MM.yyyy",
                CultureInfo.InvariantCulture),
            PlannedDuration.Format(entry.ActualMinutes),
            entry.WorkDescription ?? string.Empty,
            entry.Note ?? string.Empty);

    private void CaptureSavedState()
    {
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

    partial void OnTaskNumberChanged(string value) => OnEdited();
    partial void OnTitleChanged(string value) => OnEdited();
    partial void OnShortDescriptionChanged(string value) => OnEdited();
    partial void OnDescriptionMarkdownChanged(string value) => OnEdited();
    partial void OnPlannedTimeInputChanged(string value) => OnEdited();

    partial void OnSelectedStatusChanged(
        TaskDetailsStatusOption? value) => OnEdited();

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
    partial void OnIsWorkEntryEditorOpenChanged(bool value) => NotifyEditState();
    partial void OnIsDeleteWorkEntryConfirmationOpenChanged(bool value) => NotifyEditState();
    partial void OnIsWorkEntryBusyChanged(bool value) => NotifyEditState();

    partial void OnIsWorkEntryDraftDirtyChanged(bool value)
    {
        OnPropertyChanged(nameof(HasPendingWorkEntryDrafts));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnWorkDateInputTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsWorkDateInputInvalid));
        OnPropertyChanged(nameof(WorkCalendarAnchorDate));
        OnWorkEntryDraftEdited();
    }

    partial void OnWorkDurationInputChanged(string value) =>
        OnWorkEntryDraftEdited();

    partial void OnWorkDescriptionInputChanged(string value) =>
        OnWorkEntryDraftEdited();

    partial void OnWorkNoteInputChanged(string value) =>
        OnWorkEntryDraftEdited();

    partial void OnIsPreviewModeChanged(bool value) =>
        OnPropertyChanged(nameof(IsEditMode));

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

    partial void OnPeriodDurationInputChanged(string value) =>
        OnPeriodDraftEdited();

    private void NotifyEditState()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanEditPlannedTime));
        OnPropertyChanged(nameof(CanManagePlanningPeriods));
        OnPropertyChanged(nameof(CanSavePeriod));
        OnPropertyChanged(nameof(CanManageWorkEntries));
        OnPropertyChanged(nameof(CanSaveWorkEntry));

        SaveCommand.NotifyCanExecuteChanged();
        AddPeriodCommand.NotifyCanExecuteChanged();
        SavePeriodCommand.NotifyCanExecuteChanged();
        ConfirmDeletePeriodCommand.NotifyCanExecuteChanged();
        AddWorkEntryCommand.NotifyCanExecuteChanged();
        SaveWorkEntryCommand.NotifyCanExecuteChanged();
        ConfirmDeleteWorkEntryCommand.NotifyCanExecuteChanged();
    }

    private bool TryGetTaskDate(out DateOnly? date)
    {
        var value = DateInputText?.Trim() ?? string.Empty;
        date = null;

        if (value.Length == 0)
            return true;

        if (TryParseDate(value, out var parsed))
        {
            date = parsed;
            return true;
        }

        return false;
    }

    private bool TryGetPeriodDate(out DateOnly date) =>
        TryParseDate(PeriodDateInputText?.Trim(), out date);

    private bool TryGetWorkDate(out DateOnly date) =>
        TryParseDate(WorkDateInputText?.Trim(), out date);

    private bool TryParseDate(
        string? value,
        out DateOnly result)
    {
        result = default;

        return !string.IsNullOrWhiteSpace(value) &&
               (DateOnly.TryParseExact(
                    value,
                    DateFormats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out result) ||
                DateOnly.TryParse(
                    value,
                    DateCulture,
                    DateTimeStyles.None,
                    out result));
    }

    public void SelectCalendarDate(DateTime date)
    {
        if (CanEdit)
        {
            DateInputText = date.ToString(
                "dd.MM.yyyy",
                CultureInfo.InvariantCulture);
        }
    }

    [RelayCommand]
    private void ClearDate()
    {
        if (CanEdit)
            DateInputText = string.Empty;
    }

    public void SelectPeriodCalendarDate(DateTime date)
    {
        if (CanSavePeriod)
        {
            PeriodDateInputText = date.ToString(
                "dd.MM.yyyy",
                CultureInfo.InvariantCulture);
        }
    }

    public void SelectWorkCalendarDate(DateTime date)
    {
        if (CanSaveWorkEntry)
        {
            WorkDateInputText = date.ToString(
                "dd.MM.yyyy",
                CultureInfo.InvariantCulture);
        }
    }

    // DC-009: Planning periods.

    private void PopulatePlanningPeriods(
        IReadOnlyList<PlanningPeriod> periods)
    {
        long total = 0;

        foreach (var period in periods)
            total = checked(total + period.PlannedMinutes);

        PlanningPeriods.Clear();

        foreach (var period in periods
                     .OrderBy(p => p.PlannedDate)
                     .ThenBy(p => p.Id))
        {
            PlanningPeriods.Add(
                new PlanningPeriodRowViewModel(
                    period,
                    EditPeriod,
                    AskDeletePeriod));
        }

        TotalPlannedMinutes = total;

        OnPropertyChanged(nameof(TotalPlannedMinutes));
        OnPropertyChanged(nameof(TotalPlannedText));
        OnPropertyChanged(nameof(HasPlanningPeriods));
        OnPropertyChanged(nameof(HasNoPlanningPeriods));
        OnPropertyChanged(nameof(HasMultiplePlanningPeriods));
    }

    private async Task RefreshPlanningPeriodsAsync(
        CancellationToken token)
    {
        var periods = await _tasks.ListPlanningPeriodsAsync(
            TaskId, token);

        if (ShouldStop(token))
            return;

        PopulatePlanningPeriods(periods);

        _populating = true;

        try
        {
            PlannedTimeInput =
                PlannedDuration.Format(TotalPlannedMinutes);
        }
        finally
        {
            _populating = false;
        }
    }

    private bool CanAddPeriod() => CanManagePlanningPeriods;

    [RelayCommand(CanExecute = nameof(CanAddPeriod))]
    private void AddPeriod()
    {
        if (!CanAddPeriod())
            return;

        _editingPeriodId = null;

        PeriodDateInputText =
            TryGetTaskDate(out var taskDate) && taskDate.HasValue
                ? taskDate.Value.ToString(
                    "dd.MM.yyyy",
                    CultureInfo.InvariantCulture)
                : DateOnly.FromDateTime(DateTime.Today).ToString(
                    "dd.MM.yyyy",
                    CultureInfo.InvariantCulture);

        PeriodDurationInput = string.Empty;
        OpenPeriodEditor();
    }

    private void EditPeriod(PlanningPeriodRowViewModel period)
    {
        if (!CanManagePlanningPeriods || period.TaskId != TaskId)
            return;

        _editingPeriodId = period.Id;
        PeriodDateInputText = period.DateText;
        PeriodDurationInput =
            PlannedDuration.Format(period.PlannedMinutes);

        OpenPeriodEditor();
    }

    private void OpenPeriodEditor()
    {
        // One editor session, one meaning for the "d" unit.
        _periodInputMinutesPerDay = _appSettings.Current.MinutesPerDay;
        _periodBaseline = CurrentPeriodSnapshot();
        IsPeriodDraftDirty = false;
        SetError(null);
        IsPeriodEditorOpen = true;

        OnPropertyChanged(nameof(IsEditingExistingPeriod));
        OnPropertyChanged(nameof(PeriodEditorHeading));
    }

    private void OnPeriodDraftEdited()
    {
        if (!IsPeriodEditorOpen || _disposed)
            return;

        IsPeriodDraftDirty =
            CurrentPeriodSnapshot() != _periodBaseline;

        SetError(null);
    }

    private bool CanSavePeriodCommand() => CanSavePeriod;

    [RelayCommand(CanExecute = nameof(CanSavePeriodCommand))]
    private async Task SavePeriodAsync()
    {
        if (!CanSavePeriod)
            return;

        if (!TryGetPeriodDate(out var date) ||
            !PlannedDuration.TryParseRequired(
                PeriodDurationInput,
                _periodInputMinutesPerDay,
                out var minutes))
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
            {
                await _tasks.UpdatePlanningPeriodAsync(
                    TaskId, periodId, date, minutes, token);
            }
            else
            {
                await _tasks.AddPlanningPeriodAsync(
                    TaskId, date, minutes, token);
            }

            saved = true;
            await RefreshPlanningPeriodsAsync(token);

            if (!ShouldStop(token))
                ClosePeriodEditor();
        }
        catch (OperationCanceledException)
            when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                if (saved)
                    ClosePeriodEditor();

                SetError(
                    saved
                        ? "TaskDetailsLoadFailed"
                        : "PlanningSaveFailed",
                    ex.Message);
            }
        }
        finally
        {
            if (!_disposed)
                IsPeriodBusy = false;
        }
    }

    [RelayCommand]
    private void CancelPeriod()
    {
        if (!_disposed &&
            !IsPeriodBusy &&
            IsPeriodEditorOpen)
        {
            ClosePeriodEditor();
        }
    }

    private void ClosePeriodEditor()
    {
        IsPeriodEditorOpen = false;
        _editingPeriodId = null;
        IsPeriodDraftDirty = false;

        OnPropertyChanged(nameof(IsEditingExistingPeriod));
        OnPropertyChanged(nameof(PeriodEditorHeading));
    }

    private void AskDeletePeriod(
        PlanningPeriodRowViewModel period)
    {
        if (!CanManagePlanningPeriods ||
            period.TaskId != TaskId)
        {
            return;
        }

        _periodPendingDeletionId = period.Id;
        IsDeletePeriodConfirmationOpen = true;
        NotifyEditState();
    }

    [RelayCommand]
    private void CancelDeletePeriod()
    {
        if (_disposed || IsPeriodBusy)
            return;

        _periodPendingDeletionId = null;
        IsDeletePeriodConfirmationOpen = false;
        NotifyEditState();
    }

    private bool CanConfirmDeletePeriod() =>
        CanEdit &&
        IsDeletePeriodConfirmationOpen &&
        _periodPendingDeletionId.HasValue;

    [RelayCommand(CanExecute = nameof(CanConfirmDeletePeriod))]
    private async Task ConfirmDeletePeriodAsync()
    {
        if (!CanConfirmDeletePeriod())
            return;

        var id = _periodPendingDeletionId!.Value;

        IsPeriodBusy = true;
        SetError(null);

        var token = _lifetime.Token;
        var deleted = false;

        try
        {
            await _tasks.DeletePlanningPeriodAsync(
                TaskId, id, token);

            deleted = true;

            _periodPendingDeletionId = null;
            IsDeletePeriodConfirmationOpen = false;

            await RefreshPlanningPeriodsAsync(token);
        }
        catch (OperationCanceledException)
            when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                SetError(
                    deleted
                        ? "TaskDetailsLoadFailed"
                        : "PlanningDeleteFailed",
                    ex.Message);
            }
        }
        finally
        {
            if (!_disposed)
                IsPeriodBusy = false;
        }
    }

    // DC-010: Work entries and independent unsaved drafts.

    private void PopulateWorkEntries(
        IReadOnlyList<WorkEntry> entries)
    {
        long total = 0;
        var rows = new List<WorkEntryRowViewModel>(
            entries.Count);

        foreach (var entry in entries)
        {
            total = checked(total + entry.ActualMinutes);

            rows.Add(new WorkEntryRowViewModel(
                entry,
                EditWorkEntry,
                AskDeleteWorkEntry));
        }

        WorkEntries.Clear();

        foreach (var row in rows
                     .OrderByDescending(e => e.WorkDate)
                     .ThenByDescending(e => e.CreatedAtUtc)
                     .ThenBy(e => e.Id))
        {
            WorkEntries.Add(row);
        }

        TotalActualMinutes = total;

        UpdateWorkEntryRowStates();

        OnPropertyChanged(nameof(TotalActualMinutes));
        OnPropertyChanged(nameof(TotalActualText));
        OnPropertyChanged(nameof(HasWorkEntries));
        OnPropertyChanged(nameof(HasNoWorkEntries));
    }

    private async Task RefreshWorkEntriesAsync(
        CancellationToken token)
    {
        var entries = await _tasks.ListWorkEntriesAsync(
            TaskId, token);

        if (ShouldStop(token))
            return;

        PopulateWorkEntries(entries);
    }

    private void UpdateWorkEntryRowStates()
    {
        foreach (var row in WorkEntries)
        {
            row.IsActive =
                IsWorkEntryEditorOpen &&
                _editingWorkEntryId == row.Id;

            row.HasUnsavedDraft =
                _workEntryDrafts.ContainsKey(row.Id);
        }
    }

    private void NotifyWorkEntryDraftState()
    {
        UpdateWorkEntryRowStates();

        OnPropertyChanged(nameof(HasPendingWorkEntryDrafts));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(AddWorkEntryLabel));

        SaveCommand.NotifyCanExecuteChanged();
    }

    private void CacheCurrentWorkEntryDraft()
    {
        if (!IsWorkEntryEditorOpen)
            return;

        var snapshot = CurrentWorkEntrySnapshot();
        var dirty = snapshot != _workEntryBaseline;

        if (_editingWorkEntryId is Guid entryId)
        {
            if (dirty)
            {
                _workEntryDrafts[entryId] = snapshot;
                _workEntryDraftDayLengths[entryId] = _workEntryInputMinutesPerDay;
            }
            else
            {
                _workEntryDrafts.Remove(entryId);
                _workEntryDraftDayLengths.Remove(entryId);
            }
        }
        else
        {
            _newWorkEntryDraft = dirty ? snapshot : null;
            _newWorkEntryDraftDayLength = dirty
                ? _workEntryInputMinutesPerDay
                : null;
        }

        NotifyWorkEntryDraftState();
    }

    private void SetWorkEntryEditor(
        Guid? entryId,
        WorkEntrySnapshot baseline,
        WorkEntrySnapshot current,
        int minutesPerDay)
    {
        // Property changes during context switching must not
        // overwrite the previously selected entry's draft.
        _switchingWorkEntryContext = true;

        try
        {
            _editingWorkEntryId = entryId;
            _workEntryBaseline = baseline;
            _workEntryInputMinutesPerDay = minutesPerDay;

            WorkDateInputText = current.Date;
            WorkDurationInput = current.Duration;
            WorkDescriptionInput = current.Description;
            WorkNoteInput = current.Note;

            IsWorkEntryEditorOpen = true;
            IsWorkEntryDraftDirty = current != baseline;
        }
        finally
        {
            _switchingWorkEntryContext = false;
        }

        SetError(null);

        OnPropertyChanged(nameof(IsEditingExistingWorkEntry));
        OnPropertyChanged(nameof(WorkEditorHeading));
        OnPropertyChanged(nameof(IsWorkDateInputInvalid));
        OnPropertyChanged(nameof(WorkCalendarAnchorDate));

        NotifyWorkEntryDraftState();
    }

    private bool CanAddWorkEntry() => CanManageWorkEntries;

    [RelayCommand(CanExecute = nameof(CanAddWorkEntry))]
    private void AddWorkEntry()
    {
        if (!CanAddWorkEntry())
            return;

        WorkEntrySnapshot baseline;
        WorkEntrySnapshot current;

        if (_newWorkEntryDraft is WorkEntrySnapshot draft)
        {
            baseline = _newWorkEntryBaseline;
            current = draft;
        }
        else
        {
            baseline = new WorkEntrySnapshot(
                DateOnly.FromDateTime(DateTime.Today).ToString(
                    "dd.MM.yyyy",
                    CultureInfo.InvariantCulture),
                string.Empty,
                string.Empty,
                string.Empty);

            _newWorkEntryBaseline = baseline;
            current = baseline;
        }

        var minutesPerDay = _newWorkEntryDraft.HasValue
            ? _newWorkEntryDraftDayLength ?? _appSettings.Current.MinutesPerDay
            : _appSettings.Current.MinutesPerDay;

        SetWorkEntryEditor(null, baseline, current, minutesPerDay);
    }

    private void EditWorkEntry(
        WorkEntryRowViewModel entry)
    {
        if (!CanSwitchWorkEntry ||
            entry.TaskId != TaskId)
        {
            return;
        }

        // Already editing this entry: preserve the current input.
        if (IsWorkEntryEditorOpen &&
            _editingWorkEntryId == entry.Id)
        {
            return;
        }

        // Save the previous entry's unsaved input in memory.
        // Nothing is written to SQLite at this point.
        CacheCurrentWorkEntryDraft();

        var baseline = SnapshotFromEntry(entry);

        var current = _workEntryDrafts.TryGetValue(
            entry.Id, out var draft)
            ? draft
            : baseline;

        var minutesPerDay = _workEntryDrafts.ContainsKey(entry.Id)
            ? _workEntryDraftDayLengths.GetValueOrDefault(
                entry.Id, _appSettings.Current.MinutesPerDay)
            : _appSettings.Current.MinutesPerDay;

        SetWorkEntryEditor(entry.Id, baseline, current, minutesPerDay);
    }

    private void OnWorkEntryDraftEdited()
    {
        if (!IsWorkEntryEditorOpen ||
            _switchingWorkEntryContext ||
            _disposed)
        {
            return;
        }

        var snapshot = CurrentWorkEntrySnapshot();

        IsWorkEntryDraftDirty =
            snapshot != _workEntryBaseline;

        if (_editingWorkEntryId is Guid entryId)
        {
            if (IsWorkEntryDraftDirty)
            {
                _workEntryDrafts[entryId] = snapshot;
                _workEntryDraftDayLengths[entryId] = _workEntryInputMinutesPerDay;
            }
            else
            {
                _workEntryDrafts.Remove(entryId);
                _workEntryDraftDayLengths.Remove(entryId);
            }
        }
        else
        {
            _newWorkEntryDraft = IsWorkEntryDraftDirty
                ? snapshot
                : null;
            _newWorkEntryDraftDayLength = IsWorkEntryDraftDirty
                ? _workEntryInputMinutesPerDay
                : null;
        }

        NotifyWorkEntryDraftState();
        SetError(null);
    }

    private bool CanSaveWorkEntryCommand() =>
        CanSaveWorkEntry;

    [RelayCommand(CanExecute = nameof(CanSaveWorkEntryCommand))]
    private async Task SaveWorkEntryAsync()
    {
        if (!CanSaveWorkEntry)
            return;

        if (!TryGetWorkDate(out var date) ||
            !PlannedDuration.TryParseRequired(
                WorkDurationInput,
                _workEntryInputMinutesPerDay,
                out var minutes))
        {
            SetError("WorkValidationError");
            return;
        }

        IsWorkEntryBusy = true;
        SetError(null);

        var token = _lifetime.Token;
        var saved = false;
        var savingEntryId = _editingWorkEntryId;

        try
        {
            if (savingEntryId is Guid entryId)
            {
                await _tasks.UpdateWorkEntryAsync(
                    TaskId,
                    entryId,
                    date,
                    minutes,
                    WorkDescriptionInput,
                    WorkNoteInput,
                    token);
            }
            else
            {
                await _tasks.AddWorkEntryAsync(
                    TaskId,
                    date,
                    minutes,
                    WorkDescriptionInput,
                    WorkNoteInput,
                    token);
            }

            saved = true;

            // Only remove the draft confirmed as saved.
            // Other entries' drafts must remain intact.
            if (savingEntryId is Guid persistedId)
            {
                _workEntryDrafts.Remove(persistedId);
                _workEntryDraftDayLengths.Remove(persistedId);
            }
            else
            {
                _newWorkEntryDraft = null;
                _newWorkEntryDraftDayLength = null;
            }

            NotifyWorkEntryDraftState();

            await RefreshWorkEntriesAsync(token);

            if (!ShouldStop(token))
                CloseWorkEntryEditor();
        }
        catch (OperationCanceledException)
            when (token.IsCancellationRequested) { }
        catch (KeyNotFoundException ex)
        {
            if (!_disposed)
                SetError("WorkEntryNotFound", ex.Message);
        }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                if (saved)
                    CloseWorkEntryEditor();

                SetError(
                    saved
                        ? "TaskDetailsLoadFailed"
                        : "WorkSaveFailed",
                    ex.Message);
            }
        }
        finally
        {
            if (!_disposed)
                IsWorkEntryBusy = false;
        }
    }

    [RelayCommand]
    private void CancelWorkEntry()
    {
        if (_disposed ||
            IsWorkEntryBusy ||
            !IsWorkEntryEditorOpen)
        {
            return;
        }

        // Cancel discards only the currently selected draft.
        // Other drafts remain available.
        CloseWorkEntryEditor(discardCurrentDraft: true);
    }

    private void CloseWorkEntryEditor(
        bool discardCurrentDraft = false)
    {
        if (discardCurrentDraft)
        {
            if (_editingWorkEntryId is Guid entryId)
            {
                _workEntryDrafts.Remove(entryId);
                _workEntryDraftDayLengths.Remove(entryId);
            }
            else
            {
                _newWorkEntryDraft = null;
                _newWorkEntryDraftDayLength = null;
            }
        }

        _editingWorkEntryId = null;
        IsWorkEntryEditorOpen = false;
        IsWorkEntryDraftDirty = false;

        OnPropertyChanged(nameof(IsEditingExistingWorkEntry));
        OnPropertyChanged(nameof(WorkEditorHeading));

        NotifyWorkEntryDraftState();
    }

    private void AskDeleteWorkEntry(
        WorkEntryRowViewModel entry)
    {
        if (!CanManageWorkEntries ||
            entry.TaskId != TaskId)
        {
            return;
        }

        _workEntryPendingDeletionId = entry.Id;
        IsDeleteWorkEntryConfirmationOpen = true;
    }

    [RelayCommand]
    private void CancelDeleteWorkEntry()
    {
        if (_disposed || IsWorkEntryBusy)
            return;

        _workEntryPendingDeletionId = null;
        IsDeleteWorkEntryConfirmationOpen = false;
    }

    private bool CanConfirmDeleteWorkEntry() =>
        CanEdit &&
        IsDeleteWorkEntryConfirmationOpen &&
        _workEntryPendingDeletionId.HasValue;

    [RelayCommand(CanExecute = nameof(CanConfirmDeleteWorkEntry))]
    private async Task ConfirmDeleteWorkEntryAsync()
    {
        if (!CanConfirmDeleteWorkEntry())
            return;

        var id = _workEntryPendingDeletionId!.Value;

        IsWorkEntryBusy = true;
        SetError(null);

        var token = _lifetime.Token;
        var deleted = false;

        try
        {
            await _tasks.DeleteWorkEntryAsync(
                TaskId, id, token);

            deleted = true;

            // Remove any unsaved draft of the deleted entry.
            _workEntryDrafts.Remove(id);
            _workEntryDraftDayLengths.Remove(id);

            _workEntryPendingDeletionId = null;
            IsDeleteWorkEntryConfirmationOpen = false;

            NotifyWorkEntryDraftState();

            await RefreshWorkEntriesAsync(token);
        }
        catch (OperationCanceledException)
            when (token.IsCancellationRequested) { }
        catch (KeyNotFoundException ex)
        {
            if (!_disposed)
                SetError("WorkEntryNotFound", ex.Message);
        }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                SetError(
                    deleted
                        ? "TaskDetailsLoadFailed"
                        : "WorkDeleteFailed",
                    ex.Message);
            }
        }
        finally
        {
            if (!_disposed)
                IsWorkEntryBusy = false;
        }
    }

    // Main task saving remains independent of WorkEntries.

    private bool CanSave() =>
        CanEdit &&
        IsDirty &&
        !IsPeriodEditorOpen &&
        !IsDeletePeriodConfirmationOpen &&
        !IsWorkEntryEditorOpen &&
        !IsDeleteWorkEntryConfirmationOpen &&
        !HasPendingWorkEntryDrafts;

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

        if (!TryGetTaskDate(out var date))
        {
            SetError("TaskDetailsDateInvalid");
            return;
        }

        if (SelectedStatus is null)
        {
            SetError("TaskDetailsStatusRequired");
            return;
        }

        int? minutes = null;

        if (IsCreateMode)
        {
            if (!PlannedDuration.TryParse(
                PlannedTimeInput,
                _createInputMinutesPerDay,
                out minutes))
            {
                SetError("QuickTaskInvalidTime");
                return;
            }

            if (minutes.HasValue && !date.HasValue)
            {
                SetError("QuickTaskDateRequired");
                return;
            }
        }

        var token = _lifetime.Token;
        IsSaving = true;
        TaskItem? saved = null;

        try
        {
            if (IsCreateMode)
            {
                var request = new CreateTaskRequest(
                    TaskDate: date,
                    Title: Title,
                    ShortDescription: ShortDescription,
                    DescriptionMarkdown: DescriptionMarkdown,
                    StatusId: SelectedStatus.Id,
                    TaskNumber: TaskNumber,
                    PlannedMinutes: minutes);

                saved = await _tasks.CreateAsync(request, token);
            }
            else
            {
                var request = new UpdateTaskRequest(
                    Id: TaskId,
                    TaskDate: date,
                    Title: Title,
                    ShortDescription: ShortDescription,
                    DescriptionMarkdown: DescriptionMarkdown,
                    StatusId: SelectedStatus.Id,
                    TaskNumber: TaskNumber,
                    UpdateTaskNumber: true);

                saved = await _tasks.UpdateAsync(request, token);
            }

            if (ShouldStop(token))
                return;

            _taskId = saved.Id;
            OnPropertyChanged(nameof(TaskId));

            PopulateFromTask(
                saved,
                IsCreateMode
                    ? PlannedDuration.Format(minutes ?? 0)
                    : PlannedDuration.Format(TotalPlannedMinutes));

            IsSaveSuccessful = true;
        }
        catch (OperationCanceledException)
            when (token.IsCancellationRequested) { }
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
            if (!_disposed)
                SetError(
                    "TaskDetailsValidationError",
                    ex.Message);
        }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                SetError(
                    IsCreateMode
                        ? "TaskDetailsCreateFailed"
                        : "TaskDetailsSaveFailed",
                    ex.Message);
            }
        }
        finally
        {
            if (!_disposed)
                IsSaving = false;
        }

        if (saved is not null && !_disposed)
        {
            TaskSaved?.Invoke(
                this,
                new TaskDetailsSavedEventArgs(saved));

            if (!_disposed)
                _navigation.CloseTask();
        }
    }

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

    [RelayCommand]
    private void Back() =>
        RequestLeave(_navigation.CloseTask);

    [RelayCommand]
    private void Cancel() =>
        RequestLeave(_navigation.CloseTask);

    public void RequestLeave(Action continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);

        if (_disposed ||
            IsSaving ||
            IsPeriodBusy ||
            IsWorkEntryBusy)
        {
            return;
        }

        if (!HasUnsavedChanges)
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
        if (_disposed ||
            !IsDiscardConfirmationOpen)
        {
            return;
        }

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

    private void SetError(
        string? key,
        string? details = null)
    {
        _errorKey = key;
        _errorDetails = details;

        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ErrorMessage));
    }

    private void OnLocalizationChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (_disposed ||
            (!string.IsNullOrEmpty(e.PropertyName) &&
             e.PropertyName != nameof(
                 ILocalizationService.CurrentLanguage)))
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
            nameof(PeriodsLabel),
            nameof(AddPeriodLabel),
            nameof(TotalPlannedLabel),
            nameof(TotalPlannedText),
            nameof(PeriodEditorHeading),
            nameof(PeriodDateLabel),
            nameof(PeriodDurationLabel),
            nameof(SavePeriodLabel),
            nameof(DeletePeriodTitle),
            nameof(DeletePeriodMessage),
            nameof(DeletePeriodLabel),
            nameof(WorkLogLabel),
            nameof(WorkEntriesLabel),
            nameof(AddWorkEntryLabel),
            nameof(WorkEditorHeading),
            nameof(WorkDateLabel),
            nameof(WorkDurationLabel),
            nameof(WorkDescriptionLabel),
            nameof(WorkNoteLabel),
            nameof(TotalActualLabel),
            nameof(NoWorkEntriesLabel),
            nameof(SaveWorkEntryLabel),
            nameof(WorkEntrySaveHint),
            nameof(DeleteWorkEntryTitle),
            nameof(DeleteWorkEntryMessage),
            nameof(DeleteWorkEntryLabel),
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
            nameof(IsDateInputInvalid),
            nameof(IsPeriodDateInputInvalid),
            nameof(IsWorkDateInputInvalid),
            nameof(TotalActualText)
        ];

        foreach (var property in properties)
            OnPropertyChanged(property);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _pendingNavigation = null;
        _periodPendingDeletionId = null;
        _workEntryPendingDeletionId = null;

        _workEntryDrafts.Clear();
        _workEntryDraftDayLengths.Clear();
        _newWorkEntryDraft = null;
        _newWorkEntryDraftDayLength = null;

        _localization.PropertyChanged -= OnLocalizationChanged;

        _lifetime.Cancel();
        _lifetime.Dispose();

        TaskSaved = null;
    }
}

public sealed class TaskDetailsSavedEventArgs : EventArgs
{
    public TaskDetailsSavedEventArgs(TaskItem task) =>
        Task = task
            ?? throw new ArgumentNullException(nameof(task));

    public TaskItem Task { get; }
}