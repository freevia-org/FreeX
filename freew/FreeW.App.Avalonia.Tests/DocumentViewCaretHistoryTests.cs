using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Free.Shared.AppServices;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Options;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentViewCaretHistoryTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Theory]
    [InlineData("😀", false)]
    [InlineData("e\u0301", false)]
    [InlineData("👩‍💻", false)]
    [InlineData("😀", true)]
    [InlineData("e\u0301", true)]
    [InlineData("👩‍💻", true)]
    public Task ProductionClipboardNavigationAndHistoryPreserveTheNextPaste(string element, bool rich) =>
        Session.Dispatch(() =>
        {
            var text = "a" + element + "b";
            var clipboard = new Clipboard(text, rich);
            var window = new MainWindow([], null,
                InMemoryApplicationOptionsStore<FreeWOptions>.ForProductFile(PlatformApplicationDataPathProvider.LocalInstance),
                platformClipboard: clipboard);
            try
            {
                var view = window.Editor;
                ShellKey(window, Key.V);
                view.Document.PlainText.Should().Be(text);
                EditorKey(view, Key.Left);
                AssertCaret(view, 1 + element.Length);
                EditorKey(view, Key.Back);
                view.Document.PlainText.Should().Be("ab");
                AssertCaret(view, 1);

                // No test caret setter is allowed between these production gestures.
                ShellKey(window, Key.Z);
                view.Document.PlainText.Should().Be(text);
                AssertCaret(view, 1 + element.Length);
                ShellKey(window, Key.Z, KeyModifiers.Meta | KeyModifiers.Shift);
                view.Document.PlainText.Should().Be("ab");
                AssertCaret(view, 1);
                ShellKey(window, Key.Z);
                AssertCaret(view, 1 + element.Length);
                clipboard.Set("X", rich);
                ShellKey(window, Key.V);

                view.Document.PlainText.Should().Be("a" + element + "Xb");
                AssertCaret(view, 2 + element.Length);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

    public static IEnumerable<object[]> StoriesAndElements()
    {
        foreach (var story in new[] { "body", "cell", "header", "shape" })
        foreach (var element in new[] { "😀", "e\u0301", "👩‍💻" })
        foreach (var backward in new[] { true, false })
            yield return [story, element, backward];
    }

    [Theory]
    [MemberData(nameof(StoriesAndElements))]
    public Task HistoryRestoresTheEditingStoryAndBothCaretEndpoints(string story, string element, bool backward) =>
        Session.Dispatch(() =>
        {
            var text = "a" + element + "b";
            var (view, content) = Story(story, text);
            // Start at the story end, then reach the deletion through real navigation.
            EditorKey(view, Key.Left);
            if (!backward) EditorKey(view, Key.Left);
            var before = backward ? 1 + element.Length : 1;
            AssertCaret(view, before);
            EditorKey(view, backward ? Key.Back : Key.Delete);
            content().Should().Be("ab");
            AssertCaret(view, 1);
            var notifications = new List<(int Offset, bool Selected)>();
            view.CaretMoved += () => notifications.Add((view.AutomationSnapshot().CaretOffset,
                view.AutomationSnapshot().Selection is not null));

            EditorKey(view, Key.Z, KeyModifiers.Meta);
            content().Should().Be(text);
            AssertCaret(view, before);
            notifications.Last().Should().Be((before, false));
            EditorKey(view, Key.Z, KeyModifiers.Meta | KeyModifiers.Shift);
            content().Should().Be("ab");
            AssertCaret(view, 1);
            notifications.Last().Should().Be((1, false));
            EditorKey(view, Key.Z, KeyModifiers.Meta);
            AssertCaret(view, before);
            if (story == "cell") view.CellCaretInfo.Should().NotBeNull();
            if (story == "header") view.HeaderFooterCaretInfo.Should().NotBeNull();
            view.InsertText("X");

            content().Should().Be(backward ? "a" + element + "Xb" : "aX" + element + "b");
            AssertCaret(view, before + 1);
            if (story == "shape") ((Paragraph)view.Document.Blocks[0]).Runs[0].Text.Should().Be("body untouched");
            else if (story != "body") ((Paragraph)view.Document.Blocks[story == "cell" ? 1 : 0])
                .PlainText.Should().Be("body untouched");
        }, CancellationToken.None);

    [Fact]
    public Task CollapsingAUnicodeSelectionBeforeDeletionDoesNotReviveItDuringUndo() => Session.Dispatch(() =>
    {
        var (view, content) = Story("body", "a😀b");
        EditorKey(view, Key.Left);
        EditorKey(view, Key.Left, KeyModifiers.Shift);
        view.SelectedText.Should().Be("😀");
        EditorKey(view, Key.Right);
        AssertCaret(view, 3);
        EditorKey(view, Key.Back);
        EditorKey(view, Key.Z, KeyModifiers.Meta);
        AssertCaret(view, 3);
        view.InsertText("X");

        content().Should().Be("a😀Xb");
    }, CancellationToken.None);

    public static IEnumerable<object[]> ControlsAndElements()
    {
        foreach (var cell in new[] { false, true })
        foreach (var element in new[] { "😀", "e\u0301", "👩‍💻" })
        foreach (var forms in new[] { false, true })
        foreach (var deletionLocked in new[] { false, true })
        foreach (var backward in new[] { false, true })
            yield return [cell, element, forms, deletionLocked, backward];
    }

    [Theory]
    [MemberData(nameof(ControlsAndElements))]
    public Task EditableControlHistoryPreservesTheCaretAndFormsPermission(
        bool cell, string element, bool forms, bool deletionLocked, bool backward) => Session.Dispatch(() =>
    {
        var paragraph = new Paragraph("L");
        var control = Run.PlainTextControl("a" + element + "b");
        if (deletionLocked) control.Control = control.Control! with { LockMode = ContentControlLockMode.ControlLocked };
        paragraph.Runs.Add(control);
        paragraph.Runs.Add(new Run("R"));
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        if (cell)
        {
            var table = Table.Create(1, 1);
            table.Rows[0].Cells[0].Paragraphs.Clear();
            table.Rows[0].Cells[0].Paragraphs.Add(paragraph);
            document.Blocks.Add(table);
        }
        else document.Blocks.Add(paragraph);
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(816, 4000));
        if (forms) view.SetProtection(ProtectionMode.FillingForms);
        var before = backward ? 2 + element.Length : 2;
        if (cell) view.PlaceCaretInCell(0, 0, 0, 0, before);
        else view.MoveCaretToBlockForTest(0, before);

        EditorKey(view, backward ? Key.Back : Key.Delete);
        paragraph.PlainText.Should().Be("LabR");
        AssertCaret(view, 2);
        view.CanUndo.Should().BeTrue("the wrapper must retain the command's FormField mutation kind");
        EditorKey(view, Key.Z, KeyModifiers.Meta);
        paragraph.PlainText.Should().Be("La" + element + "bR");
        AssertCaret(view, before);
        view.CanRedo.Should().BeTrue();
        EditorKey(view, Key.Z, KeyModifiers.Meta | KeyModifiers.Shift);
        paragraph.PlainText.Should().Be("LabR");
        AssertCaret(view, 2);
        EditorKey(view, Key.Z, KeyModifiers.Meta);
        AssertCaret(view, before);
        view.InsertText("X");

        paragraph.PlainText.Should().Be(backward ? "La" + element + "XbR" : "LaX" + element + "bR");
        paragraph.Runs.Single(run => run.Control is not null).Control!.LockMode.Should().Be(
            deletionLocked ? ContentControlLockMode.ControlLocked : control.Control!.LockMode);
    }, CancellationToken.None);

    [Theory]
    [InlineData("table")]
    [InlineData("drawing")]
    [InlineData("shape-text")]
    [InlineData("header")]
    public Task HistoryDiscardsALaterSelectionInAnotherStory(string laterSelection) => Session.Dispatch(() =>
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("a😀b"));
        var table = Table.Create(1, 2);
        table.Rows[0].Cells[0] = new TableCell("left cell");
        table.Rows[0].Cells[1] = new TableCell("right cell");
        document.Blocks.Add(table);
        var shape = new Shape(ShapeKind.TextBox, 144, 108, "#FFFFFF")
        {
            Placement = new FloatingPlacement { Wrapping = ImageWrapping.InFront },
        };
        shape.TextParagraphs.Add(new Paragraph("shape untouched"));
        var shapeParagraph = new Paragraph();
        shapeParagraph.Runs.Add(new Run(shape.PlainText) { Shape = shape });
        document.Blocks.Add(shapeParagraph);
        document.FinalSectionHeadersFooters.Header = new HeaderFooter("header untouched");
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(816, 4000));
        view.MoveCaretToBlockForTest(0, 3);
        EditorKey(view, Key.Back);
        void SelectOtherStory()
        {
            if (laterSelection == "table") view.SetCellBlockSelection(1, 0, 0, 0, 1);
            else if (laterSelection == "header") view.PlaceCaretInHeaderFooter(false, 0, 2);
            else
            {
                view.SelectFloating(2, 0);
                if (laterSelection == "shape-text") view.EnterSelectedShapeTextEditing().Should().BeTrue();
            }
        }

        SelectOtherStory();
        EditorKey(view, Key.Z, KeyModifiers.Meta);
        AssertCaret(view, 3);
        view.SelectedCellRange.Should().BeNull();
        SelectOtherStory();
        EditorKey(view, Key.Z, KeyModifiers.Meta | KeyModifiers.Shift);
        AssertCaret(view, 1);
        view.SelectedCellRange.Should().BeNull();
        EditorKey(view, Key.Z, KeyModifiers.Meta);
        EditorKey(view, Key.Left);
        AssertCaret(view, 1);
        EditorKey(view, Key.Right);
        AssertCaret(view, 3);
        view.InsertText("X");

        ((Paragraph)view.Document.Blocks[0]).PlainText.Should().Be("a😀Xb");
        table.Rows[0].Cells.Select(cell => cell.PlainText).Should().Equal("left cell", "right cell");
        shape.TextParagraphs.Single().PlainText.Should().Be("shape untouched");
        document.FinalSectionHeadersFooters.Header!.Paragraphs.Single().PlainText.Should().Be("header untouched");
    }, CancellationToken.None);

    [Fact]
    public Task ThrowingSelectionObserverDoesNotReverseASuccessfulHistoryTransition() => Session.Dispatch(() =>
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("a😀b"));
        var shape = new Shape(ShapeKind.TextBox, 144, 108, "#FFFFFF")
        {
            Placement = new FloatingPlacement { Wrapping = ImageWrapping.InFront },
        };
        var drawing = new Paragraph();
        drawing.Runs.Add(new Run(string.Empty) { Shape = shape });
        document.Blocks.Add(drawing);
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(816, 4000));
        view.MoveCaretToBlockForTest(0, 3);
        EditorKey(view, Key.Back);
        view.SelectFloating(1, 0);
        var observerFailure = new InvalidOperationException("selection observer failed");
        void Observer() => throw observerFailure;
        view.FloatingSelectionChanged += Observer;
        var caretNotifications = 0;
        view.CaretMoved += () => caretNotifications++;

        var undo = () => view.Undo();
        undo.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(observerFailure);

        ((Paragraph)view.Document.Blocks[0]).PlainText.Should().Be("a😀b");
        view.CanUndo.Should().BeFalse("the successful source revert belongs on the redo stack");
        view.CanRedo.Should().BeTrue();
        AssertCaret(view, 3);
        caretNotifications.Should().Be(1, "the final caret notification survives a selection observer failure");
        view.FloatingSelectionChanged -= Observer;
        view.Redo();
        ((Paragraph)view.Document.Blocks[0]).PlainText.Should().Be("ab");
        AssertCaret(view, 1);
        view.CanUndo.Should().BeTrue();
        view.CanRedo.Should().BeFalse();
    }, CancellationToken.None);

    private static (DocumentView View, Func<string> Content) Story(string story, string text)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        if (story == "cell")
        {
            var table = Table.Create(1, 1);
            table.Rows[0].Cells[0] = new TableCell(text);
            document.Blocks.Add(table);
            document.Blocks.Add(new Paragraph("body untouched"));
        }
        else if (story == "shape")
        {
            var shape = new Shape(ShapeKind.TextBox, 144, 108, "#FFFFFF")
            {
                Placement = new FloatingPlacement { Wrapping = ImageWrapping.InFront },
            };
            shape.TextParagraphs.Add(new Paragraph(text));
            var paragraph = new Paragraph("body untouched");
            paragraph.Runs.Add(new Run(shape.PlainText) { Shape = shape });
            document.Blocks.Add(paragraph);
        }
        else
        {
            document.Blocks.Add(new Paragraph(story == "body" ? text : "body untouched"));
            if (story == "header") document.FinalSectionHeadersFooters.Header = new HeaderFooter(text);
        }
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(816, 4000));
        if (story == "cell") view.PlaceCaretInCell(0, 0, 0, 0, text.Length);
        else if (story == "header") view.PlaceCaretInHeaderFooter(false, 0, text.Length);
        else if (story == "shape")
        {
            view.SelectFloating(0, 1);
            view.EnterSelectedShapeTextEditing().Should().BeTrue();
            view.SelectShapeTextRangeForTest(0, text.Length, text.Length);
        }
        else view.MoveCaretToBlockForTest(0, text.Length);
        return (view, () => story switch
        {
            "cell" => ((Table)view.Document.Blocks[0]).Rows[0].Cells[0].Paragraphs[0].PlainText,
            "header" => view.Document.FinalSectionHeadersFooters.Header!.Paragraphs[0].PlainText,
            "shape" => ((Paragraph)view.Document.Blocks[0]).Runs[1].Shape!.TextParagraphs[0].PlainText,
            _ => view.Document.PlainText,
        });
    }

    private static void AssertCaret(DocumentView view, int offset)
    {
        view.SelectedText.Should().BeEmpty();
        var snapshot = view.AutomationSnapshot();
        snapshot.CaretOffset.Should().Be(offset);
        snapshot.Selection.Should().BeNull();
    }

    private static void ShellKey(MainWindow window, Key key, KeyModifiers modifiers = KeyModifiers.Meta)
    {
        var args = new KeyEventArgs { Key = key, KeyModifiers = modifiers };
        window.RaiseKeyDownForTest(args);
        args.Handled.Should().BeTrue();
    }

    private static void EditorKey(DocumentView view, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        var args = new KeyEventArgs { Key = key, KeyModifiers = modifiers, RoutedEvent = DocumentView.KeyDownEvent };
        view.RaiseKeyDownForContextMenuTests(args);
        args.Handled.Should().BeTrue();
    }

    private sealed class Clipboard(string text, bool rich) : IPlatformClipboard
    {
        private PlatformClipboardContent _content = Content(text, rich);
        public void Set(string value, bool useRich) => _content = Content(value, useRich);
        public ValueTask<PlatformClipboardReadResult<PlatformClipboardContent>> ReadAsync(
            PlatformClipboardReadRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformClipboardReadResult<PlatformClipboardContent>.Success(_content));
        public ValueTask<PlatformClipboardWriteResult> WriteAsync(
            PlatformClipboardContent content, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformClipboardWriteResult.Success());
        public ValueTask<PlatformClipboardWriteResult> ClearAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformClipboardWriteResult.Success());

        private static PlatformClipboardContent Content(string value, bool useRich) => useRich
            ? new(Text: value, CustomData: [PlatformClipboardData.FromText("Rich Text Format",
                @"{\rtf1\ansi " + string.Concat(value.Select(character => character > 127
                    ? @"\u" + unchecked((short)character) + "?" : character.ToString())) + "}")])
            : new(Text: value);
    }
}
