using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Daycord.Desktop.ViewModels;

namespace Daycord.Desktop.Views;

public partial class TaskListView : UserControl
{
    // Keep a readable minimum; fill the available viewport above it.
    private const double MinimumTableWidth = 760;

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
            _isAttached = false;
            _datePopup.IsOpen = false;
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
            _ = viewModel.InitializeAsync();
        }
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
        else if (
            e.PropertyName ==
                nameof(TaskListViewModel.IsDeleteConfirmationOpen) &&
            viewModel.IsDeleteConfirmationOpen)
        {
            _datePopup.IsOpen = false;
        }
    }

    // Opening a task from the table

    private void OnTaskRowPointerPressed(
        object? sender,
        PointerPressedEventArgs e)
    {
        if (e.Handled ||
            e.ClickCount != 2 ||
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
        if (e.Handled || e.Key != Key.Enter)
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

    private void OpenTaskRow(
        TaskListRowViewModel? row,
        RoutedEventArgs e)
    {
        if (row is null ||
            _loadedViewModel is not { } viewModel)
        {
            return;
        }

        if (viewModel.IsDeleting ||
            viewModel.IsDeleteConfirmationOpen)
        {
            return;
        }

        e.Handled = true;
        _datePopup.IsOpen = false;

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