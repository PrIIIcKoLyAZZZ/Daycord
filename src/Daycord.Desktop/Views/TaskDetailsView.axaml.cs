using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Daycord.Desktop.ViewModels;

namespace Daycord.Desktop.Views;

public partial class TaskDetailsView : UserControl
{
    private readonly DaycordCalendarView _calendar;
    private readonly Grid _dateSegment;
    private readonly Grid _periodDateSegment;
    private readonly StackPanel _previewBlocks;
    private readonly TextBox _titleInput;

    private TaskDetailsViewModel? _connected;
    private bool _attached;

    private CalendarTarget _calendarTarget =
        CalendarTarget.TaskDate;

    private enum CalendarTarget
    {
        TaskDate,
        PlanningPeriod
    }

    public TaskDetailsView()
    {
        AvaloniaXamlLoader.Load(this);

        _calendar =
            Required<DaycordCalendarView>("SharedCalendar");

        _dateSegment =
            Required<Grid>("DateSegment");

        _periodDateSegment =
            Required<Grid>("PeriodDateSegment");

        _previewBlocks =
            Required<StackPanel>("PreviewBlocks");

        _titleInput =
            Required<TextBox>("TitleInput");

        _calendar.DateSelected += OnCalendarDateSelected;

        AttachedToVisualTree += (_, _) =>
        {
            _attached = true;
            ConnectViewModel();
        };

        DetachedFromVisualTree += (_, _) =>
        {
            _attached = false;
            _calendar.Close();
            DisconnectViewModel();
        };

        DataContextChanged += (_, _) =>
        {
            if (_attached)
            {
                ConnectViewModel();
            }
        };
    }

    private T Required<T>(string name)
        where T : Control
    {
        return this.FindControl<T>(name)
            ?? throw new InvalidOperationException(
                $"Task Details control '{name}' was not found.");
    }

    private void ConnectViewModel()
    {
        if (ReferenceEquals(_connected, DataContext))
        {
            return;
        }

        DisconnectViewModel();

        if (DataContext is not TaskDetailsViewModel viewModel)
        {
            return;
        }

        _connected = viewModel;

        viewModel.PropertyChanged +=
            OnViewModelPropertyChanged;

        RenderPreview();
        FocusTitleWhenReady(viewModel);
    }

    private void DisconnectViewModel()
    {
        _calendar.Close();

        if (_connected is not null)
        {
            _connected.PropertyChanged -=
                OnViewModelPropertyChanged;
        }

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
            e.PropertyName is
                nameof(TaskDetailsViewModel.DescriptionMarkdown) or
                nameof(TaskDetailsViewModel.IsPreviewMode))
        {
            RenderPreview();
        }

        if (e.PropertyName ==
                nameof(TaskDetailsViewModel.IsLoaded) &&
            viewModel.IsLoaded)
        {
            FocusTitleWhenReady(viewModel);
        }

        // Close calendar whenever a modal operation begins.
        if (e.PropertyName is
            nameof(TaskDetailsViewModel.IsDiscardConfirmationOpen) or
            nameof(TaskDetailsViewModel.IsSaving) or
            nameof(TaskDetailsViewModel.IsDeletePeriodConfirmationOpen) or
            nameof(TaskDetailsViewModel.IsPeriodEditorOpen) or
            nameof(TaskDetailsViewModel.IsPeriodBusy) or
            nameof(TaskDetailsViewModel.IsTaskNotFound))
        {
            if (viewModel.IsDiscardConfirmationOpen ||
                viewModel.IsSaving ||
                viewModel.IsDeletePeriodConfirmationOpen ||
                viewModel.IsPeriodBusy ||
                viewModel.IsTaskNotFound ||
                (_calendarTarget == CalendarTarget.PlanningPeriod &&
                 !viewModel.IsPeriodEditorOpen))
            {
                _calendar.Close();
            }
        }

        if (!_calendar.IsOpen)
        {
            return;
        }

        // Refresh current selection without resetting
        // the month being viewed.
        if (_calendarTarget == CalendarTarget.TaskDate &&
            e.PropertyName is
                nameof(TaskDetailsViewModel.DateInputText) or
                nameof(TaskDetailsViewModel.DateCulture))
        {
            _calendar.Refresh(
                TaskSelectedDate(viewModel),
                viewModel.DateCulture);
        }
        else if (_calendarTarget == CalendarTarget.PlanningPeriod &&
                 e.PropertyName is
                     nameof(TaskDetailsViewModel.PeriodDateInputText) or
                     nameof(TaskDetailsViewModel.DateCulture))
        {
            _calendar.Refresh(
                PeriodSelectedDate(viewModel),
                viewModel.DateCulture);
        }
    }

    private void FocusTitleWhenReady(
        TaskDetailsViewModel viewModel)
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

    private static DateTime? TaskSelectedDate(
        TaskDetailsViewModel viewModel)
    {
        return !string.IsNullOrWhiteSpace(viewModel.DateInputText) &&
               !viewModel.IsDateInputInvalid
            ? viewModel.CalendarAnchorDate.Date
            : null;
    }

    private static DateTime? PeriodSelectedDate(
        TaskDetailsViewModel viewModel)
    {
        return !string.IsNullOrWhiteSpace(
                   viewModel.PeriodDateInputText) &&
               !viewModel.IsPeriodDateInputInvalid
            ? viewModel.PeriodCalendarAnchorDate.Date
            : null;
    }

    private void OnCalendarButtonClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (_connected is { CanEdit: true })
        {
            OpenCalendar(CalendarTarget.TaskDate);
        }
    }

    private void OnPeriodCalendarButtonClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (_connected is { CanSavePeriod: true })
        {
            OpenCalendar(CalendarTarget.PlanningPeriod);
        }
    }

    private void OpenCalendar(CalendarTarget target)
    {
        if (_connected is not { } viewModel)
        {
            return;
        }

        // Clicking the same calendar icon toggles it.
        if (_calendar.IsOpen && _calendarTarget == target)
        {
            _calendar.Close();
            return;
        }

        _calendarTarget = target;

        var placementTarget =
            target == CalendarTarget.TaskDate
                ? _dateSegment
                : _periodDateSegment;

        var anchorDate =
            target == CalendarTarget.TaskDate
                ? viewModel.CalendarAnchorDate
                : viewModel.PeriodCalendarAnchorDate;

        var selectedDate =
            target == CalendarTarget.TaskDate
                ? TaskSelectedDate(viewModel)
                : PeriodSelectedDate(viewModel);

        _calendar.Open(
            placementTarget: placementTarget,
            anchorDate: anchorDate,
            selectedDate: selectedDate,
            culture: viewModel.DateCulture,
            placement: PlacementMode.BottomEdgeAlignedLeft);
    }

    private void OnCalendarDateSelected(
        object? sender,
        CalendarDateSelectedEventArgs e)
    {
        if (_connected is not { } viewModel)
        {
            return;
        }

        if (_calendarTarget == CalendarTarget.PlanningPeriod)
        {
            if (viewModel.CanSavePeriod)
            {
                viewModel.SelectPeriodCalendarDate(e.Date);
            }
        }
        else if (viewModel.CanEdit)
        {
            viewModel.SelectCalendarDate(e.Date);
        }
    }

    private void OnClearDateClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (_connected is { CanEdit: true } viewModel)
        {
            viewModel.ClearDateCommand.Execute(null);
        }

        _calendar.Close();
    }

    private void OnDateKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        // Enter in a date field must not save the task.
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        _calendar.Close();
    }

    private void OnPeriodDateKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        OnDateKeyDown(sender, e);
    }

    private void RenderPreview()
    {
        NativeMarkdownPreview.Render(
            _previewBlocks,
            _connected?.DescriptionMarkdown);
    }
}