using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R25-remove-duplicates-consolidate-2: ConsolidateCommand.Apply unconditionally rejected source
/// ranges whose RowCount/ColCount differed, even when consolidating "by category" (top-row and/or
/// left-column labels). Real Excel's documented consolidate-by-category workflow specifically
/// supports differently-shaped/sized source tables (it matches by label text, not by position), so
/// the blanket same-size rejection defeated the feature's primary use case. The fix exempts the
/// by-labels path from the same-size requirement and has ConsolidationLabelPlanBuilder read each
/// source range over its own RowCount/ColCount instead of a size shared across all ranges.
/// </summary>
public sealed class R25_ConsolidateByCategoryDifferentSizesTests
{
    [Fact]
    public void Consolidate_ByCategory_AllowsDifferentlySizedSourceRanges()
    {
        // Sheet1!A1:C3 -- 2 products x 2 months (labels in row 1 / col A).
        //         Q1   Q2
        // Fruit   10   20
        // Veg     30   40
        //
        // Sheet2!A1:D4 -- 3 products x 3 months, extra "Nuts" row and extra "Q3" column, and the
        // months/products are in a different order than range 1 (the textbook Excel "consolidate by
        // category" scenario -- different shape, different order, matched by label text).
        //         Q2   Q1   Q3
        // Veg      5    7    1
        // Fruit    9   11    2
        // Nuts     3    4    6
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var sid = sheet.Id;

        var source1 = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 3, 3));
        sheet.SetCell(new CellAddress(sid, 1, 2), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sid, 1, 3), new TextValue("Q2"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("Fruit"));
        sheet.SetCell(new CellAddress(sid, 3, 1), new TextValue("Veg"));
        sheet.SetCell(new CellAddress(sid, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sid, 2, 3), new NumberValue(20));
        sheet.SetCell(new CellAddress(sid, 3, 2), new NumberValue(30));
        sheet.SetCell(new CellAddress(sid, 3, 3), new NumberValue(40));

        var source2 = new GridRange(new CellAddress(sid, 1, 5), new CellAddress(sid, 4, 8));
        sheet.SetCell(new CellAddress(sid, 1, 6), new TextValue("Q2"));
        sheet.SetCell(new CellAddress(sid, 1, 7), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sid, 1, 8), new TextValue("Q3"));
        sheet.SetCell(new CellAddress(sid, 2, 5), new TextValue("Veg"));
        sheet.SetCell(new CellAddress(sid, 3, 5), new TextValue("Fruit"));
        sheet.SetCell(new CellAddress(sid, 4, 5), new TextValue("Nuts"));
        sheet.SetCell(new CellAddress(sid, 2, 6), new NumberValue(5));
        sheet.SetCell(new CellAddress(sid, 2, 7), new NumberValue(7));
        sheet.SetCell(new CellAddress(sid, 2, 8), new NumberValue(1));
        sheet.SetCell(new CellAddress(sid, 3, 6), new NumberValue(9));
        sheet.SetCell(new CellAddress(sid, 3, 7), new NumberValue(11));
        sheet.SetCell(new CellAddress(sid, 3, 8), new NumberValue(2));
        sheet.SetCell(new CellAddress(sid, 4, 6), new NumberValue(3));
        sheet.SetCell(new CellAddress(sid, 4, 7), new NumberValue(4));
        sheet.SetCell(new CellAddress(sid, 4, 8), new NumberValue(6));

        var destination = new CellAddress(sid, 10, 1);
        var command = new ConsolidateCommand(
            [source1, source2],
            destination,
            ConsolidateFunction.Sum,
            useTopRowLabels: true,
            useLeftColumnLabels: true);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // Column labels are collected in first-seen order: Q1, Q2 (from range1), then Q3 (new from
        // range2). Row labels: Fruit, Veg (from range1), then Nuts (new from range2).
        sheet.GetValue(10, 1).Should().Be(BlankValue.Instance);
        sheet.GetValue(10, 2).Should().Be(new TextValue("Q1"));
        sheet.GetValue(10, 3).Should().Be(new TextValue("Q2"));
        sheet.GetValue(10, 4).Should().Be(new TextValue("Q3"));
        sheet.GetValue(11, 1).Should().Be(new TextValue("Fruit"));
        sheet.GetValue(12, 1).Should().Be(new TextValue("Veg"));
        sheet.GetValue(13, 1).Should().Be(new TextValue("Nuts"));

        sheet.GetValue(11, 2).Should().Be(new NumberValue(21)); // Fruit/Q1: 10 + 11
        sheet.GetValue(11, 3).Should().Be(new NumberValue(29)); // Fruit/Q2: 20 + 9
        sheet.GetValue(11, 4).Should().Be(new NumberValue(2));  // Fruit/Q3: only range2 has Q3
        sheet.GetValue(12, 2).Should().Be(new NumberValue(37)); // Veg/Q1: 30 + 7
        sheet.GetValue(12, 3).Should().Be(new NumberValue(45)); // Veg/Q2: 40 + 5
        sheet.GetValue(12, 4).Should().Be(new NumberValue(1));  // Veg/Q3: only range2 has Q3
        sheet.GetValue(13, 2).Should().Be(new NumberValue(4));  // Nuts/Q1: only range2 has Nuts
        sheet.GetValue(13, 3).Should().Be(new NumberValue(3));  // Nuts/Q2
        sheet.GetValue(13, 4).Should().Be(new NumberValue(6));  // Nuts/Q3

        command.Revert(ctx);
        sheet.GetCell(10, 1).Should().BeNull();
        sheet.GetCell(13, 4).Should().BeNull();
    }

    [Fact]
    public void Consolidate_ByPosition_StillRejectsDifferentlySizedSourceRanges()
    {
        // Sibling case: consolidate-by-position (no top-row/left-column labels) has no label
        // matching to fall back on, so real Excel (and FreeX) requires identically-sized source
        // ranges for that mode. This must remain rejected after the by-category fix above.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var sid = sheet.Id;

        var source1 = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 2, 2));
        var source2 = new GridRange(new CellAddress(sid, 1, 4), new CellAddress(sid, 3, 5));

        var command = new ConsolidateCommand([source1, source2], new CellAddress(sid, 5, 1));

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("same size");
    }

    [Fact]
    public void Consolidate_ByCategory_SameSizedSourceRanges_StillWorks()
    {
        // Sibling case: the already-working same-size by-category scenario (pinned by
        // ConsolidateCommand_UsesTopRowAndLeftColumnLabels in ConsolidateCommandTests.cs) must keep
        // working unchanged now that the same-size check no longer runs for the by-labels path.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var sid = sheet.Id;

        var source1 = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 2, 2));
        var source2 = new GridRange(new CellAddress(sid, 1, 4), new CellAddress(sid, 2, 5));
        sheet.SetCell(new CellAddress(sid, 1, 2), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sid, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sid, 1, 5), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sid, 2, 4), new TextValue("East"));
        sheet.SetCell(new CellAddress(sid, 2, 5), new NumberValue(7));

        var command = new ConsolidateCommand(
            [source1, source2],
            new CellAddress(sid, 5, 1),
            ConsolidateFunction.Sum,
            useTopRowLabels: true,
            useLeftColumnLabels: true);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(6, 2).Should().Be(new NumberValue(17));
    }
}
