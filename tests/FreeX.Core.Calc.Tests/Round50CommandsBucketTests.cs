using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Round-50 commands-bucket fixes:
/// - R50-commands-name-manager-crud-3-1: DefineNamedRangeCommand's new-name uniqueness check must
///   also reject a collision with an existing named FORMULA of the same name/scope, not just an
///   existing named range.
/// - R50-io-table-totals-calc-3-2: hiding a table's totals row must capture whatever is actually in
///   the totals-row cell (e.g. a manually-edited aggregate) back into the column's totals
///   definition, so re-showing it reproduces the edit instead of the stale original aggregate.
/// - R50-io-table-totals-calc-3-3: resizing/auto-expanding a table must preserve every surviving
///   column's original Id instead of renumbering every column sequentially by position.
/// </summary>
public sealed class Round50CommandsBucketTests
{
    // ── R50-commands-name-manager-crud-3-1 ────────────────────────────────────

    [Fact]
    public void DefineNamedRangeCommand_NewName_RejectsCollisionWithExistingNamedFormula()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        wb.NamedFormulas["Rate"] = "0.05";

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 1));
        var command = new DefineNamedRangeCommand("Rate", range, allowRedefine: false);
        var ctx = new TestCommandContext(wb);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse(
            "'Rate' already exists as a named formula in this scope, so the New Name dialog must reject it exactly as it would a colliding named range");
        wb.NamedFormulas.Should().ContainKey("Rate");
        wb.NamedFormulas["Rate"].Should().Be("0.05", "the pre-existing named formula must be left untouched by the rejected command");
        wb.NamedRanges.Should().NotContainKey("Rate", "no range definition should have been created for the colliding name");
    }

    [Fact]
    public void DefineNamedRangeCommand_NewName_NoCollision_StillSucceeds()
    {
        // Sibling no-regression: an ordinary brand-new name with no collision of either kind must
        // still be accepted.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 1));
        var command = new DefineNamedRangeCommand("Revenue", range, allowRedefine: false);
        var ctx = new TestCommandContext(wb);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        wb.NamedRanges.Should().ContainKey("Revenue");
        wb.NamedRanges["Revenue"].Should().Be(range);
    }

    // ── R50-io-table-totals-calc-3-2 ──────────────────────────────────────────

    [Fact]
    public void HideTotalsRow_ManuallyEditedAggregate_IsCapturedAndSurvivesShowAgain()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        // A1:A5 table ("Sales") with a totals row (row 5) whose column is defined as a Sum.
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            TotalsRowShown = true,
            Columns = { new StructuredTableColumnModel(1, "Amount", TotalsRowFunction: "sum") }
        };
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(30));
        // Totals cell was originally SUBTOTAL(109,[Amount]) (Sum) but the user directly edited it
        // to Average (SUBTOTAL(101,...)) — an ordinary cell edit that never touches the column model.
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), Cell.FromFormula("SUBTOTAL(101,[Amount])"));
        sheet.StructuredTables.Add(table);

        var ctx = new TestCommandContext(wb);

        var hide = new SetStructuredTableTotalsRowCommand(sheet.Id, table.Id, showTotalsRow: false);
        hide.Apply(ctx).Success.Should().BeTrue();

        var hiddenTable = sheet.StructuredTables.Single(t => t.Id == table.Id);
        hiddenTable.Columns[0].TotalsRowFunction.Should().Be(
            "average",
            "the manual Average edit must be captured into the column model when the totals row is hidden");

        var show = new SetStructuredTableTotalsRowCommand(sheet.Id, table.Id, showTotalsRow: true);
        show.Apply(ctx).Success.Should().BeTrue();

        var shownTable = sheet.StructuredTables.Single(t => t.Id == table.Id);
        var totalsRow = shownTable.Range.End.Row;
        sheet.GetCell(totalsRow, 1)!.FormulaText.Should().Be(
            "SUBTOTAL(101,[Amount])",
            "re-showing the totals row must reproduce the user's last Average edit, not revert to the stale original Sum");
    }

    [Fact]
    public void HideTotalsRow_UnmodifiedAggregate_RoundTripsUnchanged()
    {
        // Sibling no-regression: when the totals cell was never manually edited, hide+show must
        // still reproduce the same (Sum) aggregate as before.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            TotalsRowShown = true,
            Columns = { new StructuredTableColumnModel(1, "Amount", TotalsRowFunction: "sum") }
        };
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), Cell.FromFormula("SUBTOTAL(109,[Amount])"));
        sheet.StructuredTables.Add(table);

        var ctx = new TestCommandContext(wb);

        new SetStructuredTableTotalsRowCommand(sheet.Id, table.Id, showTotalsRow: false).Apply(ctx).Success.Should().BeTrue();
        new SetStructuredTableTotalsRowCommand(sheet.Id, table.Id, showTotalsRow: true).Apply(ctx).Success.Should().BeTrue();

        var shownTable = sheet.StructuredTables.Single(t => t.Id == table.Id);
        shownTable.Columns[0].TotalsRowFunction.Should().Be("sum");
        var totalsRow = shownTable.Range.End.Row;
        sheet.GetCell(totalsRow, 1)!.FormulaText.Should().Be("SUBTOTAL(109,[Amount])");
    }

    // ── R50-io-table-totals-calc-3-3 ──────────────────────────────────────────

    [Fact]
    public void ResizeStructuredTableCommand_PreservesNonContiguousColumnIds_WhenAutoExpanding()
    {
        // Simulates a table loaded from an XLSX where a column was deleted at some point in its
        // history, leaving tableColumn ids {1, 2, 4} (valid per ECMA-376: ids are never reused or
        // renumbered on deletion). A1:C3 table, 3 columns with those ids.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)),
            Columns =
            {
                new StructuredTableColumnModel(1, "Region"),
                new StructuredTableColumnModel(2, "Amount"),
                new StructuredTableColumnModel(4, "Notes"),
            }
        };
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Notes"));
        sheet.StructuredTables.Add(table);

        var ctx = new TestCommandContext(wb);

        // Auto-expand downward by one row (e.g. typing a value directly below the table).
        var newRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3));
        var command = new ResizeStructuredTableCommand(sheet.Id, table.Id, newRange);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        var resized = sheet.StructuredTables.Single(t => t.Id == table.Id);
        resized.Columns.Select(c => c.Id).Should().Equal(
            [1, 2, 4],
            "surviving columns must keep their original persisted ids, not be renumbered sequentially by position");
    }

    [Fact]
    public void ResizeStructuredTableCommand_NewColumn_GetsFreshIdPastHighestExisting()
    {
        // Sibling no-regression: a genuinely new column added by widening the table must get an id
        // that doesn't collide with any existing (possibly non-contiguous) id.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)),
            Columns =
            {
                new StructuredTableColumnModel(1, "Region"),
                new StructuredTableColumnModel(2, "Amount"),
                new StructuredTableColumnModel(4, "Notes"),
            }
        };
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Notes"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Extra"));
        sheet.StructuredTables.Add(table);

        var ctx = new TestCommandContext(wb);

        // Widen by one column (auto-expand rightward).
        var newRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 4));
        var command = new ResizeStructuredTableCommand(sheet.Id, table.Id, newRange);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        var resized = sheet.StructuredTables.Single(t => t.Id == table.Id);
        resized.Columns.Select(c => c.Id).Should().Equal([1, 2, 4, 5]);
    }
}
