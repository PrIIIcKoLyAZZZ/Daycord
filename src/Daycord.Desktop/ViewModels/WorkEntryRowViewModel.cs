using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daycord.Core.Domain.Entities;

namespace Daycord.Desktop.ViewModels;

public sealed class WorkEntryRowViewModel : ObservableObject
{
    private bool _isActive;
    private bool _hasUnsavedDraft;

    public WorkEntryRowViewModel(
        WorkEntry entry,
        Action<WorkEntryRowViewModel> editAction,
        Action<WorkEntryRowViewModel> deleteAction)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(editAction);
        ArgumentNullException.ThrowIfNull(deleteAction);

        if (entry.Id == Guid.Empty ||
            entry.TaskId == Guid.Empty ||
            entry.WorkDate == default ||
            entry.ActualMinutes <= 0)
        {
            throw new ArgumentException(
                "Work entry has an invalid identity, date or duration.",
                nameof(entry));
        }

        Id = entry.Id;
        TaskId = entry.TaskId;
        WorkDate = entry.WorkDate;
        ActualMinutes = entry.ActualMinutes;
        WorkDescription = entry.WorkDescription;
        Note = entry.Note;
        CreatedAtUtc = entry.CreatedAtUtc;

        EditCommand = new RelayCommand(() => editAction(this));
        DeleteCommand = new RelayCommand(() => deleteAction(this));
    }

    public Guid Id { get; }
    public Guid TaskId { get; }
    public DateOnly WorkDate { get; }
    public int ActualMinutes { get; }
    public string? WorkDescription { get; }
    public string? Note { get; }
    public DateTime CreatedAtUtc { get; }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetProperty(ref _isActive, value))
                OnPropertyChanged(nameof(DateText));
        }
    }

    public bool HasUnsavedDraft
    {
        get => _hasUnsavedDraft;
        set
        {
            if (SetProperty(ref _hasUnsavedDraft, value))
                OnPropertyChanged(nameof(DateText));
        }
    }

    public string DateText
    {
        get
        {
            var prefix = (IsActive, HasUnsavedDraft) switch
            {
                (true, true) => "› ✎ ",
                (true, false) => "› ",
                (false, true) => "✎ ",
                _ => string.Empty
            };

            return prefix + WorkDate.ToString(
                "dd.MM.yyyy",
                CultureInfo.InvariantCulture);
        }
    }

    public string DurationText =>
        PlannedDuration.Format(ActualMinutes);

    public string DescriptionPreview =>
        Preview(WorkDescription);

    public string NotePreview =>
        Preview(Note);

    public bool HasDescription =>
        !string.IsNullOrWhiteSpace(WorkDescription);

    public bool HasNote =>
        !string.IsNullOrWhiteSpace(Note);

    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }

    private static string Preview(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var singleLine = value
            .Replace("\r\n", " ")
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Trim();

        return singleLine.Length <= 120
            ? singleLine
            : singleLine[..117] + "…";
    }
}