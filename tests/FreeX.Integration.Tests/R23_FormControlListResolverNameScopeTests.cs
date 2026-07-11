using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression test for R23-name-scope-resolution-2: FormControlListResolver.TryResolveRange's
/// NamedRangeNode case resolved a defined name via Workbook.TryGetNamedRange(name, sheetId),
/// which only consults ScopedNamedRanges (range-kind) before falling back to the workbook-global
/// NamedRanges dictionary -- it never checks ScopedNamedFormulas. So a sheet-scoped named FORMULA
/// was invisible to it, and a legacy DropDown/ListBox form control's ListFillRange pointing at that
/// name would silently fall through and resolve to the shadowed workbook-global range instead of
/// leaving the value unresolved, mirroring the already-fixed
/// DataValidationService.ListSources.cs HasSheetScopedNamedFormula gap.
/// </summary>
public class R23_FormControlListResolverNameScopeTests
{
    [Fact]
    public void ResolveSelectedText_SheetScopedNamedFormulaShadowsGlobalRange_DoesNotUseGlobalRange()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        // Workbook-global: Data = Sheet1!A1:A3 = GlobalA/GlobalB/GlobalC.
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new TextValue("GlobalA"));
        sheet1.SetCell(new CellAddress(sheet1.Id, 2, 1), new TextValue("GlobalB"));
        sheet1.SetCell(new CellAddress(sheet1.Id, 3, 1), new TextValue("GlobalC"));
        workbook.DefineNamedRange(
            "Data",
            new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 3, 1)));

        // Sheet2-scoped named FORMULA "Data" must shadow the workbook-global range whenever the
        // name is resolved in the context of Sheet2 -- same Excel scope-precedence rule as named
        // ranges, regardless of whether the shadowing name is a plain range or a formula.
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 2), new TextValue("ScopedA"));
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 2), new TextValue("ScopedB"));
        workbook.DefineNamedFormula("Data", "OFFSET(Sheet2!$B$1,0,0,2,1)", sheet2.Id);

        var control = new FormControlModel
        {
            Kind = FormControlKind.DropDown,
            ListFillRange = "Data",
            SelectedIndex = 1,
        };

        // Before the fix this silently returned "GlobalA" (the shadowed workbook-global range's
        // first item). FormControlListResolver has no formula-evaluation path, so the correct,
        // fixed behavior is to leave the value unresolved (null) rather than show the wrong data.
        FormControlListResolver.ResolveSelectedText(control, sheet2, workbook)
            .Should().BeNull();
    }
}
