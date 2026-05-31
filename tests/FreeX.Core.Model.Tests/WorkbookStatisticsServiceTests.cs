using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public sealed class WorkbookStatisticsServiceTests
{
    [Fact]
    public void GetStatistics_CountsWorkbookSheetsCellsAndObjects()
    {
        var workbook = new Workbook("Budget");
        var sheet1 = workbook.AddSheet("Summary");
        var sheet2 = workbook.AddSheet("Data");
        var a1 = new CellAddress(sheet1.Id, 1, 1);
        var b1 = new CellAddress(sheet1.Id, 1, 2);

        sheet1.SetCell(a1, new NumberValue(42));
        sheet1.SetFormula(b1, "SUM(A1:A10)");
        sheet1.Comments[a1] = "Check total";
        sheet1.ThreadedComments[b1] = new ThreadedComment("Discuss formula");
        sheet1.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(a1, b1)
        });
        sheet1.Pictures.Add(new PictureModel
        {
            Id = Guid.NewGuid(),
            Kind = PictureKind.Image,
            Anchor = new CellAddress(sheet1.Id, 8, 1),
            Width = 120,
            Height = 80
        });
        sheet1.TextBoxes.Add(new TextBoxModel
        {
            Id = Guid.NewGuid(),
            Anchor = new CellAddress(sheet1.Id, 10, 1),
            Text = "Note"
        });
        sheet2.DrawingShapes.Add(new DrawingShapeModel
        {
            Id = Guid.NewGuid(),
            Kind = DrawingShapeKind.Rectangle,
            Anchor = new CellAddress(sheet2.Id, 2, 2),
            Width = 100,
            Height = 40
        });
        workbook.DefineNamedRange("Totals", new GridRange(a1, b1));

        var statistics = WorkbookStatisticsService.GetStatistics(workbook);

        statistics.WorksheetCount.Should().Be(2);
        statistics.CellCount.Should().Be(2);
        statistics.FormulaCount.Should().Be(1);
        statistics.CommentCount.Should().Be(2);
        statistics.ChartCount.Should().Be(1);
        statistics.PictureCount.Should().Be(1);
        statistics.ShapeCount.Should().Be(2);
        statistics.NamedRangeCount.Should().Be(1);
    }

    [Fact]
    public void GetStatistics_UsesTrackedFormulaCount()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.Commands", "WorkbookStatisticsService.cs"));
        var getSheetStatistics = source[
            source.IndexOf("private static SheetStatistics GetSheetStatistics", StringComparison.Ordinal)..
            source.IndexOf("private readonly record struct SheetStatistics", StringComparison.Ordinal)];

        getSheetStatistics.Should().Contain("FormulaCount: sheet.FormulaCellCount");
        getSheetStatistics.Should().NotContain("EnumerateCells().Count");
    }

    private static string FindWorkspaceFile(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine([dir, .. parts]);
            if (File.Exists(candidate))
                return candidate;

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new FileNotFoundException($"Could not find workspace file: {Path.Combine(parts)}");
    }
}
