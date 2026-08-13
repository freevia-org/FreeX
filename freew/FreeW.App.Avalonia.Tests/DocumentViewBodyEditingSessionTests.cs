using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentViewBodyEditingSessionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task UntrackedSelectionReplacement_UsesPortableUndoHistory()
    {
        IReadOnlyList<string> edited = [];
        IReadOnlyList<string> undone = [];
        var canUndoAfterUndo = true;

        await Session.Dispatch(() =>
        {
            var view = BuildView("abcdef");
            view.SetSelectionRangePublic(0, 2, 0, 5);

            view.InsertText("Z");

            edited = Paragraphs(view);
            view.Undo();
            undone = Paragraphs(view);
            canUndoAfterUndo = view.CanUndo;
        }, CancellationToken.None);

        edited.Should().Equal("abZf");
        undone.Should().Equal("abcdef");
        canUndoAfterUndo.Should().BeFalse();
    }

    [Fact]
    public async Task CrossParagraphSelectionReplacement_UsesModelRelativeAvaloniaPositions()
    {
        IReadOnlyList<string> edited = [];
        IReadOnlyList<string> undone = [];

        await Session.Dispatch(() =>
        {
            var view = BuildView("one", "two");
            view.SetSelectionRangePublic(0, 1, 1, 1);
            view.InsertText("X");
            edited = Paragraphs(view);
            view.Undo();
            undone = Paragraphs(view);
        }, CancellationToken.None);

        edited.Should().Equal("oXwo");
        undone.Should().Equal("one", "two");
    }

    [Fact]
    public async Task CharacterBackspaceAndDelete_UsePortableUndoHistory()
    {
        IReadOnlyList<string> backwardEdited = [];
        IReadOnlyList<string> backwardUndone = [];
        IReadOnlyList<string> forwardEdited = [];
        IReadOnlyList<string> forwardUndone = [];

        await Session.Dispatch(() =>
        {
            var backward = BuildView("abc");
            backward.MoveCaretToBlock(0, 3);
            backward.BackspacePublic();
            backwardEdited = Paragraphs(backward);
            backward.Undo();
            backwardUndone = Paragraphs(backward);

            var forward = BuildView("abc");
            forward.MoveCaretToBlock(0, 0);
            forward.DeleteForwardPublic();
            forwardEdited = Paragraphs(forward);
            forward.Undo();
            forwardUndone = Paragraphs(forward);
        }, CancellationToken.None);

        backwardEdited.Should().Equal("ab");
        backwardUndone.Should().Equal("abc");
        forwardEdited.Should().Equal("bc");
        forwardUndone.Should().Equal("abc");
    }

    [Fact]
    public async Task BackspaceJoinsAndForwardDeletePreservesAvaloniaBoundaryBehavior()
    {
        IReadOnlyList<string> backwardEdited = [];
        IReadOnlyList<string> backwardUndone = [];
        IReadOnlyList<string> forwardEdited = [];
        IReadOnlyList<string> forwardUndone = [];

        await Session.Dispatch(() =>
        {
            var backward = BuildView("one", "two");
            backward.MoveCaretToBlock(1, 0);
            backward.BackspacePublic();
            backwardEdited = Paragraphs(backward);
            backward.Undo();
            backwardUndone = Paragraphs(backward);

            var forward = BuildView("one", "two");
            forward.MoveCaretToBlock(0, 3);
            forward.DeleteForwardPublic();
            forwardEdited = Paragraphs(forward);
            forward.Undo();
            forwardUndone = Paragraphs(forward);
        }, CancellationToken.None);

        backwardEdited.Should().Equal("onetwo");
        backwardUndone.Should().Equal("one", "two");
        forwardEdited.Should().Equal("one", "two");
        forwardUndone.Should().Equal("one", "two");
    }

    [Fact]
    public async Task ParagraphBreakOverSelection_SplitsAndUndoesInOneStep()
    {
        IReadOnlyList<string> edited = [];
        IReadOnlyList<string> undone = [];

        await Session.Dispatch(() =>
        {
            var view = BuildView("abcdef");
            view.SetSelectionRangePublic(0, 2, 0, 4);

            view.InsertParagraphBreakPublic();

            edited = Paragraphs(view);
            view.Undo();
            undone = Paragraphs(view);
        }, CancellationToken.None);

        edited.Should().Equal("ab", "ef");
        undone.Should().Equal("abcdef");
    }

    private static DocumentView BuildView(params string[] paragraphs)
    {
        var document = new TextDocument();
        foreach (var text in paragraphs)
            document.Blocks.Add(new Paragraph(text));
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(800, 2000));
        return view;
    }

    private static IReadOnlyList<string> Paragraphs(DocumentView view) =>
        view.Document.Blocks.Cast<Paragraph>().Select(paragraph => paragraph.PlainText).ToList();
}
