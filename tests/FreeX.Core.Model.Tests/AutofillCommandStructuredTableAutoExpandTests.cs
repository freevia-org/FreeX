using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// autofill-series-F1: dragging the fill handle one row below (or one column right of) a
/// Structured Table's current data body must auto-expand the table's Range and propagate a
/// calculated column's formula into the new row -- the exact same N33/N34 effects a typed edit
/// already gets via EditCellsCommand -&gt; StructuredTableEditEffects.Apply (Commands.cs), and
/// which FreeXR12Q13Tests pins for the typed-edit path. AutofillCommand.Apply previously never
/// looked at sheet.StructuredTables at all, so a fill-handle drag wrote a plain, un-tabled value
/// and left the table's Range/calculated column untouched.
/// </summary>
public class AutofillCommandStructuredTableAutoExpandTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx, StructuredTableModel table) SetupTable()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        // Header row.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("B"));

        // Data rows 2-4: column A is a plain increasing numeric series, column B is a
        // calculated column ("=A2*2" row-shifted into every data row).
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromFormula("A2*2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), Cell.FromFormula("A3*2"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), Cell.FromFormula("A4*2"));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            HeaderRowCount = 1,
            Columns =
            {
                new StructuredTableColumnModel(1, "A"),
                new StructuredTableColumnModel(2, "B")
            }
        };
        // Mirrors what N34 (typed-edit calculated-column detection) or a native XLSX load would
        // already have persisted: column B's calculated-column formula, anchored to the first
        // data row.
        table.SetCalculatedColumnFormula(2, "A2*2");
        sheet.StructuredTables.Add(table);

        return (wb, sheet, new TestCommandContext(wb), table);
    }

    [Fact]
    public void FillHandleDragPastTableLastRow_AutoExpandsTableAndPropagatesCalculatedColumn()
    {
        var (_, sheet, ctx, table) = SetupTable();

        // Select A2:A4 (the last data row's numeric column), grab the fill handle, drag down one
        // row to A5 -- the everyday "extend my table with the fill handle" gesture.
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 4, 1));
        var fillRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 5, 1));

        var command = new AutofillCommand(sheet.Id, sourceRange, fillRange);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // The table's Range must have grown to include the new row.
        var grownTable = sheet.StructuredTables.Single(t => t.Id == table.Id);
        grownTable.Range.Should().Be(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)));

        // A5 got the fill handle's own series value (a plain +1 continuation of 2,3,4).
        sheet.GetValue(5, 1).Should().Be(new NumberValue(5));

        // B5 must be filled with the row-shifted calculated-column formula, not left blank.
        var b5 = sheet.GetCell(5, 2);
        b5.Should().NotBeNull();
        b5!.FormulaText.Should().Be("A5*2");

        // Undo must remove the auto-expanded row entirely and shrink the table back.
        command.Revert(ctx);

        sheet.GetCell(5, 1).Should().BeNull();
        sheet.GetCell(5, 2).Should().BeNull();
        var revertedTable = sheet.StructuredTables.Single(t => t.Id == table.Id);
        revertedTable.Range.Should().Be(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)));
    }

    /// <summary>
    /// Sibling no-regression case: a fill-handle drag that lands INSIDE the table's existing data
    /// body (not past its last row) must not resize the table or otherwise perturb it -- only the
    /// exact N33 adjacency gesture triggers auto-expand, matching
    /// StructuredTableDesignCommandHelpers.TryGetAutoExpandRange's own boundary check.
    /// </summary>
    [Fact]
    public void FillHandleDragWithinTableBounds_DoesNotResizeTable()
    {
        var (_, sheet, ctx, table) = SetupTable();

        // Overwrite A3:A4 (already inside the table) by filling down from A2 -- an ordinary
        // in-place fill, not an extend-the-table gesture.
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));
        var fillRange = new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 4, 1));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var unchangedTable = sheet.StructuredTables.Single(t => t.Id == table.Id);
        unchangedTable.Range.Should().Be(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)));
        sheet.StructuredTables.Should().HaveCount(1);
    }
}
