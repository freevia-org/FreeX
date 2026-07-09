using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression test for R20-defined-name-eval-deep-3: DV List validation whose Formula1 is a bare
/// named-range reference must honor Excel's scope-precedence rule — a name scoped to the current
/// sheet always wins over a same-named workbook-global name, even when the sheet-scoped name is a
/// named FORMULA (not a plain range) and the global name is a plain range.
///
/// Before the fix, DataValidationService.ListSources.cs's fast path resolved the name via
/// Workbook.TryGetNamedRange(name, sheetId), which only consults ScopedNamedRanges (range-kind)
/// before falling back to the workbook-global NamedRanges dictionary — it never looks at
/// ScopedNamedFormulas. So a sheet-scoped named formula was invisible to it, and the shadowed
/// workbook-global range was returned as if it were the correct match.
/// </summary>
public class R20_dv_listsource_scope_Tests
{
    [Fact]
    public void GetListItems_SheetScopedNamedFormula_ShadowsWorkbookGlobalRange()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        // Workbook-global: Colors = Sheet2!A1:A3 = Red/Green/Blue.
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new TextValue("Red"));
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 1), new TextValue("Green"));
        sheet2.SetCell(new CellAddress(sheet2.Id, 3, 1), new TextValue("Blue"));
        workbook.DefineNamedRange(
            "Colors",
            new GridRange(new CellAddress(sheet2.Id, 1, 1), new CellAddress(sheet2.Id, 3, 1)));

        // Sheet1-scoped: Colors = OFFSET(Sheet1!$B$1,0,0,3,1) -> Cyan/Magenta/Yellow in B1:B3.
        // Must shadow the workbook-global range whenever evaluated in the context of Sheet1.
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 2), new TextValue("Cyan"));
        sheet1.SetCell(new CellAddress(sheet1.Id, 2, 2), new TextValue("Magenta"));
        sheet1.SetCell(new CellAddress(sheet1.Id, 3, 2), new TextValue("Yellow"));
        workbook.DefineNamedFormula("Colors", "OFFSET(Sheet1!$B$1,0,0,3,1)", sheet1.Id);

        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "=Colors",
            AppliesTo = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 1, 1)),
        };

        var items = DataValidationService.GetListItems(dv, sheet1, workbook);

        items.Should().BeEquivalentTo(new[] { "Cyan", "Magenta", "Yellow" });
    }

    [Fact]
    public void Validate_SheetScopedNamedFormula_ShadowsWorkbookGlobalRange()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new TextValue("Red"));
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 1), new TextValue("Green"));
        sheet2.SetCell(new CellAddress(sheet2.Id, 3, 1), new TextValue("Blue"));
        workbook.DefineNamedRange(
            "Colors",
            new GridRange(new CellAddress(sheet2.Id, 1, 1), new CellAddress(sheet2.Id, 3, 1)));

        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 2), new TextValue("Cyan"));
        sheet1.SetCell(new CellAddress(sheet1.Id, 2, 2), new TextValue("Magenta"));
        sheet1.SetCell(new CellAddress(sheet1.Id, 3, 2), new TextValue("Yellow"));
        workbook.DefineNamedFormula("Colors", "OFFSET(Sheet1!$B$1,0,0,3,1)", sheet1.Id);

        var address = new CellAddress(sheet1.Id, 1, 1);
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "=Colors",
            AppliesTo = new GridRange(address, address),
        };

        // A value from the shadowed workbook-global range must be REJECTED on Sheet1 — the
        // sheet-scoped named formula (Cyan/Magenta/Yellow) is the only valid source there.
        DataValidationService.Validate(dv, new TextValue("Red"), sheet1, address, workbook)
            .Should().NotBeNull();

        // A value from the sheet-scoped named formula's actual source must be ACCEPTED.
        DataValidationService.Validate(dv, new TextValue("Cyan"), sheet1, address, workbook)
            .Should().BeNull();
    }
}
