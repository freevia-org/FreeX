using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression coverage for cleanup batch B6 findings P81 and P113: RenameSheetCommand must keep
/// FormControlModel.LinkedCell/ListFillRange and bookmark-less "Place in This Document" hyperlink
/// targets pointing at the renamed sheet (both round-trip through Apply/Revert).
/// </summary>
public sealed class FreeXCleanupB6Tests
{
    // ── P81: FormControlModel.LinkedCell/ListFillRange rewrite on sheet rename ────────────────

    [Fact]
    public void RenameSheetCommand_RewritesFormControlLinkedCellAndListFillRange_AndUndoRestores()
    {
        var workbook = new Workbook("RenameFormControlTest");
        var sheet = workbook.AddSheet("Sheet1");

        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            Name = "Check Box 1",
            LinkedCell = "Sheet1!$D$3",
            ListFillRange = "Sheet1!$F$1:$F$5",
        };
        sheet.FormControls.Add(control);

        var ctx = new TestCommandContext(workbook);
        var command = new RenameSheetCommand(sheet.Id, "Data");

        command.Apply(ctx).Success.Should().BeTrue();

        control.LinkedCell.Should().Be("Data!$D$3");
        control.ListFillRange.Should().Be("Data!$F$1:$F$5");

        command.Revert(ctx);

        control.LinkedCell.Should().Be("Sheet1!$D$3");
        control.ListFillRange.Should().Be("Sheet1!$F$1:$F$5");
    }

    [Fact]
    public void RenameSheetCommand_RewritesFormControlLinkedCell_OnDifferentSheetThanTheControlItself()
    {
        // A checkbox living on Sheet2 but linked to a cell on Sheet1 (the sheet being renamed) —
        // the fix must scan FormControls across ALL sheets, not just the renamed one.
        var workbook = new Workbook("RenameFormControlCrossSheetTest");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            Name = "Check Box 1",
            LinkedCell = "Sheet1!$D$3",
        };
        sheet2.FormControls.Add(control);

        var ctx = new TestCommandContext(workbook);
        var command = new RenameSheetCommand(sheet1.Id, "Data");

        command.Apply(ctx).Success.Should().BeTrue();
        control.LinkedCell.Should().Be("Data!$D$3");

        command.Revert(ctx);
        control.LinkedCell.Should().Be("Sheet1!$D$3");
    }

    [Fact]
    public void RenameSheetCommand_LeavesUnqualifiedFormControlLinkedCellUntouched()
    {
        // A LinkedCell with no sheet qualifier belongs to whichever sheet hosts the control; it
        // must not be corrupted by a rename of a different sheet, nor should renaming its own
        // host sheet touch it (Excel keeps sheet-local refs unqualified).
        var workbook = new Workbook("RenameFormControlUnqualifiedTest");
        var sheet = workbook.AddSheet("Sheet1");

        var control = new FormControlModel
        {
            Kind = FormControlKind.Spinner,
            LinkedCell = "$D$3",
        };
        sheet.FormControls.Add(control);

        var ctx = new TestCommandContext(workbook);
        var command = new RenameSheetCommand(sheet.Id, "Data");

        command.Apply(ctx).Success.Should().BeTrue();
        control.LinkedCell.Should().Be("$D$3");
    }

    // ── P113: bookmark-less "Place in This Document" hyperlink rewrite on sheet rename ────────

    [Fact]
    public void RenameSheetCommand_RewritesBookmarklessPlaceInDocumentHyperlinkTarget_AndUndoRestores()
    {
        // Mirrors FreeX's own Insert Hyperlink dialog path: SetHyperlinkCommand stores the
        // sheet-qualified ref directly in sheet.Hyperlinks[addr] and leaves
        // HyperlinkMetadata.Bookmark empty (the Bookmark field is only populated via the
        // separate Bookmark picker).
        var workbook = new Workbook("RenameHyperlinkTargetTest");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        var address = CellAddress.Parse("A1", sheet1.Id);
        var setHyperlink = new SetHyperlinkCommand(
            sheet1.Id,
            address,
            target: "Sheet2!A1",
            displayText: "Go to Sheet2",
            metadata: new HyperlinkMetadata(HyperlinkTargetKind.PlaceInThisDocument));
        var ctx = new TestCommandContext(workbook);
        setHyperlink.Apply(ctx).Success.Should().BeTrue();

        sheet1.Hyperlinks[address].Should().Be("Sheet2!A1");
        sheet1.HyperlinkMetadata[address].Bookmark.Should().BeEmpty();

        var rename = new RenameSheetCommand(sheet2.Id, "Data");
        rename.Apply(ctx).Success.Should().BeTrue();

        sheet1.Hyperlinks[address].Should().Be(
            "Data!A1", "the raw target string is what HyperlinkNavigationPlanner/CreateXlsxHyperlink read when Bookmark is empty");

        rename.Revert(ctx);
        sheet1.Hyperlinks[address].Should().Be("Sheet2!A1");
    }

    [Fact]
    public void RenameSheetCommand_LeavesHyperlinkWithExplicitBookmarkOnBookmarkPath()
    {
        // When Bookmark IS populated (the O25 path), the target string is not the
        // authoritative ref and must not be touched by the P113 fallback.
        var workbook = new Workbook("RenameHyperlinkBookmarkPrecedenceTest");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        var address = CellAddress.Parse("A1", sheet1.Id);
        var setHyperlink = new SetHyperlinkCommand(
            sheet1.Id,
            address,
            target: "Sheet2!A1",
            displayText: "Go to Sheet2",
            metadata: new HyperlinkMetadata(HyperlinkTargetKind.PlaceInThisDocument, Bookmark: "Sheet2!A1"));
        var ctx = new TestCommandContext(workbook);
        setHyperlink.Apply(ctx).Success.Should().BeTrue();

        var rename = new RenameSheetCommand(sheet2.Id, "Data");
        rename.Apply(ctx).Success.Should().BeTrue();

        sheet1.HyperlinkMetadata[address].Bookmark.Should().Be("Data!A1");
    }
}
