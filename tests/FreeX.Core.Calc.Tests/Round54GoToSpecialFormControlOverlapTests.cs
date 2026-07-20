using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R54-meta-3: Go To Special > Objects only checked a form control's anchor START cell against
/// the search range, so a control whose multi-cell anchor extent overlapped the search range but
/// whose start cell fell outside it was silently skipped -- unlike Excel, which selects any object
/// whose bounding box intersects the search range.
/// </summary>
public sealed class Round54GoToSpecialFormControlOverlapTests
{
    [Fact]
    public void GoToSpecial_Objects_IncludesFormControlWhoseAnchorOverlapsButStartsOutsideRange()
    {
        var (workbook, sheet) = TestWorkbookFixture.CreateWorkbook();

        // Control anchored across C5:D6 -- its start (C5) is OUTSIDE the search range below,
        // but its extent overlaps the search range at D6.
        var anchorStart = new CellAddress(sheet.Id, 5, 3); // C5
        var anchorEnd = new CellAddress(sheet.Id, 6, 4);   // D6
        sheet.FormControls.Add(new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            Anchor = new GridRange(anchorStart, anchorEnd),
        });

        // Search range D6:F10 excludes C5 (the anchor start) but includes D6 (part of the extent).
        var searchRange = new GridRange(new CellAddress(sheet.Id, 6, 4), new CellAddress(sheet.Id, 10, 6));
        var matches = GoToSpecialService.Find(sheet, searchRange, GoToSpecialKind.Objects);

        matches.Should().ContainSingle().Which.Should().Be(anchorStart);
    }

    // Sibling/no-regression: a form control whose anchor extent does NOT overlap the search range
    // at all is still correctly excluded.
    [Fact]
    public void GoToSpecial_Objects_ExcludesFormControlWhoseAnchorDoesNotOverlapRange()
    {
        var (workbook, sheet) = TestWorkbookFixture.CreateWorkbook();

        var anchorStart = new CellAddress(sheet.Id, 5, 3); // C5
        var anchorEnd = new CellAddress(sheet.Id, 6, 4);   // D6
        sheet.FormControls.Add(new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            Anchor = new GridRange(anchorStart, anchorEnd),
        });

        // Search range far away from C5:D6 -- no overlap at all.
        var searchRange = new GridRange(new CellAddress(sheet.Id, 20, 20), new CellAddress(sheet.Id, 25, 25));
        var matches = GoToSpecialService.Find(sheet, searchRange, GoToSpecialKind.Objects);

        matches.Should().BeEmpty();
    }
}
