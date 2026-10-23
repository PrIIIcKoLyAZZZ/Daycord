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

public partial class TaskDetailsView : UserControl
{
    private readonly Popup _datePopup;
    private readonly Grid _dateSegment;
    private readonly UniformGrid _weekdayHeader;
    private readonly UniformGrid _calendarDaysGrid;
    private readonly TextBlock _calendarMonthTitle;
    private readonly Button _calendarTodayButton;
    private readonly StackPanel _previewBlocks;
    private readonly TextBox _titleInput;

    private TaskDetailsViewModel? _connected;
    private DateTime _visibleMonth = new(
        DateTime.Today.Year, DateTime.Today.Month, 1);
    private bool _attached;

    public TaskDetailsView()
    {
        AvaloniaXamlLoader.Load(this);

        _datePopup = Required<Popup>("DatePopup");
        _dateSegment = Required<Grid>("DateSegment");
        _weekdayHeader = Required<UniformGrid>("WeekdayHeader");
        _calendarDaysGrid = Required<UniformGrid>("CalendarDaysGrid");
        _calendarMonthTitle = Required<TextBlock>("CalendarMonthTitle");
        _calendarTodayButton = Required<Button>("CalendarTodayButton");
        _previewBlocks = Required<StackPanel>("PreviewBlocks");
        _titleInput = Required<TextBox>("TitleInput");

        _datePopup.PlacementTarget = _dateSegment;

        AttachedToVisualTree += (_, _) =>
        {
            _attached = true;
            ConnectViewModel();
        };

        DetachedFromVisualTree += (_, _) =>
        {
            _attached = false;
            _datePopup.IsOpen = false;
            DisconnectViewModel();
        };

        DataContextChanged += (_, _) =>
        {
            if (_attached)
                ConnectViewModel();
        };
    }

    private T Required<T>(string name) where T : Control =>
        this.FindControl<T>(name)
        ?? throw new InvalidOperationException(
            $"Task Details control '{name}' was not found.");

    private void ConnectViewModel()
    {
        if (ReferenceEquals(_connected, DataContext))
            return;

        DisconnectViewModel();

        if (DataContext is not TaskDetailsViewModel viewModel)
            return;

        _connected = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        RenderPreview();
        FocusTitleWhenReady(viewModel);
    }

    private void DisconnectViewModel()
    {
        if (_connected is not null)
            _connected.PropertyChanged -= OnViewModelPropertyChanged;

        _connected = null;
        _previewBlocks.Children.Clear();
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (_connected is not { } viewModel ||
            !ReferenceEquals(sender, viewModel))
        {
            return;
        }

        if (viewModel.IsPreviewMode &&
            (e.PropertyName is
                nameof(TaskDetailsViewModel.DescriptionMarkdown) or
                nameof(TaskDetailsViewModel.IsPreviewMode)))
        {
            RenderPreview();
        }

        if (e.PropertyName == nameof(TaskDetailsViewModel.IsLoaded) &&
            viewModel.IsLoaded)
        {
            FocusTitleWhenReady(viewModel);
        }

        if (_datePopup.IsOpen &&
            (e.PropertyName is
                nameof(TaskDetailsViewModel.DateInputText) or
                nameof(TaskDetailsViewModel.DateCulture)))
        {
            RenderCalendar();
        }

        if (e.PropertyName is
            nameof(TaskDetailsViewModel.IsDiscardConfirmationOpen) or
            nameof(TaskDetailsViewModel.IsSaving))
        {
            if (viewModel.IsDiscardConfirmationOpen || viewModel.IsSaving)
                _datePopup.IsOpen = false;
        }
    }

    private void FocusTitleWhenReady(TaskDetailsViewModel viewModel)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_attached &&
                ReferenceEquals(_connected, viewModel) &&
                viewModel.CanEdit &&
                _titleInput.IsEnabled)
            {
                _titleInput.Focus();
            }
        }, DispatcherPriority.Background);
    }

    private void OnDateKeyDown(object? sender, KeyEventArgs e)
    {
        // Enter in the date field must not trigger task saving.
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _datePopup.IsOpen = false;
        }
    }

    private void OnClearDateClick(object? sender, RoutedEventArgs e)
    {
        if (_connected is { CanEdit: true } viewModel)
            viewModel.ClearDateCommand.Execute(null);

        _datePopup.IsOpen = false;
    }

    private void OnCalendarButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_connected is not { CanEdit: true } viewModel)
            return;

        if (_datePopup.IsOpen)
        {
            _datePopup.IsOpen = false;
            return;
        }

        var anchor = viewModel.CalendarAnchorDate;
        _visibleMonth = new DateTime(anchor.Year, anchor.Month, 1);

        RenderCalendar();
        _datePopup.IsOpen = true;
    }

    private void OnPreviousMonthClick(object? sender, RoutedEventArgs e) =>
        ChangeMonth(-1);

    private void OnNextMonthClick(object? sender, RoutedEventArgs e) =>
        ChangeMonth(1);

    private void ChangeMonth(int offset)
    {
        try
        {
            _visibleMonth = _visibleMonth.AddMonths(offset);
            RenderCalendar();
        }
        catch (ArgumentOutOfRangeException)
        {
            // Boundary of the supported DateTime range.
        }
    }

    private void RenderCalendar()
    {
        if (_connected is not { } viewModel)
            return;

        var culture = viewModel.DateCulture;

        _calendarMonthTitle.Text =
            _visibleMonth.ToString("MMMM yyyy", culture);

        _calendarTodayButton.Content =
            culture.TwoLetterISOLanguageName == "ru"
                ? "Сегодня"
                : "Today";

        _weekdayHeader.Children.Clear();

        for (var i = 0; i < 7; i++)
        {
            var weekday = (DayOfWeek)((i + 1) % 7);

            var label = new TextBlock
            {
                Text = culture.DateTimeFormat.GetShortestDayName(weekday),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            label.Classes.Add("calendar-weekday");
            _weekdayHeader.Children.Add(label);
        }

        var offset = ((int)_visibleMonth.DayOfWeek + 6) % 7;

        DateTime firstVisible;

        try
        {
            firstVisible = _visibleMonth.AddDays(-offset);
        }
        catch (ArgumentOutOfRangeException)
        {
            firstVisible = _visibleMonth;
        }

        var hasSelection =
            !string.IsNullOrWhiteSpace(viewModel.DateInputText) &&
            !viewModel.IsDateInputInvalid;

        var selected = viewModel.CalendarAnchorDate.Date;

        _calendarDaysGrid.Children.Clear();

        for (var i = 0; i < 42; i++)
        {
            DateTime day;

            try
            {
                day = firstVisible.AddDays(i);
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

            if (day.Month != _visibleMonth.Month ||
                day.Year != _visibleMonth.Year)
            {
                button.Classes.Add("outside");
            }

            if (day.Date == DateTime.Today)
                button.Classes.Add("today");

            if (hasSelection && day.Date == selected)
                button.Classes.Add("selected");

            button.Click += OnCalendarDayClick;

            _calendarDaysGrid.Children.Add(button);
        }
    }

    private void OnCalendarDayClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is Button { Tag: DateTime date })
            SelectCalendarDate(date);
    }

    private void OnTodayClick(object? sender, RoutedEventArgs e) =>
        SelectCalendarDate(DateTime.Today);

    private void SelectCalendarDate(DateTime date)
    {
        if (_connected is { CanEdit: true } viewModel)
            viewModel.SelectCalendarDate(date);

        _datePopup.IsOpen = false;
    }

    private void RenderPreview()
    {
        NativeMarkdownPreview.Render(
            _previewBlocks,
            _connected?.DescriptionMarkdown);
    }
}