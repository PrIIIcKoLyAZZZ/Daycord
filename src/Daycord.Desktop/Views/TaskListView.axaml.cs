using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Daycord.Desktop.ViewModels;

namespace Daycord.Desktop.Views;

public partial class TaskListView : UserControl
{
    // Keep a readable minimum; fill the available viewport above it.
    private const double MinimumTableWidth = 760;

    // State survives a recreated View, but not the lifetime of its VM.
    private static readonly ConditionalWeakTable<
        TaskListViewModel, ScrollSnapshot> SavedScroll = new();

    // A navigation-triggered focus request is kept separately from scroll:
    // scroll restoration must also work when no keyboard focus was requested.
    private static readonly ConditionalWeakTable<
        TaskListViewModel, ReturnFocusRequest> SavedReturnFocus = new();

    private static readonly string[] DateFormats =
    [
        "dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd", "yyyy/MM/dd",
        "MM/dd/yyyy", "M/d/yyyy", "dd/MM/yyyy", "d/M/yyyy",
        "MM.dd.yyyy", "M.d.yyyy"
    ];

    private readonly TextBox _fromInput;
    private readonly TextBox _toInput;
    private readonly Grid _fromDateSegment;
    private readonly Grid _toDateSegment;
    private readonly ScrollViewer _tableScroller;
    private readonly ListBox _taskTable;
    private readonly Button _cancelDeleteButton;
    private readonly Grid _tableContent;
    private readonly Popup _datePopup;
    private readonly UniformGrid _weekdayHeader;
    private readonly UniformGrid _calendarDaysGrid;
    private readonly TextBlock _calendarMonthTitle;
    private readonly Button _calendarTodayButton;

    private TextBox? _activeDateInput;
    private DateTime _visibleMonth = new(
        DateTime.Today.Year,
        DateTime.Today.Month,
        1);

    private TaskListViewModel? _loadedViewModel;
    private Control? _focusBeforeDeleteConfirmation;
    private bool _isAttached;
    private bool _suppressTextChanges;
    private bool _changingFromInput;
    private bool _changingToInput;

    public TaskListView()
    {
        AvaloniaXamlLoader.Load(this);

        _fromInput = Required<TextBox>("FromDateInput");
        _toInput = Required<TextBox>("ToDateInput");
        _fromDateSegment = Required<Grid>("FromDateSegment");
        _toDateSegment = Required<Grid>("ToDateSegment");
        _tableScroller = Required<ScrollViewer>("TableHorizontalScroll");
        _taskTable = Required<ListBox>("TaskTable");
        _cancelDeleteButton = Required<Button>("CancelDeleteButton");
        _tableContent = Required<Grid>("TableContent");
        _datePopup = Required<Popup>("DatePopup");
        _weekdayHeader = Required<UniformGrid>("WeekdayHeader");
        _calendarDaysGrid = Required<UniformGrid>("CalendarDaysGrid");
        _calendarMonthTitle = Required<TextBlock>("CalendarMonthTitle");
        _calendarTodayButton = Required<Button>("CalendarTodayButton");

        // Tunnel events let the table retain its native ListBox selection.
        // Only a deliberate double-click or Enter opens task details.
        AddHandler(
            InputElement.PointerPressedEvent,
            OnTaskRowPointerPressed,
            RoutingStrategies.Tunnel);

        AddHandler(
            InputElement.KeyDownEvent,
            OnTaskSafetyKeyDown,
            RoutingStrategies.Tunnel);

        AddHandler(
            InputElement.KeyDownEvent,
            OnTaskRowKeyDown,
            RoutingStrategies.Tunnel);

        _datePopup.PlacementTarget = _fromDateSegment;
        ResizeTable(_tableScroller.Bounds.Width);

        AttachedToVisualTree += (_, _) =>
        {
            _isAttached = true;
            ConnectViewModel();
        };

        DataContextChanged += (_, _) =>
        {
            if (_isAttached)
            {
                ConnectViewModel();
            }
        };

        DetachedFromVisualTree += (_, _) =>
        {
            SaveScrollState();
            _isAttached = false;
            _datePopup.IsOpen = false;
            _focusBeforeDeleteConfirmation = null;
            DisconnectViewModel();
        };
    }

    private T Required<T>(string name) where T : Control =>
        this.FindControl<T>(name)
        ?? throw new InvalidOperationException(
            $"Task List control '{name}' was not found.");

    private void ConnectViewModel()
    {
        if (DataContext is not TaskListViewModel viewModel)
        {
            DisconnectViewModel();
            return;
        }

        if (!ReferenceEquals(_loadedViewModel, viewModel))
        {
            DisconnectViewModel();

            _loadedViewModel = viewModel;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;

            SyncDateInput(_fromInput, viewModel.DateFrom);
            SyncDateInput(_toInput, viewModel.DateTo);
        }

        // Returning to Tasks reloads actual SQLite data.
        if (_isAttached && !_hasInitializedThisAttachment)
        {
            _hasInitializedThisAttachment = true;
            _ = InitializeAndRestoreAsync(viewModel);
        }
    }

    // The View is recreated by the main ContentControl on navigation.
    // Reload first, then restore only when the row order is still identical.
    private async Task InitializeAndRestoreAsync(TaskListViewModel viewModel)
    {
        await viewModel.InitializeAsync();

        if (!_isAttached ||
            !ReferenceEquals(_loadedViewModel, viewModel))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            RestoreScrollState(viewModel);
            RestoreNavigationFocus(viewModel);
        }, DispatcherPriority.Background);
    }

    private void SaveScrollState()
    {
        if (_loadedViewModel is not { } viewModel)
        {
            return;
        }

        var verticalScroller = FindTableVerticalScroller();
        if (verticalScroller is null)
        {
            return;
        }

        var snapshot = new ScrollSnapshot(
            _tableScroller.Offset.X,
            verticalScroller.Offset.Y,
            viewModel.Items.Select(row => row.Id).ToArray(),
            viewModel.SearchText,
            viewModel.SelectedStatus?.StatusId,
            viewModel.DateFrom,
            viewModel.DateTo,
            viewModel.SelectedSort?.Field.ToString(),
            viewModel.IsDescending);

        SavedScroll.Remove(viewModel);
        SavedScroll.Add(viewModel, snapshot);
    }

    private void RestoreScrollState(TaskListViewModel viewModel)
    {
        if (!_isAttached ||
            !ReferenceEquals(_loadedViewModel, viewModel) ||
            viewModel.IsLoading ||
            viewModel.HasLoadError ||
            !SavedScroll.TryGetValue(viewModel, out var snapshot))
        {
            return;
        }

        // Never apply an old pixel offset to changed search results,
        // sort order, or row sequence: it might land on another task.
        if (viewModel.SearchText != snapshot.SearchText ||
            viewModel.SelectedStatus?.StatusId != snapshot.StatusId ||
            viewModel.DateFrom != snapshot.DateFrom ||
            viewModel.DateTo != snapshot.DateTo ||
            viewModel.SelectedSort?.Field.ToString() != snapshot.SortField ||
            viewModel.IsDescending != snapshot.Descending ||
            viewModel.Items.Count != snapshot.ItemIds.Length)
        {
            return;
        }

        for (var index = 0; index < snapshot.ItemIds.Length; index++)
        {
            if (viewModel.Items[index].Id != snapshot.ItemIds[index])
            {
                return;
            }
        }

        var verticalScroller = FindTableVerticalScroller();
        if (verticalScroller is null)
        {
            return;
        }

        var horizontalMax = Math.Max(
            0, _tableScroller.Extent.Width - _tableScroller.Viewport.Width);
        var verticalMax = Math.Max(
            0, verticalScroller.Extent.Height - verticalScroller.Viewport.Height);

        _tableScroller.Offset = new Vector(
            Math.Clamp(snapshot.Horizontal, 0, horizontalMax),
            _tableScroller.Offset.Y);
        verticalScroller.Offset = new Vector(
            verticalScroller.Offset.X,
            Math.Clamp(snapshot.Vertical, 0, verticalMax));
    }

    private ScrollViewer? FindTableVerticalScroller() =>
        _taskTable.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault();

    private sealed record ScrollSnapshot(
        double Horizontal,
        double Vertical,
        Guid[] ItemIds,
        string SearchText,
        Guid? StatusId,
        DateTimeOffset? DateFrom,
        DateTimeOffset? DateTo,
        string? SortField,
        bool Descending);

    private sealed record ReturnFocusRequest(Guid TaskId, bool OnOpenButton);

    private void RestoreNavigationFocus(TaskListViewModel viewModel)
    {
        if (!_isAttached ||
            !ReferenceEquals(_loadedViewModel, viewModel) ||
            !SavedReturnFocus.TryGetValue(viewModel, out var request))
        {
            return;
        }

        // Consume exactly once. If the task disappeared or another control
        // received focus, never steal focus or select a different task.
        SavedReturnFocus.Remove(viewModel);

        var row = viewModel.SelectedTask;
        if (row?.Id != request.TaskId ||
            !viewModel.CanOpenTaskFromList(row))
        {
            return;
        }

        var focused = TopLevel.GetTopLevel(this)?
            .FocusManager?.GetFocusedElement() as Visual;

        if (focused is not null &&
            (ReferenceEquals(focused, this) ||
             focused.GetVisualAncestors().Contains(this)) &&
            !IsFromTaskTable(focused))
        {
            return;
        }

        _taskTable.ScrollIntoView(row);
        Dispatcher.UIThread.Post(
            () => FocusReturnedTask(viewModel, request, 2),
            DispatcherPriority.Background);
    }

    private void FocusReturnedTask(
        TaskListViewModel viewModel,
        ReturnFocusRequest request,
        int remainingAttempts)
    {
        if (!_isAttached ||
            !ReferenceEquals(_loadedViewModel, viewModel) ||
            viewModel.SelectedTask is not { } selected ||
            selected.Id != request.TaskId ||
            !viewModel.CanOpenTaskFromList(selected))
        {
            return;
        }

        var container = _taskTable.ContainerFromItem(selected) as ListBoxItem;
        if (container is null && remainingAttempts > 0)
        {
            _taskTable.ScrollIntoView(selected);
            Dispatcher.UIThread.Post(
                () => FocusReturnedTask(
                    viewModel, request, remainingAttempts - 1),
                DispatcherPriority.Background);
            return;
        }

        if (container is null)
        {
            _taskTable.Focus(NavigationMethod.Tab);
            return;
        }

        if (request.OnOpenButton)
        {
            var button = container.GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(x => x.Classes.Contains("task-open"));

            if (button?.Focus(NavigationMethod.Tab) == true)
            {
                return;
            }
        }

        container.Focus(NavigationMethod.Tab);
    }

    private bool _hasInitializedThisAttachment;

    private void DisconnectViewModel()
    {
        if (_loadedViewModel is not null)
        {
            _loadedViewModel.PropertyChanged -=
                OnViewModelPropertyChanged;
        }

        _loadedViewModel = null;
        _hasInitializedThisAttachment = false;
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (_loadedViewModel is not { } viewModel)
        {
            return;
        }

        if (e.PropertyName == nameof(TaskListViewModel.DateFrom) &&
            !_changingFromInput)
        {
            SyncDateInput(_fromInput, viewModel.DateFrom);
        }
        else if (
            e.PropertyName == nameof(TaskListViewModel.DateTo) &&
            !_changingToInput)
        {
            SyncDateInput(_toInput, viewModel.DateTo);
        }
        else if (
            e.PropertyName ==
                nameof(TaskListViewModel.AreFiltersExpanded) &&
            !viewModel.AreFiltersExpanded)
        {
            _datePopup.IsOpen = false;
        }
        else if (e.PropertyName ==
                 nameof(TaskListViewModel.IsDeleteConfirmationOpen))
        {
            UpdateDeleteConfirmationFocus(viewModel);
        }
    }

    // While deletion is pending, move focus into the confirmation.
    private void UpdateDeleteConfirmationFocus(TaskListViewModel viewModel)
    {
        if (viewModel.IsDeleteConfirmationOpen)
        {
            _datePopup.IsOpen = false;
            _focusBeforeDeleteConfirmation =
                TopLevel.GetTopLevel(this)?.FocusManager?
                    .GetFocusedElement() as Control;

            Dispatcher.UIThread.Post(() =>
            {
                if (_isAttached &&
                    ReferenceEquals(_loadedViewModel, viewModel) &&
                    viewModel.IsDeleteConfirmationOpen)
                {
                    _cancelDeleteButton.Focus();
                }
            }, DispatcherPriority.Background);

            return;
        }

        var previousFocus = _focusBeforeDeleteConfirmation;
        _focusBeforeDeleteConfirmation = null;

        Dispatcher.UIThread.Post(() =>
        {
            if (!_isAttached ||
                !ReferenceEquals(_loadedViewModel, viewModel) ||
                viewModel.IsDeleteConfirmationOpen)
            {
                return;
            }

            if (previousFocus is { IsVisible: true, IsEnabled: true } &&
                previousFocus.Focus())
            {
                return;
            }

            _taskTable.Focus();
        }, DispatcherPriority.Background);
    }

    // Escape is scoped to Tasks and never performs a destructive action.
    private void OnTaskSafetyKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (e.Handled ||
            e.Key != Key.Escape ||
            _loadedViewModel is not { } viewModel)
        {
            return;
        }

        if (viewModel.IsDeleteConfirmationOpen)
        {
            e.Handled = true;

            if (viewModel.CancelDeleteCommand.CanExecute(null))
            {
                viewModel.CancelDeleteCommand.Execute(null);
            }

            return;
        }

        if (_datePopup.IsOpen)
        {
            _datePopup.IsOpen = false;
            e.Handled = true;
            return;
        }

        if (IsFromTaskTable(e.Source))
        {
            viewModel.SelectedTask = null;
            e.Handled = true;
        }
    }

    private bool IsFromTaskTable(object? source) =>
        source is Visual visual &&
        (ReferenceEquals(visual, _taskTable) ||
         visual.GetVisualAncestors().Contains(_taskTable));

    private void OnCalendarPopupKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape)
        {
            return;
        }

        _datePopup.IsOpen = false;
        e.Handled = true;
        _activeDateInput?.Focus();
    }

    // Opening a task from the table

    private void OnTaskRowPointerPressed(
        object? sender,
        PointerPressedEventArgs e)
    {
        if (e.Handled ||
            e.ClickCount != 2 ||
            IsOpenButtonEvent(e.Source) ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // A double-click on empty table space must never open
        // an unrelated, previously selected task.
        var row = FindTaskRow(
            e.Source,
            allowSelectedFallback: false);

        OpenTaskRow(row, e);
    }

    private void OnTaskRowKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (e.Handled ||
            e.Key != Key.Enter ||
            IsOpenButtonEvent(e.Source))
        {
            return;
        }

        // Enter is handled only when focus is in the task table,
        // not in search, filters, calendar or delete confirmation.
        var row = FindTaskRow(
            e.Source,
            allowSelectedFallback: true);

        OpenTaskRow(row, e);
    }

    private void OnOpenTaskClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TaskListRowViewModel row })
        {
            OpenTaskRow(row, e, preferButtonFocus: true);
        }
    }

    private static bool IsOpenButtonEvent(object? source) =>
        source is Visual visual &&
        visual.GetVisualAncestors()
            .Prepend(visual)
            .OfType<Button>()
            .Any(button => button.Classes.Contains("task-open"));

    private void OpenTaskRow(
        TaskListRowViewModel? row,
        RoutedEventArgs e,
        bool preferButtonFocus = false)
    {
        if (_loadedViewModel is not { } viewModel ||
            !viewModel.CanOpenTaskFromList(row) ||
            row is null)
        {
            return;
        }

        e.Handled = true;
        _datePopup.IsOpen = false;

        SavedReturnFocus.Remove(viewModel);
        SavedReturnFocus.Add(
            viewModel, new ReturnFocusRequest(row.Id, preferButtonFocus));

        viewModel.OpenTaskFromList(row);
    }

    private static TaskListRowViewModel? FindTaskRow(
        object? source,
        bool allowSelectedFallback)
    {
        if (source is not Visual visual)
        {
            return null;
        }

        var visualPath = visual.GetVisualAncestors()
            .Prepend(visual)
            .ToArray();

        var table = visualPath
            .OfType<ListBox>()
            .FirstOrDefault(
                list => list.Classes.Contains("task-table"));

        if (table is null)
        {
            return null;
        }

        var row = visualPath
            .OfType<ListBoxItem>()
            .FirstOrDefault()
            ?.DataContext as TaskListRowViewModel;

        return row ?? (allowSelectedFallback
            ? table.SelectedItem as TaskListRowViewModel
            : null);
    }

    // Date input

    private void OnDateTextChanged(
        object? sender,
        TextChangedEventArgs e)
    {
        if (_suppressTextChanges ||
            sender is not TextBox input ||
            DataContext is not TaskListViewModel viewModel)
        {
            return;
        }

        var text = input.Text?.Trim() ?? string.Empty;

        bool valid =
            text.Length == 0 ||
            TryReadDate(text, out _);

        SetInvalid(input, !valid);

        DateTimeOffset? date =
            TryReadDate(text, out var parsed)
                ? new DateTimeOffset(
                    parsed.Year,
                    parsed.Month,
                    parsed.Day,
                    0, 0, 0,
                    TimeSpan.Zero)
                : null;

        if (ReferenceEquals(input, _fromInput))
        {
            _changingFromInput = true;

            try
            {
                viewModel.DateFrom = date;
            }
            finally
            {
                _changingFromInput = false;
            }
        }
        else if (ReferenceEquals(input, _toInput))
        {
            _changingToInput = true;

            try
            {
                viewModel.DateTo = date;
            }
            finally
            {
                _changingToInput = false;
            }
        }
    }

    private static bool TryReadDate(
        string? text,
        out DateTime value)
    {
        if (!string.IsNullOrWhiteSpace(text) &&
            DateTime.TryParseExact(
                text.Trim(),
                DateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out value))
        {
            value = value.Date;
            return true;
        }

        value = default;
        return false;
    }

    private static void SetInvalid(
        TextBox input,
        bool invalid)
    {
        if (invalid)
        {
            if (!input.Classes.Contains("invalid"))
            {
                input.Classes.Add("invalid");
            }

            ToolTip.SetTip(
                input,
                "Enter a date, for example 25.11.2025");
        }
        else
        {
            input.Classes.Remove("invalid");
            ToolTip.SetTip(input, null);
        }
    }

    private void SyncDateInput(
        TextBox input,
        DateTimeOffset? value)
    {
        _suppressTextChanges = true;

        try
        {
            input.Text = value?.Date.ToString(
                "dd.MM.yyyy",
                CultureInfo.InvariantCulture)
                ?? string.Empty;

            SetInvalid(input, false);
        }
        finally
        {
            _suppressTextChanges = false;
        }
    }

    private void OnDateLostFocus(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is not TextBox input ||
            DataContext is not TaskListViewModel vm)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(input.Text) ||
            TryReadDate(input.Text, out _))
        {
            SyncDateInput(
                input,
                ReferenceEquals(input, _fromInput)
                    ? vm.DateFrom
                    : vm.DateTo);
        }
    }

    private void OnDateKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.Enter ||
            sender is not TextBox input)
        {
            return;
        }

        e.Handled = true;

        if (DataContext is TaskListViewModel vm &&
            (string.IsNullOrWhiteSpace(input.Text) ||
             TryReadDate(input.Text, out _)))
        {
            SyncDateInput(
                input,
                ReferenceEquals(input, _fromInput)
                    ? vm.DateFrom
                    : vm.DateTo);
        }
    }

    private void OnResetFiltersClick(
        object? sender,
        RoutedEventArgs e)
    {
        // Also clears invalid/incomplete typed text.
        SyncDateInput(_fromInput, null);
        SyncDateInput(_toInput, null);

        _datePopup.IsOpen = false;
    }

    // Custom Graphite & Amber calendar

    private void OnFromCalendarClick(
        object? sender,
        RoutedEventArgs e) =>
        ToggleCalendar(_fromInput, _fromDateSegment);

    private void OnToCalendarClick(
        object? sender,
        RoutedEventArgs e) =>
        ToggleCalendar(_toInput, _toDateSegment);

    private void ToggleCalendar(
        TextBox input,
        Grid target)
    {
        if (_datePopup.IsOpen &&
            ReferenceEquals(_activeDateInput, input))
        {
            _datePopup.IsOpen = false;
            return;
        }

        _datePopup.IsOpen = false;
        _activeDateInput = input;
        _datePopup.PlacementTarget = target;

        var selected = ReferenceEquals(input, _fromInput)
            ? _loadedViewModel?.DateFrom?.Date
            : _loadedViewModel?.DateTo?.Date;

        var anchor = selected ?? DateTime.Today;

        _visibleMonth = new DateTime(
            anchor.Year,
            anchor.Month,
            1);

        _calendarTodayButton.Content =
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru"
                ? "Сегодня"
                : "Today";

        RenderCalendar();

        _datePopup.IsOpen = true;
    }

    private void OnPreviousMonthClick(
        object? sender,
        RoutedEventArgs e) =>
        ChangeMonth(-1);

    private void OnNextMonthClick(
        object? sender,
        RoutedEventArgs e) =>
        ChangeMonth(1);

    private void ChangeMonth(int step)
    {
        try
        {
            _visibleMonth = _visibleMonth.AddMonths(step);
            RenderCalendar();
        }
        catch (ArgumentOutOfRangeException)
        {
            // Reached DateTime range boundary.
        }
    }

    private void RenderCalendar()
    {
        var culture = CultureInfo.CurrentCulture;

        _calendarMonthTitle.Text =
            _visibleMonth.ToString("MMMM yyyy", culture);

        _weekdayHeader.Children.Clear();

        for (int i = 0; i < 7; i++)
        {
            var weekday = (DayOfWeek)((i + 1) % 7);

            var label = new TextBlock
            {
                Text = culture.DateTimeFormat
                    .GetShortestDayName(weekday),

                HorizontalAlignment = HorizontalAlignment.Center
            };

            label.Classes.Add("calendar-weekday");
            _weekdayHeader.Children.Add(label);
        }

        var offset =
            ((int)_visibleMonth.DayOfWeek + 6) % 7;

        DateTime first;

        try
        {
            first = _visibleMonth.AddDays(-offset);
        }
        catch (ArgumentOutOfRangeException)
        {
            first = _visibleMonth;
        }

        var selected = ReferenceEquals(
            _activeDateInput,
            _fromInput)
                ? _loadedViewModel?.DateFrom?.Date
                : _loadedViewModel?.DateTo?.Date;

        _calendarDaysGrid.Children.Clear();

        for (int i = 0; i < 42; i++)
        {
            DateTime day;

            try
            {
                day = first.AddDays(i);
            }
            catch (ArgumentOutOfRangeException)
            {
                break;
            }

            var button = new Button
            {
                Content = day.Day.ToString(
                    CultureInfo.InvariantCulture),

                Tag = day,

                HorizontalAlignment =
                    HorizontalAlignment.Stretch,

                HorizontalContentAlignment =
                    HorizontalAlignment.Center
            };

            button.Classes.Add("calendar-day");

            if (day.Month != _visibleMonth.Month ||
                day.Year != _visibleMonth.Year)
            {
                button.Classes.Add("outside");
            }

            if (day.Date == DateTime.Today)
            {
                button.Classes.Add("today");
            }

            if (selected.HasValue &&
                day.Date == selected.Value)
            {
                button.Classes.Add("selected");
            }

            button.Click += OnCalendarDayClick;

            _calendarDaysGrid.Children.Add(button);
        }
    }

    private void OnCalendarDayClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is Button { Tag: DateTime day })
        {
            SetCalendarDate(day);
        }
    }

    private void OnTodayClick(
        object? sender,
        RoutedEventArgs e) =>
        SetCalendarDate(DateTime.Today);

    private void SetCalendarDate(DateTime day)
    {
        if (_activeDateInput is null ||
            _loadedViewModel is not { } vm)
        {
            return;
        }

        var value = new DateTimeOffset(
            day.Year,
            day.Month,
            day.Day,
            0, 0, 0,
            TimeSpan.Zero);

        if (ReferenceEquals(_activeDateInput, _fromInput))
        {
            vm.DateFrom = value;
            SyncDateInput(_fromInput, value);
        }
        else
        {
            vm.DateTo = value;
            SyncDateInput(_toInput, value);
        }

        _datePopup.IsOpen = false;
    }

    // Responsive table

    private void OnTableViewportSizeChanged(
        object? sender,
        SizeChangedEventArgs e) =>
        ResizeTable(e.NewSize.Width);

    private void ResizeTable(double width)
    {
        if (double.IsFinite(width))
        {
            _tableContent.Width = Math.Max(
                MinimumTableWidth,
                width);
        }
    }
}