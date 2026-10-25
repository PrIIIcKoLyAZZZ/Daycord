using System.Globalization;
using System.Text.RegularExpressions;

namespace Daycord.Desktop.ViewModels;

internal static class PlannedDuration
{
    public const int DefaultMinutesPerDay = 24 * 60;

    private static readonly Regex DurationExpression = new(
        @"^\s*" +
        @"(?:(?<days>\d+)\s*(?:days?|d|д(?:ней|ня|ень|н)?))?\s*" +
        @"(?:(?<hours>\d+)\s*(?:hours?|hrs?|h|часов|часа|час|ч))?\s*" +
        @"(?:(?<minutes>\d+)\s*(?:minutes?|mins?|min|m|минуты|минута|минут|мин|м))?" +
        @"\s*$",
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant);
    
    public static bool TryParse(
        string? input,
        out int? plannedMinutes)
    {
        return TryParse(
            input,
            DefaultMinutesPerDay,
            out plannedMinutes);
    }

    public static bool TryParse(
        string? input,
        int minutesPerDay,
        out int? plannedMinutes)
    {
        plannedMinutes = null;

        if (minutesPerDay <= 0)
            return false;

        if (string.IsNullOrWhiteSpace(input))
            return true;

        var value = input.Trim();

        // A plain number represents hours.
        if (int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var plainHours))
        {
            var total = (long)plainHours * 60;

            if (total <= 0 || total > int.MaxValue)
                return false;

            plannedMinutes = (int)total;
            return true;
        }

        var match = DurationExpression.Match(value);

        if (!match.Success)
            return false;

        var days = match.Groups["days"];
        var hours = match.Groups["hours"];
        var minutes = match.Groups["minutes"];

        if (!days.Success &&
            !hours.Success &&
            !minutes.Success)
        {
            return false;
        }

        if (!TryReadPart(days, out var d) ||
            !TryReadPart(hours, out var h) ||
            !TryReadPart(minutes, out var m))
        {
            return false;
        }

        var totalMinutes =
            (long)d * minutesPerDay +
            (long)h * 60 +
            m;

        if (totalMinutes <= 0 ||
            totalMinutes > int.MaxValue)
        {
            return false;
        }

        plannedMinutes = (int)totalMinutes;
        return true;
    }
    
    public static bool TryParseRequired(
        string? input,
        out int plannedMinutes)
    {
        plannedMinutes = 0;

        if (!TryParse(input, out var parsed) ||
            !parsed.HasValue)
        {
            return false;
        }

        plannedMinutes = parsed.Value;
        return true;
    }

    public static string Format(long minutes)
    {
        if (minutes <= 0)
            return string.Empty;

        var hours = minutes / 60;
        var remainingMinutes = minutes % 60;

        if (hours == 0)
        {
            return $"{remainingMinutes}m";
        }

        if (remainingMinutes == 0)
        {
            return $"{hours}h";
        }

        return $"{hours}h {remainingMinutes}m";
    }

    private static bool TryReadPart(
        Group group,
        out int value)
    {
        value = 0;

        return !group.Success ||
               int.TryParse(
                   group.Value,
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out value);
    }
}