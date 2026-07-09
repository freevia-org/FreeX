using FluentAssertions;
using FreeX.App.Presentation.DefinedNames;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.DefinedNames;

/// <summary>
/// Regression coverage for R19-defined-name-3d-3: PasteNamesPlanner.BuildItems only enumerated
/// workbook.NamedRanges, so sheet-scoped named ranges/formulas (workbook.ScopedNamedRanges /
/// workbook.ScopedNamedFormulas) and workbook-scoped named formulas (workbook.NamedFormulas) were invisible
/// in the Paste Names / Paste List dialog -- unlike Excel, which lists every defined name in the workbook.
/// </summary>
public sealed class R19_paste_names_Tests
{
    private static (Workbook Workbook, Sheet Sheet1, Sheet Sheet2) NewWorkbook()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        return (workbook, sheet1, sheet2);
    }

    [Fact]
    public void BuildItems_WorkbookWithOnlyScopedRangeAndNamedFormula_IncludesBoth()
    {
        var (workbook, _, sheet2) = NewWorkbook();

        // Sheet-scoped named range: "LocalTotal" scoped to Sheet2 (Excel "localSheetId").
        workbook.DefineNamedRange(
            "LocalTotal",
            new GridRange(new CellAddress(sheet2.Id, 1, 1), new CellAddress(sheet2.Id, 1, 1)),
            metadata: null,
            scopeSheetId: sheet2.Id);

        // Workbook-scoped named formula (not a range): "TaxRate" = 0.21.
        workbook.NamedFormulas["TaxRate"] = "0.21";

        // The workbook has NO plain workbook.NamedRanges entries at all -- these two are the only names.
        workbook.NamedRanges.Should().BeEmpty();

        var items = PasteNamesPlanner.BuildItems(workbook, range => range.ToString());

        items.Select(i => i.Name).Should().Contain(new[] { "Sheet2!LocalTotal", "TaxRate" });
        items.Should().HaveCount(2);

        var localTotal = items.Single(i => i.Name == "Sheet2!LocalTotal");
        localTotal.RefersTo.Should().Be("A1:A1");

        var taxRate = items.Single(i => i.Name == "TaxRate");
        taxRate.RefersTo.Should().Be("=0.21");
    }

    [Fact]
    public void BuildItems_ScopedNamedFormula_IsQualifiedBySheetAndIncluded()
    {
        var (workbook, _, sheet2) = NewWorkbook();

        workbook.DefineNamedFormula("LocalRate", "1.05", sheet2.Id);

        var items = PasteNamesPlanner.BuildItems(workbook, range => range.ToString());

        items.Should().ContainSingle();
        items[0].Name.Should().Be("Sheet2!LocalRate");
        items[0].RefersTo.Should().Be("=1.05");
    }

    [Fact]
    public void TryBuildPasteListEdits_ScopedRangeAndNamedFormulaOnly_DoesNotReportNoNames()
    {
        var (workbook, sheet1, sheet2) = NewWorkbook();

        workbook.DefineNamedRange(
            "LocalTotal",
            new GridRange(new CellAddress(sheet2.Id, 1, 1), new CellAddress(sheet2.Id, 1, 1)),
            metadata: null,
            scopeSheetId: sheet2.Id);
        workbook.NamedFormulas["TaxRate"] = "0.21";

        var items = PasteNamesPlanner.BuildItems(workbook, range => range.ToString());

        var ok = PasteNamesPlanner.TryBuildPasteListEdits(
            new CellAddress(sheet1.Id, 1, 1), items, out var edits, out var error);

        ok.Should().BeTrue();
        error.Should().NotBe(PasteNamesListError.NoNames);
        edits.Should().HaveCount(4);
    }
}
