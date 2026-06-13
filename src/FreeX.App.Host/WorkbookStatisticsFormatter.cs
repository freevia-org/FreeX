using FreeX.Core.Commands;

namespace FreeX.App.Host;

public static class WorkbookStatisticsFormatter
{
    public static string Format(WorkbookStatistics statistics) =>
        string.Join(Environment.NewLine,
            $"Sheets: {statistics.WorksheetCount}",
            $"Used sheets: {statistics.UsedWorksheetCount}",
            $"Hidden sheets: {statistics.HiddenWorksheetCount}",
            $"Protected sheets: {statistics.ProtectedWorksheetCount}",
            $"Cells with data: {statistics.CellCount}",
            $"Rows with data: {statistics.UsedRowCount}",
            $"Columns with data: {statistics.UsedColumnCount}",
            $"Formulas: {statistics.FormulaCount}",
            $"Constants: {statistics.ConstantCount}",
            $"Text constants: {statistics.TextConstantCount}",
            $"Number constants: {statistics.NumberConstantCount}",
            $"Boolean constants: {statistics.BooleanConstantCount}",
            $"Error values: {statistics.ErrorValueCount}",
            $"Comments: {statistics.CommentCount}",
            $"Notes: {statistics.NoteCount}",
            $"Threaded comments: {statistics.ThreadedCommentCount}",
            $"Tables: {statistics.TableCount}",
            $"PivotTables: {statistics.PivotTableCount}",
            $"Charts: {statistics.ChartCount}",
            $"Pictures: {statistics.PictureCount}",
            $"Shapes and text boxes: {statistics.ShapeCount}",
            $"Drawing shapes: {statistics.DrawingShapeCount}",
            $"Text boxes: {statistics.TextBoxCount}",
            $"Named ranges: {statistics.NamedRangeCount}",
            $"Merged ranges: {statistics.MergedRangeCount}",
            $"Conditional format rules: {statistics.ConditionalFormatCount}",
            $"Data validation rules: {statistics.DataValidationCount}",
            $"Hyperlinks: {statistics.HyperlinkCount}",
            $"Hidden rows: {statistics.HiddenRowCount}",
            $"Hidden columns: {statistics.HiddenColumnCount}",
            $"Sparklines: {statistics.SparklineCount}");
}
