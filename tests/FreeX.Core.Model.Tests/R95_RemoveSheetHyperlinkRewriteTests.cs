using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression coverage for the R95 finding: RemoveSheetCommand.Apply never touched
/// Sheet.Hyperlinks / Sheet.HyperlinkMetadata.Bookmark for a 'Place in This Document' hyperlink
/// whose sheet-qualified target named the deleted sheet, unlike the parallel X3 (CF/DV), R26
/// (FormControl), K16 (chart verbatim), and T6/P84 (PivotCache/Slicer/Picture/Timeline
/// SourceSheetName) delete-sheet passes, and unlike RenameSheetCommand's own O25/P113 block,
/// which already rewrites these same two fields on rename. Left unrewritten, a hyperlink's stale
/// "Sheet2!B2" target/bookmark would silently start resolving against an unrelated sheet later
/// re-created/renamed "Sheet2" — the exact failure mode the T6/P84 comments describe and guard
/// against for the other string sheet-name refs, but did not guard against here. The fix mirrors
/// RenameSheetCommand's O25/P113 blocks, but rewrites via FormulaRewriter + DeleteSheetOp
/// (producing "#REF!") instead of renaming.
/// </summary>
public sealed class R95_RemoveSheetHyperlinkRewriteTests
{
    [Fact]
    public void RemoveSheetCommand_RewritesBookmarklessHyperlinkTargetToRef_AndUndoRestores()
    {
        // SetHyperlinkCommand's normal path: no Bookmark set, the sheet-qualified ref lives
        // directly in sheet.Hyperlinks[addr] (see the comment on SetHyperlinkCommand).
        var workbook = new Workbook("RemoveSheetHyperlinkTargetTest");
        var data = workbook.AddSheet("Data");
        var report = workbook.AddSheet("Report");

        var ctx0 = new TestCommandContext(workbook);
        new SetHyperlinkCommand(
            report.Id,
            new CellAddress(report.Id, 1, 1),
            target: "Data!B2",
            displayText: "Go to Data",
            metadata: new HyperlinkMetadata(HyperlinkTargetKind.PlaceInThisDocument))
            .Apply(ctx0).Success.Should().BeTrue();

        var addr = new CellAddress(report.Id, 1, 1);
        report.Hyperlinks[addr].Should().Be("Data!B2");

        var ctx = new TestCommandContext(workbook);
        var command = new RemoveSheetCommand(data.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        report.Hyperlinks[addr].Should().Be("#REF!",
            because: "the hyperlink's target sheet is gone, so it must go stale like any other " +
                     "reference to a deleted sheet, not keep saying the dead sheet's name forever");

        command.Revert(ctx);

        report.Hyperlinks[addr].Should().Be("Data!B2");
    }

    [Fact]
    public void RemoveSheetCommand_RewritesHyperlinkBookmarkToRef_AndUndoRestores()
    {
        // The Bookmark-picker path: the sheet-qualified ref lives on
        // HyperlinkMetadata.Bookmark instead of Hyperlinks[addr].
        var workbook = new Workbook("RemoveSheetHyperlinkBookmarkTest");
        var data = workbook.AddSheet("Data");
        var report = workbook.AddSheet("Report");

        var addr = new CellAddress(report.Id, 1, 1);
        report.SetCell(addr, Cell.FromValue(new TextValue("Go to Data")));
        report.Hyperlinks[addr] = "";
        report.HyperlinkMetadata[addr] = new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument, Bookmark: "Data!B2");

        var ctx = new TestCommandContext(workbook);
        var command = new RemoveSheetCommand(data.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        report.HyperlinkMetadata[addr].Bookmark.Should().Be("#REF!",
            because: "a bookmark naming the deleted sheet must go stale exactly like the " +
                     "bookmark-less target case above");

        command.Revert(ctx);

        report.HyperlinkMetadata[addr].Bookmark.Should().Be("Data!B2");
    }

    [Fact]
    public void RemoveSheetCommand_DoesNotReattachHyperlinkAfterFormerlyDeletedSheetNameIsReused()
    {
        // The concrete failure scenario from the finding: delete 'Data' (whose B2 was linked to),
        // then add a brand-new sheet and rename it back to 'Data'. Before the fix, the stale
        // "Data!B2" hyperlink target would silently resolve against the NEW sheet's B2.
        var workbook = new Workbook("RemoveSheetHyperlinkReattachTest");
        var data = workbook.AddSheet("Data");
        var report = workbook.AddSheet("Report");

        var addr = new CellAddress(report.Id, 1, 1);
        new SetHyperlinkCommand(
            report.Id, addr, target: "Data!B2", displayText: "Go to Data",
            metadata: new HyperlinkMetadata(HyperlinkTargetKind.PlaceInThisDocument))
            .Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        new RemoveSheetCommand(data.Id).Apply(new TestCommandContext(workbook)).Success
            .Should().BeTrue();

        var recreated = workbook.AddSheet("DataRecreated");
        new RenameSheetCommand(recreated.Id, "Data").Apply(new TestCommandContext(workbook)).Success
            .Should().BeTrue();

        report.Hyperlinks[addr].Should().Be("#REF!",
            because: "a #REF!'d hyperlink target must never silently reattach just because a " +
                     "later sheet happens to be renamed back to the deleted sheet's old name");
    }

    [Fact]
    public void RemoveSheetCommand_LeavesHyperlinkTargetUntouched_WhenItReferencesASurvivingSheet()
    {
        // Sibling already-working case: a hyperlink naming a DIFFERENT, still-alive sheet must
        // not be disturbed by deleting an unrelated sheet.
        var workbook = new Workbook("RemoveSheetHyperlinkUnrelatedTest");
        var data = workbook.AddSheet("Data");
        var report = workbook.AddSheet("Report");
        var scratch = workbook.AddSheet("Scratch");

        var addr = new CellAddress(report.Id, 1, 1);
        new SetHyperlinkCommand(
            report.Id, addr, target: "Report!D3", displayText: "Stay put",
            metadata: new HyperlinkMetadata(HyperlinkTargetKind.PlaceInThisDocument))
            .Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        var ctx = new TestCommandContext(workbook);
        var command = new RemoveSheetCommand(scratch.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        report.Hyperlinks[addr].Should().Be("Report!D3",
            because: "deleting an unrelated sheet must not touch a hyperlink to a surviving sheet");

        command.Revert(ctx);
        report.Hyperlinks[addr].Should().Be("Report!D3");
    }

    [Fact]
    public void RemoveSheetCommand_LeavesExistingFileOrWebPageHyperlinkUntouched()
    {
        // A hyperlink whose LinkType is NOT 'Place in This Document' (e.g. an external URL) never
        // carries a sheet-qualified reference, so the delete-sheet rewrite pass must skip it even
        // if its target text happens to contain a bang character.
        var workbook = new Workbook("RemoveSheetHyperlinkExternalTest");
        var data = workbook.AddSheet("Data");
        var report = workbook.AddSheet("Report");

        var addr = new CellAddress(report.Id, 1, 1);
        new SetHyperlinkCommand(
            report.Id, addr, target: "https://example.com/Data!page", displayText: "External")
            .Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        var ctx = new TestCommandContext(workbook);
        var command = new RemoveSheetCommand(data.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        report.Hyperlinks[addr].Should().Be("https://example.com/Data!page");
    }
}
