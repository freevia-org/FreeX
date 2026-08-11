using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Editing;

public sealed record PivotChartOptionsInput(
    int? ChartStyleId,
    bool ShowFieldButtons,
    bool ShowReportFilterButtons,
    bool ShowAxisFieldButtons,
    bool ShowValueFieldButtons,
    bool ShowDataTable = false,
    bool ShowDataTableLegendKeys = false,
    bool RoundedCorners = false,
    bool ShowHiddenData = false,
    ChartBlankDisplayMode BlankDisplayMode = ChartBlankDisplayMode.Gap);

public enum PivotChartOptionsDialogFieldId
{
    ChartStyle,
    ShowFieldButtons,
    ShowReportFilterButtons,
    ShowAxisFieldButtons,
    ShowValueFieldButtons,
    ShowDataTable,
    ShowDataTableLegendKeys,
    RoundedCorners,
    ShowHiddenData,
    BlankDisplayMode,
}

public sealed record PivotChartOptionsDialogFieldDescriptor(
    PivotChartOptionsDialogFieldId Id,
    string LabelResourceKey,
    string AutomationId,
    string? AutomationNameResourceKey = null);

public sealed record PivotChartOptionsDialogSectionDescriptor(
    string HeaderResourceKey,
    IReadOnlyList<PivotChartOptionsDialogFieldId> Fields);

public sealed record PivotChartOptionsBlankDisplayChoice(
    string LabelResourceKey,
    ChartBlankDisplayMode Mode);

public sealed record PivotChartOptionsResolvedBlankDisplayChoice(
    string Label,
    ChartBlankDisplayMode Mode);

/// <summary>
/// Renderer-neutral planner for PivotChart Options. It projects the chart's current options into a
/// portable input record, normalizes submitted style ids through <see cref="ChartStylePlanner"/>, and
/// single-sources the dialog sections, field labels, automation ids, and blank-cell choices.
/// </summary>
public static class PivotChartOptionsPlanner
{
    public const string DialogTitleResourceKey = "PivotChartOptions_PivotChartOptions";
    public const string DialogAutomationId = "PivotChartOptionsDialog";

    private static readonly IReadOnlyList<PivotChartOptionsDialogSectionDescriptor> DialogSections =
    [
        new(
            "PivotChartOptions_ChartStyleGroup",
            [PivotChartOptionsDialogFieldId.ChartStyle]),
        new(
            "PivotChartOptions_FieldButtonsGroup",
            [
                PivotChartOptionsDialogFieldId.ShowFieldButtons,
                PivotChartOptionsDialogFieldId.ShowReportFilterButtons,
                PivotChartOptionsDialogFieldId.ShowAxisFieldButtons,
                PivotChartOptionsDialogFieldId.ShowValueFieldButtons,
            ]),
        new(
            "PivotChartOptions_LayoutGroup",
            [
                PivotChartOptionsDialogFieldId.ShowDataTable,
                PivotChartOptionsDialogFieldId.ShowDataTableLegendKeys,
                PivotChartOptionsDialogFieldId.RoundedCorners,
                PivotChartOptionsDialogFieldId.ShowHiddenData,
                PivotChartOptionsDialogFieldId.BlankDisplayMode,
            ]),
    ];

    private static readonly IReadOnlyDictionary<PivotChartOptionsDialogFieldId, PivotChartOptionsDialogFieldDescriptor> DialogFields =
        new Dictionary<PivotChartOptionsDialogFieldId, PivotChartOptionsDialogFieldDescriptor>
        {
            [PivotChartOptionsDialogFieldId.ChartStyle] = new(
                PivotChartOptionsDialogFieldId.ChartStyle,
                "PivotChartOptions_ChartStyle",
                "PivotChartOptionsStyleGallery",
                "PivotChartOptions_PivotChartStyleGallery"),
            [PivotChartOptionsDialogFieldId.ShowFieldButtons] = new(
                PivotChartOptionsDialogFieldId.ShowFieldButtons,
                "PivotChartOptions_ShowFieldButtonsOnChart",
                "PivotChartOptionsShowFieldButtons"),
            [PivotChartOptionsDialogFieldId.ShowReportFilterButtons] = new(
                PivotChartOptionsDialogFieldId.ShowReportFilterButtons,
                "PivotChartOptions_ReportFilterButtons",
                "PivotChartOptionsReportFilterButtons"),
            [PivotChartOptionsDialogFieldId.ShowAxisFieldButtons] = new(
                PivotChartOptionsDialogFieldId.ShowAxisFieldButtons,
                "PivotChartOptions_AxisFieldButtons",
                "PivotChartOptionsAxisFieldButtons"),
            [PivotChartOptionsDialogFieldId.ShowValueFieldButtons] = new(
                PivotChartOptionsDialogFieldId.ShowValueFieldButtons,
                "PivotChartOptions_ValueFieldButtons",
                "PivotChartOptionsValueFieldButtons"),
            [PivotChartOptionsDialogFieldId.ShowDataTable] = new(
                PivotChartOptionsDialogFieldId.ShowDataTable,
                "PivotChartOptions_ShowDataTable",
                "PivotChartOptionsShowDataTable"),
            [PivotChartOptionsDialogFieldId.ShowDataTableLegendKeys] = new(
                PivotChartOptionsDialogFieldId.ShowDataTableLegendKeys,
                "PivotChartOptions_ShowLegendKeys",
                "PivotChartOptionsDataTableLegendKeys"),
            [PivotChartOptionsDialogFieldId.RoundedCorners] = new(
                PivotChartOptionsDialogFieldId.RoundedCorners,
                "PivotChartOptions_RoundedCorners",
                "PivotChartOptionsRoundedCorners"),
            [PivotChartOptionsDialogFieldId.ShowHiddenData] = new(
                PivotChartOptionsDialogFieldId.ShowHiddenData,
                "PivotChartOptions_ShowDataInHiddenRowsAndColumns",
                "PivotChartOptionsShowHiddenData"),
            [PivotChartOptionsDialogFieldId.BlankDisplayMode] = new(
                PivotChartOptionsDialogFieldId.BlankDisplayMode,
                "PivotChartOptions_BlankCells",
                "PivotChartOptionsBlankDisplayMode"),
        };

    private static readonly IReadOnlyList<PivotChartOptionsBlankDisplayChoice> BlankDisplayChoices =
    [
        new("PivotChartOptions_BlankDisplayGaps", ChartBlankDisplayMode.Gap),
        new("PivotChartOptions_BlankDisplayConnectDataPoints", ChartBlankDisplayMode.Span),
        new("PivotChartOptions_BlankDisplayZero", ChartBlankDisplayMode.Zero),
    ];

    public static IReadOnlyList<PivotChartOptionsDialogSectionDescriptor> GetDialogSections() => DialogSections;

    public static PivotChartOptionsDialogSectionDescriptor GetChartStyleSection() => DialogSections[0];

    public static PivotChartOptionsDialogSectionDescriptor GetFieldButtonsSection() => DialogSections[1];

    public static PivotChartOptionsDialogSectionDescriptor GetLayoutSection() => DialogSections[2];

    public static PivotChartOptionsDialogFieldDescriptor GetDialogField(PivotChartOptionsDialogFieldId id)
    {
        if (DialogFields.TryGetValue(id, out var field))
            return field;

        throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown PivotChart options dialog field.");
    }

    public static IReadOnlyList<PivotChartOptionsBlankDisplayChoice> GetBlankDisplayChoices() => BlankDisplayChoices;

    public static IReadOnlyList<PivotChartOptionsResolvedBlankDisplayChoice> GetResolvedBlankDisplayChoices(
        Func<string, string> resolveText)
    {
        ArgumentNullException.ThrowIfNull(resolveText);
        return BlankDisplayChoices
            .Select(choice => new PivotChartOptionsResolvedBlankDisplayChoice(
                resolveText(choice.LabelResourceKey),
                choice.Mode))
            .ToArray();
    }

    public static PivotChartOptionsInput Read(ChartModel chart) =>
        new(
            ChartStylePlanner.Read(chart).StyleId,
            chart.ShowPivotChartFieldButtons,
            chart.ShowPivotChartReportFilterButtons,
            chart.ShowPivotChartAxisFieldButtons,
            chart.ShowPivotChartValueFieldButtons,
            chart.DataTable is not null,
            chart.DataTable?.ShowLegendKeys == true,
            chart.RoundedCorners,
            chart.ShowDataInHiddenRowsAndColumns,
            chart.BlankDisplayMode);

    public static PivotChartOptionsInput CreateResult(
        string? chartStyleIdText,
        bool showFieldButtons,
        bool showReportFilterButtons = true,
        bool showAxisFieldButtons = true,
        bool showValueFieldButtons = true,
        bool showDataTable = false,
        bool showDataTableLegendKeys = false,
        bool roundedCorners = false,
        bool showHiddenData = false,
        ChartBlankDisplayMode blankDisplayMode = ChartBlankDisplayMode.Gap) =>
        CreateResult(
            ParseStyleId(chartStyleIdText),
            showFieldButtons,
            showReportFilterButtons,
            showAxisFieldButtons,
            showValueFieldButtons,
            showDataTable,
            showDataTableLegendKeys,
            roundedCorners,
            showHiddenData,
            blankDisplayMode);

    public static PivotChartOptionsInput CreateResult(
        int? chartStyleId,
        bool showFieldButtons,
        bool showReportFilterButtons = true,
        bool showAxisFieldButtons = true,
        bool showValueFieldButtons = true,
        bool showDataTable = false,
        bool showDataTableLegendKeys = false,
        bool roundedCorners = false,
        bool showHiddenData = false,
        ChartBlankDisplayMode blankDisplayMode = ChartBlankDisplayMode.Gap) =>
        new(
            ChartStylePlanner.CreateResult(chartStyleId).StyleId,
            showFieldButtons,
            showReportFilterButtons,
            showAxisFieldButtons,
            showValueFieldButtons,
            showDataTable,
            showDataTableLegendKeys,
            roundedCorners,
            showHiddenData,
            blankDisplayMode);

    private static int? ParseStyleId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return ChartStylePlanner.ParseStyleId(text);
    }
}
