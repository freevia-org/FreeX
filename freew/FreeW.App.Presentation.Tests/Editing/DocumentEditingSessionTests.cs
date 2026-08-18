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
    public void ReplaceTrackedBodyText_PreservesExplicitHyperlinkEdgePolicy()
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

        session.TryReplaceTrackedBodyText(
                new DocumentTextRange(
                    new DocumentTextPosition(0, 4),
                    new DocumentTextPosition(0, 4)),
                "X",
                RunFormatting.Default,
                hyperlink: null,
                out _)
            .Should().BeTrue();

        paragraph.Runs.Single(run => run.Text == "X").HyperlinkUrl.Should().BeNull();
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
    public void TrackedBodyTextOperations_HandleCrossParagraphReplacementAsOneUndoableEdit()
    {
        var document = DocumentWith("first", "second");
        var session = DeterministicTrackedSession();
        session.LoadDocument(document);
        var changed = 0;
        session.Changed += () => changed++;

        session.TryReplaceTrackedBodyText(
                new DocumentTextRange(
                    new DocumentTextPosition(0, 2),
                    new DocumentTextPosition(1, 2)),
                "X",
                formatting: null,
                out var result)
            .Should().BeTrue();

        result.Should().Be(new DocumentTextEditResult(
            new DocumentTextPosition(0, 3),
            KeptDeletedText: true));
        var first = (Paragraph)document.Blocks[0];
        var second = (Paragraph)document.Blocks[1];
        first.PlainText.Should().Be("fiXrst");
        first.Runs.Should().Contain(run => run.Text == "X" && run.Revision == RevisionKind.Inserted);
        first.Runs.Should().Contain(run => run.Text == "rst" && run.Revision == RevisionKind.Deleted);
        first.MarkRevision.Should().Be(RevisionKind.Deleted);
        second.Runs.Should().Contain(run => run.Text == "se" && run.Revision == RevisionKind.Deleted);
        changed.Should().Be(1);

        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Cast<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().Equal("first", "second");
        document.Blocks.Cast<Paragraph>().Should().OnlyContain(paragraph =>
            paragraph.MarkRevision == RevisionKind.None
            && paragraph.Runs.All(run => run.Revision == RevisionKind.None));
        session.Commands.CanUndo.Should().BeFalse();

        session.TryDeleteTrackedBodyText(
                new DocumentTextRange(
                    new DocumentTextPosition(0, 99),
                    new DocumentTextPosition(0, 99)),
                advancePastKeptText: false,
                out _)
            .Should().BeFalse();

        document.Blocks.Cast<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().Equal("first", "second");
    }

    [Fact]
    public void ReplaceBodyText_ReplacesSelectionAndUndoesAsOneEdit()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("abcdef", RunFormatting.Default with { Italic = true }));
        var document = new TextDocument();
        document.Blocks.Add(paragraph);
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        var changed = 0;
        session.Changed += () => changed++;

        session.TryReplaceBodyText(
                new DocumentTextRange(
                    new DocumentTextPosition(0, 5),
                    new DocumentTextPosition(0, 2)),
                "Z",
                formatting: null,
                out var result)
            .Should().BeTrue();

        result.Caret.Should().Be(new DocumentTextPosition(0, 3));
        paragraph.PlainText.Should().Be("abZf");
        paragraph.Runs.Should().ContainSingle();
        paragraph.Runs[0].Formatting.Italic.Should().BeTrue();
        changed.Should().Be(1);

        session.Commands.Undo().Should().BeTrue();
        paragraph.PlainText.Should().Be("abcdef");
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void ReplaceBodyText_AcrossParagraphsPreservesPrefixSuffixAndOneUndoEntry()
    {
        var document = DocumentWith("first", "middle", "last");
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.TryReplaceBodyText(
                new DocumentTextRange(
                    new DocumentTextPosition(0, 2),
                    new DocumentTextPosition(2, 2)),
                "X",
                RunFormatting.Default with { Bold = true },
                hyperlink: null,
                out var result)
            .Should().BeTrue();

        document.Blocks.Should().ContainSingle();
        ((Paragraph)document.Blocks[0]).PlainText.Should().Be("fiXst");
        result.Caret.Should().Be(new DocumentTextPosition(0, 3));

        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Cast<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().Equal("first", "middle", "last");
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void DeleteBodyText_HandlesCharacterAndCrossParagraphRanges()
    {
        var document = DocumentWith("abc", "def");
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.TryDeleteBodyText(
                new DocumentTextRange(
                    new DocumentTextPosition(0, 1),
                    new DocumentTextPosition(0, 2)),
                out var characterResult)
            .Should().BeTrue();
        ((Paragraph)document.Blocks[0]).PlainText.Should().Be("ac");
        characterResult.Caret.Should().Be(new DocumentTextPosition(0, 1));

        session.TryDeleteBodyText(
                new DocumentTextRange(
                    new DocumentTextPosition(0, 1),
                    new DocumentTextPosition(1, 1)),
                out var rangeResult)
            .Should().BeTrue();
        document.Blocks.Should().ContainSingle();
        ((Paragraph)document.Blocks[0]).PlainText.Should().Be("aef");
        rangeResult.Caret.Should().Be(new DocumentTextPosition(0, 1));

        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Cast<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().Equal("ac", "def");
        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Cast<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().Equal("abc", "def");
    }

    [Fact]
    public void MergeBodyParagraphs_UsesImmediateBoundariesAndOneUndoEntry()
    {
        var document = DocumentWith("first", "second", "third");
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.TryMergeBodyParagraphWithPrevious(1, out var backward).Should().BeTrue();
        backward.Caret.Should().Be(new DocumentTextPosition(0, 5));
        document.Blocks.Cast<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().Equal("firstsecond", "third");
        session.Commands.Undo().Should().BeTrue();

        session.TryMergeBodyParagraphWithNext(1, out var forward).Should().BeTrue();
        forward.Caret.Should().Be(new DocumentTextPosition(1, 6));
        document.Blocks.Cast<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().Equal("first", "secondthird");
        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Cast<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().Equal("first", "second", "third");
    }

    [Fact]
    public void InsertBodyParagraphBreak_SplitsSelectionAndContinuesListFormatting()
    {
        var paragraph = new Paragraph("abcdef")
        {
            StyleId = "List Paragraph",
            Formatting = ParagraphFormatting.Default with
            {
                ListKind = ListKind.Number,
                ListLevel = 2,
            },
        };
        var document = new TextDocument();
        document.Blocks.Add(paragraph);
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.TryInsertBodyParagraphBreak(
                new DocumentTextRange(
                    new DocumentTextPosition(0, 2),
                    new DocumentTextPosition(0, 4)),
                out var result)
            .Should().BeTrue();

        result.Caret.Should().Be(new DocumentTextPosition(1, 0));
        document.Blocks.Cast<Paragraph>().Select(item => item.PlainText)
            .Should().Equal("ab", "ef");
        document.Blocks.Cast<Paragraph>().Should().OnlyContain(item =>
            item.Formatting.ListKind == ListKind.Number && item.Formatting.ListLevel == 2);
        ((Paragraph)document.Blocks[0]).StyleId.Should().Be("List Paragraph");
        // "List Paragraph" is not registered in this document's Styles catalog (and, per
        // BuiltInStyles.cs, carries no NextStyleId even when it is), so per Word's "style for
        // following paragraph" rule the new paragraph keeps the SAME style rather than losing it.
        ((Paragraph)document.Blocks[1]).StyleId.Should().Be("List Paragraph");

        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Should().ContainSingle();
        ((Paragraph)document.Blocks[0]).PlainText.Should().Be("abcdef");
    }

    [Fact]
    public void InsertBodyParagraphBreak_AtHeadingEnd_AppliesTheHeadingsNextStyle()
    {
        // Heading1's NextStyleId is "Normal" (BuiltInStyles.cs) -- Word's "style for following
        // paragraph" -- so pressing Enter at the end of a Heading1 paragraph must start the new
        // paragraph in Normal, not with no style at all.
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Chapter One") { StyleId = "Heading1" });
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.TryInsertBodyParagraphBreak(
                new DocumentTextRange(
                    new DocumentTextPosition(0, 11),
                    new DocumentTextPosition(0, 11)),
                out var result)
            .Should().BeTrue();

        document.Blocks.Cast<Paragraph>().Select(item => item.PlainText)
            .Should().Equal("Chapter One", string.Empty);
        ((Paragraph)document.Blocks[0]).StyleId.Should().Be("Heading1");
        ((Paragraph)document.Blocks[1]).StyleId.Should().Be("Normal");
        result.Caret.Should().Be(new DocumentTextPosition(1, 0));

        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Should().ContainSingle();
    }

    [Fact]
    public void InsertBodyParagraphBreak_MidHeadingText_AppliesTheHeadingsNextStyleToTheTail()
    {
        // Splitting a paragraph mid-text (not just at its end) must apply the same w:next rule to
        // the newly created tail paragraph.
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("HeadOne Tail") { StyleId = "Heading1" });
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.TryInsertBodyParagraphBreak(
                new DocumentTextRange(
                    new DocumentTextPosition(0, 7),
                    new DocumentTextPosition(0, 8)),
                out _)
            .Should().BeTrue();

        document.Blocks.Cast<Paragraph>().Select(item => item.PlainText)
            .Should().Equal("HeadOne", "Tail");
        ((Paragraph)document.Blocks[0]).StyleId.Should().Be("Heading1");
        ((Paragraph)document.Blocks[1]).StyleId.Should().Be("Normal");
    }

    [Fact]
    public void InsertBodyParagraphBreak_StyleWithNoNextStyleId_KeepsTheSameStyle()
    {
        // A custom style with no w:next (e.g. a user-authored "Quote" style) means Word keeps the
        // SAME style on the paragraph created by Enter -- it never blanks the style.
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Styles["Quote2"] = new DocumentStyle { Id = "Quote2", Name = "My Quote" };
        document.Blocks.Add(new Paragraph("quoted text") { StyleId = "Quote2" });
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.TryInsertBodyParagraphBreak(
                new DocumentTextRange(
                    new DocumentTextPosition(0, 11),
                    new DocumentTextPosition(0, 11)),
                out _)
            .Should().BeTrue();

        ((Paragraph)document.Blocks[0]).StyleId.Should().Be("Quote2");
        ((Paragraph)document.Blocks[1]).StyleId.Should().Be("Quote2");
    }

    [Fact]
    public void InsertBodyParagraphBreak_NextStyleIdNamesAMissingStyle_KeepsTheSameStyle()
    {
        // A dangling NextStyleId (points at a style no longer in the catalog) must not blank the
        // new paragraph's style either -- Word falls back to keeping the current style.
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Styles["Dangling"] = new DocumentStyle
        {
            Id = "Dangling",
            Name = "Dangling",
            NextStyleId = "DoesNotExist",
        };
        document.Blocks.Add(new Paragraph("body text") { StyleId = "Dangling" });
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.TryInsertBodyParagraphBreak(
                new DocumentTextRange(
                    new DocumentTextPosition(0, 9),
                    new DocumentTextPosition(0, 9)),
                out _)
            .Should().BeTrue();

        ((Paragraph)document.Blocks[1]).StyleId.Should().Be("Dangling");
    }

    [Fact]
    public void InsertBodyParagraphBreak_OnEmptyListItemExitsListWithoutAddingBlock()
    {
        var paragraph = new Paragraph
        {
            Formatting = ParagraphFormatting.Default with
            {
                ListKind = ListKind.Bullet,
                ListLevel = 1,
            },
        };
        var document = new TextDocument();
        document.Blocks.Add(paragraph);
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.TryInsertBodyParagraphBreak(
                new DocumentTextRange(
                    new DocumentTextPosition(0, 0),
                    new DocumentTextPosition(0, 0)),
                out var result)
            .Should().BeTrue();

        document.Blocks.Should().ContainSingle();
        ((Paragraph)document.Blocks[0]).Formatting.ListKind.Should().Be(ListKind.None);
        ((Paragraph)document.Blocks[0]).Formatting.ListLevel.Should().Be(0);
        result.Caret.Should().Be(new DocumentTextPosition(0, 0));
        session.Commands.Undo().Should().BeTrue();
        ((Paragraph)document.Blocks[0]).Formatting.ListKind.Should().Be(ListKind.Bullet);
    }

    [Fact]
    public void OrdinaryBodyOperations_RejectTableAndStructuredParagraphBoundaries()
    {
        var document = DocumentWith("before", "after");
        document.Blocks.Insert(1, new Table());
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.TryDeleteBodyText(
                new DocumentTextRange(
                    new DocumentTextPosition(0, 2),
                    new DocumentTextPosition(2, 2)),
                out _)
            .Should().BeFalse();
        session.TryMergeBodyParagraphWithPrevious(2, out _).Should().BeFalse();
        session.TryMergeBodyParagraphWithNext(0, out _).Should().BeFalse();
        session.Commands.CanUndo.Should().BeFalse();
        document.Blocks.Should().HaveCount(3);
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
            source.Should().Contain("_editingSession.Body.TryApplyTextInput(");
            source.Should().Contain("_editingSession.Body.TryApplyDeletion(");
            source.Should().Contain("_editingSession.Body.TryApplyParagraphBreak(");
            source.Should().NotContain("_editingSession.TryDeleteTrackedBodyText(");
            source.Should().NotContain("_editingSession.TryReplaceTrackedBodyText(");
            source.Should().NotContain("_editingSession.TryReplaceBodyText(");
            source.Should().NotContain("_editingSession.TryDeleteBodyText(");
            source.Should().NotContain("_editingSession.TryInsertBodyParagraphBreak(");
            source.Should().NotContain("_editingSession.TryMergeBodyParagraphWithPrevious(");
            source.Should().NotContain("_editingSession.TryMergeBodyParagraphWithNext(");
            source.Should().NotContain("new DocumentCommandBus(");
            source.Should().NotContain("new RemoveBookmarkCommand(");
            source.Should().NotContain("class ViewContext");
        }

        wpf.Should().NotContain("RevisionEditPlanner.DeleteRangeAsRevision(");
        wpf.Should().NotContain("RevisionEditPlanner.InsertText(");
        avalonia.Should().NotContain("BackspaceOutdentListItem");
    }

    [Fact]
    public void PortableSessionHasNoRendererDependencies()
    {
        var sources = new[]
        {
            ReadSource(
                "freew", "FreeW.App.Presentation", "Editing", "DocumentEditingSession.cs"),
            ReadSource(
                "freew", "FreeW.App.Presentation", "Editing", "DocumentBodyEditingCoordinator.cs"),
        };

        foreach (var source in sources)
        {
            source.Should().NotContain("using Avalonia");
            source.Should().NotContain("using System.Windows");
            source.Should().NotContain("FreeW.App.Host.Editing")
                .And.NotContain("FreeW.App.Avalonia.Editing");
            source.Should().NotContain("TextPointer");
            source.Should().NotContain("DocPosition");
        }
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }
}
