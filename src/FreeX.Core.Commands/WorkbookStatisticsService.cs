using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed record WorkbookStatistics(
    int WorksheetCount,
    int CellCount,
    int FormulaCount,
    int CommentCount,
    int ChartCount,
    int PictureCount,
    int ShapeCount,
    int NamedRangeCount,
    int UsedWorksheetCount = 0,
    int HiddenWorksheetCount = 0,
    int ProtectedWorksheetCount = 0,
    int UsedRowCount = 0,
    int UsedColumnCount = 0,
    int ConstantCount = 0,
    int TextConstantCount = 0,
    int NumberConstantCount = 0,
    int BooleanConstantCount = 0,
    int ErrorValueCount = 0,
    int NoteCount = 0,
    int ThreadedCommentCount = 0,
    int TableCount = 0,
    int PivotTableCount = 0,
    int SparklineCount = 0,
    int DrawingShapeCount = 0,
    int TextBoxCount = 0,
    int MergedRangeCount = 0,
    int ConditionalFormatCount = 0,
    int DataValidationCount = 0,
    int HyperlinkCount = 0,
    int HiddenRowCount = 0,
    int HiddenColumnCount = 0);

public static class WorkbookStatisticsService
{
    public static WorkbookStatistics GetStatistics(Workbook workbook)
    {
        var usedWorksheetCount = 0;
        var hiddenWorksheetCount = 0;
        var protectedWorksheetCount = 0;
        var cellCount = 0;
        var formulaCount = 0;
        var usedRowCount = 0;
        var usedColumnCount = 0;
        var constantCount = 0;
        var textConstantCount = 0;
        var numberConstantCount = 0;
        var booleanConstantCount = 0;
        var errorValueCount = 0;
        var commentCount = 0;
        var noteCount = 0;
        var threadedCommentCount = 0;
        var tableCount = 0;
        var pivotTableCount = 0;
        var chartCount = 0;
        var pictureCount = 0;
        var shapeCount = 0;
        var sparklineCount = 0;
        var drawingShapeCount = 0;
        var textBoxCount = 0;
        var mergedRangeCount = 0;
        var conditionalFormatCount = 0;
        var dataValidationCount = 0;
        var hyperlinkCount = 0;
        var hiddenRowCount = 0;
        var hiddenColumnCount = 0;

        foreach (var sheet in workbook.Sheets)
        {
            var sheetStatistics = GetSheetStatistics(sheet);
            if (sheetStatistics.IsUsed)
                usedWorksheetCount++;
            if (sheetStatistics.IsHidden)
                hiddenWorksheetCount++;
            if (sheetStatistics.IsProtected)
                protectedWorksheetCount++;
            cellCount += sheetStatistics.CellCount;
            formulaCount += sheetStatistics.FormulaCount;
            usedRowCount += sheetStatistics.UsedRowCount;
            usedColumnCount += sheetStatistics.UsedColumnCount;
            constantCount += sheetStatistics.ConstantCount;
            textConstantCount += sheetStatistics.TextConstantCount;
            numberConstantCount += sheetStatistics.NumberConstantCount;
            booleanConstantCount += sheetStatistics.BooleanConstantCount;
            errorValueCount += sheetStatistics.ErrorValueCount;
            commentCount += sheetStatistics.CommentCount;
            noteCount += sheetStatistics.NoteCount;
            threadedCommentCount += sheetStatistics.ThreadedCommentCount;
            tableCount += sheetStatistics.TableCount;
            pivotTableCount += sheetStatistics.PivotTableCount;
            chartCount += sheetStatistics.ChartCount;
            pictureCount += sheetStatistics.PictureCount;
            shapeCount += sheetStatistics.ShapeCount;
            sparklineCount += sheetStatistics.SparklineCount;
            drawingShapeCount += sheetStatistics.DrawingShapeCount;
            textBoxCount += sheetStatistics.TextBoxCount;
            mergedRangeCount += sheetStatistics.MergedRangeCount;
            conditionalFormatCount += sheetStatistics.ConditionalFormatCount;
            dataValidationCount += sheetStatistics.DataValidationCount;
            hyperlinkCount += sheetStatistics.HyperlinkCount;
            hiddenRowCount += sheetStatistics.HiddenRowCount;
            hiddenColumnCount += sheetStatistics.HiddenColumnCount;
        }

        return new WorkbookStatistics(
            WorksheetCount: workbook.Sheets.Count,
            CellCount: cellCount,
            FormulaCount: formulaCount,
            CommentCount: commentCount,
            ChartCount: chartCount,
            PictureCount: pictureCount,
            ShapeCount: shapeCount,
            NamedRangeCount: workbook.NamedRanges.Count,
            UsedWorksheetCount: usedWorksheetCount,
            HiddenWorksheetCount: hiddenWorksheetCount,
            ProtectedWorksheetCount: protectedWorksheetCount,
            UsedRowCount: usedRowCount,
            UsedColumnCount: usedColumnCount,
            ConstantCount: constantCount,
            TextConstantCount: textConstantCount,
            NumberConstantCount: numberConstantCount,
            BooleanConstantCount: booleanConstantCount,
            ErrorValueCount: errorValueCount,
            NoteCount: noteCount,
            ThreadedCommentCount: threadedCommentCount,
            TableCount: tableCount,
            PivotTableCount: pivotTableCount,
            SparklineCount: sparklineCount,
            DrawingShapeCount: drawingShapeCount,
            TextBoxCount: textBoxCount,
            MergedRangeCount: mergedRangeCount,
            ConditionalFormatCount: conditionalFormatCount,
            DataValidationCount: dataValidationCount,
            HyperlinkCount: hyperlinkCount,
            HiddenRowCount: hiddenRowCount,
            HiddenColumnCount: hiddenColumnCount);
    }

    private static SheetStatistics GetSheetStatistics(Sheet sheet)
    {
        var usedRows = new HashSet<uint>();
        var usedColumns = new HashSet<uint>();
        var constantCount = 0;
        var textConstantCount = 0;
        var numberConstantCount = 0;
        var booleanConstantCount = 0;
        var errorValueCount = 0;

        foreach (var (address, cell) in sheet.EnumerateCells())
        {
            usedRows.Add(address.Row);
            usedColumns.Add(address.Col);

            if (cell.Value is ErrorValue)
                errorValueCount++;

            if (cell.HasFormula || cell.Value is BlankValue)
                continue;

            constantCount++;
            switch (cell.Value)
            {
                case TextValue:
                    textConstantCount++;
                    break;
                case NumberValue:
                case DateTimeValue:
                    numberConstantCount++;
                    break;
                case BoolValue:
                    booleanConstantCount++;
                    break;
            }
        }

        var noteCount = sheet.Comments.Count;
        var threadedCommentCount = sheet.ThreadedComments.Count;
        var drawingShapeCount = sheet.DrawingShapes.Count;
        var textBoxCount = sheet.TextBoxes.Count;
        var statistics = new SheetStatistics(
            IsUsed: false,
            IsHidden: sheet.IsHidden || sheet.IsVeryHidden,
            IsProtected: sheet.IsProtected,
            CellCount: sheet.CellCount,
            FormulaCount: sheet.FormulaCellCount,
            UsedRowCount: usedRows.Count,
            UsedColumnCount: usedColumns.Count,
            ConstantCount: constantCount,
            TextConstantCount: textConstantCount,
            NumberConstantCount: numberConstantCount,
            BooleanConstantCount: booleanConstantCount,
            ErrorValueCount: errorValueCount,
            CommentCount: noteCount + threadedCommentCount,
            NoteCount: noteCount,
            ThreadedCommentCount: threadedCommentCount,
            TableCount: sheet.StructuredTables.Count,
            PivotTableCount: sheet.PivotTables.Count,
            ChartCount: sheet.Charts.Count,
            PictureCount: sheet.Pictures.Count,
            ShapeCount: drawingShapeCount + textBoxCount,
            SparklineCount: sheet.Sparklines.Count,
            DrawingShapeCount: drawingShapeCount,
            TextBoxCount: textBoxCount,
            MergedRangeCount: sheet.MergedRegions.Count,
            ConditionalFormatCount: sheet.ConditionalFormats.Count,
            DataValidationCount: sheet.DataValidations.Count,
            HyperlinkCount: sheet.Hyperlinks.Count,
            HiddenRowCount: CountDistinct(sheet.HiddenRows, sheet.FilterHiddenRows, sheet.GroupHiddenRows),
            HiddenColumnCount: CountDistinct(sheet.HiddenCols, sheet.GroupHiddenCols));

        return statistics with { IsUsed = statistics.HasWorksheetContent };
    }

    private static int CountDistinct(params IEnumerable<uint>[] values)
    {
        var distinct = new HashSet<uint>();
        foreach (var value in values)
            distinct.UnionWith(value);

        return distinct.Count;
    }

    private readonly record struct SheetStatistics(
        bool IsUsed,
        bool IsHidden,
        bool IsProtected,
        int CellCount,
        int FormulaCount,
        int UsedRowCount,
        int UsedColumnCount,
        int ConstantCount,
        int TextConstantCount,
        int NumberConstantCount,
        int BooleanConstantCount,
        int ErrorValueCount,
        int CommentCount,
        int NoteCount,
        int ThreadedCommentCount,
        int TableCount,
        int PivotTableCount,
        int ChartCount,
        int PictureCount,
        int ShapeCount,
        int SparklineCount,
        int DrawingShapeCount,
        int TextBoxCount,
        int MergedRangeCount,
        int ConditionalFormatCount,
        int DataValidationCount,
        int HyperlinkCount,
        int HiddenRowCount,
        int HiddenColumnCount)
    {
        public bool HasWorksheetContent =>
            CellCount > 0 ||
            CommentCount > 0 ||
            TableCount > 0 ||
            PivotTableCount > 0 ||
            ChartCount > 0 ||
            PictureCount > 0 ||
            ShapeCount > 0 ||
            SparklineCount > 0 ||
            MergedRangeCount > 0 ||
            ConditionalFormatCount > 0 ||
            DataValidationCount > 0 ||
            HyperlinkCount > 0;
    }
}
