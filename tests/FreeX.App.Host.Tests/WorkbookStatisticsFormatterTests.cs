using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Commands;

namespace FreeX.App.Host.Tests;

public sealed class WorkbookStatisticsFormatterTests
{
    [Fact]
    public void Format_UsesExcelStyleWorkbookStatisticsLabels()
    {
        var statistics = new WorkbookStatistics(
            WorksheetCount: 3,
            CellCount: 42,
            FormulaCount: 5,
            CommentCount: 2,
            ChartCount: 1,
            PictureCount: 4,
            ShapeCount: 6,
            NamedRangeCount: 7,
            UsedWorksheetCount: 2,
            HiddenWorksheetCount: 1,
            ProtectedWorksheetCount: 1,
            UsedRowCount: 9,
            UsedColumnCount: 8,
            ConstantCount: 37,
            TextConstantCount: 12,
            NumberConstantCount: 20,
            BooleanConstantCount: 3,
            ErrorValueCount: 2,
            NoteCount: 1,
            ThreadedCommentCount: 1,
            TableCount: 2,
            PivotTableCount: 3,
            SparklineCount: 5,
            DrawingShapeCount: 4,
            TextBoxCount: 2,
            MergedRangeCount: 6,
            ConditionalFormatCount: 8,
            DataValidationCount: 9,
            HyperlinkCount: 10,
            HiddenRowCount: 11,
            HiddenColumnCount: 12);

        WorkbookStatisticsFormatter.Format(statistics)
            .Should()
            .Be(string.Join(Environment.NewLine,
                "Sheets: 3",
                "Used sheets: 2",
                "Hidden sheets: 1",
                "Protected sheets: 1",
                "Cells with data: 42",
                "Rows with data: 9",
                "Columns with data: 8",
                "Formulas: 5",
                "Constants: 37",
                "Text constants: 12",
                "Number constants: 20",
                "Boolean constants: 3",
                "Error values: 2",
                "Comments: 2",
                "Notes: 1",
                "Threaded comments: 1",
                "Tables: 2",
                "PivotTables: 3",
                "Charts: 1",
                "Pictures: 4",
                "Shapes and text boxes: 6",
                "Drawing shapes: 4",
                "Text boxes: 2",
                "Named ranges: 7",
                "Merged ranges: 6",
                "Conditional format rules: 8",
                "Data validation rules: 9",
                "Hyperlinks: 10",
                "Hidden rows: 11",
                "Hidden columns: 12",
                "Sparklines: 5"));
    }
}
