using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daycord.Core.Application.Contracts;
using Daycord.Core.Application.Models;
using Daycord.Core.Application.Services;
using Daycord.Desktop.Localization;
using Daycord.Desktop.Reports;

namespace Daycord.Desktop.ViewModels;

public sealed partial class ReportsViewModel :
    ObservableObject,
    IDisposable
{
    private readonly WorkReportService _reports;
    private readonly WorkReportPresetService _presetService;
    private readonly IWorkReportExporter _exporter;
    private readonly ILocalizationService _localization;

    private IReportSaveDialog? _saveDialog;

    private CancellationTokenSource? _sessionCancellation;
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _exportCancellation;

    private int _loadVersion;
    private bool _isActive;
    private bool _disposed;
    private bool _hasLoaded;
    private bool _isLoading;
    private bool _isExporting;
    private bool _isPresetBusy;
    private bool _hasUnsavedChanges;

    private Exception? _loadError;
    private Exception? _exportError;
    private Exception? _presetError;

    private WorkReport? _report;
    private bool _exportSucceeded;
    private string _exportedFileName = string.Empty;

    private DateTimeOffset? _startDate;
    private DateTimeOffset? _endDate;

    private IReadOnlyList<WorkReportPreset> _availablePresets = [];
    private WorkReportPreset? _selectedPreset;

    private string _presetNameDraft = string.Empty;
    private string _newPresetName = string.Empty;

    public ReportsViewModel(
        WorkReportService reports,
        WorkReportPresetService presetService,
        IWorkReportExporter exporter,
        ILocalizationService localization)
    {
        _reports = reports
            ?? throw new ArgumentNullException(nameof(reports));

        _presetService = presetService
            ?? throw new ArgumentNullException(nameof(presetService));

        _exporter = exporter
            ?? throw new ArgumentNullException(nameof(exporter));

        _localization = localization
            ?? throw new ArgumentNullException(nameof(localization));

        var today = DateOnly.FromDateTime(DateTime.Today);
        var start = new DateOnly(today.Year, today.Month, 1);

        _startDate = ToPickerDate(start);
        _endDate = ToPickerDate(EndOfMonth(start));

        ColumnEditors =
            new ObservableCollection<ReportColumnEditorItem>();

        _localization.PropertyChanged += OnLocalizationChanged;
    }

    // ------------------------------------------------------------
    // Date range
    // ------------------------------------------------------------

    public DateTimeOffset? StartDate
    {
        get => _startDate;
        set
        {
            if (_disposed || IsExporting)
                return;

            if (SetProperty(ref _startDate, value))
                OnRangeChanged();
        }
    }

    public DateTimeOffset? EndDate
    {
        get => _endDate;
        set
        {
            if (_disposed || IsExporting)
                return;

            if (SetProperty(ref _endDate, value))
                OnRangeChanged();
        }
    }

    public bool IsPeriodValid =>
        StartDate.HasValue &&
        EndDate.HasValue &&
        GetDate(StartDate.Value) <= GetDate(EndDate.Value);

    public DateOnly SelectedMonth =>
        StartDate.HasValue
            ? new DateOnly(
                StartDate.Value.Year,
                StartDate.Value.Month,
                1)
            : new DateOnly(
                DateTime.Today.Year,
                DateTime.Today.Month,
                1);

    public bool CanGoPrevious =>
        SelectedMonth > DateOnly.MinValue;

    public bool CanGoNext =>
        SelectedMonth.Year < 9999 ||
        SelectedMonth.Month < 12;

    public string MonthTitle
    {
        get
        {
            if (!IsPeriodValid)
                return Text("ReportsInvalidPeriod");

            var from = GetDate(StartDate!.Value);
            var to = GetDate(EndDate!.Value);

            if (from.Day == 1 && to == EndOfMonth(from))
                return from.ToString("MMMM yyyy", Culture);

            return $"{from.ToString("d", Culture)} – " +
                   $"{to.ToString("d", Culture)}";
        }
    }

    public string PeriodErrorMessage =>
        IsPeriodValid
            ? string.Empty
            : Text("ReportsInvalidPeriod");

    private static DateOnly GetDate(DateTimeOffset value) =>
        new(value.Year, value.Month, value.Day);

    private static DateOnly EndOfMonth(DateOnly date) =>
        new(
            date.Year,
            date.Month,
            DateTime.DaysInMonth(date.Year, date.Month));

    private static DateTimeOffset ToPickerDate(DateOnly date)
    {
        var localDate = date.ToDateTime(TimeOnly.MinValue);
        var offset = TimeZoneInfo.Local.GetUtcOffset(localDate);

        return new DateTimeOffset(localDate, offset);
    }

    private void SetRange(DateOnly from, DateOnly to)
    {
        if (_disposed || IsExporting)
            return;

        _startDate = ToPickerDate(from);
        _endDate = ToPickerDate(to);

        OnPropertyChanged(nameof(StartDate));
        OnPropertyChanged(nameof(EndDate));

        OnRangeChanged();
    }

    private void OnRangeChanged()
    {
        ++_loadVersion;
        _loadCancellation?.Cancel();

        _report = null;
        _hasLoaded = false;
        _loadError = null;

        IsLoading = false;

        OnPropertyChanged(nameof(IsPeriodValid));
        OnPropertyChanged(nameof(PeriodErrorMessage));
        OnPropertyChanged(nameof(SelectedMonth));
        OnPropertyChanged(nameof(MonthTitle));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));

        NotifyReportChanged();
        NotifyLoadState();

        ClearExportStatus();
        RefreshCommands();

        if (_isActive &&
            IsPeriodValid &&
            !IsExporting &&
            _selectedPreset is not null)
        {
            _ = ReloadAsync();
        }
    }

    private bool CanChangePeriod() =>
        !_disposed &&
        _isActive &&
        !IsExporting;

    [RelayCommand(CanExecute = nameof(CanChangePeriod))]
    private void GoToCurrentMonth()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var start = new DateOnly(today.Year, today.Month, 1);

        SetRange(start, EndOfMonth(start));
    }

    [RelayCommand(CanExecute = nameof(CanChangePeriod))]
    private void GoToPreviousMonth()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var current = new DateOnly(today.Year, today.Month, 1);
        var previous = current.AddMonths(-1);

        SetRange(previous, EndOfMonth(previous));
    }

    [RelayCommand(CanExecute = nameof(CanChangePeriod))]
    private void PreviousMonth()
    {
        if (!CanGoPrevious)
            return;

        var month = SelectedMonth.AddMonths(-1);
        SetRange(month, EndOfMonth(month));
    }

    [RelayCommand(CanExecute = nameof(CanChangePeriod))]
    private void NextMonth()
    {
        if (!CanGoNext)
            return;

        var month = SelectedMonth.AddMonths(1);
        SetRange(month, EndOfMonth(month));
    }

    // ------------------------------------------------------------
    // Report summary
    // ------------------------------------------------------------

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (!SetProperty(ref _isLoading, value))
                return;

            NotifyLoadState();
            RefreshCommands();
        }
    }

    public bool IsExporting
    {
        get => _isExporting;
        private set
        {
            if (SetProperty(ref _isExporting, value))
                RefreshCommands();
        }
    }

    public bool IsPresetBusy
    {
        get => _isPresetBusy;
        private set
        {
            if (SetProperty(ref _isPresetBusy, value))
                RefreshCommands();
        }
    }

    public bool HasLoadError => _loadError is not null;

    public bool IsLoaded =>
        _hasLoaded &&
        IsPeriodValid &&
        !IsLoading &&
        !HasLoadError;

    public int PlanningPeriodCount =>
        _report?.PlanningPeriodCount ?? 0;

    public int WorkEntryCount =>
        _report?.WorkEntryCount ?? 0;

    public int ReportRowCount =>
        _report?.GetRowsForColumns(GetEditedColumns()).Count ?? 0;

    public long TotalPlannedMinutes =>
        _report?.TotalPlannedMinutes ?? 0;

    public long TotalActualMinutes =>
        _report?.TotalActualMinutes ?? 0;

    public string TotalPlannedTimeText =>
        CalendarDurationText.Format(
            TotalPlannedMinutes,
            _localization);

    public string TotalActualTimeText =>
        CalendarDurationText.Format(
            TotalActualMinutes,
            _localization);

    public bool IsMonthEmpty =>
        IsLoaded && ReportRowCount == 0;

    public string LoadErrorMessage =>
        _loadError is null
            ? string.Empty
            : string.Format(
                Culture,
                Text("ReportsLoadFailed"),
                _loadError.Message);

    private void ApplyReport(WorkReport report)
    {
        _report = report;
        _loadError = null;
        _hasLoaded = true;

        NotifyReportChanged();
        NotifyLoadState();
    }

    private void NotifyReportChanged()
    {
        OnPropertyChanged(nameof(PlanningPeriodCount));
        OnPropertyChanged(nameof(WorkEntryCount));
        OnPropertyChanged(nameof(ReportRowCount));

        OnPropertyChanged(nameof(TotalPlannedMinutes));
        OnPropertyChanged(nameof(TotalActualMinutes));

        OnPropertyChanged(nameof(TotalPlannedTimeText));
        OnPropertyChanged(nameof(TotalActualTimeText));

        OnPropertyChanged(nameof(IsMonthEmpty));
    }

    private void NotifyLoadState()
    {
        OnPropertyChanged(nameof(HasLoadError));
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(IsMonthEmpty));
        OnPropertyChanged(nameof(LoadErrorMessage));

        RefreshCommands();
    }

    // ------------------------------------------------------------
    // Presets and localized column editor
    // ------------------------------------------------------------

    public IReadOnlyList<WorkReportPreset> AvailablePresets
    {
        get => _availablePresets;
        private set => SetProperty(ref _availablePresets, value);
    }

    public WorkReportPreset? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (value is null ||
                value.Id == _selectedPreset?.Id ||
                !CanManagePresets())
            {
                return;
            }

            _ = SelectPresetAsync(value);
        }
    }

    public ObservableCollection<ReportColumnEditorItem>
        ColumnEditors { get; }

    public string PresetNameDraft
    {
        get => _presetNameDraft;
        set
        {
            if (SetProperty(ref _presetNameDraft, value))
                RefreshEditorDirty();
        }
    }

    public string NewPresetName
    {
        get => _newPresetName;
        set
        {
            if (SetProperty(ref _newPresetName, value))
                RefreshCommands();
        }
    }

    public bool HasUnsavedChanges => _hasUnsavedChanges;

    public bool HasPresetError => _presetError is not null;

    public string PresetErrorMessage =>
        _presetError is null
            ? string.Empty
            : string.Format(
                Culture,
                Text("ReportsPresetFailed"),
                _presetError.Message);

    public bool CanDeleteSelectedPreset =>
        CanManagePresets() &&
        _selectedPreset is { IsBuiltIn: false };

    private bool CanManagePresets() =>
        !_disposed &&
        _isActive &&
        !IsExporting &&
        !IsPresetBusy &&
        _selectedPreset is not null;

    private bool CanCreatePreset() =>
        CanManagePresets() &&
        !string.IsNullOrWhiteSpace(NewPresetName);

    private bool CanSaveColumns() =>
        CanManagePresets() &&
        HasUnsavedChanges;

    private void ApplyPresetState(
        WorkReportPresetState state,
        bool preserveLoadedReport = false)
    {
        AvailablePresets = state.Presets;
        _selectedPreset = state.ActivePreset;

        OnPropertyChanged(nameof(SelectedPreset));
        OnPropertyChanged(nameof(CanDeleteSelectedPreset));

        _presetError = null;
        NotifyPresetError();

        RebuildColumnEditors(state.ActivePreset);

        if (!preserveLoadedReport)
        {
            _report = null;
            _hasLoaded = false;
        }

        NotifyReportChanged();
        NotifyLoadState();
        RefreshCommands();
    }

    private void RebuildColumnEditors(WorkReportPreset preset)
    {
        foreach (var editor in ColumnEditors)
            editor.PropertyChanged -= OnColumnEditorChanged;

        ColumnEditors.Clear();

        var selectedFields = new HashSet<WorkReportField>();
        var language = _localization.CurrentLanguage;

        foreach (var column in preset.Columns)
        {
            selectedFields.Add(column.Field);

            AddEditor(new ReportColumnEditorItem(
                column.Field,
                WorkReportColumnLabels.DefaultHeader(
                    column.Field,
                    language),
                column.Header,
                isIncluded: true,
                language: language));
        }

        foreach (var field in Enum.GetValues<WorkReportField>())
        {
            if (selectedFields.Contains(field))
                continue;

            AddEditor(new ReportColumnEditorItem(
                field,
                WorkReportColumnLabels.DefaultHeader(
                    field,
                    language),
                header: WorkReportColumnLabels.Token(field),
                isIncluded: false,
                language: language));
        }

        _presetNameDraft = preset.Name;
        OnPropertyChanged(nameof(PresetNameDraft));

        RefreshEditorDirty();
    }

    private void AddEditor(ReportColumnEditorItem editor)
    {
        editor.PropertyChanged += OnColumnEditorChanged;
        ColumnEditors.Add(editor);
    }

    private void OnColumnEditorChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        RefreshEditorDirty();
    }

    /// <summary>
    /// Persist raw automatic tokens, not translated labels.
    /// </summary>
    private IReadOnlyList<WorkReportColumn> GetEditedColumns()
    {
        return ColumnEditors
            .Where(static item => item.IsIncluded)
            .Select(static item =>
                new WorkReportColumn(
                    item.Field,
                    item.StoredHeader))
            .ToArray();
    }

    private void RefreshEditorDirty()
    {
        var columns = GetEditedColumns();

        var changed = _selectedPreset is not null &&
            (
                !string.Equals(
                    PresetNameDraft,
                    _selectedPreset.Name,
                    StringComparison.Ordinal)
                ||
                !_selectedPreset.Columns.SequenceEqual(columns)
            );

        if (_hasUnsavedChanges != changed)
        {
            _hasUnsavedChanges = changed;
            OnPropertyChanged(nameof(HasUnsavedChanges));
        }

        OnPropertyChanged(nameof(ReportRowCount));
        OnPropertyChanged(nameof(IsMonthEmpty));

        RefreshCommands();
    }

    private IReadOnlyList<WorkReportColumn> ValidateEditedColumns()
    {
        var columns = GetEditedColumns();

        if (columns.Count == 0)
        {
            throw new InvalidOperationException(
                "Select at least one report column.");
        }

        return columns;
    }

    private async Task<WorkReportPresetState> PersistEditsAsync(
        CancellationToken cancellationToken)
    {
        var current = _selectedPreset
            ?? throw new InvalidOperationException(
                "No report preset is selected.");

        return await _presetService.UpdatePresetAsync(
            current.Id,
            PresetNameDraft,
            ValidateEditedColumns(),
            cancellationToken);
    }

    private async Task LoadPresetStateAsync(
        CancellationToken cancellationToken)
    {
        IsPresetBusy = true;

        try
        {
            var state = await _presetService.GetStateAsync(
                cancellationToken);

            if (!_disposed &&
                _isActive &&
                !cancellationToken.IsCancellationRequested)
            {
                ApplyPresetState(state);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!_disposed && _isActive)
            {
                _presetError = error;
                _loadError = error;

                NotifyPresetError();
                NotifyLoadState();
            }
        }
        finally
        {
            IsPresetBusy = false;
        }
    }

    private async Task RunPresetOperationAsync(
        Func<CancellationToken, Task<WorkReportPresetState>> action)
    {
        if (!CanManagePresets())
            return;

        var token = _sessionCancellation?.Token
            ?? CancellationToken.None;

        IsPresetBusy = true;
        _presetError = null;
        NotifyPresetError();

        try
        {
            var state = await action(token);

            if (_disposed ||
                !_isActive ||
                token.IsCancellationRequested)
            {
                return;
            }

            ApplyPresetState(state);
            await ReloadAsync();
        }
        catch (OperationCanceledException)
            when (token.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!_disposed && _isActive)
            {
                _presetError = error;
                NotifyPresetError();
            }
        }
        finally
        {
            IsPresetBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSaveColumns))]
    private Task SaveColumnsAsync()
    {
        return RunPresetOperationAsync(PersistEditsAsync);
    }

    [RelayCommand(CanExecute = nameof(CanCreatePreset))]
    private Task CreatePresetAsync()
    {
        var name = NewPresetName.Trim();

        return RunPresetOperationAsync(async token =>
        {
            var state = await _presetService.CreatePresetAsync(
                name,
                ValidateEditedColumns(),
                token);

            var created = state.Presets.First(
                preset => preset.Name == name &&
                          !preset.IsBuiltIn);

            return await _presetService.ActivatePresetAsync(
                created.Id,
                token);
        });
    }

    [RelayCommand(CanExecute = nameof(CanManagePresets))]
    private Task DeletePresetAsync()
    {
        if (_selectedPreset is not { IsBuiltIn: false } preset)
            return Task.CompletedTask;

        return RunPresetOperationAsync(
            token => _presetService.DeletePresetAsync(
                preset.Id,
                token));
    }

    [RelayCommand(CanExecute = nameof(CanManagePresets))]
    private Task ResetToDefaultAsync()
    {
        return RunPresetOperationAsync(
            token => _presetService.ResetAsync(token));
    }

    private Task SelectPresetAsync(WorkReportPreset preset)
    {
        return RunPresetOperationAsync(async token =>
        {
            if (HasUnsavedChanges)
                await PersistEditsAsync(token);

            return await _presetService.ActivatePresetAsync(
                preset.Id,
                token);
        });
    }

    [RelayCommand]
    private void MoveColumnUp(ReportColumnEditorItem? item)
    {
        if (!CanManagePresets() || item is null)
            return;

        var index = ColumnEditors.IndexOf(item);

        if (index <= 0)
            return;

        ColumnEditors.Move(index, index - 1);
        RefreshEditorDirty();
    }

    [RelayCommand]
    private void MoveColumnDown(ReportColumnEditorItem? item)
    {
        if (!CanManagePresets() || item is null)
            return;

        var index = ColumnEditors.IndexOf(item);

        if (index < 0 || index >= ColumnEditors.Count - 1)
            return;

        ColumnEditors.Move(index, index + 1);
        RefreshEditorDirty();
    }

    private void NotifyPresetError()
    {
        OnPropertyChanged(nameof(HasPresetError));
        OnPropertyChanged(nameof(PresetErrorMessage));
    }

    // ------------------------------------------------------------
    // Lifecycle and report loading
    // ------------------------------------------------------------

    public async Task ActivateAsync()
    {
        if (_disposed)
            return;

        if (!_isActive)
        {
            _isActive = true;
            _sessionCancellation = new CancellationTokenSource();
        }

        RefreshCommands();

        if (_selectedPreset is null || !HasUnsavedChanges)
        {
            await LoadPresetStateAsync(
                _sessionCancellation!.Token);
        }

        if (_isActive && _selectedPreset is not null)
            await ReloadAsync();
    }

    public void Deactivate()
    {
        if (!_isActive)
            return;

        _isActive = false;
        ++_loadVersion;

        _sessionCancellation?.Cancel();
        _loadCancellation?.Cancel();
        _exportCancellation?.Cancel();

        _sessionCancellation?.Dispose();
        _sessionCancellation = null;

        IsLoading = false;
        RefreshCommands();
    }

    [RelayCommand]
    private Task RetryAsync()
    {
        return ActivateAsync();
    }

    private async Task ReloadAsync()
    {
        if (_disposed ||
            !_isActive ||
            !IsPeriodValid ||
            IsExporting ||
            _selectedPreset is null)
        {
            return;
        }

        _loadCancellation?.Cancel();

        var source = CancellationTokenSource.CreateLinkedTokenSource(
            _sessionCancellation?.Token ??
            CancellationToken.None);

        _loadCancellation = source;

        var version = ++_loadVersion;
        var from = GetDate(StartDate!.Value);
        var to = GetDate(EndDate!.Value);

        var fields = _selectedPreset.Columns
            .Select(static column => column.Field)
            .ToArray();

        _loadError = null;
        _hasLoaded = false;
        _report = null;

        NotifyReportChanged();
        IsLoading = true;
        NotifyLoadState();

        try
        {
            var report = await Task.Run(
                () => _reports.GetReportAsync(
                    from,
                    to,
                    fields,
                    source.Token),
                source.Token);

            if (IsCurrentLoad(source, version))
                ApplyReport(report);
        }
        catch (OperationCanceledException)
            when (source.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (IsCurrentLoad(source, version))
            {
                _loadError = error;
                NotifyLoadState();
            }
        }
        finally
        {
            if (IsCurrentLoad(source, version))
            {
                _loadCancellation = null;
                IsLoading = false;
            }

            source.Dispose();
        }
    }

    private bool IsCurrentLoad(
        CancellationTokenSource source,
        int version)
    {
        return !_disposed &&
               _isActive &&
               _loadVersion == version &&
               ReferenceEquals(_loadCancellation, source) &&
               !source.IsCancellationRequested;
    }

    // ------------------------------------------------------------
    // Excel export
    // ------------------------------------------------------------

    public bool HasExportError => _exportError is not null;

    public bool IsExportSuccessful => _exportSucceeded;

    public string ExportMessage
    {
        get
        {
            if (_exportError is not null)
            {
                return string.Format(
                    Culture,
                    Text("ReportsExportFailed"),
                    _exportError.Message);
            }

            if (_exportSucceeded)
            {
                return string.Format(
                    Culture,
                    Text("ReportsExportCompleted"),
                    _exportedFileName);
            }

            return string.Empty;
        }
    }

    public void AttachSaveDialog(IReportSaveDialog saveDialog)
    {
        ArgumentNullException.ThrowIfNull(saveDialog);

        if (_disposed)
            return;

        _saveDialog = saveDialog;
        RefreshCommands();
    }

    public void DetachSaveDialog(IReportSaveDialog saveDialog)
    {
        if (!ReferenceEquals(_saveDialog, saveDialog))
            return;

        _saveDialog = null;
        RefreshCommands();
    }

    private bool CanExecuteExport() =>
        !_disposed &&
        _isActive &&
        IsPeriodValid &&
        IsLoaded &&
        !IsExporting &&
        !IsPresetBusy &&
        _selectedPreset is not null &&
        _saveDialog is not null;

    [RelayCommand(CanExecute = nameof(CanExecuteExport))]
    private async Task ExportAsync()
    {
        if (!CanExecuteExport())
            return;

        var dialog = _saveDialog!;
        var from = GetDate(StartDate!.Value);
        var to = GetDate(EndDate!.Value);

        using var source = CancellationTokenSource.CreateLinkedTokenSource(
            _sessionCancellation?.Token ??
            CancellationToken.None);

        _exportCancellation = source;

        IsExporting = true;
        ClearExportStatus();

        try
        {
            if (HasUnsavedChanges)
            {
                var state = await PersistEditsAsync(source.Token);

                if (!IsCurrentExport(source, from, to))
                    return;

                ApplyPresetState(
                    state,
                    preserveLoadedReport: true);
            }

            var preset = _selectedPreset
                ?? throw new InvalidOperationException(
                    "No report preset is selected.");

            var fileName = from.Day == 1 &&
                           to == EndOfMonth(from)
                ? $"Daycord-WorkLog-{from:yyyy-MM}.xlsx"
                : $"Daycord-WorkLog-" +
                  $"{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.xlsx";

            var target = await dialog.PickDestinationAsync(fileName);

            if (target is null)
                return;

            source.Token.ThrowIfCancellationRequested();

            var fields = preset.Columns
                .Select(static column => column.Field)
                .ToArray();

            var freshReport = await Task.Run(
                () => _reports.GetReportAsync(
                    from,
                    to,
                    fields,
                    source.Token),
                source.Token);

            if (!IsCurrentExport(source, from, to))
                return;

            ApplyReport(freshReport);

            // The exporter captures the language once and uses
            // that snapshot for both the automatic headers and
            // the report totals. SQLite tokens stay unchanged.
            await using (var stream = await target.OpenWriteAsync())
            {
                source.Token.ThrowIfCancellationRequested();

                await _exporter.ExportAsync(
                    freshReport,
                    preset,
                    stream,
                    source.Token);
            }

            if (!IsCurrentExport(source, from, to))
                return;

            _exportedFileName = target.DisplayName;
            _exportSucceeded = true;
            _exportError = null;

            NotifyExportStatus();
        }
        catch (OperationCanceledException)
            when (source.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (IsCurrentExport(source, from, to))
            {
                _exportError = error;
                _exportSucceeded = false;

                NotifyExportStatus();
            }
        }
        finally
        {
            if (ReferenceEquals(_exportCancellation, source))
            {
                _exportCancellation = null;
                IsExporting = false;
            }
        }
    }

    private bool IsCurrentExport(
        CancellationTokenSource source,
        DateOnly from,
        DateOnly to)
    {
        return !_disposed &&
               _isActive &&
               ReferenceEquals(_exportCancellation, source) &&
               !source.IsCancellationRequested &&
               IsPeriodValid &&
               GetDate(StartDate!.Value) == from &&
               GetDate(EndDate!.Value) == to;
    }

    private void ClearExportStatus()
    {
        _exportError = null;
        _exportSucceeded = false;
        _exportedFileName = string.Empty;

        NotifyExportStatus();
    }

    private void NotifyExportStatus()
    {
        OnPropertyChanged(nameof(HasExportError));
        OnPropertyChanged(nameof(IsExportSuccessful));
        OnPropertyChanged(nameof(ExportMessage));
    }

    // ------------------------------------------------------------
    // Localization
    // ------------------------------------------------------------

    private CultureInfo Culture =>
        CultureInfo.GetCultureInfo(
            _localization.CurrentLanguage);

    private string Text(string key) =>
        _localization.GetString(key);

    public string MonthlyReportLabel =>
        Text("ReportsMonthlyWorkReport");

    public string ReportingPeriodLabel =>
        Text("ReportsReportingPeriod");

    public string PreviousMonthLabel =>
        Text("ReportsPreviousMonth");

    public string NextMonthLabel =>
        Text("ReportsNextMonth");

    public string CurrentMonthLabel =>
        Text("ReportsCurrentMonth");

    public string WorkEntriesLabel =>
        Text("ReportsWorkEntries");

    public string TotalActualTimeLabel =>
        Text("ReportsTotalActualTime");

    public string TotalPlannedTimeLabel =>
        Text("ReportsTotalPlannedTime");

    public string ExportToExcelLabel =>
        Text("ReportsExportToExcel");

    public string ExportingLabel =>
        Text("ReportsExporting");

    public string LoadingLabel =>
        Text("ReportsLoading");

    public string NoWorkEntriesLabel =>
        Text("ReportsNoWorkEntries");

    public string RetryLabel =>
        Text("ReportsRetry");

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

        // Refresh the visible labels in existing editors.
        // Automatic tokens and custom headers stay unchanged.
        foreach (var editor in ColumnEditors)
        {
            editor.SetLanguage(_localization.CurrentLanguage);
        }

        string[] properties =
        [
            nameof(MonthTitle),
            nameof(PeriodErrorMessage),
            nameof(MonthlyReportLabel),
            nameof(ReportingPeriodLabel),
            nameof(PreviousMonthLabel),
            nameof(NextMonthLabel),
            nameof(CurrentMonthLabel),
            nameof(WorkEntriesLabel),
            nameof(TotalActualTimeLabel),
            nameof(TotalPlannedTimeLabel),
            nameof(TotalPlannedTimeText),
            nameof(TotalActualTimeText),
            nameof(ExportToExcelLabel),
            nameof(ExportingLabel),
            nameof(LoadingLabel),
            nameof(NoWorkEntriesLabel),
            nameof(RetryLabel),
            nameof(LoadErrorMessage),
            nameof(PresetErrorMessage),
            nameof(ExportMessage)
        ];

        foreach (var property in properties)
            OnPropertyChanged(property);
    }

    // ------------------------------------------------------------
    // Commands and cleanup
    // ------------------------------------------------------------

    private void RefreshCommands()
    {
        GoToCurrentMonthCommand.NotifyCanExecuteChanged();
        GoToPreviousMonthCommand.NotifyCanExecuteChanged();
        PreviousMonthCommand.NotifyCanExecuteChanged();
        NextMonthCommand.NotifyCanExecuteChanged();

        SaveColumnsCommand.NotifyCanExecuteChanged();
        CreatePresetCommand.NotifyCanExecuteChanged();
        DeletePresetCommand.NotifyCanExecuteChanged();
        ResetToDefaultCommand.NotifyCanExecuteChanged();

        ExportCommand.NotifyCanExecuteChanged();

        OnPropertyChanged(nameof(CanDeleteSelectedPreset));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Deactivate();
        _disposed = true;

        _localization.PropertyChanged -= OnLocalizationChanged;

        foreach (var editor in ColumnEditors)
            editor.PropertyChanged -= OnColumnEditorChanged;

        _saveDialog = null;
    }
}