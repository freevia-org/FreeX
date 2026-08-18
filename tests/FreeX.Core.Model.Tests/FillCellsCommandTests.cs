using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class FillCellsCommandTests
{
    [Fact]
    public void FillDown_CopiesTopRowCellsAndAdjustsRelativeFormulas()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var source = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(source, Cell.FromFormula("B1+$C$1"));

        var command = new FillCellsCommand(
            sheet.Id,
            new GridRange(source, new CellAddress(sheet.Id, 3, 1)),
            FillCellsDirection.Down);

        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        sheet.GetCell(new CellAddress(sheet.Id, 2, 1))!.FormulaText.Should().Be("B2+$C$1");
        sheet.GetCell(new CellAddress(sheet.Id, 3, 1))!.FormulaText.Should().Be("B3+$C$1");
    }

    [Fact]
    public void FillRight_CopiesLeftColumnCellsAndUndoRestoresTargets()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(source, Cell.FromValue(new TextValue("copied")));
        sheet.SetCell(target, Cell.FromValue(new TextValue("old")));
        var context = new TestCommandContext(workbook);

        var command = new FillCellsCommand(
            sheet.Id,
            new GridRange(source, target),
            FillCellsDirection.Right);

        command.Apply(context).Success.Should().BeTrue();
        sheet.GetCell(target)!.Value.Should().Be(new TextValue("copied"));

        command.Revert(context);

        sheet.GetCell(target)!.Value.Should().Be(new TextValue("old"));
    }

    [Fact]
    public void FillDown_CopiesHyperlinkTargetAndMetadataAndUndoRestoresTargets()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(source, Cell.FromValue(new TextValue("Example")));
        sheet.Hyperlinks[source] = "https://example.com";
        sheet.HyperlinkMetadata[source] = new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "Open example",
            "section-one");
        sheet.SetCell(target, Cell.FromValue(new TextValue("Old")));
        sheet.Hyperlinks[target] = "mailto:old@example.com";
        sheet.HyperlinkMetadata[target] = new HyperlinkMetadata(
            HyperlinkTargetKind.EmailAddress,
            "Email old",
            "old@example.com");
        var context = new TestCommandContext(workbook);

        var command = new FillCellsCommand(
            sheet.Id,
            new GridRange(source, target),
            FillCellsDirection.Down);

        command.Apply(context).Success.Should().BeTrue();

        sheet.GetCell(target)!.Value.Should().Be(new TextValue("Example"));
        sheet.Hyperlinks[target].Should().Be("https://example.com");
        sheet.HyperlinkMetadata[target].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "Open example",
            "section-one"));

        command.Revert(context);

        sheet.GetCell(target)!.Value.Should().Be(new TextValue("Old"));
        sheet.Hyperlinks[target].Should().Be("mailto:old@example.com");
        sheet.HyperlinkMetadata[target].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.EmailAddress,
            "Email old",
            "old@example.com"));
    }

    [Fact]
    public void FillRight_ClearsTargetHyperlinkWhenSourceHasNoHyperlink()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(source, Cell.FromValue(new TextValue("plain")));
        sheet.SetCell(target, Cell.FromValue(new TextValue("linked")));
        sheet.Hyperlinks[target] = "https://example.com";
        sheet.HyperlinkMetadata[target] = new HyperlinkMetadata(ScreenTip: "Open example");

        var command = new FillCellsCommand(
            sheet.Id,
            new GridRange(source, target),
            FillCellsDirection.Right);

        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        sheet.GetCell(target)!.Value.Should().Be(new TextValue("plain"));
        sheet.Hyperlinks.Should().NotContainKey(target);
        sheet.HyperlinkMetadata.Should().NotContainKey(target);
    }

    [Fact]
    public void FillDown_FromStyleOnlyBlankSource_CopiesFormattingAndUndoRestoresTargetStyleOnly()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 2, 1);
        var sourceStyle = workbook.RegisterStyle(new CellStyle { Bold = true });
        var targetStyle = workbook.RegisterStyle(new CellStyle { Italic = true });
        sheet.SetStyleOnly(source.Row, source.Col, sourceStyle);
        sheet.SetStyleOnly(target.Row, target.Col, targetStyle);
        var context = new TestCommandContext(workbook);

        var command = new FillCellsCommand(
            sheet.Id,
            new GridRange(source, target),
            FillCellsDirection.Down);

        command.Apply(context).Success.Should().BeTrue();

        sheet.GetCell(target).Should().BeNull();
        sheet.GetStyleOnly(target.Row, target.Col).Should().Be(sourceStyle);

        command.Revert(context);

        sheet.GetCell(target).Should().BeNull();
        sheet.GetStyleOnly(target.Row, target.Col).Should().Be(targetStyle);
    }

    [Fact]
    public void FillRight_FromPlainBlankSource_ClearsTargetStyleOnlyFormatting()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 1, 2);
        var targetStyle = workbook.RegisterStyle(new CellStyle { Italic = true });
        sheet.SetStyleOnly(target.Row, target.Col, targetStyle);

        var command = new FillCellsCommand(
            sheet.Id,
            new GridRange(source, target),
            FillCellsDirection.Right);

        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        sheet.GetCell(target).Should().BeNull();
        sheet.GetStyleOnly(target.Row, target.Col).Should().BeNull();
    }

    [Fact]
    public void FillDown_RejectsLockedTargetsOnProtectedSheet()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(source, Cell.FromValue(new TextValue("source")));
        sheet.SetCell(target, Cell.FromValue(new TextValue("target")));
        sheet.IsProtected = true;

        var outcome = new FillCellsCommand(
            sheet.Id,
            new GridRange(source, target),
            FillCellsDirection.Down).Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.GetCell(target)!.Value.Should().Be(new TextValue("target"));
    }

    [Fact]
    public void FillDown_AllowsUnlockedTargetsOnProtectedSheet()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 2, 1);
        var unlockedStyle = workbook.RegisterStyle(new CellStyle { Locked = false });
        sheet.SetCell(source, Cell.FromValue(new TextValue("source")));
        var targetCell = Cell.FromValue(new TextValue("target"));
        targetCell.StyleId = unlockedStyle;
        sheet.SetCell(target, targetCell);
        sheet.IsProtected = true;

        var outcome = new FillCellsCommand(
            sheet.Id,
            new GridRange(source, target),
            FillCellsDirection.Down).Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeTrue();
        sheet.GetCell(target)!.Value.Should().Be(new TextValue("source"));
    }

    [Fact]
    public void MergeTiledFillDown_ClearsStalePhoneticGuideWhenSourceTileIsBlank()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        // Two equal-size merges stacked vertically -- the "Q1 header repeated below" shape
        // ApplyMergeTiledFill allows through. The source tile (row 1, A1:B1) is left blank; the
        // target tile (row 2, A2:B2) starts with content plus a phonetic guide.
        var sourceMerge = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 2));
        var targetMerge = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 2));
        sheet.AddMergedRegion(sourceMerge);
        sheet.AddMergedRegion(targetMerge);

        var targetAnchor = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(targetAnchor, Cell.FromValue(new TextValue("たなか")));
        var originalGuide = new CellPhoneticGuide(["<rPh sb=\"0\" eb=\"1\"><t>たなか</t></rPh>"], null);
        sheet.CellPhoneticGuides[targetAnchor] = originalGuide;

        var context = new TestCommandContext(workbook);
        var command = new FillCellsCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            FillCellsDirection.Down);

        command.Apply(context).Success.Should().BeTrue();

        // Filling a blank source tile over the target must blank the target's content too --
        // and the phonetic guide describing content that no longer exists must go with it.
        sheet.GetCell(targetAnchor).Should().BeNull();
        sheet.CellPhoneticGuides.Should().NotContainKey(targetAnchor);

        command.Revert(context);

        sheet.CellPhoneticGuides.Should().ContainKey(targetAnchor)
            .WhoseValue.Should().Be(originalGuide);
    }

    [Fact]
    public void MergeTiledFillDown_CopiesPhoneticGuideFromSourceTileAnchor()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        var sourceMerge = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 2));
        var targetMerge = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 2));
        sheet.AddMergedRegion(sourceMerge);
        sheet.AddMergedRegion(targetMerge);

        var sourceAnchor = new CellAddress(sheet.Id, 1, 1);
        var targetAnchor = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(sourceAnchor, Cell.FromValue(new TextValue("すずき")));
        var sourceGuide = new CellPhoneticGuide(["<rPh sb=\"0\" eb=\"1\"><t>すずき</t></rPh>"], null);
        sheet.CellPhoneticGuides[sourceAnchor] = sourceGuide;

        var context = new TestCommandContext(workbook);
        var command = new FillCellsCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            FillCellsDirection.Down);

        command.Apply(context).Success.Should().BeTrue();

        sheet.GetCell(targetAnchor)!.Value.Should().Be(new TextValue("すずき"));
        sheet.CellPhoneticGuides.Should().ContainKey(targetAnchor)
            .WhoseValue.Should().Be(sourceGuide);
    }

    // R142-comments-notes-1: Ctrl+D/Fill Down (FillCellsCommand) must carry a source cell's
    // legacy note (Comments/CommentAuthors/ShownComments) and threaded comment to every fill
    // target, exactly like it already does for Hyperlinks, and undo must restore precisely what
    // was at the target beforehand rather than leaving the fill's comment behind or wiping a
    // pre-existing target comment outright.
    [Fact]
    public void FillDown_CopiesLegacyNoteAndThreadedCommentAndUndoRestoresTargetOriginals()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(source, Cell.FromValue(new TextValue("Example")));
        sheet.Comments[source] = "Source note";
        sheet.CommentAuthors[source] = "Alice";
        sheet.ShownComments.Add(source);
        sheet.ThreadedComments[source] = new ThreadedComment("Source thread") { Id = "{SRC-ID}" };

        // The fill target already has its OWN pre-existing note before the fill overwrites it --
        // undo must restore exactly this, not just blank the target out.
        sheet.SetCell(target, Cell.FromValue(new TextValue("Old")));
        sheet.Comments[target] = "Original target note";
        sheet.CommentAuthors[target] = "Bob";
        sheet.ThreadedComments[target] = new ThreadedComment("Original target thread") { Id = "{DST-ID}" };
        var context = new TestCommandContext(workbook);

        var command = new FillCellsCommand(
            sheet.Id,
            new GridRange(source, target),
            FillCellsDirection.Down);

        command.Apply(context).Success.Should().BeTrue();

        sheet.GetCell(target)!.Value.Should().Be(new TextValue("Example"));
        sheet.Comments[target].Should().Be("Source note");
        sheet.CommentAuthors[target].Should().Be("Alice");
        sheet.ShownComments.Should().Contain(target);
        sheet.ThreadedComments[target].Text.Should().Be("Source thread");
        // The copy must mint its own thread id rather than duplicating the source's persisted id
        // (mirrors CopyRangeCommand.ClonedThreadedCommentForNewAddress).
        sheet.ThreadedComments[target].Id.Should().BeNull();

        command.Revert(context);

        sheet.Comments[target].Should().Be("Original target note");
        sheet.CommentAuthors[target].Should().Be("Bob");
        sheet.ShownComments.Should().NotContain(target);
        sheet.ThreadedComments[target].Text.Should().Be("Original target thread");
        sheet.ThreadedComments[target].Id.Should().Be("{DST-ID}");
    }

    // Sibling test: an unrelated cell outside the fill range keeps its own note completely
    // untouched by a fill (and by that fill's undo), proving the new comment-carry logic is
    // scoped to the actual fill targets and doesn't leak into neighbouring cells.
    [Fact]
    public void FillDown_LeavesUnrelatedCellsNoteUntouched()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 2, 1);
        var unrelated = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(source, Cell.FromValue(new TextValue("Example")));
        sheet.Comments[source] = "Source note";
        sheet.SetCell(unrelated, Cell.FromValue(new TextValue("Untouched")));
        sheet.Comments[unrelated] = "Unrelated note";
        sheet.CommentAuthors[unrelated] = "Carol";
        var context = new TestCommandContext(workbook);

        var command = new FillCellsCommand(
            sheet.Id,
            new GridRange(source, target),
            FillCellsDirection.Down);

        command.Apply(context).Success.Should().BeTrue();
        sheet.Comments[unrelated].Should().Be("Unrelated note");
        sheet.CommentAuthors[unrelated].Should().Be("Carol");

        command.Revert(context);
        sheet.Comments[unrelated].Should().Be("Unrelated note");
        sheet.CommentAuthors[unrelated].Should().Be("Carol");
    }

}
