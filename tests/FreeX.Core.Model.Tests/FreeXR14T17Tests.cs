using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round-14 bucket T17 fix verification.
///
/// R14-form-controls-2: a list-style form control (DropDown/ListBox) whose ListFillRange spans
/// multiple columns (e.g. A1:B3) must be populated from the FIRST COLUMN ONLY, matching Excel —
/// not walked row-major across all cells in the range.
/// </summary>
public sealed class FreeXR14T17Tests
{
    private static (Workbook Workbook, Sheet Sheet) NewWorkbookWithTwoColumnRange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        // A1:B3 → A1,B1 / A2,B2 / A3,B3. Excel only ever lists column A (A1, A2, A3).
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("A1")));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new TextValue("B1")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new TextValue("A2")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromValue(new TextValue("B2")));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new TextValue("A3")));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), Cell.FromValue(new TextValue("B3")));
        return (wb, sheet);
    }

    [Fact]
    public void ResolveSelectedText_MultiColumnRange_SecondSelectionResolvesFirstColumnValue_NotRowMajor()
    {
        var (workbook, sheet) = NewWorkbookWithTwoColumnRange();
        var control = new FormControlModel
        {
            Kind = FormControlKind.DropDown,
            ListFillRange = "A1:B3",
            SelectedIndex = 2, // Excel's 2nd list item is A2 (first column, 2nd row) — never B1.
        };

        FormControlListResolver.ResolveSelectedText(control, sheet, workbook)
            .Should().Be("A2", "Excel populates list-style controls from the first column only");
    }

    [Fact]
    public void ResolveSelectedText_MultiColumnRange_SelectionBeyondRowCount_ReturnsNull()
    {
        // A1:B3 has 3 rows -> only 3 valid items (A1,A2,A3), even though it has 6 cells total.
        var (workbook, sheet) = NewWorkbookWithTwoColumnRange();
        var control = new FormControlModel
        {
            Kind = FormControlKind.DropDown,
            ListFillRange = "A1:B3",
            SelectedIndex = 4, // would have been valid under the old row-major (6-item) walk.
        };

        FormControlListResolver.ResolveSelectedText(control, sheet, workbook).Should().BeNull();
    }

    [Fact]
    public void EstimateListItemCount_MultiColumnRange_ReturnsRowCountOnly_NotRowsTimesCols()
    {
        var (workbook, sheet) = NewWorkbookWithTwoColumnRange();
        var control = new FormControlModel
        {
            Kind = FormControlKind.ListBox,
            ListFillRange = "A1:B3",
        };

        FormControlInteractionService.EstimateListItemCount(control, sheet.Id, workbook)
            .Should().Be(3, "Excel's item count for a multi-column ListFillRange is the row count, not rows*cols");
    }

    [Fact]
    public void CreateSelectListItemCommand_MultiColumnRange_ClampsToRowCount_NotRowsTimesCols()
    {
        var (workbook, sheet) = NewWorkbookWithTwoColumnRange();
        var addr = new CellAddress(sheet.Id, 5, 1);
        var control = new FormControlModel
        {
            Kind = FormControlKind.ListBox,
            ListFillRange = "A1:B3",
            LinkedCell = "A5",
            SelectedIndex = 0,
        };

        // Index 4 is beyond the 3-row range and must be rejected (it was previously allowed
        // because EstimateListItemCount returned rows*cols == 6).
        var rejected = FormControlInteractionService.CreateSelectListItemCommand(control, 4, sheet.Id, workbook);
        rejected.Should().BeNull("Excel clamps selection to the row count of a multi-column ListFillRange");

        // Index 3 (last row) must still be accepted.
        var accepted = FormControlInteractionService.CreateSelectListItemCommand(control, 3, sheet.Id, workbook);
        accepted.Should().NotBeNull();
        control.SelectedIndex.Should().Be(3);
    }
}
