using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R18-defined-name-cross-context-{1,2,3}: sheet-scoped defined names must shadow a same-named
/// workbook-global name when resolved from data validation list sources and form-control list
/// ranges, mirroring formula evaluation's scope-aware <c>Workbook.TryGetNamedRange(name, sheetId,
/// out range)</c> (already used by <c>FormControlInteractionService.TryResolveLinkedCell</c>).
///
/// All three tests share the same shadowing setup:
///   - global "MyList"          -> Sheet2!B1:B3 ("G1","G2","G3")
///   - Sheet1-scoped "MyList"   -> Sheet1!A1:A6 ("R1".."R6")
/// A reference to "MyList" evaluated in Sheet1's context must resolve to the Sheet1-scoped range,
/// not the workbook-global one.
/// </summary>
public sealed class R18DefinedNameContextTests
{
    private static Workbook NewShadowedWorkbook(out Sheet sheet1, out Sheet sheet2)
    {
        var workbook = new Workbook("test");
        sheet1 = workbook.AddSheet("Sheet1");
        sheet2 = workbook.AddSheet("Sheet2");

        for (var row = 1u; row <= 6u; row++)
            sheet1.SetCell(new CellAddress(sheet1.Id, row, 1), Cell.FromValue(new TextValue($"R{row}")));

        for (var row = 1u; row <= 3u; row++)
            sheet2.SetCell(new CellAddress(sheet2.Id, row, 2), Cell.FromValue(new TextValue($"G{row}")));

        // Workbook-global "MyList" -> Sheet2!B1:B3.
        workbook.DefineNamedRange(
            "MyList",
            new GridRange(new CellAddress(sheet2.Id, 1, 2), new CellAddress(sheet2.Id, 3, 2)));

        // Sheet1-scoped "MyList" -> Sheet1!A1:A6, shadows the global name on Sheet1.
        workbook.DefineNamedRange(
            "MyList",
            new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 6, 1)),
            metadata: null,
            scopeSheetId: sheet1.Id);

        return workbook;
    }

    // ── R18-defined-name-cross-context-1 ───────────────────────────────────────

    [Fact]
    public void ListValidation_DefinedNameSource_UsesSheetScopedNameNotGlobalShadow()
    {
        var workbook = NewShadowedWorkbook(out var sheet1, out _);

        var dv = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet1.Id, 10, 5), new CellAddress(sheet1.Id, 10, 5)),
            Type = DvType.List,
            Formula1 = "=MyList",
        };
        var address = dv.AppliesTo.Start;

        // "R2" only exists in the Sheet1-scoped range — must validate OK once scope wins.
        DataValidationService.Validate(dv, new TextValue("R2"), sheet1, address, workbook)
            .Should().BeNull("R2 is a member of the Sheet1-scoped MyList, which must shadow the global name");

        // "G1" only exists in the workbook-global range — must be rejected once the scoped name
        // correctly shadows the global one on Sheet1 (pre-fix this incorrectly passes).
        DataValidationService.Validate(dv, new TextValue("G1"), sheet1, address, workbook)
            .Should().NotBeNull("G1 belongs to the shadowed global MyList, not the Sheet1-scoped one");
    }

    // ── R18-defined-name-cross-context-2 ───────────────────────────────────────

    [Fact]
    public void EstimateListItemCount_BareDefinedName_ReturnsSheetScopedRowCount()
    {
        var workbook = NewShadowedWorkbook(out var sheet1, out _);

        var control = new FormControlModel
        {
            Kind = FormControlKind.ListBox,
            ListFillRange = "MyList",
            LinkedCell = "C1",
            SelectedIndex = 1,
        };

        // The Sheet1-scoped MyList has 6 rows, so item 4 must be selectable (count > 1).
        // Pre-fix, EstimateListItemCount always returns 1 for a bare name and clamps this away.
        var command = FormControlInteractionService.CreateSelectListItemCommand(control, 4, sheet1.Id, workbook);

        command.Should().NotBeNull("the Sheet1-scoped MyList has 6 rows, so item 4 is a valid selection");
    }

    // ── R18-defined-name-cross-context-3 ───────────────────────────────────────

    [Fact]
    public void ResolveSelectedText_BareDefinedName_ResolvesFromSheetScopedRange()
    {
        var workbook = NewShadowedWorkbook(out var sheet1, out _);

        var control = new FormControlModel
        {
            Kind = FormControlKind.DropDown,
            ListFillRange = "MyList",
            SelectedIndex = 2,
        };

        // Item 2 of the Sheet1-scoped range is "R2". Pre-fix this resolves against the shadowed
        // global range (Sheet2!B1:B3) and returns "G2" instead.
        FormControlListResolver.ResolveSelectedText(control, sheet1, workbook)
            .Should().Be("R2");
    }
}
