using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression tests for two round-22 data-validation findings:
///
/// R22-data-validation-1: ValidateCustom staged the candidate value via Sheet.SetCell(address,
/// value), which tears down any live spill rooted at that address (Sheet.SetCell always calls
/// ClearSpillRange as a side effect), and the finally-block restore only replayed the anchor Cell
/// object -- never the spill members -- so validating a spill anchor cell (e.g. via Data >
/// Data Validation > Circle Invalid Data, which validates every value-bearing cell including spill
/// members) permanently blanked the spilled cells.
///
/// R22-data-validation-3: ResolveListValues fell through to ParseInlineListItems(formulaText) with
/// the RAW (unevaluated) formula text whenever a formula-based List source (e.g. a cascading
/// =INDIRECT($A2) dropdown) evaluated to an ErrorValue, so the in-cell dropdown showed a single
/// bogus item literally reading the formula text, and validation rejected every real value the user
/// entered because it never matched that literal text.
/// </summary>
public class R22_DataValidationSpillAndListErrorTests
{
    [Fact]
    public void ValidateCustom_OnSpillAnchor_PreservesSpillMembers()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 1, 1); // A1

        sheet.SetFormula(anchor, "SEQUENCE(1,3)");
        sheet.GetCell(anchor)!.Value = new NumberValue(1);
        var spillCells = new ScalarValue[1, 3]
        {
            { new NumberValue(1), new NumberValue(2), new NumberValue(3) },
        };
        sheet.SetSpillRange(anchor, new RangeValue(spillCells)); // spills A1:C1 = 1,2,3

        var dv = new DataValidation
        {
            Type = DvType.Custom,
            Formula1 = "=A1>0",
            AppliesTo = new GridRange(anchor, anchor),
        };

        // Mirrors DataValidationCirclePlanner validating the spill anchor's own current value.
        var result = DataValidationService.Validate(dv, new NumberValue(1), sheet, anchor, wb);

        result.Should().BeNull("1 > 0 satisfies the custom rule");

        sheet.TryGetSpillExtent(anchor, out var rows, out var cols).Should().BeTrue(
            "the anchor must still be recognised as a live spill anchor after validation");
        rows.Should().Be(1u);
        cols.Should().Be(3u);
        sheet.GetValue(1, 2).Should().Be(new NumberValue(2), "B1 must not be blanked by validation");
        sheet.GetValue(1, 3).Should().Be(new NumberValue(3), "C1 must not be blanked by validation");
    }

    [Fact]
    public void GetListItems_FormulaSourceEvaluatesToError_ReturnsEmptyNotRawFormulaText()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 2, 2); // B2

        // A2 is blank, so INDIRECT($A2) -> INDIRECT("") is a #REF! error, exactly like a cascading
        // dropdown before the user has picked the upstream category.
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "=INDIRECT($A2)",
            AppliesTo = new GridRange(address, address),
        };

        var items = DataValidationService.GetListItems(dv, sheet, address, wb);

        items.Should().BeEmpty("an errored formula source has no valid list items and must not " +
            "surface the raw, unevaluated formula text as a bogus dropdown entry");
    }
}
