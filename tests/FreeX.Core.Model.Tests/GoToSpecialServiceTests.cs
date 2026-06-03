using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using System.Diagnostics;

namespace FreeX.Core.Model.Tests;

public sealed class GoToSpecialServiceTests
{
    [Fact]
    public void FindConstants_ReturnsNonFormulaNonBlankCellsInRange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new TextValue("constant"));
        sheet.SetCell(b1, Cell.FromFormula("1+1"));

        var result = GoToSpecialService.Find(sheet, range, GoToSpecialKind.Constants);

        result.Should().Equal(a1);
    }

    [Fact]
    public void FindConstants_HonorsExcelValueTypeSuboptions()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 4));
        var number = new CellAddress(sheet.Id, 1, 1);
        var text = new CellAddress(sheet.Id, 1, 2);
        var logical = new CellAddress(sheet.Id, 1, 3);
        var error = new CellAddress(sheet.Id, 1, 4);
        sheet.SetCell(number, new NumberValue(12));
        sheet.SetCell(text, new TextValue("east"));
        sheet.SetCell(logical, new BoolValue(true));
        sheet.SetCell(error, ErrorValue.NA);

        var result = GoToSpecialService.Find(
            sheet,
            range,
            GoToSpecialKind.Constants,
            options: new GoToSpecialOptions(GoToSpecialValueTypes.Numbers | GoToSpecialValueTypes.Errors));

        result.Should().Equal(number, error);
    }

    [Fact]
    public void FindFormulas_HonorsExcelValueTypeSuboptions()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 4));
        var numberFormula = new CellAddress(sheet.Id, 1, 1);
        var textFormula = new CellAddress(sheet.Id, 1, 2);
        var logicalFormula = new CellAddress(sheet.Id, 1, 3);
        var errorFormula = new CellAddress(sheet.Id, 1, 4);
        sheet.SetCell(numberFormula, Cell.FromFormula("1+1"));
        sheet.GetCell(numberFormula)!.Value = new NumberValue(2);
        sheet.SetCell(textFormula, Cell.FromFormula("\"east\""));
        sheet.GetCell(textFormula)!.Value = new TextValue("east");
        sheet.SetCell(logicalFormula, Cell.FromFormula("TRUE"));
        sheet.GetCell(logicalFormula)!.Value = new BoolValue(true);
        sheet.SetCell(errorFormula, Cell.FromFormula("NA()"));
        sheet.GetCell(errorFormula)!.Value = ErrorValue.NA;

        var result = GoToSpecialService.Find(
            sheet,
            range,
            GoToSpecialKind.Formulas,
            options: new GoToSpecialOptions(GoToSpecialValueTypes.Text | GoToSpecialValueTypes.Logicals));

        result.Should().Equal(textFormula, logicalFormula);
    }

    [Fact]
    public void FindBlanks_ReturnsBlankAddressesInRange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        var result = GoToSpecialService.Find(sheet, range, GoToSpecialKind.Blanks);

        result.Should().Equal(new CellAddress(sheet.Id, 1, 2));
    }

    [Fact]
    public void FindCommentsAndValidations_ReturnsMatchingCells()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var commentCell = new CellAddress(sheet.Id, 2, 1);
        var threadedCommentCell = new CellAddress(sheet.Id, 3, 1);
        var validationCell = new CellAddress(sheet.Id, 4, 1);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        sheet.Comments[commentCell] = "note";
        sheet.ThreadedComments[threadedCommentCell] = new ThreadedComment("discussion");
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(validationCell, validationCell),
            Type = DvType.WholeNumber
        });

        GoToSpecialService.Find(sheet, range, GoToSpecialKind.Comments).Should().Equal(commentCell, threadedCommentCell);
        GoToSpecialService.Find(sheet, range, GoToSpecialKind.DataValidation).Should().Equal(validationCell);
    }

    [Fact]
    public void FindDataValidation_ReturnsCellsInAdditionalRanges()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var primaryCell = new CellAddress(sheet.Id, 1, 1);
        var additionalCell = new CellAddress(sheet.Id, 3, 3);
        var rule = new DataValidation
        {
            AppliesTo = new GridRange(primaryCell, primaryCell),
            Type = DvType.List,
            Formula1 = "A,B"
        };
        rule.AdditionalRanges.Add(new GridRange(additionalCell, additionalCell));
        sheet.DataValidations.Add(rule);
        var searchRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3));

        GoToSpecialService.Find(sheet, searchRange, GoToSpecialKind.DataValidation)
            .Should().Equal(primaryCell, additionalCell);
    }

    [Fact]
    public void FindVisibleCells_SkipsHiddenRowsAndColumns()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));
        sheet.HiddenRows.Add(2);
        sheet.HiddenCols.Add(2);

        var result = GoToSpecialService.Find(sheet, range, GoToSpecialKind.VisibleCellsOnly);

        result.Should().Equal(new CellAddress(sheet.Id, 1, 1));
    }

    [Fact]
    public void FindRowAndColumnDifferences_CompareAgainstFirstCellInEachRowOrColumn()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3));
        Set(sheet, 1, 1, "A");
        Set(sheet, 1, 2, "A");
        Set(sheet, 1, 3, "B");
        Set(sheet, 2, 1, 10);
        Set(sheet, 2, 2, 11);
        Set(sheet, 2, 3, 10);
        Set(sheet, 3, 1, "A");
        Set(sheet, 3, 2, "A");
        Set(sheet, 3, 3, "A");

        GoToSpecialService.Find(sheet, range, GoToSpecialKind.RowDifferences)
            .Should()
            .Equal(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 2, 2));

        GoToSpecialService.Find(sheet, range, GoToSpecialKind.ColumnDifferences)
            .Should()
            .Equal(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 2, 3), new CellAddress(sheet.Id, 3, 3));
    }

    [Fact]
    public void FindCurrentRegionLastCellAndConditionalFormats_ReturnsExcelLikeTargets()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var active = new CellAddress(sheet.Id, 2, 2);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 5));
        Set(sheet, 1, 1, "Region");
        Set(sheet, 1, 2, "Amount");
        Set(sheet, 2, 1, "East");
        Set(sheet, 2, 2, 10);
        Set(sheet, 5, 5, "last");
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 3, 3), new CellAddress(sheet.Id, 3, 4)),
            RuleType = CfRuleType.ColorScale
        });

        GoToSpecialService.Find(sheet, range, GoToSpecialKind.CurrentRegion, active)
            .Should()
            .Equal(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 2),
                new CellAddress(sheet.Id, 2, 1),
                new CellAddress(sheet.Id, 2, 2));

        GoToSpecialService.Find(sheet, range, GoToSpecialKind.LastCell)
            .Should()
            .Equal(new CellAddress(sheet.Id, 5, 5));

        GoToSpecialService.Find(sheet, range, GoToSpecialKind.ConditionalFormats)
            .Should()
            .Equal(new CellAddress(sheet.Id, 3, 3), new CellAddress(sheet.Id, 3, 4));
    }

    [Fact]
    public void FindObjects_ReturnsCellsAnchoringObjectsInRange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 4));
        var chartAnchor = new CellAddress(sheet.Id, 2, 2);
        var shapeAnchor = new CellAddress(sheet.Id, 3, 3);
        var pictureAnchor = new CellAddress(sheet.Id, 4, 4);
        var outsideAnchor = new CellAddress(sheet.Id, 5, 5);
        sheet.Charts.Add(new ChartModel { DataRange = new GridRange(chartAnchor, chartAnchor) });
        sheet.DrawingShapes.Add(new DrawingShapeModel { Anchor = shapeAnchor });
        sheet.Pictures.Add(new PictureModel { Anchor = pictureAnchor });
        sheet.TextBoxes.Add(new TextBoxModel { Anchor = outsideAnchor });

        GoToSpecialService.Find(wb, sheet, range, GoToSpecialKind.Objects)
            .Should()
            .Equal(chartAnchor, shapeAnchor, pictureAnchor);
    }

    [Fact]
    public void FindPrecedentsAndDependents_ReturnsDirectSameSheetFormulaReferences()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var selectedFormula = new CellAddress(sheet.Id, 3, 3);
        var selectedInput = new CellAddress(sheet.Id, 2, 2);
        var selectedRange = new GridRange(selectedFormula, selectedFormula);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(selectedInput, new NumberValue(20));
        sheet.SetCell(selectedFormula, Cell.FromFormula("A1+B2"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), Cell.FromFormula("B2*2"));

        GoToSpecialService.Find(wb, sheet, selectedRange, GoToSpecialKind.Precedents)
            .Should()
            .Equal(new CellAddress(sheet.Id, 1, 1), selectedInput);

        GoToSpecialService.Find(
                wb,
                sheet,
                new GridRange(selectedInput, selectedInput),
                GoToSpecialKind.Dependents)
            .Should()
            .Equal(selectedFormula, new CellAddress(sheet.Id, 4, 4));
    }

    [Fact]
    public void FindRuleBackedKinds_IndexedPathPreservesCellOrderAndDeduplicates()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 8));
        var expected = Enumerable.Range(1, 8)
            .Select(col => new CellAddress(sheet.Id, 1, (uint)col))
            .ToArray();

        for (var col = 8; col >= 1; col--)
        {
            var address = new CellAddress(sheet.Id, 1, (uint)col);
            sheet.ConditionalFormats.Add(new ConditionalFormat
            {
                AppliesTo = new GridRange(address, address),
                RuleType = CfRuleType.Formula
            });

            var validation = new DataValidation
            {
                AppliesTo = new GridRange(address, address),
                Type = DvType.WholeNumber
            };
            if (col == 8)
                validation.AdditionalRanges.Add(new GridRange(expected[0], expected[0]));
            sheet.DataValidations.Add(validation);
        }

        GoToSpecialService.Find(sheet, range, GoToSpecialKind.ConditionalFormats)
            .Should()
            .Equal(expected);
        GoToSpecialService.Find(sheet, range, GoToSpecialKind.DataValidation)
            .Should()
            .Equal(expected);
    }

    [Fact]
    public void FindRuleBackedKinds_IndexedPathDeduplicatesOverlappingRangesInCellOrder()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 4));
        GridRange[] ruleRanges =
        [
            Range(sheet, 4, 4, 4, 4),
            Range(sheet, 3, 3, 3, 4),
            Range(sheet, 2, 2, 3, 3),
            Range(sheet, 1, 1, 1, 2),
            Range(sheet, 2, 1, 4, 1),
            Range(sheet, 4, 2, 4, 4),
            Range(sheet, 1, 4, 2, 4),
            Range(sheet, 2, 2, 2, 2)
        ];
        var expected = new[]
        {
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 2),
            new CellAddress(sheet.Id, 1, 4),
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 2, 3),
            new CellAddress(sheet.Id, 2, 4),
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 3, 2),
            new CellAddress(sheet.Id, 3, 3),
            new CellAddress(sheet.Id, 3, 4),
            new CellAddress(sheet.Id, 4, 1),
            new CellAddress(sheet.Id, 4, 2),
            new CellAddress(sheet.Id, 4, 3),
            new CellAddress(sheet.Id, 4, 4)
        };

        foreach (var ruleRange in ruleRanges)
        {
            sheet.ConditionalFormats.Add(new ConditionalFormat
            {
                AppliesTo = ruleRange,
                RuleType = CfRuleType.Formula
            });
            sheet.DataValidations.Add(new DataValidation
            {
                AppliesTo = ruleRange,
                Type = DvType.WholeNumber
            });
        }

        GoToSpecialService.Find(sheet, range, GoToSpecialKind.ConditionalFormats)
            .Should()
            .Equal(expected);
        GoToSpecialService.Find(sheet, range, GoToSpecialKind.DataValidation)
            .Should()
            .Equal(expected);
    }

    [BenchmarkFact]
    [Trait("Category", "Benchmark")]
    public void Benchmark_FindConditionalFormatsManyRules_ReportsTimingAndAllocatedBytes()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        const uint rows = 200;
        const uint cols = 200;
        const int rules = 400;
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, rows, cols));

        for (var i = 0; i < rules; i++)
        {
            var row = (uint)(i % rows + 1);
            var col = (uint)(i / rows + 1);
            var address = new CellAddress(sheet.Id, row, col);
            sheet.ConditionalFormats.Add(new ConditionalFormat
            {
                AppliesTo = new GridRange(address, address),
                RuleType = CfRuleType.Formula
            });
        }

        var (count, elapsed, allocated) = MeasureFind(
            () => GoToSpecialService.Find(sheet, range, GoToSpecialKind.ConditionalFormats),
            iterations: 5);

        WritePerfLine(
            $"PERF GOTO_SPECIAL_CONDITIONAL_FORMATS_RANGE_LOOKUP rows={rows} cols={cols} rules={rules} steps=5 total_ms={elapsed.TotalMilliseconds:F2} allocated_bytes={allocated} matches={count}");
        count.Should().Be(rules);
    }

    [BenchmarkFact]
    [Trait("Category", "Benchmark")]
    public void Benchmark_FindDataValidationsManyRules_ReportsTimingAndAllocatedBytes()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        const uint rows = 200;
        const uint cols = 200;
        const int rules = 400;
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, rows, cols));

        for (var i = 0; i < rules; i++)
        {
            var row = (uint)(i % rows + 1);
            var col = (uint)(i / rows + 1);
            var address = new CellAddress(sheet.Id, row, col);
            sheet.DataValidations.Add(new DataValidation
            {
                AppliesTo = new GridRange(address, address),
                Type = DvType.WholeNumber
            });
        }

        var (count, elapsed, allocated) = MeasureFind(
            () => GoToSpecialService.Find(sheet, range, GoToSpecialKind.DataValidation),
            iterations: 5);

        WritePerfLine(
            $"PERF GOTO_SPECIAL_DATA_VALIDATION_RANGE_LOOKUP rows={rows} cols={cols} rules={rules} steps=5 total_ms={elapsed.TotalMilliseconds:F2} allocated_bytes={allocated} matches={count}");
        count.Should().Be(rules);
    }

    [BenchmarkFact]
    [Trait("Category", "Benchmark")]
    public void Benchmark_FindDependentsManyFormulaCells_ReportsTimingAndAllocatedBytes()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        const uint selectedRows = 30;
        const int formulas = 1_200;
        const int iterations = 2;
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, selectedRows, 1));

        for (var i = 0; i < formulas; i++)
        {
            var formulaAddress = new CellAddress(sheet.Id, (uint)(i + 1), 4);
            var precedentRow = (uint)(i % selectedRows + 1);
            sheet.SetCell(formulaAddress, Cell.FromFormula($"A{precedentRow}*2"));
        }

        var (count, elapsed, allocated) = MeasureFind(
            () => GoToSpecialService.Find(wb, sheet, range, GoToSpecialKind.Dependents),
            iterations);

        WritePerfLine(
            $"PERF GOTO_SPECIAL_DEPENDENTS_FORMULA_SCAN selected_rows={selectedRows} formulas={formulas} " +
            $"steps={iterations} total_ms={elapsed.TotalMilliseconds:F2} allocated_bytes={allocated} matches={count}");
        count.Should().Be(formulas);
        allocated.Should().BeLessThan(1_250_000);
    }

    private static void Set(Sheet sheet, uint row, uint col, string value) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), new TextValue(value));

    private static void Set(Sheet sheet, uint row, uint col, double value) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(value));

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(
            new CellAddress(sheet.Id, startRow, startCol),
            new CellAddress(sheet.Id, endRow, endCol));

    private static (int Count, TimeSpan Elapsed, long Allocated) MeasureFind(
        Func<IReadOnlyList<CellAddress>> find,
        int iterations)
    {
        find().Count.Should().BeGreaterThan(0);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var count = 0;
        for (var iteration = 0; iteration < iterations; iteration++)
            count = find().Count;
        stopwatch.Stop();

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        return (count, stopwatch.Elapsed, allocated);
    }

    private static void WritePerfLine(string line) => Console.WriteLine(line);
}
