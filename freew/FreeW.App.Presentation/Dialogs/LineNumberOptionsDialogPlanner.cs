using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public sealed record LineNumberOptionsInitialState(
    string StartAtText,
    string CountByText,
    int ModeIndex);

public sealed record LineNumberOptionsDialogInput(
    string? StartAtText,
    string? CountByText,
    int ModeIndex);

public sealed record LineNumberOptionsDialogResult(
    int StartAt,
    int CountBy,
    LineNumberMode Mode);

public enum LineNumberOptionsDialogField
{
    StartAt,
    CountBy,
    Numbering,
}

public static class LineNumberOptionsDialogPlanner
{
    public const string Title = "Line Numbering Options";
    public const string StartAtLabel = "Start at:";
    public const string CountByLabel = "Count by:";
    public const string NumberingLabel = "Numbering:";
    public const string StartAtValidationMessage = "Start At must be a whole number of 1 or greater.";
    public const string CountByValidationMessage = "Count By must be a whole number of 1 or greater.";
    public const string AutomationId = "LineNumberOptionsDialog";
    public const string StartAtAutomationId = "LineNumberStartAt";
    public const string CountByAutomationId = "LineNumberCountBy";
    public const string ModeAutomationId = "LineNumberMode";

    public static DialogSurfaceSpec<LineNumberOptionsDialogField> Surface { get; } = new(
        Title,
        AutomationId,
        Title,
        [
            new(LineNumberOptionsDialogField.StartAt, StartAtLabel, StartAtAutomationId, "Line number start"),
            new(LineNumberOptionsDialogField.CountBy, CountByLabel, CountByAutomationId, "Line number interval"),
            new(LineNumberOptionsDialogField.Numbering, NumberingLabel, ModeAutomationId, "Line number mode"),
        ],
        ValidationAutomationId: "LineNumberValidationMessage");

    private static readonly string[] ModeLabelValues = ["Continuous", "Restart Each Page", "Restart Each Section"];

    public static IReadOnlyList<string> ModeLabels => ModeLabelValues;

    public static LineNumberOptionsInitialState BuildInitialState(
        int startAt,
        int countBy,
        LineNumberMode mode,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return new LineNumberOptionsInitialState(
            StartAtText: startAt.ToString(culture),
            CountByText: countBy.ToString(culture),
            ModeIndex: ModeIndexFor(mode));
    }

    public static int ModeIndexFor(LineNumberMode mode) => mode switch
    {
        LineNumberMode.RestartEachPage => 1,
        LineNumberMode.RestartEachSection => 2,
        _ => 0,
    };

    public static LineNumberMode ModeForIndex(int selectedIndex) => selectedIndex switch
    {
        1 => LineNumberMode.RestartEachPage,
        2 => LineNumberMode.RestartEachSection,
        _ => LineNumberMode.Continuous,
    };

    public static bool TryBuildResult(
        LineNumberOptionsDialogInput input,
        CultureInfo culture,
        out LineNumberOptionsDialogResult? result,
        out string? errorMessage)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(culture);

        result = null;
        errorMessage = null;

        if (!TryParsePositiveWholeNumber(input.StartAtText, culture, out var startAt))
        {
            errorMessage = StartAtValidationMessage;
            return false;
        }

        if (!TryParsePositiveWholeNumber(input.CountByText, culture, out var countBy))
        {
            errorMessage = CountByValidationMessage;
            return false;
        }

        result = new LineNumberOptionsDialogResult(startAt, countBy, ModeForIndex(input.ModeIndex));
        return true;
    }

    private static bool TryParsePositiveWholeNumber(string? text, CultureInfo culture, out int value)
    {
        var trimmed = (text ?? string.Empty).Trim();
        return int.TryParse(trimmed, NumberStyles.Integer, culture, out value) && value >= 1;
    }
}
