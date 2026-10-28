namespace Daycord.Core.Application.Models;

public sealed class WorkReportPreset
{
    public const int MaxNameLength = 120;
    public const int MaxHeaderLength = 120;

    public static readonly Guid DefaultPresetId =
        Guid.Parse("7b9f3e18-6fc7-449e-9a12-e59063d58101");

    public const string DefaultPresetName = "Default";

    public WorkReportPreset(
        Guid id,
        string name,
        bool isBuiltIn,
        IReadOnlyCollection<WorkReportColumn> columns)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Preset ID cannot be empty.",
                nameof(id));
        }

        if (isBuiltIn != (id == DefaultPresetId))
        {
            throw new ArgumentException(
                "Only the default preset may be built-in.",
                nameof(isBuiltIn));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(columns);

        name = name.Trim();

        if (name.Length > MaxNameLength)
        {
            throw new ArgumentException(
                $"Preset name cannot exceed {MaxNameLength} characters.",
                nameof(name));
        }

        if (columns.Count == 0)
        {
            throw new ArgumentException(
                "A report must contain at least one column.",
                nameof(columns));
        }

        var fields = new HashSet<WorkReportField>();
        var validated = new List<WorkReportColumn>();

        foreach (var column in columns)
        {
            if (column is null)
            {
                throw new ArgumentException(
                    "Report columns cannot contain null values.",
                    nameof(columns));
            }

            if (!Enum.IsDefined(column.Field))
            {
                throw new ArgumentException(
                    $"Unknown report field: {column.Field}.",
                    nameof(columns));
            }

            if (!fields.Add(column.Field))
            {
                throw new ArgumentException(
                    $"Duplicate report field: {column.Field}.",
                    nameof(columns));
            }

            if (string.IsNullOrWhiteSpace(column.Header))
            {
                throw new ArgumentException(
                    "Report column headers cannot be empty.",
                    nameof(columns));
            }

            var header = column.Header.Trim();

            if (header.Length > MaxHeaderLength)
            {
                throw new ArgumentException(
                    $"Column headers cannot exceed " +
                    $"{MaxHeaderLength} characters.",
                    nameof(columns));
            }

            validated.Add(
                new WorkReportColumn(
                    column.Field,
                    header));
        }

        Id = id;
        Name = name;
        IsBuiltIn = isBuiltIn;
        Columns = Array.AsReadOnly(validated.ToArray());
    }

    public Guid Id { get; }

    public string Name { get; }

    public bool IsBuiltIn { get; }

    public IReadOnlyList<WorkReportColumn> Columns { get; }

    public static WorkReportPreset CreateDefault()
    {
        return new WorkReportPreset(
            DefaultPresetId,
            DefaultPresetName,
            true,
            WorkReportColumnLabels.DefaultColumns);
    }
}

public sealed class WorkReportPresetState
{
    public WorkReportPresetState(
        Guid activePresetId,
        IReadOnlyCollection<WorkReportPreset> presets)
    {
        ArgumentNullException.ThrowIfNull(presets);

        var snapshot = presets.ToArray();

        if (snapshot.Length == 0)
        {
            throw new ArgumentException(
                "At least one report preset is required.",
                nameof(presets));
        }

        if (snapshot.Any(static preset => preset is null))
        {
            throw new ArgumentException(
                "The preset collection contains a null value.",
                nameof(presets));
        }

        if (snapshot
            .Select(static preset => preset.Id)
            .Distinct()
            .Count() != snapshot.Length)
        {
            throw new ArgumentException(
                "The preset collection contains duplicate IDs.",
                nameof(presets));
        }

        if (!snapshot.Any(
            static preset =>
                preset.Id == WorkReportPreset.DefaultPresetId &&
                preset.IsBuiltIn))
        {
            throw new ArgumentException(
                "The built-in report preset is missing.",
                nameof(presets));
        }

        var active = snapshot.FirstOrDefault(
            preset => preset.Id == activePresetId);

        if (active is null)
        {
            throw new ArgumentException(
                "The active preset does not exist.",
                nameof(activePresetId));
        }

        ActivePresetId = activePresetId;
        ActivePreset = active;
        Presets = Array.AsReadOnly(snapshot);
    }

    public Guid ActivePresetId { get; }

    public WorkReportPreset ActivePreset { get; }

    public IReadOnlyList<WorkReportPreset> Presets { get; }
}