using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R175-auditA-F1 regression: <see cref="ApplyStructuredTableStyleCommand.Apply"/> loops over its
/// per-region <c>ApplyStyleCommand</c> children (header row, each striped data row, column
/// stripes, totals row, first/last column) with no try/catch. Each child mutates cell.StyleId in
/// its own per-cell loop, so a child that THROWS mid-loop (not merely returns a failed
/// CommandOutcome) is never added to <c>_appliedStyleCommands</c> -- the same shape
/// <see cref="CompositeWorkbookCommand"/> had before this round's fix. Before the fix, a throwing
/// second child left the table half-styled (the header region, styled by the first child,
/// permanently kept its new fill/bold) with no undo entry pushed (Apply returned a failed
/// outcome). After the fix, the whole table-style application rolls back to exactly its
/// pre-Apply state.
/// </summary>
public sealed class R175_ApplyStructuredTableStyleCommandRollbackTests
{
    /// <summary>
    /// Delegates every call to the real workbook except the Nth call to <see cref="GetSheet"/>
    /// (1-based, counting across the WHOLE Apply/Revert flow), which throws instead -- simulating
    /// a real child command (here, the second ApplyStyleCommand yielded by BuildStyleCommands,
    /// for the table's first data row) throwing partway through its own Apply, before this test's
    /// production code path gets a chance to add it to _appliedStyleCommands.
    /// </summary>
    private sealed class ThrowOnNthGetSheetCommandContext(Workbook workbook, int throwOnCall) : ICommandContext
    {
        private int _calls;

        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId)
        {
            _calls++;
            if (_calls == throwOnCall)
                throw new InvalidOperationException($"boom mid-loop (GetSheet call #{_calls})");

            return Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
        }
    }

    private static (Workbook Wb, Sheet Sheet, StructuredTableModel Table) BuildHeaderPlusTwoDataRowsTable()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Open"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Closed"));
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            HeaderRowCount = 1,
            TotalsRowShown = false,
            ShowFirstColumn = false,
            ShowLastColumn = false,
            ShowRowStripes = true,
            ShowColumnStripes = false,
            StyleName = "TableStyleLight1",
            Columns =
            {
                new StructuredTableColumnModel(1, "Region"),
                new StructuredTableColumnModel(2, "Status")
            }
        };
        sheet.StructuredTables.Add(table);
        return (wb, sheet, table);
    }

    [Fact]
    public void Apply_RevertsHeaderStylingWhenSecondDataRowChildThrowsMidLoop()
    {
        // R175-auditA-F1 regression: BuildStyleCommands yields, in order: [0] the header row
        // style command, [1] the first data row (row 2) style command, [2] the second data row
        // (row 3) style command. Rig the context so the SECOND child overall (the first data
        // row's ApplyStyleCommand.Apply) throws the instant it asks for the sheet -- before this
        // production loop's own try/catch existed, that exception unwound out of
        // ApplyStructuredTableStyleCommand.Apply entirely, skipping RevertAppliedCommands, so the
        // header child's already-applied fill/bold/font-color permanently stuck.
        var (wb, sheet, table) = BuildHeaderPlusTwoDataRowsTable();

        // GetSheet call sequence for this Apply attempt:
        //   #1 ApplyStructuredTableStyleCommand.Apply's own sheet fetch
        //   #2 ConfigureStructuredTableStyleOptionsCommand.Apply's sheet fetch
        //   #3 header ApplyStyleCommand.Apply's sheet fetch (succeeds)
        //   #4 first-data-row ApplyStyleCommand.Apply's sheet fetch -- THROW HERE
        var ctx = new ThrowOnNthGetSheetCommandContext(wb, throwOnCall: 4);
        var command = new ApplyStructuredTableStyleCommand(
            sheet.Id,
            table.Id,
            new StructuredTableStyleBanding(
                HeaderFill: new CellColor(31, 78, 121),
                OddRowFill: new CellColor(222, 235, 247),
                EvenRowFill: CellColor.White,
                HeaderFontColor: CellColor.White),
            "TableStyleMedium2",
            updateStyleName: true);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("boom mid-loop");

        // The header row (styled by the FIRST child, which succeeded before the second child
        // threw) must be rolled back to its original, unstyled state -- not left half-applied.
        var headerStyle = wb.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 1, 1))!.StyleId);
        headerStyle.Should().Be(wb.GetStyle(StyleId.Default), "the header child's styling must be undone, not left applied");

        // The option-change sub-command (style name) must also be rolled back.
        sheet.StructuredTables.Single().StyleName.Should().Be(
            "TableStyleLight1",
            "the whole compound operation failed, so the style-name option change must revert too");
    }

    [Fact]
    public void Apply_StillAppliesAllRegionsWhenNoChildThrows()
    {
        // Sibling no-regression: the ordinary, fully-successful multi-child path (header + every
        // striped data row) must still apply completely -- the new try/catch must not interfere
        // with the happy path.
        var (wb, sheet, table) = BuildHeaderPlusTwoDataRowsTable();
        var ctx = new TestCommandContext(wb);
        var command = new ApplyStructuredTableStyleCommand(
            sheet.Id,
            table.Id,
            new StructuredTableStyleBanding(
                HeaderFill: new CellColor(31, 78, 121),
                OddRowFill: new CellColor(222, 235, 247),
                EvenRowFill: CellColor.White,
                HeaderFontColor: CellColor.White),
            "TableStyleMedium2",
            updateStyleName: true);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        var headerStyle = wb.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 1, 1))!.StyleId);
        headerStyle.FillColor.Should().Be(new CellColor(31, 78, 121));
        headerStyle.Bold.Should().BeTrue();
        // Row offset 0 (the first data row, row 2) is even -> EvenRowFill (white); row offset 1
        // (row 3) is odd -> OddRowFill. Check the odd-striped row for the non-white banding color.
        var secondDataRowStyle = wb.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 3, 1))!.StyleId);
        secondDataRowStyle.FillColor.Should().Be(new CellColor(222, 235, 247));
        sheet.StructuredTables.Single().StyleName.Should().Be("TableStyleMedium2");

        command.Revert(ctx);

        wb.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 1, 1))!.StyleId).Should().Be(wb.GetStyle(StyleId.Default));
        sheet.StructuredTables.Single().StyleName.Should().Be("TableStyleLight1");
    }
}
