using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class FlashFillRangePlannerTests
{
    [Fact]
    public void Plan_SingleCellBelowExample_IncludesExamplesAboveAndAdjacentDataBelow()
    {
        var sheet = CreateSheet();
        SetText(sheet, 1, 1, "John Smith");
        SetText(sheet, 1, 2, "John");
        SetText(sheet, 2, 1, "Jane Doe");
        SetText(sheet, 3, 1, "Bob Brown");

        var plan = FlashFillRangePlanner.Plan(sheet, Range(sheet, 2, 2, 2, 2));

        plan.Should().Be(new FlashFillCommandPlan(
            FillColumn: 2,
            SourceColumn: 1,
            StartRow: 1,
            EndRow: 3));
    }

    [Fact]
    public void Plan_FirstColumnSingleCell_UsesSourceColumnOnRight()
    {
        var sheet = CreateSheet();
        SetText(sheet, 1, 1, "john");
        SetText(sheet, 1, 2, "JOHN");
        SetText(sheet, 2, 2, "JANE");
        SetText(sheet, 3, 2, "BOB");

        var plan = FlashFillRangePlanner.Plan(sheet, Range(sheet, 2, 1, 2, 1));

        plan.Should().Be(new FlashFillCommandPlan(
            FillColumn: 1,
            SourceColumn: 2,
            StartRow: 1,
            EndRow: 3));
    }

    [Fact]
    public void Plan_MultiRowSelection_PreservesExplicitSelectedRows()
    {
        var sheet = CreateSheet();
        SetText(sheet, 1, 1, "John Smith");
        SetText(sheet, 1, 2, "John");
        SetText(sheet, 2, 1, "Jane Doe");
        SetText(sheet, 4, 1, "Bob Brown");

        var plan = FlashFillRangePlanner.Plan(sheet, Range(sheet, 2, 2, 4, 2));

        plan.Should().Be(new FlashFillCommandPlan(
            FillColumn: 2,
            SourceColumn: 1,
            StartRow: 2,
            EndRow: 4));
    }

    [Fact]
    public void Plan_SourceColumnSelectionWithAdjacentExample_UsesAdjacentBlankFillColumn()
    {
        var sheet = CreateSheet();
        SetText(sheet, 1, 1, "John Smith");
        SetText(sheet, 1, 2, "John");
        SetText(sheet, 2, 1, "Jane Doe");
        SetText(sheet, 3, 1, "Bob Brown");

        var plan = FlashFillRangePlanner.Plan(sheet, Range(sheet, 1, 1, 3, 1));

        plan.Should().Be(new FlashFillCommandPlan(
            FillColumn: 2,
            SourceColumn: 1,
            StartRow: 1,
            EndRow: 3));
        FlashFillRangePlanner.HasExamples(sheet, plan).Should().BeTrue();
        FlashFillRangePlanner.HasFillTargets(sheet, plan).Should().BeTrue();
    }

    [Fact]
    public void Plan_SourceColumnSelectionWithAdjacentExample_TreatsEmptyTextTargetsAsBlank()
    {
        var sheet = CreateSheet();
        SetText(sheet, 1, 1, "John Smith");
        SetText(sheet, 1, 2, "John");
        SetText(sheet, 2, 1, "Jane Doe");
        SetText(sheet, 2, 2, "");
        SetText(sheet, 3, 1, "Bob Brown");
        SetText(sheet, 3, 2, "");

        var plan = FlashFillRangePlanner.Plan(sheet, Range(sheet, 1, 1, 3, 1));

        plan.Should().Be(new FlashFillCommandPlan(
            FillColumn: 2,
            SourceColumn: 1,
            StartRow: 1,
            EndRow: 3));
        FlashFillRangePlanner.HasExamples(sheet, plan).Should().BeTrue();
        FlashFillRangePlanner.HasFillTargets(sheet, plan).Should().BeTrue();
    }

    [Fact]
    public void Plan_SingleCellBelowSeparatedExamples_OnlyIncludesContiguousExampleRows()
    {
        var sheet = CreateSheet();
        SetText(sheet, 1, 1, "Ada Lovelace");
        SetText(sheet, 1, 2, "Ada");
        SetText(sheet, 3, 1, "Grace Hopper");
        SetText(sheet, 3, 2, "Grace");
        SetText(sheet, 4, 1, "Alan Turing");

        var plan = FlashFillRangePlanner.Plan(sheet, Range(sheet, 4, 2, 4, 2));

        plan.Should().Be(new FlashFillCommandPlan(
            FillColumn: 2,
            SourceColumn: 1,
            StartRow: 3,
            EndRow: 4));
    }

    [Fact]
    public void HasExamplesAndFillTargets_DistinguishCompleteExampleColumnFromFillableRange()
    {
        var sheet = CreateSheet();
        SetText(sheet, 1, 1, "John Smith");
        SetText(sheet, 1, 2, "John");
        SetText(sheet, 2, 1, "Jane Doe");
        SetText(sheet, 2, 2, "Jane");

        var plan = FlashFillRangePlanner.Plan(sheet, Range(sheet, 2, 2, 2, 2));

        FlashFillRangePlanner.HasExamples(sheet, plan).Should().BeTrue();
        FlashFillRangePlanner.HasFillTargets(sheet, plan).Should().BeFalse();
    }

    [Fact]
    public void HasExamplesAndFillTargets_ReportEmptySelectionAsNoExampleAndNoTarget()
    {
        var sheet = CreateSheet();

        var plan = FlashFillRangePlanner.Plan(sheet, Range(sheet, 2, 2, 2, 2));

        FlashFillRangePlanner.HasExamples(sheet, plan).Should().BeFalse();
        FlashFillRangePlanner.HasFillTargets(sheet, plan).Should().BeFalse();
    }

    private static Sheet CreateSheet()
    {
        var workbook = new Workbook("test");
        return workbook.AddSheet("Sheet1");
    }

    private static GridRange Range(Sheet sheet, uint startRow, uint startColumn, uint endRow, uint endColumn) =>
        new(new CellAddress(sheet.Id, startRow, startColumn), new CellAddress(sheet.Id, endRow, endColumn));

    private static void SetText(Sheet sheet, uint row, uint column, string value) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, column), new TextValue(value));
}
