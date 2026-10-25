using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Daycord.Desktop.ViewModels;

namespace Daycord.Desktop.Views;

public partial class TerminalView : UserControl
{
    private readonly TextBox _titleInput;
    private readonly ScrollViewer _historyScrollViewer;
    private readonly Grid _dateSegment;
    private readonly DaycordCalendarView _calendar;

    private TerminalViewModel? _connectedViewModel;
    private bool _isAttached;

    public TerminalView()
    {
        AvaloniaXamlLoader.Load(this);

        _titleInput =
            Required<TextBox>("TitleInput");

        _historyScrollViewer =
            Required<ScrollViewer>("HistoryScrollViewer");

        _dateSegment =
            Required<Grid>("DateSegment");

        _calendar =
            Required<DaycordCalendarView>("SharedCalendar");

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
            DisconnectViewModel();
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
                $"Quick Task control '{name}' was not found.");
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
        _calendar.Close();

        if (_connectedViewModel is null)
            return;

        _connectedViewModel.TaskCreated -= OnTaskCreated;
        _connectedViewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _connectedViewModel = null;
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (sender is not TerminalViewModel viewModel ||
            !ReferenceEquals(viewModel, _connectedViewModel))
        {
            return;
        }

        if (!_calendar.IsOpen)
            return;

        if (!viewModel.CanEditForm)
        {
            _calendar.Close();
            return;
        }

        if (e.PropertyName is
            nameof(TerminalViewModel.CalendarCulture) or
            nameof(TerminalViewModel.DateInputText))
        {
            DateTime? selectedDate =
                viewModel.TryResolveDate(out var date)
                    ? date
                    : null;

            _calendar.Refresh(
                selectedDate,
                viewModel.CalendarCulture);
        }
    }

    private void OnCalendarButtonClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (_connectedViewModel is not { CanEditForm: true } viewModel)
            return;

        if (_calendar.IsOpen)
        {
            _calendar.Close();
            return;
        }

        DateTime? selectedDate =
            viewModel.TryResolveDate(out var date)
                ? date
                : null;

        _calendar.Open(
            placementTarget: _dateSegment,
            anchorDate: viewModel.CalendarAnchorDate,
            selectedDate: selectedDate,
            culture: viewModel.CalendarCulture,
            placement: PlacementMode.TopEdgeAlignedLeft);
    }

    private void OnCalendarDateSelected(
        object? sender,
        CalendarDateSelectedEventArgs e)
    {
        if (_connectedViewModel is not { CanEditForm: true } viewModel)
            return;

        viewModel.SelectCalendarDate(e.Date);

        // Preserve the terminal workflow after selecting a date.
        ScheduleTitleFocus(viewModel);
    }

    private void OnDateKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        SubmitOnEnter(e);
    }

    private void OnNumberKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        SubmitOnEnter(e);
    }

    private void OnTitleKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        SubmitOnEnter(e);
    }

    private void OnTimeKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        SubmitOnEnter(e);
    }

    private void SubmitOnEnter(KeyEventArgs e)
    {
        if (e.Key != Key.Enter ||
            e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        if (_connectedViewModel is not { } viewModel)
            return;

        e.Handled = true;

        if (viewModel.CreateTaskCommand.CanExecute(null))
        {
            _ = viewModel.CreateTaskCommand.ExecuteAsync(null);
        }
    }

    private void OnTaskCreated(
        object? sender,
        EventArgs e)
    {
        if (sender is not TerminalViewModel viewModel)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (!_isAttached ||
                !ReferenceEquals(DataContext, viewModel))
            {
                return;
            }

            _calendar.Close();
            _historyScrollViewer.ScrollToEnd();

            if (_titleInput.IsEnabled)
                _titleInput.Focus();

        }, DispatcherPriority.Background);
    }

    private void ScheduleTitleFocus(
        TerminalViewModel viewModel)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_isAttached &&
                ReferenceEquals(DataContext, viewModel) &&
                _titleInput.IsEnabled)
            {
                _titleInput.Focus();
            }

        }, DispatcherPriority.Background);
    }
}