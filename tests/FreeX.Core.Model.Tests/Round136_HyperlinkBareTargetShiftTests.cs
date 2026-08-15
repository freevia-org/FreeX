using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round-136 finding: the Avalonia (and WPF) hyperlink hover tooltip renders
/// <c>sheet.Hyperlinks[addr]</c> live (FreeX.App.Avalonia/MainWindow.cs FormatHyperlinkTooltip,
/// FreeX.App.Host GridView.CommentPreview.cs's twin), so it is only as fresh as the model's raw
/// target string. FreeX's own Insert Hyperlink dialog stores a same-sheet "Place in This Document"
/// link the user types straight into the address box (e.g. "A10") as an UNQUALIFIED bare cell
/// reference in sheet.Hyperlinks[addr], with HyperlinkMetadata.Bookmark left empty (see
/// SetHyperlinkCommand / HyperlinkDialogPlanner and the sibling FreeXR13S13Tests, which covers the
/// already-fixed sheet-QUALIFIED form "Sheet1!A10").
///
/// ShiftRawHyperlinkTarget (RowColumnShiftHelpers.Annotations.cs) used to bail out unconditionally
/// whenever the raw target had no '!' -- lumping the bare-cell-ref case in with the (genuinely
/// unshiftable) named-range case. That left every same-sheet, unqualified hyperlink target frozen
/// at its pre-edit address once rows or columns were inserted/deleted on the hyperlink's own sheet:
/// the hover tooltip and Ctrl+Click navigation kept pointing at the old, now-wrong cell.
/// </summary>
public sealed class Round136_HyperlinkBareTargetShiftTests
{
    [Fact]
    public void InsertRows_ShiftsBareUnqualifiedHyperlinkTarget_OnHyperlinksOwnSheet_AndUndoRestores()
    {
        // A1 has a PlaceInThisDocument hyperlink to A10 on the SAME sheet, stored the way FreeX's
        // own Insert Hyperlink dialog stores a typed-in same-sheet ref with no sheet qualifier:
        // sheet.Hyperlinks[A1] = "A10" (no "Sheet1!" prefix), Bookmark left empty.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var sourceAddr = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.Hyperlinks[sourceAddr] = "A10";
        sheet.HyperlinkMetadata[sourceAddr] = new HyperlinkMetadata(HyperlinkTargetKind.PlaceInThisDocument);

        // Insert 5 rows above row 10 (Excel/FreeX shift the data at A10 down to A15).
        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 10, count: 5);
        cmd.Apply(ctx);

        sheet.Hyperlinks[sourceAddr].Should().Be("A15",
            because: "the bare, sheet-unqualified target is what the hover tooltip and " +
                     "HyperlinkNavigationPlanner read verbatim when Bookmark is empty, so it must " +
                     "shift on the hyperlink's own sheet just like the sheet-qualified form does");

        cmd.Revert(ctx);

        sheet.Hyperlinks[sourceAddr].Should().Be("A10",
            because: "undo must restore the original bare hyperlink target");
    }

    [Fact]
    public void InsertRows_LeavesBareHyperlinkTargetOnUnaffectedSheet_AndNamedRangeStyleTargetsAlone()
    {
        // No-regression sibling: (1) a bare hyperlink target living on a DIFFERENT sheet than the
        // one being edited must not be touched (it is implicitly relative to ITS OWN sheet, not the
        // edited one), and (2) a bare target that is actually a defined-name reference (not a cell
        // ref at all) must still be left completely alone -- FormulaRewriter.Rewrite only ever
        // returns non-null when it substantively changed a CellRefNode, so a NamedRangeNode target
        // is inherently a no-op through the same code path, but this pins that behavior explicitly.
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new TestCommandContext(wb);

        var otherSheetAddr = new CellAddress(sheet2.Id, 1, 1); // Sheet2!A1
        sheet2.Hyperlinks[otherSheetAddr] = "A10"; // bare ref relative to Sheet2, not Sheet1
        sheet2.HyperlinkMetadata[otherSheetAddr] = new HyperlinkMetadata(HyperlinkTargetKind.PlaceInThisDocument);

        var namedRangeAddr = new CellAddress(sheet1.Id, 2, 1); // A2
        sheet1.Hyperlinks[namedRangeAddr] = "MyNamedRange";
        sheet1.HyperlinkMetadata[namedRangeAddr] = new HyperlinkMetadata(HyperlinkTargetKind.PlaceInThisDocument);

        // Insert rows on Sheet1 -- must not disturb Sheet2's own bare target or Sheet1's
        // named-range-style target.
        var cmd = new InsertRowsCommand(sheet1.Id, beforeRow: 5, count: 5);
        cmd.Apply(ctx);

        sheet2.Hyperlinks[otherSheetAddr].Should().Be("A10",
            because: "a bare hyperlink target on an unrelated sheet is relative to THAT sheet, " +
                     "not the sheet being edited, and must not shift");
        sheet1.Hyperlinks[namedRangeAddr].Should().Be("MyNamedRange",
            because: "a defined-name target has no row/column coordinates to shift");

        cmd.Revert(ctx);

        sheet2.Hyperlinks[otherSheetAddr].Should().Be("A10");
        sheet1.Hyperlinks[namedRangeAddr].Should().Be("MyNamedRange");
    }
}
