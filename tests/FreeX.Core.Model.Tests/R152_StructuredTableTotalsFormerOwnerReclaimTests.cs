using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

// B4-fmlstructuredref-totals-formerowner-reclaim: R151 taught RefreshStructuredTableTotalsCommand
// to detect when a column's live header text duplicates an EARLIER column's CURRENT live header
// text, and fall back to a direct data-body range instead of an ambiguous structured reference.
// But StructuredReferenceResolver.FindColumnIndex has a second pass (R141's "former owner reclaim"
// rule): a column can reclaim a selector by its STORED (pre-rename) Name even when no column's
// CURRENT live header text collides with anyone else's at all. R151's fix only ever compared live
// header text across columns, so it could not see this collision.
//
// Reproduction: column A was "Sales", live-renamed to "Revenue" (its stored Name is still
// "Sales" -- there is no rename command, only an ordinary header-cell edit that never syncs the
// model). Column B was "Region", live-renamed to reuse the text "Sales". Right now no two columns'
// LIVE header texts match each other (A shows "Revenue", B shows "Sales") -- R151's check sees
// nothing wrong. But FindColumnIndex("Sales") does not return B: B is the live match, yet B's own
// stored Name ("Region") no longer equals its live text ("Sales"), so the resolver's first-pass
// immediate-return shortcut does not apply; its second pass then finds column A first, because A's
// stored Name ("Sales") still equals the searched text while A's own live text differs from it (a
// "former owner"). So B's regenerated SUBTOTAL(n,[Sales]) would silently resolve to A's data.
public sealed class R152_StructuredTableTotalsFormerOwnerReclaimTests
{
    private static void SeedTotalsTable(Sheet sheet)
    {
        // Columns start out with distinct headers matching their stored model Names below.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("South"));
    }

    private static StructuredTableModel BuildSalesTable(Sheet sheet) => new()
    {
        Id = 5,
        Name = "SalesTable",
        DisplayName = "SalesTable",
        Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
        TotalsRowShown = true,
        Columns =
        {
            new StructuredTableColumnModel(1, "Sales", TotalsRowFunction: "sum"),
            new StructuredTableColumnModel(2, "Region", TotalsRowFunction: "count")
        }
    };

    [Fact]
    public void RefreshStructuredTableTotalsCommand_ColumnReclaimsFormerOwnersStoredName_ComputesOwnColumnsAggregate()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTotalsTable(sheet);
        var table = BuildSalesTable(sheet);
        sheet.StructuredTables.Add(table);

        // Two ordinary header-cell edits (FreeX has no rename command -- see R94/R141/R151):
        // column A moves off its own stored name, and column B moves onto the text A just vacated.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Revenue"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));

        // No two columns' LIVE header text collide right now -- A shows "Revenue", B shows "Sales".
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Revenue"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new TextValue("Sales"));

        var ctx = new TestCommandContext(wb);
        var outcome = new RefreshStructuredTableTotalsCommand(sheet.Id, table.Id).Apply(ctx);

        outcome.Success.Should().BeTrue();

        // Column A resolves to itself under FindColumnIndex's pass-2 fallback (no former owner of
        // "Revenue" exists), so it is unaffected and keeps the plain structured reference.
        sheet.GetCell(4, 1)!.FormulaText.Should().Be("SUBTOTAL(109,[Revenue])");

        // Column B's totals formula must not be the ambiguous [Sales] selector -- FindColumnIndex's
        // former-owner pass would silently redirect it to column A (the original "Sales" owner)
        // instead of column B, which merely reuses that text today.
        var columnBFormula = sheet.GetCell(4, 2)!.FormulaText;
        columnBFormula.Should().NotBe("SUBTOTAL(103,[Sales])");
        columnBFormula.Should().Be("SUBTOTAL(103,B2:B3)");

        // Prove the collision is real at evaluation time, not just at the string level: resolving
        // the literal ambiguous selector SUBTOTAL(109,[Sales]) (A's own SUM aggregate, written from
        // B's position) lands on column A's SUM (30) via FindColumnIndex's former-owner reclaim --
        // confirming both the resolver's actual behavior and this command's detection of it agree,
        // and that column B's real, non-ambiguous formula correctly avoids landing there too.
        var evaluator = new FormulaEvaluator();
        var columnAFormula = sheet.GetCell(4, 1)!.FormulaText!;
        var columnAValue = evaluator.Evaluate(columnAFormula, sheet, wb, new CellAddress(sheet.Id, 4, 1));
        var columnBValue = evaluator.Evaluate(columnBFormula, sheet, wb, new CellAddress(sheet.Id, 4, 2));
        var ambiguousSalesSumValue = evaluator.Evaluate("SUBTOTAL(109,[Sales])", sheet, wb, new CellAddress(sheet.Id, 4, 2));

        columnAValue.Should().Be(new NumberValue(30));
        columnBValue.Should().Be(new NumberValue(2));
        columnBValue.Should().NotBe(columnAValue);
        ambiguousSalesSumValue.Should().Be(columnAValue);
    }

    [Fact]
    public void RefreshStructuredTableTotalsCommand_RenamedToUniqueTextWithNoFormerOwner_StillUsesStructuredReferenceAsBefore()
    {
        // No-regression sibling: both columns are renamed, but to texts that no OTHER column ever
        // owned (no former-owner collision exists for either). Renaming alone must not be treated
        // as unsafe -- only a genuine reclaim collision should fall back to a direct range.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTotalsTable(sheet);
        var table = BuildSalesTable(sheet);
        sheet.StructuredTables.Add(table);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Revenue"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Territory"));

        var ctx = new TestCommandContext(wb);
        var outcome = new RefreshStructuredTableTotalsCommand(sheet.Id, table.Id).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetCell(4, 1)!.FormulaText.Should().Be("SUBTOTAL(109,[Revenue])");
        sheet.GetCell(4, 2)!.FormulaText.Should().Be("SUBTOTAL(103,[Territory])");
    }
}
