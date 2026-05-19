using Freexcel.Core.Commands;

namespace Freexcel.App.Host;

public static class WorkbookStatisticsFormatter
{
    public static string Format(WorkbookStatistics statistics) =>
        string.Join(Environment.NewLine,
            $"Sheets: {statistics.WorksheetCount}",
            $"Cells with data: {statistics.CellCount}",
            $"Formulas: {statistics.FormulaCount}",
            $"Comments: {statistics.CommentCount}",
            $"Charts: {statistics.ChartCount}",
            $"Pictures: {statistics.PictureCount}",
            $"Shapes and text boxes: {statistics.ShapeCount}",
            $"Named ranges: {statistics.NamedRangeCount}");
}
