using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed class ForecastSheetCommand : IWorkbookCommand
{
    private readonly GridRange _sourceRange;
    private readonly uint _forecastPeriods;
    private SheetId? _addedSheetId;

    public string Label => "Forecast Sheet";

    public ForecastSheetCommand(GridRange sourceRange, uint forecastPeriods)
    {
        _sourceRange = sourceRange;
        _forecastPeriods = forecastPeriods;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;
        if (_sourceRange.ColCount != 2 || _sourceRange.RowCount < 3)
            return new CommandOutcome(false, "Forecast Sheet requires a two-column range with headers and at least two data rows.");
        if (_forecastPeriods == 0)
            return new CommandOutcome(false, "Forecast periods must be greater than zero.");

        var sourceSheet = ctx.GetSheet(_sourceRange.Start.Sheet);
        var forecastSheet = ctx.Workbook.AddSheet(GetForecastSheetName(ctx.Workbook));
        _addedSheetId = forecastSheet.Id;

        var timelineHeader = sourceSheet.GetCell(_sourceRange.Start)?.Clone()
            ?? Cell.FromValue(new TextValue("Timeline"));
        var valuesHeader = sourceSheet.GetCell(new CellAddress(
            _sourceRange.Start.Sheet,
            _sourceRange.Start.Row,
            _sourceRange.Start.Col + 1))?.Clone()
            ?? Cell.FromValue(new TextValue("Values"));
        forecastSheet.SetCell(new CellAddress(forecastSheet.Id, 1, 1), timelineHeader);
        forecastSheet.SetCell(new CellAddress(forecastSheet.Id, 1, 2), valuesHeader);
        forecastSheet.SetCell(new CellAddress(forecastSheet.Id, 1, 3), new TextValue("Forecast"));
        forecastSheet.SetCell(new CellAddress(forecastSheet.Id, 1, 4), new TextValue("Lower Confidence Bound"));
        forecastSheet.SetCell(new CellAddress(forecastSheet.Id, 1, 5), new TextValue("Upper Confidence Bound"));

        var dataRowCount = _sourceRange.RowCount - 1;
        for (uint offset = 0; offset < dataRowCount; offset++)
        {
            var sourceRow = _sourceRange.Start.Row + 1 + offset;
            var targetRow = 2 + offset;
            CopyCell(sourceSheet, forecastSheet, sourceRow, _sourceRange.Start.Col, targetRow, 1);
            CopyCell(sourceSheet, forecastSheet, sourceRow, _sourceRange.Start.Col + 1, targetRow, 2);
        }

        var step = GetTimelineStep(sourceSheet);
        var lastTimeline = GetNumber(sourceSheet.GetValue(_sourceRange.End.Row, _sourceRange.Start.Col));
        var knownX = $"A2:A{dataRowCount + 1}";
        var knownY = $"B2:B{dataRowCount + 1}";
        for (uint offset = 1; offset <= _forecastPeriods; offset++)
        {
            var row = dataRowCount + 1 + offset;
            forecastSheet.SetCell(new CellAddress(forecastSheet.Id, row, 1), new NumberValue(lastTimeline + (step * offset)));
            forecastSheet.SetCell(new CellAddress(forecastSheet.Id, row, 3), Cell.FromFormula($"FORECAST.LINEAR(A{row},{knownY},{knownX})"));
            var confidence = $"CONFIDENCE.NORM(0.05,STEYX({knownY},{knownX}),COUNT({knownX}))";
            forecastSheet.SetCell(new CellAddress(forecastSheet.Id, row, 4), Cell.FromFormula($"C{row}-{confidence}"));
            forecastSheet.SetCell(new CellAddress(forecastSheet.Id, row, 5), Cell.FromFormula($"C{row}+{confidence}"));
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_addedSheetId is { } sheetId)
            ctx.Workbook.RemoveSheet(sheetId);
        _addedSheetId = null;
    }

    private double GetTimelineStep(Sheet sourceSheet)
    {
        var last = GetNumber(sourceSheet.GetValue(_sourceRange.End.Row, _sourceRange.Start.Col));
        var previous = GetNumber(sourceSheet.GetValue(_sourceRange.End.Row - 1, _sourceRange.Start.Col));
        var step = last - previous;
        return Math.Abs(step) < double.Epsilon ? 1 : step;
    }

    private static double GetNumber(ScalarValue value) => value is NumberValue number ? number.Value : 0;

    private static void CopyCell(Sheet sourceSheet, Sheet forecastSheet, uint sourceRow, uint sourceCol, uint targetRow, uint targetCol)
    {
        var source = sourceSheet.GetCell(new CellAddress(sourceSheet.Id, sourceRow, sourceCol));
        forecastSheet.SetCell(
            new CellAddress(forecastSheet.Id, targetRow, targetCol),
            source?.Clone() ?? Cell.FromValue(BlankValue.Instance));
    }

    private static string GetForecastSheetName(Workbook workbook)
    {
        if (workbook.ValidateSheetName("Forecast") is null)
            return "Forecast";

        for (var i = 2; i < 10_000; i++)
        {
            var name = $"Forecast {i}";
            if (workbook.ValidateSheetName(name) is null)
                return name;
        }

        return $"Forecast {Guid.NewGuid():N}"[..31];
    }
}
