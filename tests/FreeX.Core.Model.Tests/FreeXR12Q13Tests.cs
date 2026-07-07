using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round-12 fix bucket Q13 — focused regression test for the adversarially-verified finding:
///   R12-xlsx-tables-1: auto-expanding a structured table filled a calculated column's formula
///   into new rows VERBATIM (the source row's raw text), instead of row-shifting it the way Excel
///   does. Table1 spans A1:C4 (header row 1, data rows 2-4). Typing "=A2*B2" into C2 auto-fills
///   the rest of the calculated column (C3="A3*B3", C4="A4*B4") via N34 propagation. Typing a
///   value into A5 (the row directly below the table) auto-expands the table (N33) and must fill
///   C5 with "A5*B5" — not the frozen source-row text "A2*B2".
/// </summary>
public class FreeXR12Q13Tests
{
    [Fact]
    public void AutoExpand_FillsCalculatedColumnFormula_RowShiftedIntoNewRow()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        // Header row.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("C"));

        // Data rows 2-4: A/B are plain numbers, C starts blank (to be typed as a formula).
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(6));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(7));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3)),
            HeaderRowCount = 1,
            Columns =
            {
                new StructuredTableColumnModel(1, "A"),
                new StructuredTableColumnModel(2, "B"),
                new StructuredTableColumnModel(3, "C")
            }
        };
        sheet.StructuredTables.Add(table);

        var ctx = new TestCommandContext(wb);

        // Type "=A2*B2" into C2 -- N34 detects the calculated column (C3/C4 are blank) and
        // propagates the row-shifted formula into C3/C4, persisting the column's
        // CalculatedColumnFormula for future auto-expand fills.
        var typeC2 = EditCellsCommand.ForFormula(sheet.Id, new CellAddress(sheet.Id, 2, 3), "A2*B2");
        typeC2.Apply(ctx).Success.Should().BeTrue();

        sheet.GetCell(3, 3)!.FormulaText.Should().Be("A3*B3");
        sheet.GetCell(4, 3)!.FormulaText.Should().Be("A4*B4");

        // Type a value into A5, directly below the table -- N33 auto-expands Table1 to A1:C5 and
        // fills the grown calculated column. Excel writes "=A5*B5" here (row-shifted from the
        // anchor row), never the frozen source-row text "=A2*B2".
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new NumberValue(8));
        var typeA5 = EditCellsCommand.ForValue(sheet.Id, new CellAddress(sheet.Id, 5, 1), new NumberValue(8));
        var outcome = typeA5.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        var newCell = sheet.GetCell(5, 3);
        newCell.Should().NotBeNull();
        newCell!.FormulaText.Should().Be("A5*B5");

        // Undo must remove the auto-expanded row's calculated cell and restore the table shape.
        typeA5.Revert(ctx);

        sheet.GetCell(5, 3).Should().BeNull();
    }
}
