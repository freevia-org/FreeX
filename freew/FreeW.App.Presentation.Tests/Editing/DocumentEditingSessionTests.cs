using FreeW.App.Presentation.Editing;

namespace FreeW.App.Presentation.Tests.Editing;

public sealed class DocumentEditingSessionTests
{
    [Fact]
    public void LoadDocument_ReplacesTheAuthoritativeDocumentAndResetsHistory()
    {
        var session = new DocumentEditingSession();
        session.InsertBlockAfter(0, new Paragraph("old edit"));
        session.Commands.CanUndo.Should().BeTrue();

        var replacement = DocumentWith("replacement");
        session.LoadDocument(replacement);

        session.Document.Should().BeSameAs(replacement);
        session.Commands.CanUndo.Should().BeFalse();
        session.Commands.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void InsertBlocksAfter_ClampsCaretAndGroupsTheMutationForUndoRedo()
    {
        var document = DocumentWith("body");
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        var changed = 0;
        session.Changed += () => changed++;

        var insertedAt = session.InsertBlocksAfter(
            99,
            [new Paragraph("first"), new Paragraph("second")],
            "Insert pair");

        insertedAt.Should().Be(1);
        document.Blocks.Cast<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().Equal("body", "first", "second");
        changed.Should().Be(1);

        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Cast<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().Equal("body");
        session.Commands.Redo().Should().BeTrue();
        document.Blocks.Cast<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().Equal("body", "first", "second");
    }

    [Fact]
    public void InsertDocumentAfter_ClonesContentTransfersStylesAndUsesOneUndoEntry()
    {
        var target = DocumentWith("target");
        var source = DocumentWith("source one", "source two");
        source.Styles["Imported"] = new DocumentStyle { Id = "Imported", Name = "Imported" };
        ((Paragraph)source.Blocks[0]).StyleId = "Imported";
        var session = new DocumentEditingSession();
        session.LoadDocument(target);

        session.InsertDocumentAfter(0, source).Should().Be(1);

        target.Blocks.Cast<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().Equal("target", "source one", "source two");
        target.Blocks[1].Should().NotBeSameAs(source.Blocks[0]);
        target.Styles.Should().ContainKey("Imported");
        session.Commands.Undo().Should().BeTrue();
        target.Blocks.Cast<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().Equal("target");
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void RemoveBookmark_NormalizesNameAndKeepsTheMutationUndoable()
    {
        var paragraph = new Paragraph("target");
        paragraph.BookmarkNames.Add("Here");
        var document = new TextDocument();
        document.Blocks.Add(paragraph);
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.RemoveBookmark("  Here  ").Should().BeTrue();
        paragraph.BookmarkNames.Should().BeEmpty();
        session.RemoveBookmark("missing").Should().BeFalse();

        session.Commands.Undo().Should().BeTrue();
        paragraph.BookmarkNames.Should().Equal("Here");
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void ReplaceTrackedBodyText_NormalizesSelectionAndKeepsOneUndoEntry()
    {
        var document = DocumentWith("abcdef");
        var session = DeterministicTrackedSession();
        session.LoadDocument(document);
        var changed = 0;
        session.Changed += () => changed++;

        var applied = session.TryReplaceTrackedBodyText(
            new DocumentTextRange(
                new DocumentTextPosition(0, 5),
                new DocumentTextPosition(0, 2)),
            "Z",
            formatting: null,
            out var result);

        applied.Should().BeTrue();
        result.Caret.Should().Be(new DocumentTextPosition(0, 3));
        result.KeptDeletedText.Should().BeTrue();
        var paragraph = (Paragraph)document.Blocks[0];
        paragraph.PlainText.Should().Be("abZcdef");
        paragraph.Runs.Should().Contain(run =>
            run.Text == "Z"
            && run.Revision == RevisionKind.Inserted
            && run.RevisionAuthor == "Ada"
            && run.RevisionDateXml == "2026-08-05T10:20:30Z");
        paragraph.Runs.Should().Contain(run =>
            run.Text == "cde"
            && run.Revision == RevisionKind.Deleted
            && run.RevisionAuthor == "Ada");
        changed.Should().Be(1);

        session.Commands.Undo().Should().BeTrue();
        paragraph.PlainText.Should().Be("abcdef");
        paragraph.Runs.Should().OnlyContain(run => run.Revision == RevisionKind.None);
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void InsertTrackedBodyText_PreservesRendererFormattingAndExplicitLinkPolicy()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("link", RunFormatting.Default)
        {
            HyperlinkUrl = "https://example.test",
        });
        var document = new TextDocument();
        document.Blocks.Add(paragraph);
        var session = DeterministicTrackedSession();
        session.LoadDocument(document);
        var formatting = RunFormatting.Default with { Bold = true };

        session.TryInsertTrackedBodyText(
                new DocumentTextPosition(0, 2),
                "X",
                formatting,
                hyperlink: null,
                out var result)
            .Should().BeTrue();

        result.Caret.Should().Be(new DocumentTextPosition(0, 3));
        var inserted = paragraph.Runs.Single(run => run.Text == "X");
        inserted.Formatting.Should().Be(formatting);
        inserted.Revision.Should().Be(RevisionKind.Inserted);
        inserted.HyperlinkUrl.Should().BeNull();
    }

    [Fact]
    public void InsertTrackedBodyText_CanInheritThePreviousModelLink()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("link", RunFormatting.Default)
        {
            HyperlinkUrl = "https://example.test",
            HyperlinkTooltip = "Example",
        });
        var document = new TextDocument();
        document.Blocks.Add(paragraph);
        var session = DeterministicTrackedSession();
        session.LoadDocument(document);

        session.TryInsertTrackedBodyText(
                new DocumentTextPosition(0, 4),
                "X",
                formatting: null,
                out _)
            .Should().BeTrue();

        var inserted = paragraph.Runs.Single(run => run.Text == "X");
        inserted.HyperlinkUrl.Should().Be("https://example.test");
        inserted.HyperlinkTooltip.Should().Be("Example");
    }

    [Fact]
    public void DeleteTrackedBodyText_ReportsForwardAndCollapsedCaretOutcomes()
    {
        var document = DocumentWith("abc");
        var session = DeterministicTrackedSession();
        session.LoadDocument(document);

        session.TryDeleteTrackedBodyText(
                new DocumentTextRange(
                    new DocumentTextPosition(0, 0),
                    new DocumentTextPosition(0, 1)),
                advancePastKeptText: true,
                out var retained)
            .Should().BeTrue();

        retained.KeptDeletedText.Should().BeTrue();
        retained.Caret.Should().Be(new DocumentTextPosition(0, 1));
        ((Paragraph)document.Blocks[0]).PlainText.Should().Be("abc");

        var ownInsertion = new Paragraph();
        ownInsertion.Runs.Add(new Run("X", RunFormatting.Default)
        {
            Revision = RevisionKind.Inserted,
            RevisionAuthor = "Ada",
        });
        var ownDocument = new TextDocument();
        ownDocument.Blocks.Add(ownInsertion);
        session.LoadDocument(ownDocument);

        session.TryDeleteTrackedBodyText(
                new DocumentTextRange(
                    new DocumentTextPosition(0, 0),
                    new DocumentTextPosition(0, 1)),
                advancePastKeptText: true,
                out var collapsed)
            .Should().BeTrue();

        collapsed.KeptDeletedText.Should().BeFalse();
        collapsed.Caret.Should().Be(new DocumentTextPosition(0, 0));
        ownInsertion.PlainText.Should().BeEmpty();
    }

    [Fact]
    public void TrackedBodyTextOperations_RejectCrossParagraphAndCollapsedDeleteTargets()
    {
        var document = DocumentWith("first", "second");
        var session = DeterministicTrackedSession();
        session.LoadDocument(document);

        session.TryReplaceTrackedBodyText(
                new DocumentTextRange(
                    new DocumentTextPosition(0, 2),
                    new DocumentTextPosition(1, 2)),
                "X",
                formatting: null,
                out _)
            .Should().BeFalse();
        session.TryDeleteTrackedBodyText(
                new DocumentTextRange(
                    new DocumentTextPosition(0, 99),
                    new DocumentTextPosition(0, 99)),
                advancePastKeptText: false,
                out _)
            .Should().BeFalse();

        document.Blocks.Cast<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().Equal("first", "second");
        session.Commands.CanUndo.Should().BeFalse();
    }

    private static DocumentEditingSession DeterministicTrackedSession() =>
        new(() => "Ada", () => "2026-08-05T10:20:30Z");

    private static TextDocument DocumentWith(params string[] paragraphs)
    {
        var document = new TextDocument();
        foreach (var text in paragraphs)
            document.Blocks.Add(new Paragraph(text));
        return document;
    }
}

public sealed class DocumentEditingSessionSourceOwnershipTests
{
    [Fact]
    public void BothRenderersDelegateDocumentOwnershipAndMigratedMutationsToThePortableSession()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("DocumentEditingSession _editingSession");
            source.Should().Contain("_editingSession.LoadDocument(document)");
            source.Should().Contain("_editingSession.InsertBlockAfter(");
            source.Should().Contain("_editingSession.InsertBlocksAfter(");
            source.Should().Contain("_editingSession.InsertDocumentAfter(");
            source.Should().Contain("_editingSession.RemoveBookmark(name)");
            source.Should().Contain("new DocumentTextPosition(");
            source.Should().Contain("_editingSession.TryDeleteTrackedBodyText(");
            source.Should().NotContain("new DocumentCommandBus(");
            source.Should().NotContain("new RemoveBookmarkCommand(");
            source.Should().NotContain("class ViewContext");
        }

        wpf.Should().Contain("_editingSession.TryReplaceTrackedBodyText(");
        wpf.Should().NotContain("RevisionEditPlanner.DeleteRangeAsRevision(");
        wpf.Should().NotContain("RevisionEditPlanner.InsertText(");
        avalonia.Should().Contain("_editingSession.TryInsertTrackedBodyText(");
        System.Text.RegularExpressions.Regex.Matches(
                avalonia,
                "_editingSession\\.TryDeleteTrackedBodyText\\(")
            .Count.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void PortableSessionHasNoRendererDependencies()
    {
        var source = ReadSource(
            "freew", "FreeW.App.Presentation", "Editing", "DocumentEditingSession.cs");

        source.Should().NotContain("using Avalonia");
        source.Should().NotContain("using System.Windows");
        source.Should().NotContain("DocumentView");
        source.Should().NotContain("TextPointer");
        source.Should().NotContain("DocPosition");
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }
}
