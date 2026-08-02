namespace FreeP.Core.Model;

/// <summary>Atomically updates authored pie, doughnut, and pie-of-pie settings.</summary>
public sealed class SetChartPieOptionsCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly ChartPieOptions _newOptions;
    private ChartPieOptions? _oldOptions;

    public SetChartPieOptionsCommand(int slideIndex, uint shapeId, ChartPieOptions options)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newOptions = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Label => "Set Pie/Doughnut Options";

    public void Apply(Presentation p)
    {
        var chart = ChartHelper.FindFormattingEditable(p, _slideIndex, _shapeId);
        if (!Supports(chart))
            return;

        _oldOptions = ReadOptions(chart!);
        ApplyOptions(chart!, _newOptions);
        ChartHelper.MarkWorkbookDirty(chart!);
    }

    public void Revert(Presentation p)
    {
        var chart = ChartHelper.FindFormattingEditable(p, _slideIndex, _shapeId);
        if (!Supports(chart) || _oldOptions is null)
            return;

        ApplyOptions(chart!, _oldOptions);
        ChartHelper.MarkWorkbookDirty(chart!);
    }

    private static bool Supports(ChartShape? chart) =>
        chart is not null && chart.ChartType is (ChartType.Pie or ChartType.Doughnut or ChartType.OfPie);

    private static ChartPieOptions ReadOptions(ChartShape chart) => new(
        chart.FirstSliceAngleDegrees,
        Math.Clamp(chart.DoughnutHolePercent, 10, 90),
        chart.ChartType == ChartType.OfPie ? chart.OfPieType : null,
        chart.ChartType == ChartType.OfPie ? chart.OfPieSplitType : null,
        chart.ChartType == ChartType.OfPie ? chart.OfPieSplitPosition : null,
        chart.ChartType == ChartType.OfPie ? chart.OfPieSecondPieSizePercent : null,
        chart.ChartType == ChartType.OfPie ? chart.OfPieCustomPointIndices.ToArray() : null);

    private static void ApplyOptions(ChartShape chart, ChartPieOptions options)
    {
        chart.FirstSliceAngleDegrees = options.FirstSliceAngleDegrees is { } angle
            ? Math.Clamp(angle, 0, 359)
            : null;
        chart.DoughnutHolePercent = Math.Clamp(options.DoughnutHolePercent, 10, 90);
        if (chart.ChartType != ChartType.OfPie || options.OfPieType is null)
            return;

        chart.OfPieType = options.OfPieType.Value;
        chart.OfPieSplitType = options.OfPieSplitType;
        chart.OfPieSplitPosition = options.OfPieSplitPosition;
        chart.OfPieSecondPieSizePercent = options.OfPieSecondPieSizePercent is { } size
            ? Math.Clamp(size, 5, 200)
            : null;
        chart.OfPieCustomPointIndices = options.OfPieCustomPointIndices?
            .Where(index => index >= 0)
            .Distinct()
            .ToList() ?? [];
    }
}
