using System.Globalization;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using PresentationPlanner = FreeX.App.Presentation.FillSeries.FillSeriesPlanner;
using PresentationDirection = FreeX.App.Presentation.FillSeries.FillSeriesDirection;
using PresentationType = FreeX.App.Presentation.FillSeries.FillSeriesType;
using PresentationDateUnit = FreeX.App.Presentation.FillSeries.FillSeriesDateUnit;
using PresentationOptions = FreeX.App.Presentation.FillSeries.FillSeriesOptions;

namespace FreeX.App.Host;

/// <summary>
/// Thin WPF-side adapter over the portable <see cref="PresentationPlanner"/>. It keeps the host's enum/result
/// surface (bound to <see cref="FillSeriesStepDialog"/>) and forwards all parsing and series math to the shared
/// planner so the logic lives in exactly one place.
/// </summary>
public static class FillSeriesPlanner
{
    // Kept host-local: this WPF entry point parses in the current UI culture only (so a French "1,5" reads as
    // 1.5), which differs from the portable planner's invariant-first parse. Behaviour pinned by host tests.
    public static bool TryParseStep(string input, out double step)
        => NumericInputParser.TryParseFiniteDouble(
            input,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.CurrentCulture,
            out step);

    public static bool CanFill(GridRange range, FillCellsDirection direction) =>
        PresentationPlanner.CanFill(range, direction);

    public static List<(CellAddress Address, Cell NewCell)> BuildLinearSeriesEdits(Sheet sheet, GridRange range, double step)
        => PresentationPlanner.BuildLinearSeriesEdits(sheet, range, step, PresentationDirection.Rows);

    public static List<(CellAddress Address, Cell NewCell)> BuildSeriesEdits(
        Sheet sheet,
        GridRange range,
        FillSeriesStepDialogResult result)
        => PresentationPlanner.BuildSeriesEdits(
            sheet,
            range,
            new PresentationOptions(
                result.Step,
                ToPresentation(result.SeriesIn),
                ToPresentation(result.Type),
                ToPresentation(result.DateUnit),
                result.StopValue));

    public static List<(CellAddress Address, Cell NewCell)> BuildLinearSeriesEdits(
        Sheet sheet,
        GridRange range,
        double step,
        FillSeriesDirection seriesIn,
        double? stopValue = null)
        => PresentationPlanner.BuildLinearSeriesEdits(sheet, range, step, ToPresentation(seriesIn), stopValue);

    public static List<(CellAddress Address, Cell NewCell)> BuildGrowthSeriesEdits(
        Sheet sheet,
        GridRange range,
        double step,
        FillSeriesDirection seriesIn,
        double? stopValue = null)
        => PresentationPlanner.BuildGrowthSeriesEdits(sheet, range, step, ToPresentation(seriesIn), stopValue);

    public static List<(CellAddress Address, Cell NewCell)> BuildDateSeriesEdits(
        Sheet sheet,
        GridRange range,
        double step,
        FillSeriesDirection seriesIn,
        FillSeriesDateUnit dateUnit,
        double? stopValue = null)
        => PresentationPlanner.BuildDateSeriesEdits(sheet, range, step, ToPresentation(seriesIn), ToPresentation(dateUnit), stopValue);

    private static PresentationDirection ToPresentation(FillSeriesDirection direction) => direction switch
    {
        FillSeriesDirection.Columns => PresentationDirection.Columns,
        _ => PresentationDirection.Rows,
    };

    private static PresentationType ToPresentation(FillSeriesType type) => type switch
    {
        FillSeriesType.Growth => PresentationType.Growth,
        FillSeriesType.Date => PresentationType.Date,
        FillSeriesType.AutoFill => PresentationType.AutoFill,
        _ => PresentationType.Linear,
    };

    private static PresentationDateUnit ToPresentation(FillSeriesDateUnit dateUnit) => dateUnit switch
    {
        FillSeriesDateUnit.Weekday => PresentationDateUnit.Weekday,
        FillSeriesDateUnit.Month => PresentationDateUnit.Month,
        FillSeriesDateUnit.Year => PresentationDateUnit.Year,
        _ => PresentationDateUnit.Day,
    };
}
