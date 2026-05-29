using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed record WorkbookStatistics(
    int WorksheetCount,
    int CellCount,
    int FormulaCount,
    int CommentCount,
    int ChartCount,
    int PictureCount,
    int ShapeCount,
    int NamedRangeCount);

public static class WorkbookStatisticsService
{
    public static WorkbookStatistics GetStatistics(Workbook workbook)
    {
        var cellCount = 0;
        var formulaCount = 0;
        var commentCount = 0;
        var chartCount = 0;
        var pictureCount = 0;
        var shapeCount = 0;

        foreach (var sheet in workbook.Sheets)
        {
            var sheetStatistics = GetSheetStatistics(sheet);
            cellCount += sheetStatistics.CellCount;
            formulaCount += sheetStatistics.FormulaCount;
            commentCount += sheetStatistics.CommentCount;
            chartCount += sheetStatistics.ChartCount;
            pictureCount += sheetStatistics.PictureCount;
            shapeCount += sheetStatistics.ShapeCount;
        }

        return new WorkbookStatistics(
            WorksheetCount: workbook.Sheets.Count,
            CellCount: cellCount,
            FormulaCount: formulaCount,
            CommentCount: commentCount,
            ChartCount: chartCount,
            PictureCount: pictureCount,
            ShapeCount: shapeCount,
            NamedRangeCount: workbook.NamedRanges.Count);
    }

    private static SheetStatistics GetSheetStatistics(Sheet sheet) =>
        new(
            CellCount: sheet.CellCount,
            FormulaCount: sheet.EnumerateCells().Count(cell => cell.Cell.HasFormula),
            CommentCount: sheet.Comments.Count + sheet.ThreadedComments.Count,
            ChartCount: sheet.Charts.Count,
            PictureCount: sheet.Pictures.Count,
            ShapeCount: sheet.DrawingShapes.Count + sheet.TextBoxes.Count);

    private readonly record struct SheetStatistics(
        int CellCount,
        int FormulaCount,
        int CommentCount,
        int ChartCount,
        int PictureCount,
        int ShapeCount);
}
