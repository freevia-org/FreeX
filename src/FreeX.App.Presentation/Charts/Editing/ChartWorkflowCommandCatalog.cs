namespace FreeX.App.Presentation.Charts.Editing;

public enum ChartWorkflowCommandId
{
    ChangeChartType,
    SelectDataSource,
    MoveChart,
    FormatChartArea,
}

public sealed record ChartWorkflowCommandDescriptor(
    ChartWorkflowCommandId Id,
    string Label,
    string HostMissingSelectionMessageResourceKey);

/// <summary>
/// Shared labels and shell resource keys for chart contextual command workflows. Platform renderers still
/// own the dialogs and command execution; this catalog keeps cross-platform chart action text in one place.
/// </summary>
public static class ChartWorkflowCommandCatalog
{
    public const string DefaultHostMissingSelectionMessageResourceKey = "MainWindowMessage_ChartSelectBeforeCommand";

    public static readonly ChartWorkflowCommandDescriptor ChangeChartType = new(
        ChartWorkflowCommandId.ChangeChartType,
        "Change Chart Type",
        DefaultHostMissingSelectionMessageResourceKey);

    public static readonly ChartWorkflowCommandDescriptor SelectDataSource = new(
        ChartWorkflowCommandId.SelectDataSource,
        "Select Data Source",
        DefaultHostMissingSelectionMessageResourceKey);

    public static readonly ChartWorkflowCommandDescriptor MoveChart = new(
        ChartWorkflowCommandId.MoveChart,
        "Move Chart",
        DefaultHostMissingSelectionMessageResourceKey);

    public static readonly ChartWorkflowCommandDescriptor FormatChartArea = new(
        ChartWorkflowCommandId.FormatChartArea,
        "Format Chart Area",
        "MainWindowMessage_ChartSelectForChartAreaFormatting");

    private static readonly ChartWorkflowCommandDescriptor[] Commands =
    [
        ChangeChartType,
        SelectDataSource,
        MoveChart,
        FormatChartArea,
    ];

    public static IReadOnlyList<ChartWorkflowCommandDescriptor> All => Commands;

    public static ChartWorkflowCommandDescriptor Get(ChartWorkflowCommandId id)
    {
        foreach (var command in Commands)
        {
            if (command.Id == id)
                return command;
        }

        throw new ArgumentOutOfRangeException(nameof(id), id, null);
    }
}
