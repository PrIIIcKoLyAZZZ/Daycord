using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
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
    private readonly Button _confirmDeleteButton;
    private readonly Grid _tasksBlurContent;
    private readonly Grid _deleteConfirmationOverlay;
    private readonly Border _deleteConfirmationPanel;
    private readonly BlurEffect _deleteBlurEffect;
    private readonly double _targetDeleteBlur;
    private readonly Grid _tableContent;
    private readonly DaycordCalendarView _calendar;

    private TextBox? _activeDateInput;

    private TaskListViewModel? _loadedViewModel;
    private Control? _focusBeforeDeleteConfirmation;
    private bool _isAttached;
    private bool _suppressTextChanges;
    private bool _changingFromInput;
    private bool _changingToInput;
    private long _deleteTransitionGeneration;

    // DC-041: The confirmation is temporarily hosted by MainWindow so the
    // matte and blur cover the sidebar and page header, not only Tasks.
    private Grid? _applicationRoot;
    private Grid? _applicationShell;
    private Panel? _originalOverlayParent;

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
        _confirmDeleteButton = Required<Button>("ConfirmDeleteButton");
        _tasksBlurContent = Required<Grid>("TasksBlurContent");
        _deleteConfirmationOverlay = Required<Grid>("DeleteConfirmationOverlay");
        _deleteConfirmationPanel = Required<Border>("DeleteConfirmationPanel");
        _deleteBlurEffect = _tasksBlurContent.Effect as BlurEffect
            ?? throw new InvalidOperationException("Tasks blur effect was not found.");
        _targetDeleteBlur = _deleteBlurEffect.Radius;

        // No render effect or animated radius while the confirmation is hidden.
        _tasksBlurContent.Effect = null;
        SetDeleteBlurImmediately(0);

        _tableContent = Required<Grid>("TableContent");
        _calendar = Required<DaycordCalendarView>("TaskFilterCalendar");
        _calendar.DateSelected += OnCalendarDateSelected;

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

        // Once hosted in MainWindow, the overlay is no longer a descendant
        // of this view. Keep keyboard modal handling on the overlay itself.
        _deleteConfirmationOverlay.AddHandler(
            InputElement.KeyDownEvent,
            OnTaskSafetyKeyDown,
            RoutingStrategies.Tunnel);

        AddHandler(
            InputElement.KeyDownEvent,
            OnTaskRowKeyDown,
            RoutingStrategies.Tunnel);

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
            _calendar.Close();
            _activeDateInput = null;
            _focusBeforeDeleteConfirmation = null;
            ResetDeleteOverlay();
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
            ResetDeleteOverlay();
            _focusBeforeDeleteConfirmation = null;
            DisconnectViewModel();
            return;
        }

        if (!ReferenceEquals(_loadedViewModel, viewModel))
        {
            ResetDeleteOverlay();
            _focusBeforeDeleteConfirmation = null;
            DisconnectViewModel();

            _loadedViewModel = viewModel;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;

            SyncDateInput(_fromInput, viewModel.DateFrom);
            SyncDateInput(_toInput, viewModel.DateTo);
            if (viewModel.IsDeleteConfirmationOpen)
            {
                UpdateDeleteConfirmation(viewModel);
            }
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
            _calendar.Close();
        }
        else if (e.PropertyName ==
                 nameof(TaskListViewModel.IsDeleteConfirmationOpen))
        {
            UpdateDeleteConfirmation(viewModel);
        }

        if (_calendar.IsOpen &&
            (e.PropertyName == nameof(TaskListViewModel.CalendarCulture) ||
             (e.PropertyName == nameof(TaskListViewModel.DateFrom) &&
              ReferenceEquals(_activeDateInput, _fromInput)) ||
             (e.PropertyName == nameof(TaskListViewModel.DateTo) &&
              ReferenceEquals(_activeDateInput, _toInput))))
        {
            DateTime? selected = ReferenceEquals(_activeDateInput, _fromInput)
                ? viewModel.DateFrom?.Date
                : viewModel.DateTo?.Date;

            _calendar.Refresh(selected, viewModel.CalendarCulture);
        }
    }

    // The VM owns deletion; this View owns only the visual transition.
    // Keep the overlay hit-testable until both exit transitions have finished.
    private void UpdateDeleteConfirmation(TaskListViewModel viewModel)
    {
        var generation = ++_deleteTransitionGeneration;

        if (viewModel.IsDeleteConfirmationOpen)
        {
            _calendar.Close();
            if (!_deleteConfirmationOverlay.IsVisible)
            {
                _focusBeforeDeleteConfirmation =
                    TopLevel.GetTopLevel(this)?.FocusManager?
                        .GetFocusedElement() as Control;

                _deleteConfirmationOverlay.Classes.Remove("open");
                SetDeleteBlurImmediately(0);

                // In the actual application, reparent the existing dialog
                // into the full-window root (no second dialog or task list).
                // Standalone previews can still use the Tasks-local fallback.
                AttachDeleteOverlayToWindow(viewModel);
                if (_applicationShell is { } shell)
                {
                    shell.Effect = _deleteBlurEffect;
                }
                else
                {
                    _tasksBlurContent.Effect = _deleteBlurEffect;
                }

                _deleteConfirmationOverlay.IsVisible = true;
            }

            if (_applicationShell is { } fullShell)
            {
                fullShell.IsHitTestVisible = false;
            }
            else
            {
                _tasksBlurContent.IsHitTestVisible = false;
            }

            // Do not switch the opening style in the same layout update as
            // IsVisible: opacity and radius must both start at zero.
            Dispatcher.UIThread.Post(() =>
            {
                if (!IsCurrentDeleteTransition(viewModel, generation) ||
                    !viewModel.IsDeleteConfirmationOpen)
                {
                    return;
                }

                _deleteConfirmationOverlay.Classes.Add("open");
                _deleteBlurEffect.Radius = _targetDeleteBlur;

                // Programmatic focus must not imitate a Tab press.
                _cancelDeleteButton.Focus(NavigationMethod.Unspecified);
            }, DispatcherPriority.Background);

            return;
        }

        if (!_deleteConfirmationOverlay.IsVisible)
        {
            return;
        }

        _deleteConfirmationOverlay.Classes.Remove("open");
        _deleteBlurEffect.Radius = 0;
        _ = FinishDeleteOverlayCloseAsync(viewModel, generation);
    }

    private async Task FinishDeleteOverlayCloseAsync(
        TaskListViewModel viewModel,
        long generation)
    {
        // Durations come from the AXAML transitions, not a duplicated C# timer.
        var blurDuration = _deleteBlurEffect.Transitions?
            .OfType<DoubleTransition>()
            .Where(t => t.Property == BlurEffect.RadiusProperty)
            .Select(t => t.Duration)
            .DefaultIfEmpty(TimeSpan.Zero)
            .Max() ?? TimeSpan.Zero;

        var matteDuration = _deleteConfirmationOverlay.Transitions?
            .OfType<DoubleTransition>()
            .Where(t => t.Property == Visual.OpacityProperty)
            .Select(t => t.Duration)
            .DefaultIfEmpty(TimeSpan.Zero)
            .Max() ?? TimeSpan.Zero;

        var duration = blurDuration > matteDuration
            ? blurDuration : matteDuration;

        if (duration > TimeSpan.Zero)
        {
            await Task.Delay(duration);
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!IsCurrentDeleteTransition(viewModel, generation) ||
                viewModel.IsDeleteConfirmationOpen)
            {
                return;
            }

            // Commit the terminal state only after the reverse transition.
            _deleteConfirmationOverlay.IsVisible = false;
            ReleaseDeleteVisuals();

            var previousFocus = _focusBeforeDeleteConfirmation;
            _focusBeforeDeleteConfirmation = null;

            Dispatcher.UIThread.Post(() =>
            {
                if (!IsCurrentDeleteTransition(viewModel, generation) ||
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
        });
    }

    private bool IsCurrentDeleteTransition(
        TaskListViewModel viewModel,
        long generation) =>
        _isAttached &&
        ReferenceEquals(_loadedViewModel, viewModel) &&
        generation == _deleteTransitionGeneration;

    private void ResetDeleteOverlay()
    {
        ++_deleteTransitionGeneration; // Invalidates pending UI callbacks.
        _deleteConfirmationOverlay.Classes.Remove("open");
        _deleteConfirmationOverlay.IsVisible = false;
        ReleaseDeleteVisuals();
        SetDeleteBlurImmediately(0);
    }

    private void AttachDeleteOverlayToWindow(TaskListViewModel viewModel)
    {
        if (_applicationRoot is not null)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is not MainWindow window ||
            window.FindControl<Grid>("ApplicationRoot") is not { } root ||
            window.FindControl<Grid>("ApplicationShell") is not { } shell ||
            _deleteConfirmationOverlay.Parent is not Panel originalParent)
        {
            return;
        }

        originalParent.Children.Remove(_deleteConfirmationOverlay);

        // Once reparented outside TaskListView, the dialog must keep its
        // TaskListViewModel binding context explicitly.
        _deleteConfirmationOverlay.DataContext = viewModel;
        root.Children.Add(_deleteConfirmationOverlay);

        _originalOverlayParent = originalParent;
        _applicationRoot = root;
        _applicationShell = shell;
    }

    private void ReleaseDeleteVisuals()
    {
        if (_applicationShell is { } shell)
        {
            shell.Effect = null;
            shell.IsHitTestVisible = true;
        }

        _tasksBlurContent.Effect = null;
        _tasksBlurContent.IsHitTestVisible = true;

        if (_applicationRoot is { } root)
        {
            root.Children.Remove(_deleteConfirmationOverlay);

            // Restore inherited DataContext for the next opening.
            _deleteConfirmationOverlay.ClearValue(
                StyledElement.DataContextProperty);

            _originalOverlayParent?.Children.Add(_deleteConfirmationOverlay);
        }

        _applicationRoot = null;
        _applicationShell = null;
        _originalOverlayParent = null;
    }

    private void SetDeleteBlurImmediately(double radius)
    {
        // Removing this local effect must not create an offscreen transition
        // that leaks into the next opening of the confirmation.
        var transitions = _deleteBlurEffect.Transitions;
        _deleteBlurEffect.Transitions = null;
        _deleteBlurEffect.Radius = radius;
        _deleteBlurEffect.Transitions = transitions;
    }

    // Modal keyboard handling is scoped to Tasks, including the exit fade.
    private void OnTaskSafetyKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (e.Handled || _loadedViewModel is not { } viewModel)
        {
            return;
        }

        if (_deleteConfirmationOverlay.IsVisible)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                if (viewModel.IsDeleteConfirmationOpen &&
                    viewModel.CancelDeleteCommand.CanExecute(null))
                {
                    viewModel.CancelDeleteCommand.Execute(null);
                }

                return;
            }

            if (e.Key == Key.Tab)
            {
                e.Handled = true;
                var focused = TopLevel.GetTopLevel(this)?
                    .FocusManager?.GetFocusedElement();

                bool backwards = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

                // Both buttons are the only tab stops in this dialog.
                var target = ReferenceEquals(focused, _cancelDeleteButton)
                    ? _confirmDeleteButton
                    : ReferenceEquals(focused, _confirmDeleteButton)
                        ? _cancelDeleteButton
                        : backwards ? _confirmDeleteButton : _cancelDeleteButton;

                target.Focus(NavigationMethod.Tab);
                return;
            }

            // A parent list must not react to Enter while the modal exits.
            if (e.Key == Key.Enter && !IsFromDeleteDialog(e.Source))
            {
                e.Handled = true;
            }

            return;
        }

        if (e.Key != Key.Escape)
        {
            return;
        }

        if (_calendar.IsOpen)
        {
            _calendar.Close();
            e.Handled = true;
            return;
        }

        if (IsFromTaskTable(e.Source))
        {
            viewModel.SelectedTask = null;
            e.Handled = true;
        }
    }

    private bool IsFromDeleteDialog(object? source) =>
        source is Visual visual &&
        (ReferenceEquals(visual, _deleteConfirmationPanel) ||
         visual.GetVisualAncestors().Contains(_deleteConfirmationPanel));

    private bool IsFromTaskTable(object? source) =>
        source is Visual visual &&
        (ReferenceEquals(visual, _taskTable) ||
         visual.GetVisualAncestors().Contains(_taskTable));

    // Opening a task from the table

    private void OnTaskRowPointerPressed(
        object? sender,
        PointerPressedEventArgs e)
    {
        if (e.Handled ||
            _deleteConfirmationOverlay.IsVisible ||
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
            _deleteConfirmationOverlay.IsVisible ||
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
        if (_deleteConfirmationOverlay.IsVisible ||
            _loadedViewModel is not { } viewModel ||
            !viewModel.CanOpenTaskFromList(row) ||
            row is null)
        {
            return;
        }

        e.Handled = true;
        _calendar.Close();

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

        _calendar.Close();
    }

    // Tasks reuses the same calendar Popup as Quick Task and Task Details.
    // Only the filter ViewModel decides what the selected date means.

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
        if (_deleteConfirmationOverlay.IsVisible ||
            _loadedViewModel is not { } viewModel ||
            viewModel.IsDeleteConfirmationOpen)
        {
            return;
        }

        if (_calendar.IsOpen &&
            ReferenceEquals(_activeDateInput, input))
        {
            _calendar.Close();
            return;
        }

        _calendar.Close();
        _activeDateInput = input;

        // An invalid unfinished text entry remains untouched. Valid text
        // was already applied by OnDateTextChanged, without changing the
        // date when the popup is merely opened.
        DateTime? selected = ReferenceEquals(input, _fromInput)
            ? viewModel.DateFrom?.Date
            : viewModel.DateTo?.Date;

        _calendar.Open(
            placementTarget: target,
            anchorDate: selected ?? DateTime.Today,
            selectedDate: selected,
            culture: viewModel.CalendarCulture,
            placement: PlacementMode.BottomEdgeAlignedLeft);
    }

    private void OnCalendarDateSelected(
        object? sender,
        CalendarDateSelectedEventArgs e)
    {
        if (_activeDateInput is not { } input ||
            _loadedViewModel is not { } viewModel)
        {
            return;
        }

        // Keep the existing Tasks date-filter representation. Date is
        // calendar-local and must not be converted through UTC.
        var day = e.Date;
        var value = new DateTimeOffset(
            day.Year, day.Month, day.Day,
            0, 0, 0, TimeSpan.Zero);

        if (ReferenceEquals(input, _fromInput))
        {
            viewModel.DateFrom = value;
            SyncDateInput(_fromInput, value);
        }
        else if (ReferenceEquals(input, _toInput))
        {
            viewModel.DateTo = value;
            SyncDateInput(_toInput, value);
        }
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