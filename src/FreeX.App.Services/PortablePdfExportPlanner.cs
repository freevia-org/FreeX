using FreeX.Core.Model;

namespace FreeX.App.Services;

public enum PortablePdfExportPlanStatus
{
    Ready,
    ExportPrintPlanNotReady,
    OutputKindUnavailable,
    InvalidPageGrid
}

public sealed record PortablePdfExportPageSpans(
    IReadOnlyList<uint> TitleRows,
    IReadOnlyList<uint> BodyRows,
    IReadOnlyList<uint> TitleColumns,
    IReadOnlyList<uint> BodyColumns);

public sealed record PortablePdfExportPageRequest(
    int ExportPageNumber,
    int SheetIndex,
    string SheetName,
    int SheetPageNumber,
    GridRange PrintRange,
    WorkbookExportPrintRangeSource RangeSource,
    int RowPageIndex,
    int ColumnPageIndex,
    int RowPageCount,
    int ColumnPageCount,
    PortablePdfExportPageSpans PageSpans,
    WorksheetPageOrder PageOrder)
{
    public int RowPageNumber => RowPageIndex + 1;

    public int ColumnPageNumber => ColumnPageIndex + 1;

    public IReadOnlyList<uint> TitleRows => PageSpans.TitleRows;

    public IReadOnlyList<uint> BodyRows => PageSpans.BodyRows;

    public IReadOnlyList<uint> TitleColumns => PageSpans.TitleColumns;

    public IReadOnlyList<uint> BodyColumns => PageSpans.BodyColumns;
}

public sealed record PortablePdfExportPlan(
    PortablePdfExportPlanStatus Status,
    string StatusText,
    WorkbookExportPrintPlan ExportPrintPlan,
    IReadOnlyList<PortablePdfExportPageRequest> PageRequests)
{
    public bool IsReady => Status == PortablePdfExportPlanStatus.Ready;

    public int TotalPageCount => PageRequests.Count;
}

public static class PortablePdfExportPlanner
{
    public static PortablePdfExportPlan CreatePlan(WorkbookExportPrintPlan exportPrintPlan)
    {
        ArgumentNullException.ThrowIfNull(exportPrintPlan);

        if (!exportPrintPlan.IsReady)
        {
            return new PortablePdfExportPlan(
                PortablePdfExportPlanStatus.ExportPrintPlanNotReady,
                $"Portable PDF export cannot start because the export print plan is not ready: {exportPrintPlan.StatusText}",
                exportPrintPlan,
                []);
        }

        if (exportPrintPlan.Intent.OutputKind != WorkbookExportPrintOutputKind.Pdf)
        {
            return new PortablePdfExportPlan(
                PortablePdfExportPlanStatus.OutputKindUnavailable,
                "Portable PDF export only accepts PDF export print plans; XPS remains Windows-only.",
                exportPrintPlan,
                []);
        }

        if (!HasValidPageGrid(exportPrintPlan))
        {
            return new PortablePdfExportPlan(
                PortablePdfExportPlanStatus.InvalidPageGrid,
                "Portable PDF export requires positive row and column page counts for every planned sheet.",
                exportPrintPlan,
                []);
        }

        var pageRequests = BuildPageRequests(exportPrintPlan.SheetPlans);
        return new PortablePdfExportPlan(
            PortablePdfExportPlanStatus.Ready,
            $"Ready to export portable PDF: {pageRequests.Count} {Pluralize(pageRequests.Count, "page")} across {exportPrintPlan.SheetPlans.Count} {Pluralize(exportPrintPlan.SheetPlans.Count, "sheet")}.",
            exportPrintPlan,
            pageRequests);
    }

    private static bool HasValidPageGrid(WorkbookExportPrintPlan exportPrintPlan)
    {
        if (exportPrintPlan.SheetPlans.Count == 0)
            return false;

        foreach (var sheetPlan in exportPrintPlan.SheetPlans)
        {
            if (sheetPlan.RowPageCount <= 0 ||
                sheetPlan.ColumnPageCount <= 0 ||
                sheetPlan.RowPagePlans.Count != sheetPlan.RowPageCount ||
                sheetPlan.ColumnPagePlans.Count != sheetPlan.ColumnPageCount ||
                sheetPlan.PageCount != (long)sheetPlan.RowPageCount * sheetPlan.ColumnPageCount)
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<PortablePdfExportPageRequest> BuildPageRequests(
        IReadOnlyList<WorkbookSheetExportPrintPlanSummary> sheetPlans)
    {
        var pageRequests = new List<PortablePdfExportPageRequest>();
        for (var sheetIndex = 0; sheetIndex < sheetPlans.Count; sheetIndex++)
            AddSheetPageRequests(sheetPlans[sheetIndex], sheetIndex, pageRequests);

        return pageRequests;
    }

    private static void AddSheetPageRequests(
        WorkbookSheetExportPrintPlanSummary sheetPlan,
        int sheetIndex,
        List<PortablePdfExportPageRequest> pageRequests)
    {
        var sheetPageNumber = 1;
        if (sheetPlan.PageOrder == WorksheetPageOrder.OverThenDown)
        {
            for (var rowPageIndex = 0; rowPageIndex < sheetPlan.RowPageCount; rowPageIndex++)
            {
                for (var columnPageIndex = 0; columnPageIndex < sheetPlan.ColumnPageCount; columnPageIndex++)
                    AddPageRequest(sheetPlan, sheetIndex, rowPageIndex, columnPageIndex, sheetPageNumber++, pageRequests);
            }

            return;
        }

        for (var columnPageIndex = 0; columnPageIndex < sheetPlan.ColumnPageCount; columnPageIndex++)
        {
            for (var rowPageIndex = 0; rowPageIndex < sheetPlan.RowPageCount; rowPageIndex++)
                AddPageRequest(sheetPlan, sheetIndex, rowPageIndex, columnPageIndex, sheetPageNumber++, pageRequests);
        }
    }

    private static void AddPageRequest(
        WorkbookSheetExportPrintPlanSummary sheetPlan,
        int sheetIndex,
        int rowPageIndex,
        int columnPageIndex,
        int sheetPageNumber,
        List<PortablePdfExportPageRequest> pageRequests)
    {
        var rowPlan = sheetPlan.RowPagePlans[rowPageIndex];
        var columnPlan = sheetPlan.ColumnPagePlans[columnPageIndex];
        pageRequests.Add(new PortablePdfExportPageRequest(
            pageRequests.Count + 1,
            sheetIndex,
            sheetPlan.SheetName,
            sheetPageNumber,
            sheetPlan.PrintRange,
            sheetPlan.RangeSource,
            rowPageIndex,
            columnPageIndex,
            sheetPlan.RowPageCount,
            sheetPlan.ColumnPageCount,
            new PortablePdfExportPageSpans(
                rowPlan.TitleRows.ToArray(),
                rowPlan.BodyRows.ToArray(),
                columnPlan.TitleColumns.ToArray(),
                columnPlan.BodyColumns.ToArray()),
            sheetPlan.PageOrder));
    }

    private static string Pluralize(int count, string singular) =>
        count == 1 ? singular : $"{singular}s";
}
