using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Daycord.Desktop.Reports;
using Daycord.Desktop.ViewModels;

namespace Daycord.Desktop.Views;

public partial class ReportsView :
    UserControl,
    IReportSaveDialog
{
    private static readonly string[] DateFormats =
    [
        "dd.MM.yyyy",
        "d.M.yyyy",
        "yyyy-MM-dd",
        "yyyy/MM/dd"
    ];

    private static readonly FilePickerFileType ExcelFileType =
        new("Excel Workbook (*.xlsx)")
        {
            Patterns = ["*.xlsx"],
            MimeTypes =
            [
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            ]
        };

    private readonly ScrollViewer _scroll;
    private readonly StackPanel _mainPage;
    private readonly StackPanel _settingsPage;

    private readonly StackPanel _createPresetEditor;
    private readonly StackPanel _renamePresetEditor;

    private readonly TextBox _fromInput;
    private readonly TextBox _toInput;

    private readonly Grid _fromSegment;
    private readonly Grid _toSegment;

    private readonly DaycordCalendarView _calendar;

    private readonly Grid _deleteConfirmation;
    private readonly TextBlock _deleteMessage;
    private readonly Button _confirmDeletionButton;
    private readonly Button _cancelDeletionButton;

    private ReportsViewModel? _connectedViewModel;
    private ReportSaveTarget? _lastTarget;

    private TextBox? _activeCalendarInput;

    private Guid? _pendingDeletePresetId;

    private bool _isAttached;
    private bool _updatingDates;
    private bool _isLeavingSettings;
    private bool _isDeletingPreset;

    public ReportsView()
    {
        AvaloniaXamlLoader.Load(this);

        _scroll = Required<ScrollViewer>("ReportsScroll");

        _mainPage = Required<StackPanel>("MainReportPage");
        _settingsPage = Required<StackPanel>("ExportSettingsPage");

        _createPresetEditor =
            Required<StackPanel>("CreatePresetEditor");

        _renamePresetEditor =
            Required<StackPanel>("RenamePresetEditor");

        _fromInput = Required<TextBox>("ReportFromInput");
        _toInput = Required<TextBox>("ReportToInput");

        _fromSegment = Required<Grid>("ReportFromSegment");
        _toSegment = Required<Grid>("ReportToSegment");

        _calendar =
            Required<DaycordCalendarView>("ReportCalendar");

        _deleteConfirmation =
            Required<Grid>("DeletePresetConfirmation");

        _deleteMessage =
            Required<TextBlock>("DeletePresetMessage");

        _confirmDeletionButton =
            Required<Button>("ConfirmPresetDeletionButton");

        _cancelDeletionButton =
            Required<Button>("CancelPresetDeletionButton");

        _calendar.DateSelected += OnCalendarDateSelected;

        AttachedToVisualTree += (_, _) =>
        {
            _isAttached = true;
            ConnectViewModel();
        };

        DetachedFromVisualTree += (_, _) =>
        {
            _isAttached = false;

            _calendar.Close();
            HideDeleteConfirmation();

            DisconnectViewModel();
            ReleaseUnusedTarget();

            ShowMainPage();
        };

        DataContextChanged += (_, _) =>
        {
            if (_isAttached)
                ConnectViewModel();
        };
    }

    private T Required<T>(string name)
        where T : Control
    {
        return this.FindControl<T>(name)
            ?? throw new InvalidOperationException(
                $"Reports control '{name}' was not found.");
    }

    // ------------------------------------------------------------
    // ViewModel connection
    // ------------------------------------------------------------

    private void ConnectViewModel()
    {
        if (ReferenceEquals(_connectedViewModel, DataContext))
        {
            SynchronizeDateInputs();
            return;
        }

        HideDeleteConfirmation();
        DisconnectViewModel();

        if (DataContext is not ReportsViewModel viewModel)
            return;

        _connectedViewModel = viewModel;

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.AttachSaveDialog(this);

        SynchronizeDateInputs();
    }

    private void DisconnectViewModel()
    {
        _calendar.Close();

        if (_connectedViewModel is not { } viewModel)
            return;

        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        viewModel.DetachSaveDialog(this);

        _connectedViewModel = null;
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (sender is not ReportsViewModel viewModel ||
            !ReferenceEquals(viewModel, _connectedViewModel))
        {
            return;
        }

        if (string.IsNullOrEmpty(e.PropertyName) ||
            e.PropertyName is
                nameof(ReportsViewModel.StartDate) or
                nameof(ReportsViewModel.EndDate))
        {
            SynchronizeDateInputs();
        }

        if (e.PropertyName ==
            nameof(ReportsViewModel.SelectedPreset))
        {
            HidePresetEditors();
            viewModel.NewPresetName = string.Empty;

            // A successfully deleted preset is no longer selected.
            if (_pendingDeletePresetId.HasValue &&
                viewModel.SelectedPreset?.Id !=
                    _pendingDeletePresetId.Value)
            {
                HideDeleteConfirmation();
            }
        }

        if (_calendar.IsOpen &&
            (string.IsNullOrEmpty(e.PropertyName) ||
             e.PropertyName is
                 nameof(ReportsViewModel.StartDate) or
                 nameof(ReportsViewModel.EndDate) or
                 nameof(ReportsViewModel.MonthTitle)))
        {
            var selected = ReferenceEquals(
                _activeCalendarInput,
                _fromInput)
                ? viewModel.StartDate?.Date
                : viewModel.EndDate?.Date;

            _calendar.Refresh(
                selected,
                viewModel.CalendarCulture);
        }
    }

    // ------------------------------------------------------------
    // Nested navigation
    // ------------------------------------------------------------

    private void OnOpenSettingsClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (_isLeavingSettings ||
            _isDeletingPreset ||
            _connectedViewModel is not { IsExporting: false })
        {
            return;
        }

        _calendar.Close();

        _mainPage.IsVisible = false;
        _settingsPage.IsVisible = true;

        ResetScroll();
    }

    private async void OnBackToReportClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (_isLeavingSettings ||
            _isDeletingPreset ||
            _deleteConfirmation.IsVisible)
        {
            return;
        }

        var viewModel = _connectedViewModel;

        if (viewModel is null)
        {
            ShowMainPage();
            return;
        }

        if (viewModel.IsExporting || viewModel.IsPresetBusy)
            return;

        _isLeavingSettings = true;

        try
        {
            if (viewModel.HasUnsavedChanges)
            {
                if (!viewModel.SaveColumnsCommand.CanExecute(null))
                    return;

                await viewModel.SaveColumnsCommand.ExecuteAsync(null);

                if (!_isAttached ||
                    !ReferenceEquals(_connectedViewModel, viewModel))
                {
                    return;
                }

                if (viewModel.HasUnsavedChanges ||
                    viewModel.HasPresetError)
                {
                    return;
                }
            }

            viewModel.NewPresetName = string.Empty;
            ShowMainPage();
        }
        finally
        {
            _isLeavingSettings = false;
        }
    }

    private void ShowMainPage()
    {
        _calendar.Close();
        HideDeleteConfirmation();

        _settingsPage.IsVisible = false;
        _mainPage.IsVisible = true;

        HidePresetEditors();
        ResetScroll();
    }

    private void ResetScroll()
    {
        _scroll.Offset = new Vector(0, 0);
    }

    // ------------------------------------------------------------
    // Preset editing
    // ------------------------------------------------------------

    private void OnShowCreatePresetClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (_isLeavingSettings ||
            _deleteConfirmation.IsVisible ||
            _connectedViewModel is not
                { IsPresetBusy: false, IsExporting: false }
                    viewModel)
        {
            return;
        }

        var shouldOpen = !_createPresetEditor.IsVisible;

        _renamePresetEditor.IsVisible = false;
        _createPresetEditor.IsVisible = shouldOpen;

        if (shouldOpen)
            viewModel.NewPresetName = string.Empty;
    }

    private void OnShowRenamePresetClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (_isLeavingSettings ||
            _deleteConfirmation.IsVisible ||
            _connectedViewModel is not
                { CanDeleteSelectedPreset: true }
                    viewModel)
        {
            return;
        }

        var shouldOpen = !_renamePresetEditor.IsVisible;

        _createPresetEditor.IsVisible = false;
        _renamePresetEditor.IsVisible = shouldOpen;

        if (shouldOpen &&
            viewModel.SelectedPreset is { } preset)
        {
            viewModel.PresetNameDraft = preset.Name;
        }
    }

    private void OnCancelPresetEditClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (_isLeavingSettings ||
            _deleteConfirmation.IsVisible ||
            _connectedViewModel is not { } viewModel)
        {
            return;
        }

        viewModel.NewPresetName = string.Empty;

        if (viewModel.SelectedPreset is { } preset)
            viewModel.PresetNameDraft = preset.Name;

        HidePresetEditors();
    }

    private void HidePresetEditors()
    {
        _createPresetEditor.IsVisible = false;
        _renamePresetEditor.IsVisible = false;
    }

    // ------------------------------------------------------------
    // Delete preset confirmation
    // ------------------------------------------------------------

    private void OnDeletePresetClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (_isLeavingSettings ||
            _isDeletingPreset ||
            _deleteConfirmation.IsVisible ||
            _connectedViewModel is not
                { CanDeleteSelectedPreset: true }
                    viewModel ||
            viewModel.SelectedPreset is not
                { IsBuiltIn: false } preset)
        {
            return;
        }

        _pendingDeletePresetId = preset.Id;

        var russian =
            viewModel.CalendarCulture.TwoLetterISOLanguageName
            == "ru";

        _deleteMessage.Text = russian
            ? $"Удалить пресет «{preset.Name}»? " +
              "Его настройки столбцов будут удалены. " +
              "Это действие нельзя отменить."
            : $"Delete preset “{preset.Name}”? " +
              "Its column settings will be removed. " +
              "This action cannot be undone.";

        _confirmDeletionButton.IsEnabled = true;
        _cancelDeletionButton.IsEnabled = true;

        _deleteConfirmation.IsVisible = true;
        _deleteConfirmation.Focus();
    }

    private void OnCancelDeletePresetClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (_isDeletingPreset)
            return;

        HideDeleteConfirmation();
    }

    private async void OnConfirmDeletePresetClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (_isDeletingPreset ||
            !_pendingDeletePresetId.HasValue ||
            _connectedViewModel is not { } viewModel ||
            viewModel.SelectedPreset is not { } preset ||
            preset.IsBuiltIn ||
            preset.Id != _pendingDeletePresetId.Value ||
            !viewModel.DeletePresetCommand.CanExecute(null))
        {
            return;
        }

        _isDeletingPreset = true;

        _confirmDeletionButton.IsEnabled = false;
        _cancelDeletionButton.IsEnabled = false;

        try
        {
            // The ViewModel and SQLite repository remain
            // responsible for the actual deletion.
            await viewModel.DeletePresetCommand.ExecuteAsync(null);

            if (!_isAttached ||
                !ReferenceEquals(viewModel, _connectedViewModel))
            {
                return;
            }

            if (!viewModel.HasPresetError)
                HideDeleteConfirmation();

            // On failure the confirmation stays open and
            // displays PresetErrorMessage through its binding.
        }
        finally
        {
            _isDeletingPreset = false;

            _confirmDeletionButton.IsEnabled = true;
            _cancelDeletionButton.IsEnabled = true;
        }
    }

    private void OnDeleteConfirmationKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.Escape &&
            _deleteConfirmation.IsVisible &&
            !_isDeletingPreset)
        {
            HideDeleteConfirmation();
            e.Handled = true;
        }
    }

    private void HideDeleteConfirmation()
    {
        _pendingDeletePresetId = null;
        _deleteConfirmation.IsVisible = false;
    }

    // ------------------------------------------------------------
    // Text date input
    // ------------------------------------------------------------

    private void SynchronizeDateInputs()
    {
        if (_updatingDates ||
            _connectedViewModel is not { } viewModel)
        {
            return;
        }

        _updatingDates = true;

        try
        {
            _fromInput.Text = FormatDate(viewModel.StartDate);
            _toInput.Text = FormatDate(viewModel.EndDate);
        }
        finally
        {
            _updatingDates = false;
        }
    }

    private static string FormatDate(DateTimeOffset? date)
    {
        return date.HasValue
            ? date.Value.ToString(
                "dd.MM.yyyy",
                CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private void OnDateInputKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;

        if (sender is TextBox input)
            CommitDateInput(input);
    }

    private void OnDateInputLostFocus(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is TextBox input)
            CommitDateInput(input);
    }

    private void CommitDateInput(TextBox input)
    {
        if (_updatingDates ||
            _connectedViewModel is not { } viewModel ||
            viewModel.IsExporting)
        {
            return;
        }

        var isFrom = ReferenceEquals(input, _fromInput);
        var isTo = ReferenceEquals(input, _toInput);

        if (!isFrom && !isTo)
            return;

        var isValid = TryParseDate(input.Text, out var date);

        _updatingDates = true;

        try
        {
            DateTimeOffset? selected = isValid
                ? ToOffsetDate(date)
                : null;

            if (isFrom)
                viewModel.StartDate = selected;
            else
                viewModel.EndDate = selected;

            if (isValid)
            {
                input.Text = date.ToString(
                    "dd.MM.yyyy",
                    CultureInfo.InvariantCulture);
            }
        }
        finally
        {
            _updatingDates = false;
        }
    }

    private static bool TryParseDate(
        string? text,
        out DateTime date)
    {
        return DateTime.TryParseExact(
            text?.Trim(),
            DateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    private static DateTimeOffset ToOffsetDate(DateTime date)
    {
        var local = new DateTime(
            date.Year,
            date.Month,
            date.Day,
            0,
            0,
            0,
            DateTimeKind.Unspecified);

        return new DateTimeOffset(
            local,
            TimeZoneInfo.Local.GetUtcOffset(local));
    }

    // ------------------------------------------------------------
    // Shared Daycord calendar
    // ------------------------------------------------------------

    private void OnFromCalendarClick(
        object? sender,
        RoutedEventArgs e)
    {
        OpenCalendar(_fromInput, _fromSegment);
    }

    private void OnToCalendarClick(
        object? sender,
        RoutedEventArgs e)
    {
        OpenCalendar(_toInput, _toSegment);
    }

    private void OpenCalendar(
        TextBox input,
        Grid segment)
    {
        if (_connectedViewModel is not { } viewModel ||
            viewModel.IsExporting ||
            _deleteConfirmation.IsVisible)
        {
            return;
        }

        if (_calendar.IsOpen &&
            ReferenceEquals(_activeCalendarInput, input))
        {
            _calendar.Close();
            return;
        }

        _activeCalendarInput = input;

        DateTime? selected = ReferenceEquals(input, _fromInput)
            ? viewModel.StartDate?.Date
            : viewModel.EndDate?.Date;

        if (TryParseDate(input.Text, out var enteredDate))
            selected = enteredDate;

        _calendar.Open(
            placementTarget: segment,
            anchorDate: selected ?? DateTime.Today,
            selectedDate: selected,
            culture: viewModel.CalendarCulture,
            placement: PlacementMode.BottomEdgeAlignedLeft);
    }

    private void OnCalendarDateSelected(
        object? sender,
        CalendarDateSelectedEventArgs e)
    {
        if (_connectedViewModel is not { } viewModel)
            return;

        var selected = ToOffsetDate(e.Date);

        if (ReferenceEquals(_activeCalendarInput, _fromInput))
            viewModel.StartDate = selected;
        else if (ReferenceEquals(_activeCalendarInput, _toInput))
            viewModel.EndDate = selected;

        _calendar.Close();
        SynchronizeDateInputs();
    }

    // ------------------------------------------------------------
    // Native Save File Picker
    // ------------------------------------------------------------

    public async Task<IReportSaveTarget?> PickDestinationAsync(
        string suggestedFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            suggestedFileName);

        if (!_isAttached)
        {
            throw new InvalidOperationException(
                "Reports is not attached to a window.");
        }

        var topLevel = TopLevel.GetTopLevel(this);

        if (topLevel is null)
        {
            throw new InvalidOperationException(
                "Cannot access the storage provider.");
        }

        ReleaseUnusedTarget();

        var options = new FilePickerSaveOptions
        {
            Title = _connectedViewModel?
                .ExportToExcelLabel ?? "Export to Excel",

            SuggestedFileName = suggestedFileName,
            DefaultExtension = "xlsx",
            FileTypeChoices = [ExcelFileType],
            SuggestedFileType = ExcelFileType,
            ShowOverwritePrompt = true
        };

        var file = await topLevel.StorageProvider
            .SaveFilePickerAsync(options);

        if (file is null)
            return null;

        if (!_isAttached)
        {
            file.Dispose();
            return null;
        }

        var target = new ReportSaveTarget(file);
        _lastTarget = target;

        return target;
    }

    private void ReleaseUnusedTarget()
    {
        _lastTarget?.DisposeIfUnused();
        _lastTarget = null;
    }

    // ------------------------------------------------------------
    // Storage target
    // ------------------------------------------------------------

    private sealed class ReportSaveTarget : IReportSaveTarget
    {
        private readonly IStorageFile _file;

        private bool _opened;
        private bool _released;

        public ReportSaveTarget(IStorageFile file)
        {
            _file = file
                ?? throw new ArgumentNullException(nameof(file));
        }

        public string DisplayName => _file.Name;

        public async Task<Stream> OpenWriteAsync()
        {
            if (_opened || _released)
            {
                throw new InvalidOperationException(
                    "The destination cannot be opened twice.");
            }

            _opened = true;

            try
            {
                var stream = await _file.OpenWriteAsync();

                return new OwnedStorageStream(
                    stream,
                    _file);
            }
            catch
            {
                ReleaseFile();
                throw;
            }
        }

        public void DisposeIfUnused()
        {
            if (!_opened)
                ReleaseFile();
        }

        private void ReleaseFile()
        {
            if (_released)
                return;

            _released = true;
            _file.Dispose();
        }
    }

    // ------------------------------------------------------------
    // Stream ownership
    // ------------------------------------------------------------

    private sealed class OwnedStorageStream : Stream
    {
        private readonly Stream _inner;
        private readonly IStorageFile _file;

        private bool _released;

        public OwnedStorageStream(
            Stream inner,
            IStorageFile file)
        {
            _inner = inner
                ?? throw new ArgumentNullException(nameof(inner));

            _file = file
                ?? throw new ArgumentNullException(nameof(file));
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() =>
            _inner.Flush();

        public override Task FlushAsync(
            CancellationToken cancellationToken) =>
            _inner.FlushAsync(cancellationToken);

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            _inner.Read(buffer, offset, count);

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            _inner.ReadAsync(
                buffer,
                offset,
                count,
                cancellationToken);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(
                buffer,
                cancellationToken);

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            _inner.Write(buffer, offset, count);

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            _inner.WriteAsync(
                buffer,
                offset,
                count,
                cancellationToken);

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            _inner.WriteAsync(
                buffer,
                cancellationToken);

        public override long Seek(
            long offset,
            SeekOrigin origin) =>
            _inner.Seek(offset, origin);

        public override void SetLength(long value) =>
            _inner.SetLength(value);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    _inner.Dispose();
                }
                finally
                {
                    ReleaseFile();
                }
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            try
            {
                await _inner.DisposeAsync()
                    .ConfigureAwait(false);
            }
            finally
            {
                ReleaseFile();
            }

            GC.SuppressFinalize(this);
        }

        private void ReleaseFile()
        {
            if (_released)
                return;

            _released = true;
            _file.Dispose();
        }
    }
}