using System.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R123-calculated-column-propagation-formatting: PropagateCalculatedColumnCommand.Apply (the N34
/// sub-command EditCellsCommand runs when a user's first formula in a table column qualifies as a
/// calculated column) wrote every sibling data-body row via
/// <c>sheet.SetCell(address, Cell.FromFormula(shiftedFormula))</c> with no attempt to preserve that
/// row's existing style. Cell.FromFormula always defaults StyleId to StyleId.Default, and
/// Sheet.SetCell(CellAddress, Cell) unconditionally calls ClearStyleOnly on the address it writes
/// to -- so a blank sibling row that carried nothing but a style-only override (exactly what
/// ApplyStructuredTableStyleCommand bakes onto every data-body row of a freshly Ctrl+T-created
/// table -- the banding stripe fill) had that formatting silently discarded the instant the user
/// typed a formula into any OTHER row of the same calculated column. Real Excel's calculated-column
/// autofill only ever changes the formula; it never touches existing fill/number-format/borders.
/// <para>
/// This mirrors the guard EditCellsCommand.Apply already applies for the one row the user actually
/// typed into (Commands.cs ~102-117: reattach oldCell.StyleId, or the blank cell's GetStyleOnly,
/// before calling SetCell) -- PropagateCalculatedColumnCommand never received that guard for the
/// sibling rows it fills.
/// </para>
/// </summary>
public sealed class R123_CalculatedColumnPropagationPreservesRowFormattingTests
{
    private const string CustomNumberFormat = "0.00%";

    private static (Workbook Workbook, Sheet Sheet, ICommandContext Context) Setup()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet, new TestCommandContext(workbook));
    }

    private static StructuredTableModel LiveTable(Sheet sheet, int tableId) =>
        sheet.StructuredTables.Single(t => t.Id == tableId);

    // Table1 A1:B4 (header row 1; data rows 2-4). Column A has plain values in every data row.
    // Column B is blank in every data row, but rows 3 and 4 carry a style-only override (StyleId)
    // simulating the banding fill ApplyStructuredTableStyleCommand bakes onto every blank
    // data-body cell of a freshly Ctrl+T-created table, BEFORE any formula gets typed in.
    private static void BuildTableWithBandedBlankSiblings(Workbook wb, Sheet sheet, StyleId bandingStyle)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell { Value = new TextValue("Value") });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new Cell { Value = new TextValue("Double") });
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new Cell { Value = new NumberValue(1) });
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new Cell { Value = new NumberValue(2) });
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new Cell { Value = new NumberValue(3) });

        // Rows 3 and 4's column-B cells are blank but style-only-banded (nothing in _cells yet).
        sheet.SetStyleOnly(3, 2, bandingStyle);
        sheet.SetStyleOnly(4, 2, bandingStyle);

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            Columns =
            {
                new StructuredTableColumnModel(1, "Value"),
                new StructuredTableColumnModel(2, "Double")
            }
        };
        sheet.StructuredTables.Add(table);
    }

    [Fact]
    public void TypingCalculatedColumnFormula_PreservesBandingStyleOnBlankSiblingRows()
    {
        var (wb, sheet, ctx) = Setup();
        var bandingStyle = wb.RegisterStyle(new CellStyle { NumberFormat = CustomNumberFormat });
        BuildTableWithBandedBlankSiblings(wb, sheet, bandingStyle);

        // Type "=A2*2" into row 2's column B -- the only non-blank cell in the calculated column,
        // triggering TryCreateCalculatedColumnPropagation/PropagateCalculatedColumnCommand to fill
        // rows 3 and 4.
        var edit = new EditCellsCommand(sheet.Id, [(new CellAddress(sheet.Id, 2, 2), Cell.FromFormula("A2*2"))]);
        edit.Apply(ctx).Success.Should().BeTrue();

        LiveTable(sheet, 1).Columns[1].CalculatedColumnFormula.Should().Be("A2*2");

        var row3 = sheet.GetCell(new CellAddress(sheet.Id, 3, 2));
        var row4 = sheet.GetCell(new CellAddress(sheet.Id, 4, 2));
        row3!.FormulaText.Should().Be("A3*2", "the propagated formula must still be row-shifted correctly");
        row4!.FormulaText.Should().Be("A4*2");

        // The fix under test: the pre-existing banding style-only override must survive the
        // propagation instead of being silently reset to StyleId.Default.
        wb.GetStyle(row3.StyleId).NumberFormat.Should().Be(CustomNumberFormat,
            "PropagateCalculatedColumnCommand must not discard a blank sibling row's pre-existing style-only formatting");
        wb.GetStyle(row4.StyleId).NumberFormat.Should().Be(CustomNumberFormat,
            "PropagateCalculatedColumnCommand must not discard a blank sibling row's pre-existing style-only formatting");

        // Undo must restore the original blank-but-style-only-banded state exactly.
        edit.Revert(ctx);
        sheet.GetCell(new CellAddress(sheet.Id, 3, 2)).Should().BeNull("row 3 must go back to being a blank cell");
        sheet.GetCell(new CellAddress(sheet.Id, 4, 2)).Should().BeNull("row 4 must go back to being a blank cell");
        sheet.GetStyleOnly(3, 2).Should().Be(bandingStyle,
            "the style-only banding override must be restored on revert, not left cleared");
        sheet.GetStyleOnly(4, 2).Should().Be(bandingStyle,
            "the style-only banding override must be restored on revert, not left cleared");
    }

    // Sibling/regression case: a sibling row that already carries a real (non-blank) formula cell
    // with its own custom style -- e.g. re-typing/re-confirming the same formula in row 2 after
    // rows 3-4 were already filled and individually formatted -- must also keep its own style, not
    // just the special blank/style-only case above.
    [Fact]
    public void TypingCalculatedColumnFormula_PreservesStyleOnNonBlankMatchingSiblingRow()
    {
        var (wb, sheet, ctx) = Setup();
        var customStyle = wb.RegisterStyle(new CellStyle { NumberFormat = CustomNumberFormat });

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell { Value = new TextValue("Value") });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new Cell { Value = new TextValue("Double") });
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new Cell { Value = new NumberValue(1) });
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new Cell { Value = new NumberValue(2) });

        // Row 3's column B already holds the row-shifted formula "A3*2" (matching what row 2 will
        // produce), with its own custom number-format style that must survive re-propagation.
        var row3Cell = Cell.FromFormula("A3*2");
        row3Cell.StyleId = customStyle;
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), row3Cell);

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            Columns =
            {
                new StructuredTableColumnModel(1, "Value"),
                new StructuredTableColumnModel(2, "Double")
            }
        };
        sheet.StructuredTables.Add(table);

        var edit = new EditCellsCommand(sheet.Id, [(new CellAddress(sheet.Id, 2, 2), Cell.FromFormula("A2*2"))]);
        edit.Apply(ctx).Success.Should().BeTrue();

        var row3 = sheet.GetCell(new CellAddress(sheet.Id, 3, 2));
        row3!.FormulaText.Should().Be("A3*2");
        wb.GetStyle(row3.StyleId).NumberFormat.Should().Be(CustomNumberFormat,
            "a sibling row that already carried its own custom style must keep it across re-propagation");

        edit.Revert(ctx);
        var revertedRow3 = sheet.GetCell(new CellAddress(sheet.Id, 3, 2));
        revertedRow3!.StyleId.Should().Be(customStyle, "undo must restore row 3's exact prior cell, style included");
    }
}
