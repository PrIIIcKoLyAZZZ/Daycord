using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Daycord.Desktop.Views;

/// <summary>
/// Shared Graphite & Amber date calendar.
/// The parent owns the text field; this view owns the popup,
/// calendar layout and month navigation.
/// </summary>
public partial class DaycordCalendarView : UserControl
{
    private readonly Popup _popup;
    private readonly Border _calendarSurface;
    private readonly TextBlock _monthTitle;
    private readonly UniformGrid _weekdays;
    private readonly UniformGrid _days;
    private readonly Button _todayButton;

    private DateTime _visibleMonth = new(
        DateTime.Today.Year,
        DateTime.Today.Month,
        1);

    private DateTime? _selectedDate;

    private CultureInfo _culture =
        CultureInfo.GetCultureInfo("en");

    // Only the caller's calendar button is a focus return target.
    // Do not restore focus for light dismiss, navigation or other
    // programmatic closes: doing so would steal it from the user.
    private Control? _returnFocusTarget;
    private int _focusVersion;

    public DaycordCalendarView()
    {
        AvaloniaXamlLoader.Load(this);

        _popup = FindRequired<Popup>("CalendarPopup");
        _calendarSurface = FindRequired<Border>("CalendarSurface");
        _monthTitle = FindRequired<TextBlock>("MonthTitle");
        _weekdays = FindRequired<UniformGrid>("WeekdayHeader");
        _days = FindRequired<UniformGrid>("CalendarDaysGrid");
        _todayButton = FindRequired<Button>("TodayButton");

        // Popup content lives in its own popup tree. Handle Escape there,
        // not in the host View or a global keyboard listener.
        _calendarSurface.AddHandler(
            InputElement.KeyDownEvent,
            OnPopupKeyDown,
            RoutingStrategies.Tunnel);

        // Light-dismiss does not call our public Close() method.
        _popup.Closed += (_, _) =>
        {
            _focusVersion++;
            _returnFocusTarget = null;
        };

        DetachedFromVisualTree += (_, _) => Close();
    }

    public bool IsOpen => _popup.IsOpen;

    public event EventHandler<CalendarDateSelectedEventArgs>?
        DateSelected;

    public void Open(
        Control placementTarget,
        DateTime anchorDate,
        DateTime? selectedDate,
        CultureInfo culture,
        PlacementMode placement)
    {
        ArgumentNullException.ThrowIfNull(placementTarget);
        ArgumentNullException.ThrowIfNull(culture);

        Close();

        // The last button in each existing date segment opens the
        // calendar (Task Details also has a preceding Clear button).
        _returnFocusTarget = placementTarget
            .GetVisualDescendants()
            .OfType<Button>()
            .LastOrDefault(button =>
                button.IsVisible && button.IsEnabled);

        _popup.PlacementTarget = placementTarget;
        _popup.Placement = placement;

        _culture = culture;
        _selectedDate = selectedDate?.Date;

        _visibleMonth = new DateTime(
            anchorDate.Year,
            anchorDate.Month,
            1);

        Render();

        _popup.IsOpen = true;

        // The popup is attached and laid out asynchronously. Focus a
        // day after opening so Tab/Shift+Tab and Enter/Space work.
        var version = _focusVersion;
        Dispatcher.UIThread.Post(() =>
        {
            if (IsOpen && version == _focusVersion)
            {
                FocusDay(_selectedDate);
            }
        }, DispatcherPriority.Input);
    }

    /// <summary>
    /// Refresh selection and language without changing
    /// the visible month.
    /// </summary>
    public void Refresh(
        DateTime? selectedDate,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        _selectedDate = selectedDate?.Date;
        _culture = culture;

        if (IsOpen)
        {
            // Rendering replaces day buttons; keep keyboard focus on
            // the same date when a live localization refresh occurs.
            DateTime? focusedDay = null;

            foreach (var button in _days.Children.OfType<Button>())
            {
                if (button.IsFocused &&
                    button.Tag is DateTime focusedButtonDate)
                {
                    focusedDay = focusedButtonDate;
                    break;
                }
            }

            Render();

            if (focusedDay is { } day)
            {
                var version = _focusVersion;
                Dispatcher.UIThread.Post(() =>
                {
                    if (IsOpen && version == _focusVersion)
                    {
                        FocusDay(day);
                    }
                }, DispatcherPriority.Input);
            }
        }
    }

    public void Close()
    {
        _focusVersion++;
        _popup.IsOpen = false;
        _returnFocusTarget = null;
    }

    private void CloseAndRestoreFocus()
    {
        var target = _returnFocusTarget;
        Close();

        if (target is { IsVisible: true, IsEnabled: true })
        {
            target.Focus(NavigationMethod.Tab);
        }
    }

    private void OnPopupKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape || !IsOpen)
        {
            return;
        }

        e.Handled = true;
        CloseAndRestoreFocus();
    }

    private void FocusDay(DateTime? preferredDate)
    {
        Button? FindDay(DateTime day) => _days.Children
            .OfType<Button>()
            .FirstOrDefault(button =>
                button.Tag is DateTime candidate &&
                candidate.Date == day.Date);

        var firstOfMonth = _visibleMonth;
        var target = (preferredDate is { } selected
                          ? FindDay(selected)
                          : null)
                     ?? FindDay(DateTime.Today)
                     ?? FindDay(firstOfMonth);

        target?.Focus(NavigationMethod.Tab);
    }

    private T FindRequired<T>(string name)
        where T : Control
    {
        return this.FindControl<T>(name)
            ?? throw new InvalidOperationException(
                $"Calendar control '{name}' was not found.");
    }

    private void OnPreviousMonthClick(
        object? sender,
        RoutedEventArgs e)
    {
        ChangeMonth(-1);
    }

    private void OnNextMonthClick(
        object? sender,
        RoutedEventArgs e)
    {
        ChangeMonth(1);
    }

    private void ChangeMonth(int delta)
    {
        try
        {
            _visibleMonth = _visibleMonth.AddMonths(delta);
            Render();
        }
        catch (ArgumentOutOfRangeException)
        {
            // Outside supported DateTime range.
        }
    }

    private void Render()
    {
        _monthTitle.Text = _visibleMonth.ToString(
            "MMMM yyyy",
            _culture);

        _todayButton.Content =
            _culture.TwoLetterISOLanguageName == "ru"
                ? "Сегодня"
                : "Today";

        RenderWeekdays();
        RenderDays();
    }

    private void RenderWeekdays()
    {
        _weekdays.Children.Clear();

        for (var i = 0; i < 7; i++)
        {
            // Monday first.
            var weekday = (DayOfWeek)((i + 1) % 7);

            var label = new TextBlock
            {
                Text = _culture.DateTimeFormat
                    .GetShortestDayName(weekday),

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

            label.Classes.Add("dc-weekday");
            _weekdays.Children.Add(label);
        }
    }

    private void RenderDays()
    {
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

        _days.Children.Clear();

        for (var i = 0; i < 42; i++)
        {
            DateTime day;

            try
            {
                day = first.AddDays(i);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Preserve grid geometry at DateTime limits.
                _days.Children.Add(new Border());
                continue;
            }

            var button = new Button
            {
                Content = day.Day.ToString(
                    CultureInfo.InvariantCulture),

                Tag = day,

                HorizontalAlignment =
                    HorizontalAlignment.Stretch,

                HorizontalContentAlignment =
                    HorizontalAlignment.Center,

                VerticalContentAlignment =
                    VerticalAlignment.Center
            };

            button.Classes.Add("dc-day");

            if (day.Month != _visibleMonth.Month ||
                day.Year != _visibleMonth.Year)
            {
                button.Classes.Add("outside");
            }

            if (day.Date == DateTime.Today)
            {
                button.Classes.Add("today");
            }

            if (_selectedDate.HasValue &&
                day.Date == _selectedDate.Value)
            {
                button.Classes.Add("selected");
            }

            button.Click += OnDayClicked;

            _days.Children.Add(button);
        }
    }

    private void OnDayClicked(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is Button { Tag: DateTime date })
        {
            Select(date);
        }
    }

    private void OnTodayClick(
        object? sender,
        RoutedEventArgs e)
    {
        Select(DateTime.Today);
    }

    private void Select(DateTime date)
    {
        CloseAndRestoreFocus();

        DateSelected?.Invoke(
            this,
            new CalendarDateSelectedEventArgs(date.Date));
    }
}

public sealed class CalendarDateSelectedEventArgs : EventArgs
{
    public CalendarDateSelectedEventArgs(DateTime date)
    {
        Date = date.Date;
    }

    public DateTime Date { get; }
}