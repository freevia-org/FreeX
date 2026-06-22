namespace FreeX.Core.Model;

/// <summary>
/// One page coordinate in a worksheet print grid. PageIndex and SheetPageNumber describe the
/// sheet's configured print order; the containing sequence may be print order or visual grid order.
/// </summary>
public sealed record PrintPageGridIndex(
    int PageIndex,
    int SheetPageNumber,
    int RowPageIndex,
    int ColumnPageIndex);

/// <summary>
/// One worksheet print page resolved to both its grid indexes and row/column page plans.
/// </summary>
public sealed record PrintPageGridEntry(
    int PageIndex,
    int SheetPageNumber,
    int RowPageIndex,
    int ColumnPageIndex,
    PrintPageRowPlan RowPlan,
    PrintPageColumnPlan ColumnPlan);

/// <summary>
/// Shared worksheet page-grid traversal for print, PDF export, page-content layout, and page-break
/// preview. It owns only row/column page ordering; renderers still own page drawing and numbering chrome.
/// </summary>
public static class PrintPageGridPlanner
{
    public static IReadOnlyList<PrintPageGridEntry> Build(
        IReadOnlyList<PrintPageRowPlan> rowPlans,
        IReadOnlyList<PrintPageColumnPlan> columnPlans,
        WorksheetPageOrder pageOrder)
    {
        ArgumentNullException.ThrowIfNull(rowPlans);
        ArgumentNullException.ThrowIfNull(columnPlans);

        var indexes = BuildIndexes(rowPlans.Count, columnPlans.Count, pageOrder);
        var pages = new List<PrintPageGridEntry>(indexes.Count);
        foreach (var index in indexes)
        {
            pages.Add(new PrintPageGridEntry(
                index.PageIndex,
                index.SheetPageNumber,
                index.RowPageIndex,
                index.ColumnPageIndex,
                rowPlans[index.RowPageIndex],
                columnPlans[index.ColumnPageIndex]));
        }

        return pages;
    }

    public static IReadOnlyList<PrintPageGridIndex> BuildIndexes(
        int rowPageCount,
        int columnPageCount,
        WorksheetPageOrder pageOrder)
    {
        if (rowPageCount <= 0 || columnPageCount <= 0)
            return [];

        var pages = new List<PrintPageGridIndex>(rowPageCount * columnPageCount);
        if (pageOrder == WorksheetPageOrder.OverThenDown)
        {
            for (var rowPageIndex = 0; rowPageIndex < rowPageCount; rowPageIndex++)
            {
                for (var columnPageIndex = 0; columnPageIndex < columnPageCount; columnPageIndex++)
                    AddIndex(pages, rowPageIndex, columnPageIndex);
            }
        }
        else
        {
            for (var columnPageIndex = 0; columnPageIndex < columnPageCount; columnPageIndex++)
            {
                for (var rowPageIndex = 0; rowPageIndex < rowPageCount; rowPageIndex++)
                    AddIndex(pages, rowPageIndex, columnPageIndex);
            }
        }

        return pages;
    }

    public static IReadOnlyList<PrintPageGridIndex> BuildVisualIndexes(
        int rowPageCount,
        int columnPageCount,
        WorksheetPageOrder pageOrder)
    {
        if (rowPageCount <= 0 || columnPageCount <= 0)
            return [];

        var pages = new List<PrintPageGridIndex>(rowPageCount * columnPageCount);
        for (var rowPageIndex = 0; rowPageIndex < rowPageCount; rowPageIndex++)
        {
            for (var columnPageIndex = 0; columnPageIndex < columnPageCount; columnPageIndex++)
            {
                var pageIndex = GetPageIndex(rowPageIndex, columnPageIndex, rowPageCount, columnPageCount, pageOrder);
                pages.Add(new PrintPageGridIndex(
                    pageIndex,
                    pageIndex + 1,
                    rowPageIndex,
                    columnPageIndex));
            }
        }

        return pages;
    }

    private static void AddIndex(List<PrintPageGridIndex> pages, int rowPageIndex, int columnPageIndex)
    {
        var pageIndex = pages.Count;
        pages.Add(new PrintPageGridIndex(
            pageIndex,
            pageIndex + 1,
            rowPageIndex,
            columnPageIndex));
    }

    private static int GetPageIndex(
        int rowPageIndex,
        int columnPageIndex,
        int rowPageCount,
        int columnPageCount,
        WorksheetPageOrder pageOrder)
    {
        return pageOrder == WorksheetPageOrder.OverThenDown
            ? (rowPageIndex * columnPageCount) + columnPageIndex
            : (columnPageIndex * rowPageCount) + rowPageIndex;
    }
}
