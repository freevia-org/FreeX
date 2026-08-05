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
            source.Should().NotContain("new DocumentCommandBus(");
            source.Should().NotContain("new RemoveBookmarkCommand(");
            source.Should().NotContain("class ViewContext");
        }
    }

    [Fact]
    public void PortableSessionHasNoRendererDependencies()
    {
        var source = ReadSource(
            "freew", "FreeW.App.Presentation", "Editing", "DocumentEditingSession.cs");

        source.Should().NotContain("using Avalonia");
        source.Should().NotContain("using System.Windows");
        source.Should().NotContain("DocumentView");
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }
}
