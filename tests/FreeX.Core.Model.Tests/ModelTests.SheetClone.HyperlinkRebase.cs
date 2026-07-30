using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R95: a duplicated sheet's same-sheet-qualified 'Place in This Document' hyperlinks must
/// follow the copy (matching Excel's Move-or-Copy > Create a Copy behavior), exactly like the
/// analogous rebase already applied to ConditionalFormat.FormulaText / DataValidation.Formula1-2
/// (<see cref="Sheet.Clone(SheetId, string)"/>, <c>RewriteSameSheetQualifiedFormula</c>) and to
/// chart verbatim text / <c>PictureModel.LinkedSourceSheetName</c> in
/// <c>DuplicateSheetDrawingCloner</c>.
/// </summary>
public partial class SheetCloneTests
{
    [Fact]
    public void R95_Sheet_Clone_RebasesHyperlinkTargetSameSheetQualifiedReferenceToCopy()
    {
        // No Bookmark set -- HyperlinkNavigationPlanner falls back to reading the sheet-qualified
        // reference straight out of sheet.Hyperlinks[addr] (see Sheet.Clone.cs / HyperlinkCommands.cs).
        var wb = new Workbook("T");
        var src = wb.AddSheet("Sheet1");
        var addr = new CellAddress(src.Id, 1, 1);
        src.Hyperlinks[addr] = "Sheet1!B2";
        src.HyperlinkMetadata[addr] = new HyperlinkMetadata(HyperlinkTargetKind.PlaceInThisDocument);

        var copy = src.Clone(SheetId.New(), "Sheet1 (2)");

        var copyAddr = new CellAddress(copy.Id, 1, 1);
        copy.Hyperlinks[copyAddr].Should().Be("'Sheet1 (2)'!B2");
    }

    [Fact]
    public void R95_Sheet_Clone_RebasesHyperlinkBookmarkSameSheetQualifiedReferenceToCopy()
    {
        var wb = new Workbook("T");
        var src = wb.AddSheet("Sheet1");
        var addr = new CellAddress(src.Id, 1, 1);
        src.Hyperlinks[addr] = "Sheet1!B2";
        src.HyperlinkMetadata[addr] = new HyperlinkMetadata(HyperlinkTargetKind.PlaceInThisDocument, Bookmark: "Sheet1!B2");

        var copy = src.Clone(SheetId.New(), "Sheet1 (2)");

        var copyAddr = new CellAddress(copy.Id, 1, 1);
        copy.HyperlinkMetadata[copyAddr].Bookmark.Should().Be("'Sheet1 (2)'!B2");
    }

    [Fact]
    public void R95_Sheet_Clone_LeavesHyperlinkOtherSheetQualifiedReferenceUnchanged()
    {
        // Sibling already-working-shape case: a hyperlink explicitly pointing at a DIFFERENT
        // sheet must keep pointing at that sheet, not follow the duplicate.
        var wb = new Workbook("T");
        var src = wb.AddSheet("Sheet1");
        wb.AddSheet("Sheet2");
        var addr = new CellAddress(src.Id, 1, 1);
        src.Hyperlinks[addr] = "Sheet2!B2";
        src.HyperlinkMetadata[addr] = new HyperlinkMetadata(HyperlinkTargetKind.PlaceInThisDocument);

        var copy = src.Clone(SheetId.New(), "Sheet1 (2)");

        var copyAddr = new CellAddress(copy.Id, 1, 1);
        copy.Hyperlinks[copyAddr].Should().Be("Sheet2!B2");
    }

    [Fact]
    public void R95_Sheet_Clone_LeavesWebUrlHyperlinkUnchanged()
    {
        // Sibling already-working-shape case: an ordinary web/file hyperlink is not a
        // sheet-qualified reference at all and must never be touched by the rebase, even if it
        // happens to contain "!" characters.
        var wb = new Workbook("T");
        var src = wb.AddSheet("Sheet1");
        var addr = new CellAddress(src.Id, 1, 1);
        src.Hyperlinks[addr] = "https://example.com/Sheet1!page";
        src.HyperlinkMetadata[addr] = new HyperlinkMetadata(HyperlinkTargetKind.ExistingFileOrWebPage);

        var copy = src.Clone(SheetId.New(), "Sheet1 (2)");

        var copyAddr = new CellAddress(copy.Id, 1, 1);
        copy.Hyperlinks[copyAddr].Should().Be("https://example.com/Sheet1!page");
    }
}
