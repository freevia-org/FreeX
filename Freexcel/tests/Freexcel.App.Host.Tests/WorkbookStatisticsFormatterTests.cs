using FluentAssertions;
using Freexcel.Core.Commands;

namespace Freexcel.App.Host.Tests;

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
            NamedRangeCount: 7);

        WorkbookStatisticsFormatter.Format(statistics)
            .Should()
            .Be(string.Join(Environment.NewLine,
                "Sheets: 3",
                "Cells with data: 42",
                "Formulas: 5",
                "Comments: 2",
                "Charts: 1",
                "Pictures: 4",
                "Shapes and text boxes: 6",
                "Named ranges: 7"));
    }
}
