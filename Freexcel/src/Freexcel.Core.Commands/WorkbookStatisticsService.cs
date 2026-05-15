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
            cellCount += sheet.CellCount;
            formulaCount += sheet.EnumerateCells().Count(cell => cell.Cell.HasFormula);
            commentCount += sheet.Comments.Count;
            chartCount += sheet.Charts.Count;
            pictureCount += sheet.Pictures.Count;
            shapeCount += sheet.DrawingShapes.Count + sheet.TextBoxes.Count;
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
}
