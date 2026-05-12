using Freexcel.Core.Model;
using Freexcel.Core.Commands;
using FluentAssertions;

namespace Freexcel.Core.Calc.Tests;

public class SortFilterTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static (Workbook wb, Sheet sheet, ICommandContext ctx) MakeContext()
    {
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCommandContext(wb);
        return (wb, sheet, ctx);
    }

    // ICommandContext backed by a Workbook — same pattern used by CommandBus
    private sealed class SimpleCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook => workbook;
        public Sheet GetSheet(SheetId id) => workbook.GetSheet(id)!;
    }

    // ── Sort tests ────────────────────────────────────────────────────────────

    [Fact]
    public void Sort_Range_AscendingByFirstColumn()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;

        // A1=3, A2=1, A3=2
        sheet.SetCell(new CellAddress(sid, 1, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sid, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sid, 3, 1), new NumberValue(2));

        var range = new GridRange(
            new CellAddress(sid, 1, 1),
            new CellAddress(sid, 3, 1));

        var cmd = new SortCommand(sid, range, sortByColOffset: 0, ascending: true);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetValue(new CellAddress(sid, 1, 1)).Should().Be(new NumberValue(1));
        sheet.GetValue(new CellAddress(sid, 2, 1)).Should().Be(new NumberValue(2));
        sheet.GetValue(new CellAddress(sid, 3, 1)).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Sort_Range_DescendingByFirstColumn()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;

        // A1=3, A2=1, A3=2
        sheet.SetCell(new CellAddress(sid, 1, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sid, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sid, 3, 1), new NumberValue(2));

        var range = new GridRange(
            new CellAddress(sid, 1, 1),
            new CellAddress(sid, 3, 1));

        var cmd = new SortCommand(sid, range, sortByColOffset: 0, ascending: false);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetValue(new CellAddress(sid, 1, 1)).Should().Be(new NumberValue(3));
        sheet.GetValue(new CellAddress(sid, 2, 1)).Should().Be(new NumberValue(2));
        sheet.GetValue(new CellAddress(sid, 3, 1)).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Sort_Revert_RestoresOriginalOrder()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;

        sheet.SetCell(new CellAddress(sid, 1, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sid, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sid, 3, 1), new NumberValue(2));

        var range = new GridRange(
            new CellAddress(sid, 1, 1),
            new CellAddress(sid, 3, 1));

        var cmd = new SortCommand(sid, range, sortByColOffset: 0, ascending: true);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.GetValue(new CellAddress(sid, 1, 1)).Should().Be(new NumberValue(3));
        sheet.GetValue(new CellAddress(sid, 2, 1)).Should().Be(new NumberValue(1));
        sheet.GetValue(new CellAddress(sid, 3, 1)).Should().Be(new NumberValue(2));
    }

    // ── Filter tests ──────────────────────────────────────────────────────────

    [Fact]
    public void Filter_HidesNonMatchingRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;

        // A1=Header, A2=Apple, A3=Banana, A4=Apple
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Header"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sid, 3, 1), new TextValue("Banana"));
        sheet.SetCell(new CellAddress(sid, 4, 1), new TextValue("Apple"));

        var range = new GridRange(
            new CellAddress(sid, 1, 1),
            new CellAddress(sid, 4, 1));

        var cmd = new FilterCommand(sid, range, filterColOffset: 0, allowedValues: ["Apple"]);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        // Row 3 (Banana) should be hidden; rows 1, 2, 4 (Header, Apple, Apple) should be visible
        sheet.HiddenRows.Should().Contain(3u);
        sheet.HiddenRows.Should().NotContain(1u);
        sheet.HiddenRows.Should().NotContain(2u);
        sheet.HiddenRows.Should().NotContain(4u);
    }

    [Fact]
    public void Filter_Clear_UnhidesAllRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;

        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Header"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sid, 3, 1), new TextValue("Banana"));

        var range = new GridRange(
            new CellAddress(sid, 1, 1),
            new CellAddress(sid, 3, 1));

        // First apply a filter
        var filterCmd = new FilterCommand(sid, range, filterColOffset: 0, allowedValues: ["Apple"]);
        filterCmd.Apply(ctx);
        sheet.HiddenRows.Should().NotBeEmpty();

        // Then clear it
        var clearCmd = new FilterCommand(sid, range, filterColOffset: 0, allowedValues: []);
        clearCmd.Apply(ctx);

        sheet.HiddenRows.Should().BeEmpty();
    }

    // ── New edge-case tests ───────────────────────────────────────────────────

    [Fact]
    public void Sort_MultiColumn_RevertRestoresAllColumns()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;

        // Row 1: A1="X", B1="Y"
        // Row 2: A2="A", B2="2"
        // Row 3: A3="B", B3="1"
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("X"));
        sheet.SetCell(new CellAddress(sid, 1, 2), new TextValue("Y"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sid, 2, 2), new TextValue("2"));
        sheet.SetCell(new CellAddress(sid, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sid, 3, 2), new TextValue("1"));

        var range = new GridRange(
            new CellAddress(sid, 1, 1),
            new CellAddress(sid, 3, 2));

        var cmd = new SortCommand(sid, range, sortByColOffset: 0, ascending: true);
        cmd.Apply(ctx);

        // After ascending sort by col A: [A,2], [B,1], [X,Y]
        sheet.GetValue(new CellAddress(sid, 1, 1)).Should().Be(new TextValue("A"));
        sheet.GetValue(new CellAddress(sid, 1, 2)).Should().Be(new TextValue("2"));
        sheet.GetValue(new CellAddress(sid, 2, 1)).Should().Be(new TextValue("B"));
        sheet.GetValue(new CellAddress(sid, 2, 2)).Should().Be(new TextValue("1"));
        sheet.GetValue(new CellAddress(sid, 3, 1)).Should().Be(new TextValue("X"));
        sheet.GetValue(new CellAddress(sid, 3, 2)).Should().Be(new TextValue("Y"));

        // Revert: original order restored in BOTH columns
        cmd.Revert(ctx);

        sheet.GetValue(new CellAddress(sid, 1, 1)).Should().Be(new TextValue("X"));
        sheet.GetValue(new CellAddress(sid, 1, 2)).Should().Be(new TextValue("Y"));
        sheet.GetValue(new CellAddress(sid, 2, 1)).Should().Be(new TextValue("A"));
        sheet.GetValue(new CellAddress(sid, 2, 2)).Should().Be(new TextValue("2"));
        sheet.GetValue(new CellAddress(sid, 3, 1)).Should().Be(new TextValue("B"));
        sheet.GetValue(new CellAddress(sid, 3, 2)).Should().Be(new TextValue("1"));
    }

    [Fact]
    public void Filter_Clear_DoesNotDestroyExternallyHiddenRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;

        // A1=Header, A2=Apple, A3=Banana
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Header"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sid, 3, 1), new TextValue("Banana"));

        // Externally hide row 5 (e.g. imported from XLSX) BEFORE applying any filter
        sheet.HiddenRows.Add(5u);

        var range = new GridRange(
            new CellAddress(sid, 1, 1),
            new CellAddress(sid, 3, 1));

        // Apply then clear filter on A1:A3
        var filterCmd = new FilterCommand(sid, range, filterColOffset: 0, allowedValues: ["Apple"]);
        filterCmd.Apply(ctx);

        var clearCmd = new FilterCommand(sid, range, filterColOffset: 0, allowedValues: []);
        clearCmd.Apply(ctx);

        // Row 5 must still be hidden — it was outside the filter's range
        sheet.HiddenRows.Should().Contain(5u);
        // Rows in filter range should be visible after clear
        sheet.HiddenRows.Should().NotContain(2u);
        sheet.HiddenRows.Should().NotContain(3u);
    }

    [Fact]
    public void Filter_ReplacesExistingFilterInRange()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;

        // A1=Header, A2=Apple, A3=Banana, A4=Apple
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Header"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sid, 3, 1), new TextValue("Banana"));
        sheet.SetCell(new CellAddress(sid, 4, 1), new TextValue("Apple"));

        var range = new GridRange(
            new CellAddress(sid, 1, 1),
            new CellAddress(sid, 4, 1));

        // First filter: show only Apple → Banana (row 3) hidden
        var appleCmd = new FilterCommand(sid, range, filterColOffset: 0, allowedValues: ["Apple"]);
        appleCmd.Apply(ctx);
        sheet.HiddenRows.Should().Contain(3u);

        // Second filter: show only Banana → Apple rows (2, 4) hidden, Banana (row 3) visible
        var bananaCmd = new FilterCommand(sid, range, filterColOffset: 0, allowedValues: ["Banana"]);
        bananaCmd.Apply(ctx);

        sheet.HiddenRows.Should().Contain(2u);
        sheet.HiddenRows.Should().Contain(4u);
        sheet.HiddenRows.Should().NotContain(3u);
    }
}
