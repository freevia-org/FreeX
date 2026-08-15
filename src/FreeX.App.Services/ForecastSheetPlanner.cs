using System.Globalization;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public enum ForecastSheetWorkflowState
{
    Ready,
    Deferred
}

public enum ForecastSheetPlanStatus
{
    Ready,
    NoWorkbook,
    NoSelection,
    WorkbookStructureProtected,
    SourceRangeOutsideWorkbook,
    SourceRangeRequiresTwoColumns,
    SourceRangeRequiresHeaderAndTwoDataRows,
    InvalidForecastPeriods
}

public sealed record ForecastSheetInputExpectation(
    GridRange SourceRange,
    GridRange TimelineRange,
    GridRange ValueRange,
    CellAddress TimelineHeaderCell,
    CellAddress ValueHeaderCell,
    GridRange TimelineDataRange,
    GridRange ValueDataRange,
    uint HistoricalDataRowCount);

public sealed record ForecastSheetPlan(
    ForecastSheetWorkflowState WorkflowState,
    ForecastSheetPlanStatus Status,
    string StatusText,
    GridRange? SourceRange,
    ForecastSheetInputExpectation? InputExpectation,
    uint ForecastPeriods,
    string InvalidText = "")
{
    public bool IsReady => WorkflowState == ForecastSheetWorkflowState.Ready &&
                           Status == ForecastSheetPlanStatus.Ready;

    public bool IsDeferred => WorkflowState == ForecastSheetWorkflowState.Deferred;

    public ForecastSheetCommand? TryCreateCommand() =>
        IsReady && SourceRange is { } sourceRange
            ? new ForecastSheetCommand(sourceRange, ForecastPeriods)
            : null;
}

public static class ForecastSheetPlanner
{
    public const uint DefaultForecastPeriods = 3;
    public const uint RequiredColumnCount = 2;
    public const uint MinimumHistoricalDataRows = 2;
    public const uint MinimumSourceRowCount = MinimumHistoricalDataRows + 1;

    public static ForecastSheetPlan CreatePlan(
        Workbook? workbook,
        GridRange? selectedRange,
        uint forecastPeriods = DefaultForecastPeriods) =>
        CreatePlanCore(
            workbook,
            selectedRange,
            forecastPeriods,
            hasInvalidForecastPeriods: forecastPeriods == 0,
            invalidText: forecastPeriods == 0 ? "0" : "");

    public static ForecastSheetPlan CreatePlan(
        Workbook? workbook,
        GridRange? selectedRange,
        string? forecastPeriodsText)
    {
        var periodInput = NormalizeInput(forecastPeriodsText);
        var hasInvalidForecastPeriods = !TryParseForecastPeriods(periodInput, out var forecastPeriods);
        return CreatePlanCore(
            workbook,
            selectedRange,
            forecastPeriods,
            hasInvalidForecastPeriods,
            hasInvalidForecastPeriods ? periodInput : "");
    }

    public static bool TryParseForecastPeriods(string? input, out uint forecastPeriods)
    {
        var normalized = NormalizeInput(input);
        if (uint.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
            parsed > 0)
        {
            forecastPeriods = parsed;
            return true;
        }

        forecastPeriods = 0;
        return false;
    }

    private static ForecastSheetPlan CreatePlanCore(
        Workbook? workbook,
        GridRange? selectedRange,
        uint forecastPeriods,
        bool hasInvalidForecastPeriods,
        string invalidText)
    {
        if (workbook is null)
            return Deferred(
                ForecastSheetPlanStatus.NoWorkbook,
                "Open a workbook before creating a Forecast Sheet.",
                null,
                null,
                forecastPeriods,
                invalidText);

        if (selectedRange is not { } sourceRange)
            return Deferred(
                ForecastSheetPlanStatus.NoSelection,
                "Select a two-column timeline and value range before creating a Forecast Sheet.",
                null,
                null,
                forecastPeriods,
                invalidText);

        if (workbook.IsStructureProtected)
            return Deferred(
                ForecastSheetPlanStatus.WorkbookStructureProtected,
                "Forecast Sheet is deferred because the workbook structure is protected.",
                sourceRange,
                null,
                forecastPeriods,
                invalidText);

        if (workbook.GetSheet(sourceRange.Start.Sheet) is not { } sourceSheet)
            return Deferred(
                ForecastSheetPlanStatus.SourceRangeOutsideWorkbook,
                "Forecast Sheet source range must belong to this workbook.",
                sourceRange,
                null,
                forecastPeriods,
                invalidText);

        sourceRange = ForecastSheetSourceRangePlanner.Create(sourceSheet, sourceRange);

        if (sourceRange.ColCount != RequiredColumnCount)
            return Deferred(
                ForecastSheetPlanStatus.SourceRangeRequiresTwoColumns,
                "Forecast Sheet requires exactly two columns: timeline followed by values.",
                sourceRange,
                null,
                forecastPeriods,
                invalidText);

        if (sourceRange.RowCount < MinimumSourceRowCount)
            return Deferred(
                ForecastSheetPlanStatus.SourceRangeRequiresHeaderAndTwoDataRows,
                "Forecast Sheet requires a header row and at least two data rows.",
                sourceRange,
                null,
                forecastPeriods,
                invalidText);

        var inputExpectation = CreateInputExpectation(sourceRange);
        if (hasInvalidForecastPeriods)
            return Deferred(
                ForecastSheetPlanStatus.InvalidForecastPeriods,
                "Forecast periods must be a positive whole number.",
                sourceRange,
                inputExpectation,
                forecastPeriods,
                invalidText);

        return new ForecastSheetPlan(
            ForecastSheetWorkflowState.Ready,
            ForecastSheetPlanStatus.Ready,
            $"Ready to create Forecast Sheet from {sourceRange} with {inputExpectation.HistoricalDataRowCount} {Pluralize(inputExpectation.HistoricalDataRowCount, "historical data row")} and {forecastPeriods} {Pluralize(forecastPeriods, "forecast period")}.",
            sourceRange,
            inputExpectation,
            forecastPeriods);
    }

    private static ForecastSheetInputExpectation CreateInputExpectation(GridRange sourceRange)
    {
        var timelineHeaderCell = sourceRange.Start;
        var valueHeaderCell = new CellAddress(
            sourceRange.Start.Sheet,
            sourceRange.Start.Row,
            sourceRange.Start.Col + 1);
        var timelineDataStart = new CellAddress(
            sourceRange.Start.Sheet,
            sourceRange.Start.Row + 1,
            sourceRange.Start.Col);
        var valueDataStart = new CellAddress(
            sourceRange.Start.Sheet,
            sourceRange.Start.Row + 1,
            sourceRange.Start.Col + 1);
        var timelineEnd = new CellAddress(
            sourceRange.Start.Sheet,
            sourceRange.End.Row,
            sourceRange.Start.Col);
        var valueEnd = sourceRange.End;

        return new ForecastSheetInputExpectation(
            sourceRange,
            new GridRange(timelineHeaderCell, timelineEnd),
            new GridRange(valueHeaderCell, valueEnd),
            timelineHeaderCell,
            valueHeaderCell,
            new GridRange(timelineDataStart, timelineEnd),
            new GridRange(valueDataStart, valueEnd),
            sourceRange.RowCount - 1);
    }

    private static ForecastSheetPlan Deferred(
        ForecastSheetPlanStatus status,
        string statusText,
        GridRange? sourceRange,
        ForecastSheetInputExpectation? inputExpectation,
        uint forecastPeriods,
        string invalidText) =>
        new(
            ForecastSheetWorkflowState.Deferred,
            status,
            statusText,
            sourceRange,
            inputExpectation,
            forecastPeriods,
            invalidText);

    private static string NormalizeInput(string? input) => input?.Trim() ?? "";

    private static string Pluralize(uint count, string singular) =>
        count == 1 ? singular : $"{singular}s";
}
