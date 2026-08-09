using System.Diagnostics;
using FluentAssertions;
using FreeX.App.Presentation.TextToColumns;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.TextToColumns;

public sealed class TextToColumnsApplyPlannerTests
{
    [Fact]
    public void BuildEdits_SplitsTextFromFirstColumnAcrossColumns()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 2, 3), new CellAddress(sheet.Id, 3, 3));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("East, 42, Open"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new TextValue("West, 7, Closed"));

        var edits = TextToColumnsApplyPlanner.BuildEdits(sheet, range, ',');

        edits.Select(edit => edit.Address).Should().Equal(
            new CellAddress(sheet.Id, 2, 3),
            new CellAddress(sheet.Id, 2, 4),
            new CellAddress(sheet.Id, 2, 5),
            new CellAddress(sheet.Id, 3, 3),
            new CellAddress(sheet.Id, 3, 4),
            new CellAddress(sheet.Id, 3, 5));
        edits.Select(edit => edit.NewCell.Value).Should().Equal(
            new TextValue("East"),
            new NumberValue(42),
            new TextValue("Open"),
            new TextValue("West"),
            new NumberValue(7),
            new TextValue("Closed"));
    }

    [Fact]
    public void BuildEdits_IgnoresNonTextCellsInSourceColumn()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A;B"));

        var edits = TextToColumnsApplyPlanner.BuildEdits(sheet, range, ';');

        edits.Should().HaveCount(2);
        edits[0].Address.Should().Be(new CellAddress(sheet.Id, 2, 1));
        edits[0].NewCell.Value.Should().Be(new TextValue("A"));
        edits[1].Address.Should().Be(new CellAddress(sheet.Id, 2, 2));
        edits[1].NewCell.Value.Should().Be(new TextValue("B"));
    }

    [Fact]
    public void BuildEdits_CanWriteSplitOutputToExplicitDestination()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 3, 1));
        var destination = new CellAddress(sheet.Id, 2, 6);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East,42"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West,7"));

        var edits = TextToColumnsApplyPlanner.BuildEdits(sheet, range, destination, ',');

        edits.Select(edit => edit.Address).Should().Equal(
            new CellAddress(sheet.Id, 2, 6),
            new CellAddress(sheet.Id, 2, 7),
            new CellAddress(sheet.Id, 3, 6),
            new CellAddress(sheet.Id, 3, 7));
        edits.Select(edit => edit.Address.Col).Should().NotContain(1u);
    }

    [Fact]
    public void BuildEdits_SplitsOnAnySelectedDelimiter()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("East,42;Open"));

        var edits = TextToColumnsApplyPlanner.BuildEdits(sheet, range, ",;");

        edits.Select(edit => edit.NewCell.Value).Should().Equal(
            new TextValue("East"),
            new NumberValue(42),
            new TextValue("Open"));
    }

    [Fact]
    public void SplitText_DefaultsToCommaWhenDelimiterListIsEmpty()
    {
        TextToColumnsApplyPlanner.SplitText("A,B", "").Should().Equal("A", "B");
    }

    [Fact]
    public void SplitText_HonorsExcelTextQualifier()
    {
        TextToColumnsApplyPlanner.SplitText("\"Smith, John\",42,\"He said \"\"OK\"\"\"", ",", '"', false)
            .Should()
            .Equal("Smith, John", "42", "He said \"OK\"");
    }

    [Fact]
    public void SplitText_CanTreatConsecutiveDelimitersAsOne()
    {
        TextToColumnsApplyPlanner.SplitText("A,,B", ",", '"', true)
            .Should()
            .Equal("A", "B");

        TextToColumnsApplyPlanner.SplitText("A,,B", ",", '"', false)
            .Should()
            .Equal("A", "", "B");
    }

    [Fact]
    public void BuildEdits_UsesTextQualifierAndConsecutiveDelimiterOptions()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("\"Smith, John\",,42"));

        var edits = TextToColumnsApplyPlanner.BuildEdits(sheet, range, ",", '"', true);

        edits.Select(edit => edit.NewCell.Value).Should().Equal(
            new TextValue("Smith, John"),
            new NumberValue(42));
    }

    [Fact]
    public void SplitFixedWidthText_UsesSortedUniqueBreakPositions()
    {
        TextToColumnsApplyPlanner.SplitFixedWidthText("East0042Open", [8, 4, 4])
            .Should()
            .Equal("East", "0042", "Open");
    }

    [Fact]
    public void BuildFixedWidthEdits_SplitsTextAcrossColumns()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 2, 3), new CellAddress(sheet.Id, 2, 3));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("East0042Open"));

        var edits = TextToColumnsApplyPlanner.BuildFixedWidthEdits(sheet, range, [4, 8]);

        edits.Select(edit => edit.NewCell.Value).Should().Equal(
            new TextValue("East"),
            new NumberValue(42),
            new TextValue("Open"));
    }

    [Fact]
    public void BuildEdits_AppliesTextAndSkipColumnFormats()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));
        var destination = new CellAddress(sheet.Id, 2, 5);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("00123,Skip Me,42"));

        var edits = TextToColumnsApplyPlanner.BuildEdits(
            sheet,
            range,
            destination,
            ',',
            [
                TextToColumnsColumnFormat.Text,
                TextToColumnsColumnFormat.Skip,
                TextToColumnsColumnFormat.General
            ]);

        edits.Select(edit => edit.Address).Should().Equal(
            new CellAddress(sheet.Id, 2, 5),
            new CellAddress(sheet.Id, 2, 6));
        edits.Select(edit => edit.NewCell.Value).Should().Equal(
            new TextValue("00123"),
            new NumberValue(42));
    }

    [Fact]
    public void BuildEdits_UsesAdvancedNumberOptionsForGeneralColumns()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("1.234,50;42-"));

        var edits = TextToColumnsApplyPlanner.BuildEdits(
            sheet,
            range,
            new CellAddress(sheet.Id, 2, 3),
            ";",
            advancedOptions: new TextToColumnsAdvancedOptions(",", ".", TrailingMinusNumbers: true));

        edits.Select(edit => edit.NewCell.Value).Should().Equal(
            new NumberValue(1234.50),
            new NumberValue(-42));
    }

    [Fact]
    public void BuildEdits_UsesCurrentCultureForGeneralNumbersWithInvariantFallback()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("fr-FR");

        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("1,25;1.25;NaN;Infinity"));

        var edits = TextToColumnsApplyPlanner.BuildEdits(sheet, range, new CellAddress(sheet.Id, 2, 3), ";");

        edits.Select(edit => edit.NewCell.Value).Should().Equal(
            new NumberValue(1.25),
            new NumberValue(1.25),
            new TextValue("NaN"),
            new TextValue("Infinity"));
    }

    [Fact]
    public void BuildEdits_UsesSelectedDateColumnFormat()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("31/12/2025,2026-01-15"));

        var edits = TextToColumnsApplyPlanner.BuildEdits(
            sheet,
            range,
            new CellAddress(sheet.Id, 2, 3),
            ",",
            [
                TextToColumnsColumnFormat.DateDMY,
                TextToColumnsColumnFormat.DateYMD
            ]);

        edits.Select(edit => edit.NewCell.Value).Should().Equal(
            new DateTimeValue(new DateTime(2025, 12, 31).ToOADate()),
            new DateTimeValue(new DateTime(2026, 1, 15).ToOADate()));
    }

    [Fact]
    public void FindOverwriteTargets_ReportsExistingDestinationCells()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A,B"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Existing"));
        var edits = TextToColumnsApplyPlanner.BuildEdits(
            sheet,
            sourceRange,
            new CellAddress(sheet.Id, 1, 2),
            ',');

        TextToColumnsApplyPlanner.FindOverwriteTargets(sheet, edits, sourceRange)
            .Should()
            .Equal(new CellAddress(sheet.Id, 1, 3));
    }

    [Fact]
    public void FindOverwriteTargets_DoesNotWarnForOriginalSourceCellsInPlace()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A,B"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Existing"));
        var edits = TextToColumnsApplyPlanner.BuildEdits(sheet, sourceRange, ',');

        TextToColumnsApplyPlanner.FindOverwriteTargets(sheet, edits, sourceRange)
            .Should()
            .Equal(new CellAddress(sheet.Id, 1, 2));
    }

    [Fact]
    public void FindOverwriteTargets_IgnoresEmptyDestinationCells()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A,B"));
        var edits = TextToColumnsApplyPlanner.BuildEdits(
            sheet,
            sourceRange,
            new CellAddress(sheet.Id, 2, 1),
            ',');

        TextToColumnsApplyPlanner.FindOverwriteTargets(sheet, edits, sourceRange)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void BuildSheetPlans_CreatesPerSheetGroupedPlans()
    {
        var workbook = new Workbook("test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var range = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 1, 1));
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new TextValue("East,42"));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new TextValue("West,7"));

        var plans = TextToColumnsApplyPlanner.BuildSheetPlans(
            workbook,
            [sheet1.Id, sheet2.Id],
            range,
            new TextToColumnsDialogResult(TextToColumnsDelimiterKind.Comma, ","));

        plans.Should().HaveCount(2);
        plans.Select(plan => plan.SheetId).Should().Equal(sheet1.Id, sheet2.Id);
        plans[1].SourceRange.Start.Sheet.Should().Be(sheet2.Id);
        plans[1].Destination.Sheet.Should().Be(sheet2.Id);
        plans[0].Edits.Select(edit => edit.NewCell.Value).Should().Equal(new TextValue("East"), new NumberValue(42));
        plans[1].Edits.Select(edit => edit.NewCell.Value).Should().Equal(new TextValue("West"), new NumberValue(7));
    }

    [Fact]
    public void FindOverwriteTargets_FindsOverwritesAcrossGroupedSheets()
    {
        var workbook = new Workbook("test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var range = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 1, 1));
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new TextValue("East,42"));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new TextValue("West,7"));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 2), new TextValue("Existing"));

        TextToColumnsApplyPlanner.FindOverwriteTargets(
                workbook,
                [sheet1.Id, sheet2.Id],
                range,
                new TextToColumnsDialogResult(TextToColumnsDelimiterKind.Comma, ","))
            .Should()
            .Equal(new CellAddress(sheet2.Id, 1, 2));
    }

    [Fact]
    public void SplitFixedWidthText_SourceAvoidsEmptyBreakNormalizationAndPreallocatesParts()
    {
        var source = ReadPresentationTextToColumnsSource("TextToColumnsSplitter.cs");

        source.Should().Contain("if (breakPositions.Count == 0)");
        source.Should().Contain("new List<string>(positions.Count + 1)");
    }

    [Fact]
    public void SplitText_SourceAvoidsDelimiterArrayAllocation()
    {
        var source = ReadPresentationTextToColumnsSource("TextToColumnsSplitter.cs");

        source.Should().Contain("private static bool IsDelimiter(char ch, string delimiters)");
        source.Should().NotContain("delimiters.Distinct().ToArray()");
    }

    [Fact]
    public void SplitText_LongSingleDelimiterInput_StaysWithinInteractiveBudget()
    {
        var row = string.Join(",", Enumerable.Range(0, 200).Select(index => $"Value{index}"));

        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < 1_000; index++)
            TextToColumnsApplyPlanner.SplitText(row, ",", '"', false).Should().HaveCount(200);
        stopwatch.Stop();

        Console.WriteLine($"Text-to-columns single-delimiter split benchmark: {stopwatch.Elapsed.TotalMilliseconds:F2}ms for 1000 runs");
        // R122-flaky-wallclock-budget: this ran ~209ms in isolation but failed the central gate in
        // r120, r121 AND r122 -- the gate runs 21 test assemblies in parallel, and a 1s ceiling left
        // only ~5x headroom over the isolated baseline, which contention alone eats.
        // Deliberately NOT moved to BenchmarkFactAttribute (gated behind FREEX_RUN_BENCHMARK_TESTS):
        // that would stop it running in the routine gate at all, trading a flaky check for a dead
        // one -- the exact class rounds 117-121 kept finding. 3s keeps it live while giving ~14x
        // headroom over the baseline, which still catches any real algorithmic regression (the
        // defect this guards -- quadratic re-scanning -- blows past 3s by orders of magnitude).
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void SplitText_UnqualifiedInput_AvoidsBuilderAndListOverhead()
    {
        var row = string.Join(",", Enumerable.Range(0, 200).Select(index => $"Value{index}"));

        TextToColumnsApplyPlanner.SplitText(row, ",", '"', false).Should().HaveCount(200);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 500; index++)
            TextToColumnsApplyPlanner.SplitText(row, ",", '"', false).Should().HaveCount(200);
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Console.WriteLine($"Text-to-columns unqualified split allocations: {allocatedBytes:N0} bytes for 500 runs");
        allocatedBytes.Should().BeLessThan(7_000_000);
    }

    private static string ReadPresentationTextToColumnsSource(string fileName)
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        return File.ReadAllText(Path.Combine(presentationRoot, "TextToColumns", fileName));
    }
}
