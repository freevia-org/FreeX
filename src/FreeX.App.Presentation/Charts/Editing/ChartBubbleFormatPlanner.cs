using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Editing;

/// <summary>The bubble-chart sizing state read from a chart and edited back through the dialog.</summary>
public readonly record struct ChartBubbleFormatInput(
    int BubbleScale,
    bool ShowNegativeBubbles,
    ChartBubbleSizeRepresents BubbleSizeRepresents);

public enum ChartBubbleFormatParseIssue
{
    None,
    BubbleScale
}

public enum ChartBubbleFormatDialogControlKind
{
    Number,
    CheckBox,
    ComboBox,
}

public enum ChartBubbleFormatDialogFieldId
{
    BubbleScale,
    ShowNegativeBubbles,
    SizeRepresents,
}

public sealed record ChartBubbleFormatDialogFieldDescriptor(
    ChartBubbleFormatDialogFieldId Id,
    ChartBubbleFormatDialogControlKind ControlKind,
    string LabelResourceKey,
    string AutomationId,
    string? HelpResourceKey = null);

public sealed record ChartBubbleFormatDialogSectionDescriptor(
    string HeaderResourceKey,
    IReadOnlyList<ChartBubbleFormatDialogFieldDescriptor> Fields);

/// <summary>
/// Portable (no UI) planner for the "Format Bubble Chart" editing dialog: the bubble scale (percent), whether
/// negative-value bubbles are drawn, and whether the third column represents bubble area or width. Single-
/// sources the read/validate/project rules and maps an edited <see cref="ChartBubbleFormatInput"/> onto the
/// <see cref="ChartLayoutOptions"/> the shell hands to the Core <see cref="SetChartLayoutCommand"/>. Every
/// field already exists on <see cref="ChartModel"/> and is clamped by Core's <c>ApplyOptions</c>, so no Core
/// change is needed. Reused across every shell. (The WPF host's <c>ChartBubbleFormatDialog</c> is the
/// behavior reference.)
/// </summary>
public static class ChartBubbleFormatPlanner
{
    public const string TitleResourceKey = "ChartBubbleFormat_Title";
    public const string DialogAutomationId = "ChartBubbleFormatDialog";

    public const int MinBubbleScale = 1;
    public const int MaxBubbleScale = 300;

    private static readonly ChartBubbleFormatDialogFieldDescriptor[] OptionFields =
    [
        new(ChartBubbleFormatDialogFieldId.BubbleScale, ChartBubbleFormatDialogControlKind.Number, "ChartBubbleFormat_BubbleScaleLabel", "ChartBubbleFormatScaleBox", "ChartBubbleFormat_BubbleScaleHelpText"),
        new(ChartBubbleFormatDialogFieldId.ShowNegativeBubbles, ChartBubbleFormatDialogControlKind.CheckBox, "ChartBubbleFormat_ShowNegativeBubbles", "ChartBubbleFormatNegativeCheck"),
        new(ChartBubbleFormatDialogFieldId.SizeRepresents, ChartBubbleFormatDialogControlKind.ComboBox, "ChartBubbleFormat_SizeRepresentsLabel", "ChartBubbleFormatSizeCombo"),
    ];

    private static readonly ChartBubbleFormatDialogSectionDescriptor[] DialogSections =
    [
        new("ChartBubbleFormat_OptionsGroup", OptionFields),
    ];

    public static IReadOnlyList<ChartBubbleFormatDialogSectionDescriptor> GetDialogSections() => DialogSections;

    public static ChartBubbleFormatDialogSectionDescriptor GetOptionsSection() => DialogSections[0];

    public static ChartBubbleFormatDialogFieldDescriptor GetDialogField(ChartBubbleFormatDialogFieldId id)
    {
        foreach (var section in DialogSections)
        {
            foreach (var field in section.Fields)
            {
                if (field.Id == id)
                    return field;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(id), id, null);
    }

    public static string InvalidInputMessageResourceKey(ChartBubbleFormatParseIssue issue) =>
        "ChartBubbleFormat_InvalidBubbleScaleMessage";

    /// <summary>True when the chart is a bubble chart that has these sizing options.</summary>
    public static bool Supports(ChartModel chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return chart.Type == ChartType.Bubble;
    }

    /// <summary>Reads the chart's current bubble sizing into the dialog input shape.</summary>
    public static ChartBubbleFormatInput Read(ChartModel chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return Normalize(new ChartBubbleFormatInput(chart.BubbleScale, chart.ShowNegativeBubbles, chart.BubbleSizeRepresents));
    }

    /// <summary>Validates the edited input. Returns null when valid, else an English reason.</summary>
    public static string? Validate(ChartBubbleFormatInput input)
    {
        if (input.BubbleScale < MinBubbleScale || input.BubbleScale > MaxBubbleScale)
            return $"Enter a bubble scale between {MinBubbleScale} and {MaxBubbleScale}.";

        return null;
    }

    /// <summary>The bubble-size-represents choices the dialog should offer, in display order.</summary>
    public static IReadOnlyList<ChartBubbleSizeRepresents> GetSizeRepresentsChoices() =>
        Enum.GetValues<ChartBubbleSizeRepresents>();

    public static bool TryParseDialogInput(
        string bubbleScaleText,
        bool showNegativeBubbles,
        ChartBubbleSizeRepresents? selectedSizeRepresents,
        out ChartBubbleFormatInput input,
        out ChartBubbleFormatParseIssue issue)
    {
        if (!NumericInputParser.TryParseInt32InRange(
                bubbleScaleText,
                MinBubbleScale,
                MaxBubbleScale,
                out var scale))
        {
            input = default;
            issue = ChartBubbleFormatParseIssue.BubbleScale;
            return false;
        }

        input = new ChartBubbleFormatInput(
            scale,
            showNegativeBubbles,
            NormalizeSizeRepresents(selectedSizeRepresents));
        issue = ChartBubbleFormatParseIssue.None;
        return true;
    }

    public static ChartBubbleSizeRepresents NormalizeSizeRepresents(ChartBubbleSizeRepresents? sizeRepresents) =>
        sizeRepresents is { } value && Enum.IsDefined(value)
            ? value
            : ChartBubbleSizeRepresents.Area;

    public static ChartBubbleFormatInput Normalize(ChartBubbleFormatInput input) =>
        new(
            Math.Clamp(input.BubbleScale, MinBubbleScale, MaxBubbleScale),
            input.ShowNegativeBubbles,
            NormalizeSizeRepresents(input.BubbleSizeRepresents));

    /// <summary>Builds the <see cref="ChartLayoutOptions"/> delta for the edited bubble sizing.</summary>
    public static ChartLayoutOptions Plan(ChartBubbleFormatInput input)
    {
        var normalized = Normalize(input);
        return new(
            BubbleScale: normalized.BubbleScale,
            ShowNegativeBubbles: normalized.ShowNegativeBubbles,
            BubbleSizeRepresents: normalized.BubbleSizeRepresents);
    }

}
