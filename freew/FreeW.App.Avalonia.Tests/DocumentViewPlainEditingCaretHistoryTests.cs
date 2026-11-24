using Avalonia;
using Avalonia.Automation;
using Avalonia.Headless;
using Avalonia.Input;
using Free.Shared.AppServices;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Options;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentViewPlainEditingCaretHistoryTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public Task ConsecutiveAsciiTypingThenTwoUndosKeepsTheOriginalInsertionPoint() => Session.Dispatch(() =>
    {
        var view = View("Abcd");
        Key(view, global::Avalonia.Input.Key.Home);
        Key(view, global::Avalonia.Input.Key.Right);
        Key(view, global::Avalonia.Input.Key.Right);
        view.SimulateTextInputForTest("xy");
        Paragraphs(view).Should().Equal("Abxycd");
        Key(view, global::Avalonia.Input.Key.Z, KeyModifiers.Meta);
        Key(view, global::Avalonia.Input.Key.Z, KeyModifiers.Meta);
        view.SimulateTextInputForTest("q");
        Paragraphs(view).Should().Equal("Abqcd");
    }, CancellationToken.None);

    [Fact]
    public Task AsciiInsertionUndoRedoRestoresBothCaretPositionsAndSubsequentTyping() => Session.Dispatch(() =>
    {
        var view = View("abcd");
        view.MoveCaretToBlockForTest(0, 2);
        view.SimulateTextInputForTest("X");
        view.CaretPositionForTest.Should().Be((0, 3));
        view.Undo();
        Paragraphs(view).Should().Equal("abcd");
        view.CaretPositionForTest.Should().Be((0, 2));
        view.Redo();
        view.CaretPositionForTest.Should().Be((0, 3));
        view.SimulateTextInputForTest("Y");
        Paragraphs(view).Should().Equal("abXYcd");
        view.Undo();
        view.Undo();
        view.SimulateTextInputForTest("Q");
        Paragraphs(view).Should().Equal("abQcd");
    }, CancellationToken.None);

    [Fact]
    public Task AsciiInsertionPublishesItsFinalCaretToAccessibilityObservers() => Session.Dispatch(() =>
    {
        var view = View("abcd");
        view.MoveCaretToBlockForTest(0, 2);
        var observed = new List<int>();
        view.CaretMoved += () => observed.Add(view.AutomationSnapshot().CaretOffset);
        view.SimulateTextInputForTest("X");
        observed.Should().NotBeEmpty();
        observed.Should().OnlyContain(offset => offset == 3);
        view.AutomationSnapshot().CaretOffset.Should().Be(3);
    }, CancellationToken.None);

    [Theory]
    [InlineData("insert")]
    [InlineData("backspace")]
    [InlineData("enter")]
    public Task CachedAutomationHelpMatchesTheFinalCaretImmediatelyAfterEditing(string operation) => Session.Dispatch(() =>
    {
        var view = View("abcd");
        view.MoveCaretToBlockForTest(0, 2);
        view.CreateAutomationPeerForTests();
        if (operation == "insert") view.SimulateTextInputForTest("X");
        else Key(view, operation == "backspace" ? global::Avalonia.Input.Key.Back : global::Avalonia.Input.Key.Enter);
        // Read the attached property exported to the native bridge, rather than asking the peer
        // to compute a fresh semantic value and conceal a stale notification.
        AutomationProperties.GetHelpText(view).Should().Be(view.AutomationSelectionStatus());
        if (operation == "insert") view.CaretPositionForTest.Should().Be((0, 3));
        else if (operation == "backspace") view.CaretPositionForTest.Should().Be((0, 1));
        else view.CaretPositionForTest.Should().Be((1, 0));
    }, CancellationToken.None);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task DeletingAnAsciiSelectionRestoresItsDirectionOnUndo(bool backward, bool reversed) => Session.Dispatch(() =>
    {
        var view = View("abcdef");
        Select(view, reversed);
        Key(view, backward ? global::Avalonia.Input.Key.Back : global::Avalonia.Input.Key.Delete);
        Paragraphs(view).Should().Equal("abef");
        view.CaretPositionForTest.Should().Be((0, 2));
        view.Undo();
        AssertSelection(view, reversed);
        view.Redo();
        Paragraphs(view).Should().Equal("abef");
        view.CaretPositionForTest.Should().Be((0, 2));
        view.Undo();
        view.SimulateTextInputForTest("Q");
        Paragraphs(view).Should().Equal("abQef");
    }, CancellationToken.None);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task EnterOverSelectionIsOneUndoAndRestoresTheSelection(bool reversed) => Session.Dispatch(() =>
    {
        var view = View("abcdef");
        Select(view, reversed);
        Key(view, global::Avalonia.Input.Key.Enter);
        Paragraphs(view).Should().Equal("ab", "ef");
        view.CaretPositionForTest.Should().Be((1, 0));
        view.Undo();
        AssertSelection(view, reversed);
        view.CanUndo.Should().BeFalse();
        view.Redo();
        Paragraphs(view).Should().Equal("ab", "ef");
        view.CaretPositionForTest.Should().Be((1, 0));
        view.Undo();
        view.SimulateTextInputForTest("Q");
        Paragraphs(view).Should().Equal("abQef");
    }, CancellationToken.None);

    [Fact]
    public Task EnterAtCaretRestoresTheUnsplitInsertionPoint() => Session.Dispatch(() =>
    {
        var view = View("abcd");
        view.MoveCaretToBlockForTest(0, 2);
        Key(view, global::Avalonia.Input.Key.Enter);
        Paragraphs(view).Should().Equal("ab", "cd");
        view.CaretPositionForTest.Should().Be((1, 0));
        view.Undo();
        view.CaretPositionForTest.Should().Be((0, 2));
        view.Redo();
        view.CaretPositionForTest.Should().Be((1, 0));
        view.SimulateTextInputForTest("Q");
        Paragraphs(view).Should().Equal("ab", "Qcd");
    }, CancellationToken.None);

    [Fact]
    public Task BackspaceParagraphMergeRestoresTheSecondParagraphCaret() => Session.Dispatch(() =>
    {
        var view = View("ab", "cd");
        view.MoveCaretToBlockForTest(1, 0);
        Key(view, global::Avalonia.Input.Key.Back);
        Paragraphs(view).Should().Equal("abcd");
        view.CaretPositionForTest.Should().Be((0, 2));
        view.Undo();
        Paragraphs(view).Should().Equal("ab", "cd");
        view.CaretPositionForTest.Should().Be((1, 0));
        view.Redo();
        view.CaretPositionForTest.Should().Be((0, 2));
        view.Undo();
        view.SimulateTextInputForTest("Q");
        Paragraphs(view).Should().Equal("ab", "Qcd");
    }, CancellationToken.None);

    [Theory]
    [InlineData("plain", false)]
    [InlineData("plain", true)]
    [InlineData("multiline", false)]
    [InlineData("multiline", true)]
    [InlineData("rich", false)]
    [InlineData("rich", true)]
    public Task ProductionPasteIsOneUndoAndRestoresSelectionBeforeTheNextTyping(string kind, bool reversed) => Session.Dispatch(() =>
    {
        var clipboard = new Clipboard(kind);
        var window = new MainWindow([], null,
            InMemoryApplicationOptionsStore<FreeWOptions>.ForProductFile(PlatformApplicationDataPathProvider.LocalInstance),
            platformClipboard: clipboard);
        try
        {
            var view = window.Editor;
            view.LoadDocument(Document("abcdef"));
            view.Measure(new Size(816, 4000));
            Select(view, reversed);
            var args = new KeyEventArgs { Key = global::Avalonia.Input.Key.V, KeyModifiers = KeyModifiers.Meta };
            window.RaiseKeyDownForTest(args);
            args.Handled.Should().BeTrue();
            var expected = kind == "multiline" ? new[] { "abX", "Yef" } : ["abXYef"];
            Paragraphs(view).Should().Equal(expected);
            view.Undo();
            AssertSelection(view, reversed);
            view.CanUndo.Should().BeFalse("paste replaces the selection as one user action");
            view.Redo();
            Paragraphs(view).Should().Equal(expected);
            view.CaretPositionForTest.Should().Be(kind == "multiline" ? (1, 1) : (0, 4));
            view.Undo();
            view.SimulateTextInputForTest("Q");
            Paragraphs(view).Should().Equal("abQef");
        }
        finally { window.Close(); }
    }, CancellationToken.None);

    [Theory]
    [InlineData("cell")]
    [InlineData("header")]
    [InlineData("shape")]
    public Task PlainInsertionHistoryReturnsToItsStoryAfterSwitchingToBody(string story) => Session.Dispatch(() =>
    {
        var document = Document("body untouched");
        var paragraph = new Paragraph("abcd");
        if (story == "cell")
        {
            var table = Table.Create(1, 1);
            table.Rows[0].Cells[0].Paragraphs.Clear();
            table.Rows[0].Cells[0].Paragraphs.Add(paragraph);
            document.Blocks.Add(table);
        }
        else if (story == "header")
        {
            document.FinalSectionHeadersFooters.Header = new HeaderFooter("abcd");
            paragraph = document.FinalSectionHeadersFooters.Header.Paragraphs[0];
        }
        else
        {
            var shape = new Shape(ShapeKind.TextBox, 144, 108, "#FFFFFF")
            { Placement = new FloatingPlacement { Wrapping = ImageWrapping.InFront } };
            shape.TextParagraphs.Add(paragraph);
            ((Paragraph)document.Blocks[0]).Runs.Add(new Run(shape.PlainText) { Shape = shape });
        }
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(816, 4000));
        if (story == "cell") view.PlaceCaretInCell(1, 0, 0, 0, 2);
        else if (story == "header") view.PlaceCaretInHeaderFooter(false, 0, 2);
        else
        {
            view.SelectFloating(0, 1);
            view.EnterSelectedShapeTextEditing().Should().BeTrue();
            view.SelectShapeTextRangeForTest(0, 2, 2);
            view.AutomationSnapshot().CaretOffset.Should().Be(2);
        }
        view.SimulateTextInputForTest("X");
        paragraph.PlainText.Should().Be("abXcd");
        if (story == "shape") view.ExitShapeTextEditing();
        view.MoveCaretToBlock(0, 0);
        view.AutomationSnapshot().CaretOffset.Should().Be(0);
        view.Undo();
        paragraph.PlainText.Should().Be("abcd");
        var documentPrefix = story == "cell" ? ((Paragraph)document.Blocks[0]).PlainText.Length + 1 : 0;
        view.AutomationSnapshot().CaretOffset.Should().Be(documentPrefix + 2);
        if (story == "cell") view.CellCaretInfo.Should().Be((1, 0, 0, 0, 2));
        view.Redo();
        paragraph.PlainText.Should().Be("abXcd");
        view.AutomationSnapshot().CaretOffset.Should().Be(documentPrefix + 3);
        if (story == "cell") view.CellCaretInfo.Should().Be((1, 0, 0, 0, 3));
        view.SimulateTextInputForTest("Q");
        paragraph.PlainText.Should().Be("abXQcd");
        ((Paragraph)document.Blocks[0]).Runs[0].Text.Should().Be("body untouched");
    }, CancellationToken.None);

    private static DocumentView View(params string[] paragraphs)
    {
        var view = new DocumentView();
        view.LoadDocument(Document(paragraphs));
        view.Measure(new Size(816, 4000));
        return view;
    }

    private static TextDocument Document(params string[] paragraphs)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        foreach (var paragraph in paragraphs) document.Blocks.Add(new Paragraph(paragraph));
        return document;
    }

    private static string[] Paragraphs(DocumentView view) =>
        view.Document.Blocks.Cast<Paragraph>().Select(paragraph => paragraph.PlainText).ToArray();

    private static void Select(DocumentView view, bool reversed) =>
        view.SetSelectionRangePublic(0, reversed ? 4 : 2, 0, reversed ? 2 : 4);

    private static void AssertSelection(DocumentView view, bool reversed)
    {
        Paragraphs(view).Should().Equal("abcdef");
        view.SelectedText.Should().Be("cd");
        view.CaretPositionForTest.Should().Be((0, reversed ? 2 : 4));
    }

    private static void Key(DocumentView view, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        var args = new KeyEventArgs { Key = key, KeyModifiers = modifiers, RoutedEvent = DocumentView.KeyDownEvent };
        view.RaiseKeyDownForContextMenuTests(args);
        args.Handled.Should().BeTrue();
    }

    private sealed class Clipboard(string kind) : IPlatformClipboard
    {
        public ValueTask<PlatformClipboardReadResult<PlatformClipboardContent>> ReadAsync(
            PlatformClipboardReadRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformClipboardReadResult<PlatformClipboardContent>.Success(kind == "rich"
                ? new(Text: "XY", CustomData: [PlatformClipboardData.FromText("Rich Text Format", @"{\rtf1\ansi\b XY}")])
                : new(Text: kind == "multiline" ? "X\nY" : "XY")));
        public ValueTask<PlatformClipboardWriteResult> WriteAsync(PlatformClipboardContent content,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(PlatformClipboardWriteResult.Success());
        public ValueTask<PlatformClipboardWriteResult> ClearAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformClipboardWriteResult.Success());
    }
}
