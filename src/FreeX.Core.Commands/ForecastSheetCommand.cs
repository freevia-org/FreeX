using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class ForecastSheetCommand : IWorkbookCommand
{
    private readonly GridRange _sourceRange;
    private readonly uint _forecastPeriods;
    private SheetId? _addedSheetId;
    private readonly List<CellAddress> _affectedFormulaCells = [];

    public string Label => "Forecast Sheet";

    public ForecastSheetCommand(GridRange sourceRange, uint forecastPeriods)
    {
        _sourceRange = sourceRange;
        _forecastPeriods = forecastPeriods;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        _affectedFormulaCells.Clear();

        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;
        if (_sourceRange.ColCount != 2 || _sourceRange.RowCount < 3)
            return new CommandOutcome(false, "Forecast Sheet requires a two-column range with headers and at least two data rows.");
        if (_forecastPeriods == 0)
            return new CommandOutcome(false, "Forecast periods must be greater than zero.");

        var sourceSheet = ctx.Workbook.GetSheet(_sourceRange.Start.Sheet);
        if (sourceSheet is null)
            return new CommandOutcome(false, "Forecast Sheet source range must belong to this workbook.");

        Sheet forecastSheet;
        if (_addedSheetId is { } existingSheetId)
        {
            // R17: redo. Workbook.AddSheet always mints a brand-new SheetId, which would give
            // the re-created forecast sheet a DIFFERENT id than the first Apply produced --
            // breaking any later redo-stack command that captured the original id. Re-create
            // with the SAME id captured below instead, via the "reinsert an existing sheet
            // instance" overload (mirrors AddSheetCommand's R16 redo fix).
            forecastSheet = new Sheet(existingSheetId, GetForecastSheetName(ctx.Workbook));
            ctx.Workbook.InsertSheet(ctx.Workbook.Sheets.Count, forecastSheet);
        }
        else
        {
            forecastSheet = ctx.Workbook.AddSheet(GetForecastSheetName(ctx.Workbook));
            _addedSheetId = forecastSheet.Id;
        }
        forecastSheet.ResetViewStateToA1();

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
        ApplyForecastSheetColumnWidths(forecastSheet);

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
        var lastHistoricalRow = dataRowCount + 1;
        SeedForecastChartJoinPoint(forecastSheet, lastHistoricalRow);

        for (uint offset = 1; offset <= _forecastPeriods; offset++)
        {
            var row = dataRowCount + 1 + offset;
            forecastSheet.SetCell(new CellAddress(forecastSheet.Id, row, 1), new NumberValue(lastTimeline + (step * offset)));
            SetFormula(forecastSheet, row, 3, $"FORECAST.LINEAR(A{row},{knownY},{knownX})");
            var confidence = $"CONFIDENCE.NORM(0.05,STEYX({knownY},{knownX}),COUNT({knownX}))";
            SetFormula(forecastSheet, row, 4, $"C{row}-{confidence}");
            SetFormula(forecastSheet, row, 5, $"C{row}+{confidence}");
        }

        // Insert the accompanying forecast chart (Excel parity). Reverting removes the whole
        // generated sheet, so the chart is torn down with it.
        var lastRow = dataRowCount + 1 + _forecastPeriods;
        forecastSheet.Charts.Add(ForecastChartPlanner.Plan(
            new ForecastChartLayout(forecastSheet.Id, HeaderRow: 1, LastRow: lastRow)));

        return new CommandOutcome(true, AffectedCells: _affectedFormulaCells.ToArray());
    }

    public void Revert(ICommandContext ctx)
    {
        if (_addedSheetId is { } sheetId)
            ctx.Workbook.RemoveSheet(sheetId);
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

    private void SetFormula(Sheet forecastSheet, uint row, uint col, string formulaText)
    {
        var address = new CellAddress(forecastSheet.Id, row, col);
        forecastSheet.SetCell(address, Cell.FromFormula(formulaText));
        _affectedFormulaCells.Add(address);
    }

    private static void SeedForecastChartJoinPoint(Sheet forecastSheet, uint lastHistoricalRow)
    {
        var lastActual = forecastSheet.GetValue(lastHistoricalRow, ForecastChartLayout.ActualColumn);

        forecastSheet.SetCell(new CellAddress(forecastSheet.Id, lastHistoricalRow, ForecastChartLayout.ForecastColumn), lastActual);
        forecastSheet.SetCell(new CellAddress(forecastSheet.Id, lastHistoricalRow, ForecastChartLayout.LowerBoundColumn), lastActual);
        forecastSheet.SetCell(new CellAddress(forecastSheet.Id, lastHistoricalRow, ForecastChartLayout.UpperBoundColumn), lastActual);
    }

    private static void ApplyForecastSheetColumnWidths(Sheet forecastSheet)
    {
        forecastSheet.ColumnWidths[ForecastChartLayout.TimelineColumn] = 10;
        forecastSheet.ColumnWidths[ForecastChartLayout.ActualColumn] = 12;
        forecastSheet.ColumnWidths[ForecastChartLayout.ForecastColumn] = 14;
        forecastSheet.ColumnWidths[ForecastChartLayout.LowerBoundColumn] = 21;
        forecastSheet.ColumnWidths[ForecastChartLayout.UpperBoundColumn] = 21;
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
