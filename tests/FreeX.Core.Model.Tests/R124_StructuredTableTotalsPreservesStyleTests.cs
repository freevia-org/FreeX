using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R124: RefreshStructuredTableTotalsCommand.Apply used to unconditionally overwrite the
/// totals-row cell with a freshly-constructed Cell (Cell.FromFormula/Cell.FromValue), both of
/// which default to StyleId.Default -- silently resetting any pre-existing formatting on that
/// cell (bold, fill/shading, borders, a custom number format such as currency) every time the
/// totals row is (re)computed. This strips formatting on a table auto-expand (typing into the
/// row/column adjacent to an existing table with an active totals row -- the ordinary path a
/// table loaded from a real Excel file with totalsRowFunction set hits on its very first edit),
/// matching real Excel keeping the Totals Row's own formatting intact as it relocates. Fixed by
/// preserving the destination cell's existing StyleId, mirroring
/// PropagateCalculatedColumnCommand.Apply's identical-bug-class fix (Commands.cs ~530-536).
/// </summary>
public sealed class R124_StructuredTableTotalsPreservesStyleTests
{
    private static StyleId SeedBoldFillStyle(Workbook workbook, Sheet sheet, uint row, uint col)
    {
        var styleId = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FillColor = new CellColor(0xD0, 0xE0, 0xFF)
        });
        sheet.GetCell(new CellAddress(sheet.Id, row, col))!.StyleId = styleId;
        return styleId;
    }

    private static void SeedTotalsTable(Sheet sheet)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(15));
    }

    // ── fail-before / pass-after: direct RefreshStructuredTableTotalsCommand entry point ──

    [Fact]
    public void RefreshStructuredTableTotalsCommand_PreservesExistingBoldFillStyleOnFormulaCell()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTotalsTable(sheet);
        // Row 4 is the totals row; seed it (as if loaded from a real Excel file whose totals row
        // already carries bold + shaded "Total Row" table-style formatting) before refreshing.
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Total"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), BlankValue.Instance);
        var expectedStyle = SeedBoldFillStyle(wb, sheet, 4, 2);

        var table = new StructuredTableModel
        {
            Id = 9,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            TotalsRowShown = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region", TotalsRowLabel: "Total"),
                new StructuredTableColumnModel(2, "Sales", TotalsRowFunction: "sum")
            }
        };
        sheet.StructuredTables.Add(table);
        var ctx = new TestCommandContext(wb);
        var command = new RefreshStructuredTableTotalsCommand(sheet.Id, table.Id);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        var totalsCell = sheet.GetCell(4, 2);
        totalsCell.Should().NotBeNull();
        totalsCell!.FormulaText.Should().Be("SUBTOTAL(109,[Sales])");
        // This is the assertion that failed before the fix: StyleId was silently reset to
        // StyleId.Default (wiping the bold/fill formatting) even though the underlying computed
        // content (the SUBTOTAL formula) was correct.
        totalsCell.StyleId.Should().Be(expectedStyle);
    }

    [Fact]
    public void RefreshStructuredTableTotalsCommand_PreservesExistingStyleOnLabelCell()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTotalsTable(sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), BlankValue.Instance);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), BlankValue.Instance);
        var expectedStyle = SeedBoldFillStyle(wb, sheet, 4, 1);

        var table = new StructuredTableModel
        {
            Id = 9,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            TotalsRowShown = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region", TotalsRowLabel: "Total"),
                new StructuredTableColumnModel(2, "Sales", TotalsRowFunction: "sum")
            }
        };
        sheet.StructuredTables.Add(table);
        var ctx = new TestCommandContext(wb);

        new RefreshStructuredTableTotalsCommand(sheet.Id, table.Id).Apply(ctx).Success.Should().BeTrue();

        var labelCell = sheet.GetCell(4, 1);
        labelCell!.Value.Should().Be(new TextValue("Total"));
        labelCell.StyleId.Should().Be(expectedStyle);
    }

    // ── no-regression: blank branch (no totalsRowFunction/label/formula) still preserves a
    // ── style-only override that was carried on an otherwise-empty totals cell ──

    [Fact]
    public void RefreshStructuredTableTotalsCommand_PreservesStyleOnlyOverrideWhenColumnHasNoTotalsContent()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTotalsTable(sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Total"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), BlankValue.Instance);

        // The 3rd column has no totalsRowFunction/label/formula, so ResolveTotalsCell returns
        // null and the cell should stay blank -- but a style-only override sitting on that empty
        // cell (e.g. banding fill baked on by ApplyStructuredTableStyleCommand) must survive.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Notes"));
        // Rows 2-4 of the "Notes" column are left with no Cell object at all -- row 4 (the
        // totals row) carries only a style-only override, matching a blank-but-styled cell
        // (e.g. banding fill baked on by ApplyStructuredTableStyleCommand).
        var styleOnlyId = wb.RegisterStyle(new CellStyle { Bold = true });
        sheet.SetStyleOnly(4, 3, styleOnlyId);

        var table = new StructuredTableModel
        {
            Id = 9,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3)),
            TotalsRowShown = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region", TotalsRowLabel: "Total"),
                new StructuredTableColumnModel(2, "Sales", TotalsRowFunction: "sum"),
                new StructuredTableColumnModel(3, "Notes")
            }
        };
        sheet.StructuredTables.Add(table);
        var ctx = new TestCommandContext(wb);

        var outcome = new RefreshStructuredTableTotalsCommand(sheet.Id, table.Id).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        var notesCell = sheet.GetCell(4, 3);
        notesCell.Should().NotBeNull();
        notesCell!.Value.Should().Be(BlankValue.Instance);
        notesCell.StyleId.Should().Be(styleOnlyId);
    }

    // ── combination: the real table-auto-expand path (ResizeStructuredTableCommand relocating
    // ── the totals row) also must not strip the new totals cell's pre-existing formatting ──

    [Fact]
    public void ResizeStructuredTableCommand_GrowingTableWithShownTotalsRowPreservesNewTotalsCellStyle()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTotalsTable(sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Total"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), BlankValue.Instance);

        // Row 5 is where the user types (auto-expand grows the table to include it), and it
        // already carries a manually-applied style before the type-in/resize -- e.g. a currency
        // number format matching the rest of the "Sales" column, exactly as a normal user-typed
        // row (via EditCellsCommand, which preserves the destination style) would.
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(30));
        var preExistingStyle = SeedBoldFillStyle(wb, sheet, 5, 2);

        var table = new StructuredTableModel
        {
            Id = 9,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            TotalsRowShown = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region", TotalsRowLabel: "Total"),
                new StructuredTableColumnModel(2, "Sales", TotalsRowFunction: "sum")
            }
        };
        sheet.StructuredTables.Add(table);
        var ctx = new TestCommandContext(wb);
        var newRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2));
        var command = new ResizeStructuredTableCommand(sheet.Id, table.Id, newRange);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        var resized = sheet.StructuredTables.Should().ContainSingle().Subject;
        resized.Range.End.Row.Should().Be(5);

        // The new totals row (now row 5) must hold the live SUBTOTAL formula...
        var newTotalsCell = sheet.GetCell(5, 2);
        newTotalsCell.Should().NotBeNull();
        newTotalsCell!.FormulaText.Should().Be("SUBTOTAL(109,[Sales])");
        // ...while keeping the formatting that was already sitting on that cell, not reset to
        // StyleId.Default.
        newTotalsCell.StyleId.Should().Be(preExistingStyle);
    }
}
