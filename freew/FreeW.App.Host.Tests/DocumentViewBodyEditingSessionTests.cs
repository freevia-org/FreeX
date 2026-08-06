using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Editing;

namespace FreeW.App.Host.Tests;

public sealed class DocumentViewBodyEditingSessionTests
{
    [StaFact]
    public void UntrackedSelectionReplacement_UsesPortableUndoHistory()
    {
        var view = BuildView("abcdef");
        view.SetSelectionRangeForTest(0, 2, 0, 5);

        view.InsertText("Z");

        Paragraphs(view).Should().Equal("abZf");
        view.Commands.CanUndo.Should().BeTrue();
        view.Commands.Undo().Should().BeTrue();
        Paragraphs(view).Should().Equal("abcdef");
        view.Commands.CanUndo.Should().BeFalse();
    }

    [StaFact]
    public void CrossParagraphSelectionReplacement_UsesModelRelativeWpfPositions()
    {
        var view = BuildView("one", "two");
        view.SetSelectionRangeForTest(0, 1, 1, 1);

        view.InsertText("X");

        Paragraphs(view).Should().Equal("oXwo");
        view.Commands.Undo().Should().BeTrue();
        Paragraphs(view).Should().Equal("one", "two");
    }

    [StaFact]
    public void CharacterBackspaceAndDelete_UsePortableUndoHistory()
    {
        var backward = BuildView("abc");
        backward.MoveCaretToBlockForTest(0, 3);
        backward.BackspaceForTest();
        Paragraphs(backward).Should().Equal("ab");
        backward.Commands.Undo().Should().BeTrue();
        Paragraphs(backward).Should().Equal("abc");

        var forward = BuildView("abc");
        forward.MoveCaretToBlockForTest(0, 0);
        forward.DeleteForwardForTest();
        Paragraphs(forward).Should().Equal("bc");
        forward.Commands.Undo().Should().BeTrue();
        Paragraphs(forward).Should().Equal("abc");
    }

    [StaFact]
    public void BackspaceAndDeleteAtParagraphBoundaries_JoinThroughPortableSession()
    {
        var backward = BuildView("one", "two");
        backward.MoveCaretToBlockForTest(1, 0);

        backward.BackspaceForTest();

        Paragraphs(backward).Should().Equal("onetwo");
        backward.Commands.Undo().Should().BeTrue();
        Paragraphs(backward).Should().Equal("one", "two");

        var forward = BuildView("one", "two");
        forward.MoveCaretToBlockForTest(0, 3);

        forward.DeleteForwardForTest();

        Paragraphs(forward).Should().Equal("onetwo");
        forward.Commands.Undo().Should().BeTrue();
        Paragraphs(forward).Should().Equal("one", "two");
    }

    [StaFact]
    public void BackspaceAtListStart_UsesPortableOutdentTransition()
    {
        var paragraph = new Paragraph("item")
        {
            Formatting = new ParagraphFormatting
            {
                ListKind = ListKind.Bullet,
                ListLevel = 1,
            },
        };
        var view = BuildView(paragraph);
        view.MoveCaretToBlockForTest(0, 0);
        view.BodyTextRangeForTest().Should().Be(new DocumentTextRange(
            new DocumentTextPosition(0, 0),
            new DocumentTextPosition(0, 0)));

        view.BackspaceForTest();

        ((Paragraph)view.Model.Blocks[0]).Formatting.ListLevel.Should().Be(0);
        view.Commands.Undo().Should().BeTrue();
        ((Paragraph)view.Model.Blocks[0]).Formatting.ListLevel.Should().Be(1);
    }

    [StaFact]
    public void ParagraphBreakOverSelection_SplitsAndUndoesInOneStep()
    {
        var view = BuildView("abcdef");
        view.SetSelectionRangeForTest(0, 2, 0, 4);

        view.InsertParagraphBreakForTest();

        Paragraphs(view).Should().Equal("ab", "ef");
        view.Commands.Undo().Should().BeTrue();
        Paragraphs(view).Should().Equal("abcdef");
        view.Commands.CanUndo.Should().BeFalse();
    }

    private static DocumentView BuildView(params string[] paragraphs)
    {
        var document = new TextDocument();
        foreach (var text in paragraphs)
            document.Blocks.Add(new Paragraph(text));
        var view = new DocumentView();
        view.LoadModel(document);
        return view;
    }

    private static DocumentView BuildView(params Paragraph[] paragraphs)
    {
        var document = new TextDocument();
        document.Blocks.AddRange(paragraphs);
        var view = new DocumentView();
        view.LoadModel(document);
        return view;
    }

    private static IEnumerable<string> Paragraphs(DocumentView view) =>
        view.Model.Blocks.Cast<Paragraph>().Select(paragraph => paragraph.PlainText);
}
