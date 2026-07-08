using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round-13 bucket S13 regression test.
///
/// R13-hyperlinks-deep-1: a 'Place in This Document' hyperlink whose ref lives in the raw
/// sheet.Hyperlinks[addr] target (Bookmark left empty — the shape FreeX's own Insert Hyperlink
/// dialog produces when the ref is typed straight into the address field) must have that raw
/// target shifted on row/column insert/delete, exactly like the Bookmark-populated form already
/// is. Before the fix, ShiftHyperlinkBookmarksOnSheet's `if (string.IsNullOrEmpty(bookmark))
/// continue;` skipped these entirely, so HyperlinkNavigationPlanner and CreateXlsxHyperlink (both
/// of which read the raw target when Bookmark is empty) kept pointing at the pre-shift cell.
/// </summary>
public sealed class FreeXR13S13Tests
{
    [Fact]
    public void InsertRows_ShiftsRawHyperlinkTarget_WhenBookmarkEmpty_AndUndoRestores()
    {
        // A1 has a PlaceInThisDocument hyperlink to Sheet1!A10, stored the way FreeX's own Insert
        // Hyperlink dialog stores a typed-in ref: sheet.Hyperlinks[A1] = "Sheet1!A10" with
        // HyperlinkMetadata.Bookmark left empty (see SheetCommands.cs:283-286).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var sourceAddr = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.Hyperlinks[sourceAddr] = "Sheet1!A10";
        sheet.HyperlinkMetadata[sourceAddr] = new HyperlinkMetadata(HyperlinkTargetKind.PlaceInThisDocument);

        // Insert 5 rows above row 10 (Excel/FreeX shift the data at A10 down to A15).
        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 10, count: 5);
        cmd.Apply(ctx);

        sheet.Hyperlinks[sourceAddr].Should().Be("Sheet1!A15",
            because: "the raw hyperlink target is the string HyperlinkNavigationPlanner/CreateXlsxHyperlink " +
                     "actually read when Bookmark is empty, so it must shift just like a populated Bookmark would");

        cmd.Revert(ctx);

        sheet.Hyperlinks[sourceAddr].Should().Be("Sheet1!A10",
            because: "undo must restore the original raw hyperlink target");
    }
}
