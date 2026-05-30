using System.Diagnostics;
using System.IO;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class TextToColumnsPlannerTests
{
    [Theory]
    [InlineData(1, false, true, false, false, false, false, false, true, false)]
    [InlineData(2, false, false, true, false, false, false, true, true, false)]
    [InlineData(2, true, false, false, true, false, false, true, true, false)]
    [InlineData(3, false, false, false, false, true, true, true, false, true)]
    public void CreateWizardStepPlan_MapsExcelWizardPanelsAndButtons(
        int step,
        bool fixedWidth,
        bool showOriginal,
        bool showDelimited,
        bool showFixedWidth,
        bool showFormat,
        bool showDestination,
        bool backEnabled,
        bool nextEnabled,
        bool finishDefault)
    {
        var plan = TextToColumnsWizardPlanner.CreateStepPlan(step, fixedWidth);

        plan.Header.Should().Be(UiText.Format("TextToColumns_TextWizardStepOf3", Math.Clamp(step, 1, 3)));
        plan.ShowOriginalDataTypePanel.Should().Be(showOriginal);
        plan.ShowDelimiterPanel.Should().Be(showDelimited);
        plan.ShowFixedWidthPanel.Should().Be(showFixedWidth);
        plan.ShowColumnFormatPanel.Should().Be(showFormat);
        plan.ShowDestinationPanel.Should().Be(showDestination);
        plan.BackEnabled.Should().Be(backEnabled);
        plan.NextEnabled.Should().Be(nextEnabled);
        plan.NextDefault.Should().Be(nextEnabled);
        plan.FinishDefault.Should().Be(finishDefault);
    }

    [Theory]
    [InlineData(false, false, true, false, false, 0.55)]
    [InlineData(false, true, true, true, false, 0.55)]
    [InlineData(true, true, false, false, true, 1.0)]
    public void CreateWizardModePlan_MapsDelimitedAndFixedWidthControlState(
        bool fixedWidth,
        bool otherSelected,
        bool delimitedEnabled,
        bool customEnabled,
        bool fixedWidthEnabled,
        double rulerOpacity)
    {
        TextToColumnsWizardPlanner.CreateModePlan(fixedWidth, otherSelected)
            .Should()
            .Be(new TextToColumnsWizardModePlan(
                delimitedEnabled,
                customEnabled,
                fixedWidthEnabled,
                rulerOpacity));
    }

    [Fact]
    public void BuildEdits_SplitsTextFromFirstColumnAcrossColumns()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 2, 3), new CellAddress(sheet.Id, 3, 3));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("East, 42, Open"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new TextValue("West, 7, Closed"));

        var edits = TextToColumnsPlanner.BuildEdits(sheet, range, ',');

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

        var edits = TextToColumnsPlanner.BuildEdits(sheet, range, ';');

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

        var edits = TextToColumnsPlanner.BuildEdits(sheet, range, destination, ',');

        edits.Select(edit => edit.Address).Should().Equal(
            new CellAddress(sheet.Id, 2, 6),
            new CellAddress(sheet.Id, 2, 7),
            new CellAddress(sheet.Id, 3, 6),
            new CellAddress(sheet.Id, 3, 7));
        edits.Select(edit => edit.Address.Col).Should().NotContain(1u);
    }

    [Fact]
    public void FindOverwriteTargets_ReportsExistingDestinationCells()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A,B"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Existing"));
        var edits = TextToColumnsPlanner.BuildEdits(
            sheet,
            sourceRange,
            new CellAddress(sheet.Id, 1, 2),
            ',');

        TextToColumnsPlanner.FindOverwriteTargets(sheet, edits, sourceRange)
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
        var edits = TextToColumnsPlanner.BuildEdits(sheet, sourceRange, ',');

        TextToColumnsPlanner.FindOverwriteTargets(sheet, edits, sourceRange)
            .Should()
            .Equal(new CellAddress(sheet.Id, 1, 2));
    }

    [Fact]
    public void FindOverwriteTargets_IgnoresEmptyDestinationCells()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A,B"));
        var edits = TextToColumnsPlanner.BuildEdits(
            sheet,
            sourceRange,
            new CellAddress(sheet.Id, 2, 1),
            ',');

        TextToColumnsPlanner.FindOverwriteTargets(sheet, edits, sourceRange)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void BuildEdits_AppliesTextAndSkipColumnFormats()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));
        var destination = new CellAddress(sheet.Id, 2, 5);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("00123,Skip Me,42"));

        var edits = TextToColumnsPlanner.BuildEdits(
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

        var edits = TextToColumnsPlanner.BuildEdits(
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
    public void BuildEdits_UsesSelectedDateColumnFormat()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("31/12/2025,2026-01-15"));

        var edits = TextToColumnsPlanner.BuildEdits(
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
    public void BuildEdits_SplitsOnAnySelectedDelimiter()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("East,42;Open"));

        var edits = TextToColumnsPlanner.BuildEdits(sheet, range, ",;");

        edits.Select(edit => edit.NewCell.Value).Should().Equal(
            new TextValue("East"),
            new NumberValue(42),
            new TextValue("Open"));
    }

    [Fact]
    public void SplitText_DefaultsToCommaWhenDelimiterListIsEmpty()
    {
        TextToColumnsPlanner.SplitText("A,B", "").Should().Equal("A", "B");
    }

    [Fact]
    public void SplitText_HonorsExcelTextQualifier()
    {
        TextToColumnsPlanner.SplitText("\"Smith, John\",42,\"He said \"\"OK\"\"\"", ",", '"', false)
            .Should()
            .Equal("Smith, John", "42", "He said \"OK\"");
    }

    [Fact]
    public void SplitText_CanTreatConsecutiveDelimitersAsOne()
    {
        TextToColumnsPlanner.SplitText("A,,B", ",", '"', true)
            .Should()
            .Equal("A", "B");

        TextToColumnsPlanner.SplitText("A,,B", ",", '"', false)
            .Should()
            .Equal("A", "", "B");
    }

    [Fact]
    public void BuildEdits_UsesTextQualifierAndConsecutiveDelimiterOptions()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("\"Smith, John\",,42"));

        var edits = TextToColumnsPlanner.BuildEdits(sheet, range, ",", '"', true);

        edits.Select(edit => edit.NewCell.Value).Should().Equal(
            new TextValue("Smith, John"),
            new NumberValue(42));
    }

    [Fact]
    public void SplitFixedWidthText_UsesSortedUniqueBreakPositions()
    {
        TextToColumnsPlanner.SplitFixedWidthText("East0042Open", [8, 4, 4])
            .Should()
            .Equal("East", "0042", "Open");
    }

    [Fact]
    public void SplitFixedWidthText_SourceAvoidsEmptyBreakNormalizationAndPreallocatesParts()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextToColumnsSplitter.cs"));

        source.Should().Contain("if (breakPositions.Count == 0)");
        source.Should().Contain("new List<string>(positions.Count + 1)");
    }

    [Fact]
    public void SplitText_SourceAvoidsDelimiterArrayAllocation()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextToColumnsSplitter.cs"));

        source.Should().Contain("private static bool IsDelimiter(char ch, string delimiters)");
        source.Should().NotContain("delimiters.Distinct().ToArray()");
    }

    [Fact]
    public void SplitText_LongSingleDelimiterInput_StaysWithinInteractiveBudget()
    {
        var row = string.Join(",", Enumerable.Range(0, 200).Select(index => $"Value{index}"));

        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < 1_000; index++)
            TextToColumnsPlanner.SplitText(row, ",", '"', false).Should().HaveCount(200);
        stopwatch.Stop();

        Console.WriteLine($"Text-to-columns single-delimiter split benchmark: {stopwatch.Elapsed.TotalMilliseconds:F2}ms for 1000 runs");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void SplitText_UnqualifiedInput_AvoidsBuilderAndListOverhead()
    {
        var row = string.Join(",", Enumerable.Range(0, 200).Select(index => $"Value{index}"));

        TextToColumnsPlanner.SplitText(row, ",", '"', false).Should().HaveCount(200);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 500; index++)
            TextToColumnsPlanner.SplitText(row, ",", '"', false).Should().HaveCount(200);
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Console.WriteLine($"Text-to-columns unqualified split allocations: {allocatedBytes:N0} bytes for 500 runs");
        allocatedBytes.Should().BeLessThan(7_000_000);
    }

    [Fact]
    public void BuildFixedWidthEdits_SplitsTextAcrossColumns()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 2, 3), new CellAddress(sheet.Id, 2, 3));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("East0042Open"));

        var edits = TextToColumnsPlanner.BuildFixedWidthEdits(sheet, range, [4, 8]);

        edits.Select(edit => edit.NewCell.Value).Should().Equal(
            new TextValue("East"),
            new NumberValue(42),
            new TextValue("Open"));
    }
}
