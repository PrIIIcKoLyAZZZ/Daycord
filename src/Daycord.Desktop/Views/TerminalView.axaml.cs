using System.ComponentModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Daycord.Desktop.ViewModels;

namespace Daycord.Desktop.Views;

public partial class TerminalView : UserControl
{
    private readonly TextBox _titleInput;
    private readonly ScrollViewer _historyScrollViewer;
    private readonly Popup _datePopup;
    private readonly UniformGrid _weekdayHeader;
    private readonly UniformGrid _calendarDaysGrid;
    private readonly TextBlock _calendarMonthTitle;
    private DateTime _visibleMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    private TerminalViewModel? _connectedViewModel;
    private bool _isAttached;

    public TerminalView()
    {
        AvaloniaXamlLoader.Load(this);

        _titleInput = this.FindControl<TextBox>("TitleInput")
            ?? throw new InvalidOperationException("Quick Task title field was not found.");
        _historyScrollViewer = this.FindControl<ScrollViewer>("HistoryScrollViewer")
            ?? throw new InvalidOperationException("Quick Task history was not found.");
        _datePopup = this.FindControl<Popup>("DatePopup")
            ?? throw new InvalidOperationException("Quick Task calendar popup was not found.");
        _weekdayHeader = this.FindControl<UniformGrid>("WeekdayHeader")
            ?? throw new InvalidOperationException("Calendar weekdays were not found.");
        _calendarDaysGrid = this.FindControl<UniformGrid>("CalendarDaysGrid")
            ?? throw new InvalidOperationException("Calendar day grid was not found.");
        _calendarMonthTitle = this.FindControl<TextBlock>("CalendarMonthTitle")
            ?? throw new InvalidOperationException("Calendar month title was not found.");
        _datePopup.PlacementTarget = this.FindControl<Grid>("DateSegment")
            ?? throw new InvalidOperationException("Calendar target was not found.");

        AttachedToVisualTree += (_, _) =>
        {
            _isAttached = true;
            ConnectViewModel();
        };

        DetachedFromVisualTree += (_, _) =>
        {
            _isAttached = false;
            _datePopup.IsOpen = false;
            DisconnectViewModel();
        };

        DataContextChanged += (_, _) =>
        {
            if (_isAttached)
                ConnectViewModel();
        };
    }

    private void ConnectViewModel()
    {
        if (ReferenceEquals(_connectedViewModel, DataContext))
            return;

        DisconnectViewModel();
        if (DataContext is not TerminalViewModel viewModel)
            return;

        _connectedViewModel = viewModel;
        viewModel.TaskCreated += OnTaskCreated;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ScheduleTitleFocus(viewModel);
    }

    private void DisconnectViewModel()
    {
        if (_connectedViewModel is null)
            return;

        _connectedViewModel.TaskCreated -= OnTaskCreated;
        _connectedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _connectedViewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_datePopup.IsOpen &&
            (e.PropertyName == nameof(TerminalViewModel.CalendarCulture) ||
             e.PropertyName == nameof(TerminalViewModel.DateInputText)))
        {
            RenderCalendar();
        }
    }

    private void OnCalendarButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TerminalViewModel viewModel || !viewModel.CanEditForm)
            return;

        if (_datePopup.IsOpen)
        {
            _datePopup.IsOpen = false;
            return;
        }

        var selected = viewModel.CalendarAnchorDate;
        _visibleMonth = new DateTime(selected.Year, selected.Month, 1);
        RenderCalendar();
        _datePopup.IsOpen = true;
    }

    private void OnPreviousMonthClick(object? sender, RoutedEventArgs e) => ChangeMonth(-1);
    private void OnNextMonthClick(object? sender, RoutedEventArgs e) => ChangeMonth(1);

    private void ChangeMonth(int offset)
    {
        try
        {
            _visibleMonth = _visibleMonth.AddMonths(offset);
            RenderCalendar();
        }
        catch (ArgumentOutOfRangeException)
        {
            // Dates outside the supported calendar range are unavailable.
        }
    }

    private void RenderCalendar()
    {
        if (_connectedViewModel is not { } viewModel)
            return;

        CultureInfo culture = viewModel.CalendarCulture;
        _calendarMonthTitle.Text = _visibleMonth.ToString("MMMM yyyy", culture);

        _weekdayHeader.Children.Clear();
        for (var i = 0; i < 7; i++)
        {
            // Monday is the first column on both macOS and Windows.
            var weekday = (DayOfWeek)((i + 1) % 7);
            var label = new TextBlock
            {
                Text = culture.DateTimeFormat.GetShortestDayName(weekday),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            label.Classes.Add("calendar-weekday");
            _weekdayHeader.Children.Add(label);
        }

        var firstDayOffset = ((int)_visibleMonth.DayOfWeek + 6) % 7;
        DateTime firstVisibleDay;
        try
        {
            firstVisibleDay = _visibleMonth.AddDays(-firstDayOffset);
        }
        catch (ArgumentOutOfRangeException)
        {
            firstVisibleDay = _visibleMonth;
        }

        var hasSelectedDate = viewModel.TryResolveDate(out var selected);
        _calendarDaysGrid.Children.Clear();

        for (var i = 0; i < 42; i++)
        {
            DateTime day;
            try
            {
                day = firstVisibleDay.AddDays(i);
            }
            catch (ArgumentOutOfRangeException)
            {
                break;
            }

            var button = new Button
            {
                Content = day.Day.ToString(CultureInfo.InvariantCulture),
                Tag = day,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };

            button.Classes.Add("calendar-day");
            if (day.Month != _visibleMonth.Month || day.Year != _visibleMonth.Year)
                button.Classes.Add("outside");
            if (day.Date == DateTime.Today)
                button.Classes.Add("today");
            if (hasSelectedDate && day.Date == selected.Date)
                button.Classes.Add("selected");

            button.Click += OnCalendarDayClicked;
            _calendarDaysGrid.Children.Add(button);
        }
    }

    private void OnCalendarDayClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DateTime selected } ||
            DataContext is not TerminalViewModel viewModel)
            return;

        viewModel.SelectCalendarDate(selected);
        _datePopup.IsOpen = false;
        ScheduleTitleFocus(viewModel);
    }

    private void OnTodayClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TerminalViewModel viewModel)
            return;

        viewModel.SelectCalendarDate(DateTime.Today);
        _datePopup.IsOpen = false;
        ScheduleTitleFocus(viewModel);
    }

    private void OnDateKeyDown(object? sender, KeyEventArgs e) => SubmitOnEnter(e);
    private void OnNumberKeyDown(object? sender, KeyEventArgs e) => SubmitOnEnter(e);
    private void OnTitleKeyDown(object? sender, KeyEventArgs e) => SubmitOnEnter(e);
    private void OnTimeKeyDown(object? sender, KeyEventArgs e) => SubmitOnEnter(e);

    private void SubmitOnEnter(KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None)
            return;

        if (DataContext is not TerminalViewModel viewModel)
            return;

        e.Handled = true;
        if (viewModel.CreateTaskCommand.CanExecute(null))
            _ = viewModel.CreateTaskCommand.ExecuteAsync(null);
    }

    private void OnTaskCreated(object? sender, EventArgs e)
    {
        if (sender is TerminalViewModel viewModel)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_isAttached || !ReferenceEquals(DataContext, viewModel))
                    return;

                _historyScrollViewer.ScrollToEnd();
                if (_titleInput.IsEnabled)
                    _titleInput.Focus();
            }, DispatcherPriority.Background);
        }
    }

    private void ScheduleTitleFocus(TerminalViewModel viewModel)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_isAttached && ReferenceEquals(DataContext, viewModel) &&
                _titleInput.IsEnabled)
            {
                _titleInput.Focus();
            }
        }, DispatcherPriority.Background);
    }
}