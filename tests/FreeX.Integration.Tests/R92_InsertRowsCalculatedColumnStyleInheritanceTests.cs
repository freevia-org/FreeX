using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R92-render-cellstyle-inheritance-5-2 (structured-table interaction): InsertRowsCommand's
/// row-above format inheritance (RowColumnShiftHelpers.InheritVacatedRowFormatFromAbove) plants a
/// style-only entry on every blank cell of the newly-inserted row -- including cells that fall
/// inside a structured table's calculated column. FillGrownCalculatedColumnsForInsertedRows then
/// auto-fills that same cell with a real formula Cell a few lines later
/// (R26-table-structured-ref-deep-2), and Sheet.SetCell unconditionally clears any style-only entry
/// at the address it writes to -- so without carrying the inherited style onto the newly-created
/// formula cell, the just-applied row format would be silently discarded the instant the
/// calculated-column auto-fill runs.
/// <para>
/// The assertions use NumberFormat (rather than fill/border/Bold) because
/// StructuredTableStyleService.RebandTable also runs immediately afterward and merges the table's
/// banding fill/border onto every body cell (MergeStyleOntoCell) -- NumberFormat is never touched
/// by that merge for header, totals, or body cells, so it isolates this fix's effect from the
/// unrelated (and, for this table, intentional) banding reflow.
/// </para>
/// </summary>
public sealed class R92_InsertRowsCalculatedColumnStyleInheritanceTests
{
    private const string CustomNumberFormat = "0.00%";

    private static (Workbook Workbook, Sheet Sheet, ICommandContext Context) Setup()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet, new TestCommandContext(workbook));
    }

    // Table1 spans A1:B4 (A1:B1 header; column B is a calculated column holding "A2*2"), rows 2-4.
    private static void BuildCalculatedColumnTable(Workbook wb, Sheet sheet, StyleId? row2Style)
    {
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell { Value = new TextValue("Category") });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new Cell { Value = new TextValue("Double") });
        var a2Cell = new Cell { Value = new NumberValue(1) };
        var b2Cell = Cell.FromFormula("A2*2");
        if (row2Style is { } style)
        {
            a2Cell.StyleId = style;
            b2Cell.StyleId = style;
        }
        sheet.SetCell(a2, a2Cell);
        sheet.SetCell(b2, b2Cell);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new Cell { Value = new NumberValue(2) });
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), Cell.FromFormula("A3*2"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new Cell { Value = new NumberValue(3) });
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), Cell.FromFormula("A4*2"));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            Columns =
            {
                new StructuredTableColumnModel(1, "Category"),
                new StructuredTableColumnModel(2, "Double", CalculatedColumnFormula: "A2*2")
            }
        };
        sheet.StructuredTables.Add(table);
    }

    [Fact]
    public void InsertRowInsideTableBody_CalculatedColumnFill_KeepsRowAboveInheritedNumberFormat()
    {
        var (wb, sheet, ctx) = Setup();
        var customFormatStyle = wb.RegisterStyle(new CellStyle { NumberFormat = CustomNumberFormat });
        BuildCalculatedColumnTable(wb, sheet, customFormatStyle);

        var command = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 1);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        // The calculated column's auto-fill (R26) must still happen...
        var newCalcCell = new CellAddress(sheet.Id, 3, 2);
        var filledCell = sheet.GetCell(newCalcCell);
        filledCell!.FormulaText.Should().Be("A3*2");

        // ...but the row-above format inheritance (this fix) must survive both the auto-fill AND the
        // subsequent table-banding reflow (RebandTable), instead of being silently discarded the
        // instant Sheet.SetCell writes the formula cell (ClearStyleOnly side-effect).
        wb.GetStyle(filledCell.StyleId).NumberFormat.Should().Be(CustomNumberFormat,
            "the calculated-column auto-fill must not discard the row-above-inherited format");
    }

    [Fact]
    public void InsertRowInsideTableBody_NoRowAboveFormat_CalculatedColumnFillHasNoInheritedNumberFormat()
    {
        // No-regression sibling: row above has no custom number format -- the auto-filled formula
        // cell must not pick up CustomNumberFormat from thin air (banding may still give it some
        // other non-default style via RebandTable -- that part is unrelated to this fix).
        var (wb, sheet, ctx) = Setup();
        BuildCalculatedColumnTable(wb, sheet, row2Style: null);

        var command = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 1);
        command.Apply(ctx).Success.Should().BeTrue();

        var newCalcCell = new CellAddress(sheet.Id, 3, 2);
        var filledCell = sheet.GetCell(newCalcCell);
        filledCell!.FormulaText.Should().Be("A3*2");
        wb.GetStyle(filledCell.StyleId).NumberFormat.Should().NotBe(CustomNumberFormat);
    }
}
