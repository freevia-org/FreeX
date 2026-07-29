using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record ChartProtectionBooleanOption(bool? Value, string Label);

public sealed record ChartProtectionOptionsSurfacePlan(
    string CommandId,
    string Title,
    string ChartObjectLabel,
    string DataLabel,
    string FormattingLabel,
    string SelectionLabel,
    string Hint,
    string OkLabel,
    string CancelLabel);

/// <summary>Working-copy planner for the four native chart protection flags.</summary>
public sealed class ChartProtectionOptionsPlanner
{
    public const string CommandId = "freep.chart.protection-options";
    public const string DialogTitle = "Chart Protection";
    public const string ChartObjectLabel = "Chart object";
    public const string DataLabel = "Chart data";
    public const string FormattingLabel = "Chart formatting";
    public const string SelectionLabel = "Chart selection";
    public const string Hint = "Automatic omits the authored setting; Protected blocks the corresponding edit; Unprotected explicitly permits it.";
    public const string OkLabel = "OK";
    public const string CancelLabel = "Cancel";
    public const double DefaultDialogWidth = 460;
    public const double DefaultDialogHeight = 300;

    public static IReadOnlyList<ChartProtectionBooleanOption> BooleanOptions { get; } =
    [
        new(null, "Automatic"),
        new(true, "Protected"),
        new(false, "Unprotected"),
    ];

    private bool? _chartObject;
    private bool? _data;
    private bool? _formatting;
    private bool? _selection;

    private ChartProtectionOptionsPlanner(ChartShape chart)
    {
        _chartObject = chart.ChartObjectProtected;
        _data = chart.ChartDataProtected;
        _formatting = chart.ChartFormattingProtected;
        _selection = chart.ChartSelectionProtected;
    }

    public static ChartProtectionOptionsSurfacePlan BuildSurfacePlan() => new(
        CommandId,
        DialogTitle,
        ChartObjectLabel,
        DataLabel,
        FormattingLabel,
        SelectionLabel,
        Hint,
        OkLabel,
        CancelLabel);

    public static ChartProtectionOptionsPlanner FromChart(ChartShape chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return new ChartProtectionOptionsPlanner(chart);
    }

    public bool? ChartObject => _chartObject;
    public bool? Data => _data;
    public bool? Formatting => _formatting;
    public bool? Selection => _selection;

    public void SetChartObject(bool? value) => _chartObject = value;
    public void SetData(bool? value) => _data = value;
    public void SetFormatting(bool? value) => _formatting = value;
    public void SetSelection(bool? value) => _selection = value;

    public ChartProtectionOptions BuildCommitPlan() => new(
        _chartObject,
        _data,
        _formatting,
        _selection);
}
