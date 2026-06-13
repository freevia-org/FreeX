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
        var c1 = new CellAddress(sheet1.Id, 1, 3);
        var d1 = new CellAddress(sheet1.Id, 1, 4);
        var e1 = new CellAddress(sheet1.Id, 1, 5);
        var range = new GridRange(a1, e1);

        sheet1.SetCell(a1, new NumberValue(42));
        sheet1.SetFormula(b1, "SUM(A1:A10)");
        sheet1.SetCell(c1, new TextValue("North"));
        sheet1.SetCell(d1, new BoolValue(true));
        sheet1.SetCell(e1, ErrorValue.Ref);
        sheet1.Comments[a1] = "Check total";
        sheet1.ThreadedComments[b1] = new ThreadedComment("Discuss formula");
        sheet1.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = range
        });
        sheet1.PivotTables.Add(new PivotTableModel
        {
            Name = "Pivot1",
            CacheId = 1,
            SourceRange = range,
            TargetRange = new GridRange(new CellAddress(sheet1.Id, 4, 1), new CellAddress(sheet1.Id, 7, 3))
        });
        sheet1.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(a1, b1)
        });
        sheet1.Sparklines.Add(new SparklineModel
        {
            DataRange = new GridRange(a1, b1),
            Location = new CellAddress(sheet1.Id, 1, 6)
        });
        sheet1.AddMergedRegion(new GridRange(new CellAddress(sheet1.Id, 3, 1), new CellAddress(sheet1.Id, 3, 2)));
        sheet1.ConditionalFormats.Add(new ConditionalFormat { AppliesTo = range, RuleType = CfRuleType.CellValue });
        sheet1.DataValidations.Add(new DataValidation { AppliesTo = range, Type = DvType.List });
        sheet1.Hyperlinks[c1] = "https://example.test/report";
        sheet1.IsProtected = true;
        sheet1.HiddenRows.Add(4);
        sheet1.FilterHiddenRows.Add(5);
        sheet1.GroupHiddenRows.Add(5);
        sheet1.HiddenCols.Add(2);
        sheet1.GroupHiddenCols.Add(3);
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
        sheet2.IsHidden = true;
        workbook.DefineNamedRange("Totals", new GridRange(a1, b1));

        var statistics = WorkbookStatisticsService.GetStatistics(workbook);

        statistics.WorksheetCount.Should().Be(2);
        statistics.UsedWorksheetCount.Should().Be(2);
        statistics.HiddenWorksheetCount.Should().Be(1);
        statistics.ProtectedWorksheetCount.Should().Be(1);
        statistics.CellCount.Should().Be(5);
        statistics.UsedRowCount.Should().Be(1);
        statistics.UsedColumnCount.Should().Be(5);
        statistics.FormulaCount.Should().Be(1);
        statistics.ConstantCount.Should().Be(4);
        statistics.TextConstantCount.Should().Be(1);
        statistics.NumberConstantCount.Should().Be(1);
        statistics.BooleanConstantCount.Should().Be(1);
        statistics.ErrorValueCount.Should().Be(1);
        statistics.CommentCount.Should().Be(2);
        statistics.NoteCount.Should().Be(1);
        statistics.ThreadedCommentCount.Should().Be(1);
        statistics.TableCount.Should().Be(1);
        statistics.PivotTableCount.Should().Be(1);
        statistics.ChartCount.Should().Be(1);
        statistics.PictureCount.Should().Be(1);
        statistics.ShapeCount.Should().Be(2);
        statistics.SparklineCount.Should().Be(1);
        statistics.DrawingShapeCount.Should().Be(1);
        statistics.TextBoxCount.Should().Be(1);
        statistics.NamedRangeCount.Should().Be(1);
        statistics.MergedRangeCount.Should().Be(1);
        statistics.ConditionalFormatCount.Should().Be(1);
        statistics.DataValidationCount.Should().Be(1);
        statistics.HyperlinkCount.Should().Be(1);
        statistics.HiddenRowCount.Should().Be(2);
        statistics.HiddenColumnCount.Should().Be(2);
    }

    [Fact]
    public void GetStatistics_CountsFormulaCachedErrorsButNotAsConstants()
    {
        var workbook = new Workbook("Formula Errors");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new Cell
        {
            FormulaText = "1/0",
            Value = ErrorValue.DivByZero
        });

        var statistics = WorkbookStatisticsService.GetStatistics(workbook);

        statistics.CellCount.Should().Be(1);
        statistics.FormulaCount.Should().Be(1);
        statistics.ConstantCount.Should().Be(0);
        statistics.ErrorValueCount.Should().Be(1);
    }

    [Fact]
    public void GetStatistics_UsesTrackedFormulaCount()
    {
        var source = ModelSourceTestSupport.ReadCommandsSource("WorkbookStatisticsService.cs");
        var getSheetStatistics = source[
            source.IndexOf("private static SheetStatistics GetSheetStatistics", StringComparison.Ordinal)..
            source.IndexOf("private readonly record struct SheetStatistics", StringComparison.Ordinal)];

        getSheetStatistics.Should().Contain("FormulaCount: sheet.FormulaCellCount");
        getSheetStatistics.Should().NotContain("EnumerateCells().Count");
    }

}
